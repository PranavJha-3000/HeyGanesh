using System.Text;

namespace HeyGanesh.Tray;

/// <summary>
/// Where the shell's console lines go when there is no console (DEC-052): one file per run,
/// <c>logs/shell-&lt;stamp&gt;.log</c> under the runtime data folder, and only the newest <see cref="Keep"/> kept.
/// It writes exactly what the console would show, so it carries no key value (the shell only ever prints
/// "present (N chars, source)") but it does carry transcribed questions, as the console does.
/// </summary>
internal sealed class TrayLog : IDisposable
{
    /// <summary>How many run logs stay on disk, this run's included.</summary>
    public const int Keep = 5;

    private const string Prefix = "shell-";
    private const string Extension = ".log";

    private readonly StreamWriter _file;

    private TrayLog(string folder, string path, StreamWriter file)
    {
        Folder = folder;
        Path = path;
        _file = file;
        Writer = TextWriter.Synchronized(file);
    }

    public string Folder { get; }

    public string Path { get; }

    /// <summary>Thread-safe and flushed per write, so a crash or a forced exit loses nothing already written.</summary>
    public TextWriter Writer { get; }

    /// <summary><c>$HEYGANESH_DATA_DIR/logs</c>, else <c>%LOCALAPPDATA%/HeyGanesh/logs</c>: the project's runtime-data convention.</summary>
    public static string DefaultFolder()
    {
        var root = Environment.GetEnvironmentVariable("HEYGANESH_DATA_DIR");
        if (string.IsNullOrWhiteSpace(root))
        {
            root = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HeyGanesh");
        }

        return System.IO.Path.Combine(root, "logs");
    }

    /// <summary>Prunes to the newest <see cref="Keep"/> - 1 old logs, then opens a new one (shared for reading, so it can be watched).</summary>
    public static TrayLog Open(string folder, DateTime now)
    {
        Directory.CreateDirectory(folder);

        // The stamp sorts by time, so name order is age order; anything that does not match the pattern is left alone.
        var old = Directory.GetFiles(folder, Prefix + "*" + Extension)
            .OrderByDescending(p => System.IO.Path.GetFileName(p), StringComparer.Ordinal)
            .Skip(Keep - 1);
        foreach (var stale in old)
        {
            try
            {
                File.Delete(stale);
            }
            catch (IOException)
            {
                // Still open elsewhere (another session's tray, an editor): kept, and pruned next time.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        var stamp = now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        for (var n = 1; ; n++)
        {
            var path = System.IO.Path.Combine(folder, Prefix + stamp + (n == 1 ? string.Empty : $"-{n}") + Extension);
            try
            {
                var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                return new TrayLog(folder, path, new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true });
            }
            catch (IOException) when (File.Exists(path) && n < 100)
            {
                // Two runs in the same second: the next suffix.
            }
        }
    }

    public void Dispose() => _file.Dispose();
}
