// Ported (trimmed) from openclicky windows/src/OpenClicky.Core/Answers/PlanCLIs.cs
// (MIT, Copyright (c) 2026 Prasanth Sasikumar and OpenClicky contributors).

using System.ComponentModel;
using System.Diagnostics;

namespace HeyGanesh.Providers.ClaudeCode;

/// <summary>What one CLI run left behind.</summary>
public sealed record PlanCliOutput(int ExitCode, string StandardError, bool TimedOut);

/// <summary>
/// Finding, preparing and running a plan CLI. Shared by the Claude Code client now and the Codex client in
/// FEATURE-010 step 2.
/// </summary>
public static class PlanCli
{
    /// <summary>
    /// Variables that would make the CLI bill something other than the person's plan (an API key wins over
    /// the plan's sign-in), or that mark a nested Claude Code session and would make <c>claude -p</c> talk to
    /// the outer one (present whenever HeyGanesh is started from a terminal inside Claude Code).
    /// </summary>
    public static readonly IReadOnlyList<string> RemovedVariables =
    [
        "ANTHROPIC_API_KEY", "ANTHROPIC_AUTH_TOKEN", "OPENAI_API_KEY", "CODEX_HOME",
        "CLAUDECODE", "CLAUDE_CODE_ENTRYPOINT", "CLAUDE_CODE_SESSION_ID", "CLAUDE_CODE_CHILD_SESSION",
        "CLAUDE_CODE_SESSION_ATTENDED", "CLAUDE_CODE_MESSAGING_SOCKET", "CLAUDE_CODE_MESSAGING_TOKEN",
        "CLAUDE_CODE_EXECPATH", "CLAUDE_PID", "CLAUDE_EFFORT", "AI_AGENT",
    ];

    /// <summary>
    /// The native installer's <c>%USERPROFILE%\.local\bin\claude.exe</c>, then the first <c>claude.exe</c> on PATH.
    /// </summary>
    /// <remarks>
    /// shortcut: only a real .exe is found; an npm install (<c>claude.cmd</c> shim) needs openclicky's
    /// PlanCliLaunch shim resolution, port it when someone runs Claude Code from npm.
    /// </remarks>
    public static string? LocateClaude()
    {
        var native = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", "claude.exe");
        if (File.Exists(native)) return native;
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir.Trim('"'), "claude.exe");
            if (File.Exists(candidate)) return candidate;
        }

        return null;
    }

    /// <summary>This process's environment minus <see cref="RemovedVariables"/>, with the CLI's own folder first on PATH.</summary>
    public static Dictionary<string, string> ProcessEnvironment(string executablePath)
    {
        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry e in Environment.GetEnvironmentVariables())
        {
            env[(string)e.Key] = (string?)e.Value ?? "";
        }

        foreach (var name in RemovedVariables) env.Remove(name);
        env.TryGetValue("PATH", out var path);
        env["PATH"] = (Path.GetDirectoryName(executablePath) ?? "") + Path.PathSeparator + path;
        return env;
    }

    /// <summary>
    /// A fresh empty folder for one run: the CLI treats its working folder as a project, so it must never be
    /// the person's home or Documents. The caller removes it.
    /// </summary>
    public static string MakeScratch()
    {
        var dir = Path.Combine(Path.GetTempPath(), "heyganesh-plan-cli", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Removes a scratch folder quietly (a file a dying process still holds stays for the temp clean-up).</summary>
    public static void RemoveScratch(string dir)
    {
        try
        {
            Directory.Delete(dir, recursive: true);
        }
        catch (Exception)
        {
        }
    }

    /// <summary>The last non-empty line of stderr, where the CLIs put a one-line reason.</summary>
    public static string? LastLine(string text) =>
        text.Split('\n', '\r').Select(l => l.Trim()).LastOrDefault(l => l.Length > 0);

    /// <summary>
    /// Runs the CLI. stdout and stderr are drained to end of file (a full pipe would stall the child), stdin is
    /// written on its own task so a large request (base64 screenshots) cannot block, and the whole process
    /// tree is killed at the timeout or on cancellation (then this throws <see cref="OperationCanceledException"/>).
    /// </summary>
    public static async Task<PlanCliOutput> RunAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        byte[] standardInput,
        string workingDirectory,
        IReadOnlyDictionary<string, string> environment,
        TimeSpan timeout,
        Action<byte[]> onStandardOutput,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var info = new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in arguments) info.ArgumentList.Add(a);
        info.Environment.Clear();
        foreach (var (k, v) in environment) info.Environment[k] = v;

        using var process = new Process { StartInfo = info };
        try
        {
            process.Start();
        }
        catch (Win32Exception e)
        {
            throw new IOException($"couldn't start {executablePath}: {e.Message}", e);
        }

        var stderr = new MemoryStream();
        var gate = new object();
        var timedOut = false;
        var stopped = false;

        void Stop(bool becauseOfTimeout)
        {
            lock (gate)
            {
                if (stopped) return;
                try
                {
                    if (process.HasExited) return;
                }
                catch (InvalidOperationException)
                {
                    return;
                }

                stopped = true;
                timedOut = becauseOfTimeout;
            }

            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Exception)
            {
                // it ended on its own meanwhile
            }
        }

        var outTask = Task.Run(async () =>
        {
            var buffer = new byte[16 * 1024];
            var stream = process.StandardOutput.BaseStream;
            int n;
            while ((n = await stream.ReadAsync(buffer).ConfigureAwait(false)) > 0) onStandardOutput(buffer.AsSpan(0, n).ToArray());
        });
        var errTask = Task.Run(async () =>
        {
            var buffer = new byte[16 * 1024];
            var stream = process.StandardError.BaseStream;
            int n;
            while ((n = await stream.ReadAsync(buffer).ConfigureAwait(false)) > 0)
            {
                lock (gate) stderr.Write(buffer, 0, n);
            }
        });
        var inTask = Task.Run(async () =>
        {
            try
            {
                // A child that exits before reading all of stdin makes the write fail; nothing more.
                await process.StandardInput.BaseStream.WriteAsync(standardInput).ConfigureAwait(false);
                await process.StandardInput.BaseStream.FlushAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
            }
            finally
            {
                try
                {
                    process.StandardInput.Close();
                }
                catch (Exception)
                {
                }
            }
        });

        using var timer = new CancellationTokenSource(timeout);
        using var onTimeout = timer.Token.Register(() => Stop(becauseOfTimeout: true));
        using var onCancel = ct.Register(() => Stop(becauseOfTimeout: false));

        await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            await Task.WhenAll(outTask, errTask, inTask).ConfigureAwait(false);
        }
        catch (Exception) when (stopped)
        {
            // a killed tree can break the pipes mid-read
        }

        ct.ThrowIfCancellationRequested();
        lock (gate)
        {
            return new PlanCliOutput(process.ExitCode, System.Text.Encoding.UTF8.GetString(stderr.ToArray()), timedOut);
        }
    }
}
