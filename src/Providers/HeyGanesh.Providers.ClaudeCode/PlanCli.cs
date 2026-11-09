// Ported (trimmed) from openclicky windows/src/OpenClicky.Core/Answers/PlanCLIs.cs
// (MIT, Copyright (c) 2026 Prasanth Sasikumar and OpenClicky contributors).

using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace HeyGanesh.Providers.ClaudeCode;

/// <summary>What one CLI run left behind.</summary>
public sealed record PlanCliOutput(int ExitCode, string StandardError, bool TimedOut);

/// <summary>
/// Finding, preparing and running a plan CLI. Shared by the Claude Code client and the Codex client
/// (FEATURE-010 steps 1 and 2).
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

    /// <summary>Claude Code: the native installer's <c>%USERPROFILE%\.local\bin</c>, then npm's folder, then PATH.</summary>
    public static string? LocateClaude() => Locate("claude",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin"), NpmDirectory);

    /// <summary>Codex: npm's folder (<c>npm i -g @openai/codex</c>), then PATH.</summary>
    public static string? LocateCodex() => Locate("codex", NpmDirectory);

    private static string NpmDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm");

    /// <summary>The first <c>name.exe</c> or <c>name.cmd</c> in the preferred folders, then on PATH.</summary>
    public static string? Locate(string name, params string[] preferredDirectories)
    {
        var path = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        foreach (var dir in preferredDirectories.Concat(path.Select(d => d.Trim('"'))))
        {
            foreach (var ext in new[] { ".exe", ".cmd" })
            {
                var candidate = Path.Combine(dir, name + ext);
                if (File.Exists(candidate)) return candidate;
            }
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
        // An npm CLI is a Node script: node must resolve, and npm keeps the shim next to it.
        var node = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs");
        env["PATH"] = string.Join(Path.PathSeparator, Path.GetDirectoryName(executablePath) ?? "", node, path);
        return env;
    }

    /// <summary>How a CLI path becomes a process: what to start and its arguments (a list, or one raw string for cmd.exe).</summary>
    public sealed record Launch(string FileName, IReadOnlyList<string> ArgumentList, string? RawArguments);

    private static readonly Regex NpmShimScriptPattern = new(@"""%~?dp0%?\\([^""]+?\.(?:c|m)?js)""", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    // Characters cmd.exe treats specially (cross-spawn's list).
    private static readonly Regex CmdMetaCharacters = new(@"([()\][%!^""`<>&|;, *?])", RegexOptions.CultureInvariant);

    /// <summary>
    /// An .exe starts directly; an npm <c>.cmd</c> shim runs its Node script with node (which takes any argument as it
    /// is); any other .cmd/.bat goes through <c>cmd.exe /d /s /c</c> with every argument escaped. Ported from openclicky.
    /// </summary>
    /// <remarks>On the cmd.exe path a line break would end the command line, so multi-line arguments are folded onto
    /// one line; the clients therefore pass long text on stdin, never as an argument.</remarks>
    public static Launch Resolve(string executablePath, IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string> environment)
    {
        var ext = Path.GetExtension(executablePath).ToLowerInvariant();
        if (ext is not (".cmd" or ".bat")) return new Launch(executablePath, arguments, null);

        if (NpmShimScript(executablePath, ReadOrNull, File.Exists) is { } script)
        {
            var bundled = Path.Combine(Path.GetDirectoryName(executablePath) ?? "", "node.exe");
            environment.TryGetValue("PATH", out var pathVariable);
            var node = File.Exists(bundled)
                ? bundled
                : (pathVariable ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                    .Select(d => Path.Combine(d.Trim('"'), "node.exe")).FirstOrDefault(File.Exists);
            if (node is not null) return new Launch(node, [script, .. arguments], null);
        }

        var line = string.Join(" ", new[] { EscapeForCmd(executablePath) }.Concat(arguments.Select(a =>
            EscapeForCmd(a.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ')))));
        var cmd = environment.TryGetValue("ComSpec", out var comSpec) && !string.IsNullOrEmpty(comSpec) ? comSpec : "cmd.exe";
        return new Launch(cmd, [], $"/d /s /c \"{line}\"");
    }

    private static string? ReadOrNull(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>The Node script an npm <c>.cmd</c> shim runs ("%dp0%\node_modules\…\cli.js"), when it names one that exists.</summary>
    public static string? NpmShimScript(string shimPath, Func<string, string?> readShim, Func<string, bool> fileExists)
    {
        if (readShim(shimPath) is not { } text) return null;
        var matches = NpmShimScriptPattern.Matches(text);
        if (matches.Count == 0) return null;
        var script = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(shimPath) ?? "", matches[^1].Groups[1].Value));
        return fileExists(script) ? script : null;
    }

    /// <summary>One argument for <c>cmd.exe /d /s /c "…"</c>: quoted as the C runtime reads it back, then every cmd metacharacter escaped with ^.</summary>
    public static string EscapeForCmd(string argument)
    {
        // Backslashes before a quote are doubled and the quote escaped; trailing backslashes doubled.
        var quoted = Regex.Replace(argument, @"(\\*)""", m => m.Groups[1].Value + m.Groups[1].Value + "\\\"");
        quoted = Regex.Replace(quoted, @"(\\*)$", m => m.Groups[1].Value + m.Groups[1].Value);
        return CmdMetaCharacters.Replace("\"" + quoted + "\"", "^$1");
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
        var launch = Resolve(executablePath, arguments, environment);
        var info = new ProcessStartInfo
        {
            FileName = launch.FileName,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        if (launch.RawArguments is { } raw) info.Arguments = raw;
        else foreach (var a in launch.ArgumentList) info.ArgumentList.Add(a);
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
