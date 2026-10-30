using System.Text;
using HeyGanesh.Security;

namespace HeyGanesh.App;

// Key commands (DEC-045): --keys, --set-key <name>, --remove-key <name>. They manage the KeyRing's Credential
// Manager entries and exit before the shell starts anything else. No value is ever written to the console:
// a key is typed or pasted with nothing echoed (or piped on stdin), and every report is KeyState's
// "present (N chars, source)" or "MISSING".

internal static partial class ShellHost
{
    /// <summary>
    /// The process's one key ring (DEC-046): every API client the shell composes reads its key through it, per
    /// request. Static and never disposed: it holds no key, only the Credential Manager handle, for the life
    /// of the process.
    /// </summary>
    private static readonly KeyRing Keys = new();

    /// <summary>True when the key is in Credential Manager or its environment variable: the gate for each leg.</summary>
    private static bool HasKey(string name) => Keys.Describe(name).Source != KeySource.Missing;

    private static int RunKeyCommand(string command, string name)
    {
        try
        {
            var ring = Keys;
            if (command == "keys")
            {
                Console.WriteLine("Provider keys (Credential Manager first, then the environment variable; values never shown):");
                foreach (var key in KeyRing.Known)
                {
                    Console.WriteLine($"  {key.Name,-9} {key.EnvironmentVariable,-19} {ring.Describe(key.Name)}");
                }

                return 0;
            }

            var known = KeyRing.Find(name);
            if (command == "remove")
            {
                Console.WriteLine(ring.Remove(known.Name)
                    ? $"removed the stored {known.Name} key from Credential Manager"
                    : $"no {known.Name} key was stored in Credential Manager; nothing removed");
                Console.WriteLine($"  {known.Name}: {ring.Describe(known.Name)}");
                return 0;
            }

            var typed = ReadHidden($"Paste or type the {known.Name} key, then press Enter (nothing is shown): ");
            try
            {
                if (typed.Length == 0 || typed.ToString().Trim().Length == 0)
                {
                    Console.WriteLine("nothing was entered; nothing stored (exit 1)");
                    return 1;
                }

                var length = ring.Store(known.Name, typed.ToString());
                Console.WriteLine($"stored the {known.Name} key in Windows Credential Manager ({length} chars; the value is not shown)");
                Console.WriteLine($"  {known.Name}: {ring.Describe(known.Name)}");
                return 0;
            }
            finally
            {
                typed.Clear();
            }
        }
        catch (ArgumentException ex) when (ex.ParamName == "name")
        {
            Console.WriteLine($"{ex.Message.Split(" (Parameter", 2)[0]} (exit 2)");
            return 2;
        }
        catch (ArgumentException ex) when (ex.ParamName == "value")
        {
            Console.WriteLine($"refused: {ex.Message.Split(" (Parameter", 2)[0]}; nothing stored (exit 1)");
            return 1;
        }
        catch (SecretStoreException ex)
        {
            Console.WriteLine($"Credential Manager refused the request (Win32 error {ex.Win32ErrorCode}) (exit 4)");
            return 4;
        }
    }

    /// <summary>
    /// One line of input with nothing echoed. Piped input (a script, or a row) is read as a line; a console is
    /// read key by key so the value never appears on screen. Backspace edits; Enter ends.
    /// </summary>
    private static StringBuilder ReadHidden(string prompt)
    {
        var text = new StringBuilder();
        if (Console.IsInputRedirected)
        {
            text.Append(Console.In.ReadLine());
            return text;
        }

        Console.Write(prompt);
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                break;
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (text.Length > 0)
                {
                    text.Length--;
                }

                continue;
            }

            if (key.KeyChar != '\0')
            {
                text.Append(key.KeyChar);
            }
        }

        Console.WriteLine();
        return text;
    }

}
