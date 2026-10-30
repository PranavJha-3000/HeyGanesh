using System.Diagnostics;
using System.Runtime.InteropServices;
using HeyGanesh.App;
using HeyGanesh.Security;
using HeyGanesh.Tray;

namespace HeyGanesh.Tools.TraySmoke;

internal static partial class TraySmokeHost
{
    private const string MadeUp = "hgmadeup7Q2xLm9VtR4wZp";

    // TK-1: the key screen's rules with no UI, over an in-memory store: what it says, what it writes, what it refuses.
    private static int RunAssertKeysModel()
    {
        var failures = 0;
        var store = new MemoryStore();
        var environment = new Dictionary<string, string>(StringComparer.Ordinal);
        var ring = new KeyRing(store, name => environment.GetValueOrDefault(name));
        var model = new KeyScreenModel(ring);
        var said = new List<string>();

        failures = Check(model.Keys.Select(k => k.Name).SequenceEqual(new[] { "deepseek", "typesafe", "fish" }) && model.Keys.All(k => model.Status(k.Name) == "missing"),
            "three keys, each 'missing' to start", failures);
        failures = Check(model.NeedsSetup, "with DeepSeek and TypeSafe missing, the screen opens by itself", failures);

        said.Add(model.Save("deepseek", "  " + MadeUp + " \r\n"));
        failures = Check(store.Sets == 1 && store.Text("deepseek") == MadeUp, "Save stores the trimmed key through the key ring: one write", failures);
        failures = Check(said[^1] == $"Saved the deepseek key in Windows Credential Manager ({MadeUp.Length} chars).", $"Save says what was stored, by length only ({said[^1]})", failures);
        said.Add(model.Status("deepseek"));
        failures = Check(said[^1] == $"present ({MadeUp.Length} chars, Credential Manager)", $"the status after Save ({said[^1]})", failures);

        said.Add(model.Save("typesafe", "   "));
        said.Add(model.Save("typesafe", "two words"));
        failures = Check(store.Sets == 1 && said[^2] == "Not saved: the key is empty." && said[^1] == "Not saved: the key contains a space, a line break or a control character.",
            $"an empty key and a key with a space inside are refused, nothing written ({said[^2]} / {said[^1]})", failures);

        said.Add(model.Save("typesafe", MadeUp + "ts"));
        failures = Check(!model.NeedsSetup, "with both required keys present, it does not open by itself", failures);
        failures = Check(model.Missing("fish") && !model.NeedsSetup, "Fish is optional: missing does not count", failures);

        store.Broken = true;
        said.Add(model.Save("fish", MadeUp + "fx"));
        failures = Check(said[^1] == "Not saved: Windows Credential Manager could not store the fish key.", $"a failing store is said, not thrown ({said[^1]})", failures);
        store.Broken = false;

        said.Add(model.Remove("deepseek"));
        failures = Check(said[^1] == "Removed the stored deepseek key." && model.Status("deepseek") == "missing", $"Remove deletes it ({said[^1]})", failures);
        said.Add(model.Remove("deepseek"));
        failures = Check(said[^1] == "No deepseek key was stored in Credential Manager.", $"removing twice says nothing was stored ({said[^1]})", failures);

        environment["FISH_AUDIO_API_KEY"] = MadeUp + "env";
        said.Add(model.Remove("fish"));
        said.Add(model.Status("fish"));
        failures = Check(said[^2].EndsWith("The FISH_AUDIO_API_KEY environment variable still provides one.", StringComparison.Ordinal) && said[^1].EndsWith("chars, environment)", StringComparison.Ordinal),
            $"a key that comes from the environment is said, and left alone ({said[^2]})", failures);

        said.AddRange(model.Keys.Select(k => KeyScreenModel.Purpose(k.Name)));
        failures = Check(said.All(s => !s.Contains("hgmadeup", StringComparison.Ordinal)), $"none of the {said.Count} texts the screen would show contains a value", failures);
        return Summarize(failures, "KEYS MODEL OK");
    }

    // TK-2: the real window, and the tray opening it. Local: it shows a window and takes focus, by design.
    private static int RunAssertKeysWindow()
    {
        var failures = 0;
        var store = new MemoryStore();
        using var ring = new KeyRing(store, _ => null);
        var results = new List<(bool Ok, string Label)>();

        RunSta(() =>
        {
            using var screen = new KeyScreen(new KeyScreenModel(ring));
            screen.Show();
            Application.DoEvents();
            results.Add((screen.Visible && KeyRing.Known.All(k => screen.Box(k.Name).UseSystemPasswordChar), "the window shows one masked box per key"));

            screen.Box("deepseek").Text = MadeUp;
            Application.DoEvents();
            results.Add((store.Sets == 0, "typing writes nothing until Save is clicked"));

            screen.SaveButton("deepseek").PerformClick();
            Application.DoEvents();
            results.Add((store.Sets == 1 && store.Text("deepseek") == MadeUp && screen.Box("deepseek").Text.Length == 0, "Save writes once through the key ring and clears the box"));
            results.Add((screen.StatusText("deepseek") == $"Status: present ({MadeUp.Length} chars, Credential Manager)" && screen.StatusText("typesafe") == "Status: missing",
                $"the statuses update ({screen.StatusText("deepseek")} / {screen.StatusText("typesafe")})"));
            results.Add((!screen.AllLabelText().Contains("hgmadeup", StringComparison.Ordinal), "no label on the window holds the value"));

            screen.RemoveButton("deepseek").PerformClick();
            Application.DoEvents();
            results.Add((store.Text("deepseek") is null && screen.StatusText("deepseek") == "Status: missing" && screen.Message == "Removed the stored deepseek key.", $"Remove deletes it ({screen.Message})"));
        });

        // The tray: the screen opens by itself while a required key is missing, and pauses the trigger while open.
        RunSta(() =>
        {
            var pausedSeen = new List<bool>();
            Func<bool>? asked = null;
            using var tray = new TrayApp(async (stop, hookLive, paused) =>
            {
                asked = paused;
                hookLive();
                try
                {
                    await Task.Delay(Timeout.Infinite, stop);
                }
                catch (OperationCanceledException)
                {
                }

                return 0;
            }, ring, TempFolder("keys"), null, openKeysIfMissing: true);

            var clock = Stopwatch.StartNew();
            while (tray.OpenKeyScreen is null && clock.ElapsedMilliseconds < 3000)
            {
                Application.DoEvents();
                Thread.Sleep(20);
            }

            results.Add((tray.OpenKeyScreen is { Visible: true }, "with required keys missing, the tray opens the key screen by itself"));
            results.Add((tray.MenuTexts.Contains("Keys…"), "the menu has Keys…"));
            pausedSeen.Add(asked?.Invoke() == true);
            tray.OpenKeys();
            results.Add((tray.OpenKeyScreen is not null && Application.OpenForms.OfType<KeyScreen>().Count() == 1, "Keys… while it is open brings the same screen forward, never a second one"));
            tray.OpenKeyScreen!.Close();
            Application.DoEvents();
            pausedSeen.Add(asked?.Invoke() == true);
            results.Add((pausedSeen.SequenceEqual(new[] { true, false }) && tray.OpenKeyScreen is null, "the shell is told the trigger is paused while the screen is open, and not after it closes"));
            tray.Quit();
        });

        RunSta(() =>
        {
            ring.Store("deepseek", MadeUp);
            ring.Store("typesafe", MadeUp);
            using var tray = new TrayApp(async (stop, hookLive, _) => { hookLive(); try { await Task.Delay(Timeout.Infinite, stop); } catch (OperationCanceledException) { } return 0; },
                ring, TempFolder("keys"), null, openKeysIfMissing: true);
            for (var i = 0; i < 25; i++)
            {
                Application.DoEvents();
                Thread.Sleep(20);
            }

            results.Add((tray.OpenKeyScreen is null, "with both required keys present, it stays closed"));
            tray.Quit();
        });

        foreach (var (ok, label) in results)
        {
            failures = Check(ok, label, failures);
        }

        return Summarize(failures, "KEYS WINDOW OK");
    }

    // TK-3: the real shell and hook. While paused, an injected Ctrl+Alt starts no turn. MOVES NO CURSOR, but presses Ctrl+Alt once.
    private static async Task<int> RunAssertKeysPauseAsync()
    {
        var failures = 0;
        var original = Console.Out;
        var captured = new StringWriter();
        Console.SetOut(TextWriter.Synchronized(captured));
        string output;
        int code;
        try
        {
            using var stop = new CancellationTokenSource();
            var live = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var run = Task.Run(() => ShellHost.RunAsync(Array.Empty<string>(), stop.Token, () => live.TrySetResult(), () => true));
            await Task.WhenAny(live.Task, Task.Delay(TimeSpan.FromSeconds(20)));
            await Task.Delay(300);
            PressCtrlAlt();
            await Task.Delay(1000);
            stop.Cancel();
            code = await run.WaitAsync(TimeSpan.FromSeconds(30));
            output = captured.ToString();
        }
        finally
        {
            Console.SetOut(original);
        }

        failures = Check(output.Contains("REFUSED: the key screen is open", StringComparison.Ordinal), "a Ctrl+Alt press while paused is refused, and says why", failures);
        failures = Check(output.Contains("chord released (nothing was listening)", StringComparison.Ordinal) && !output.Contains("closing the microphone", StringComparison.Ordinal) && !output.Contains("VOICE", StringComparison.Ordinal),
            "no turn started: nothing was listening when the chord came up", failures);
        failures = Check(code == 0 && output.Contains("still running       : False", StringComparison.Ordinal), $"the shell then stopped cleanly (exit {code})", failures);
        return Summarize(failures, "KEYS PAUSE OK");
    }

    private static void RunSta(Action body)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                body();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(20)) || error is not null)
        {
            throw new InvalidOperationException($"the window thread {(error is null ? "did not finish" : "failed: " + error.Message)}");
        }
    }

    /// <summary>Left Ctrl and left Alt down, held 400 ms, then up: one chord, as a person would press it.</summary>
    private static void PressCtrlAlt()
    {
        const ushort LeftControl = 0xA2, LeftAlt = 0xA4;
        const uint KeyUp = 0x0002;
        Send(LeftControl, 0);
        Send(LeftAlt, 0);
        Thread.Sleep(400);
        Send(LeftAlt, KeyUp);
        Send(LeftControl, KeyUp);
    }

    private static void Send(ushort virtualKey, uint flags)
    {
        var input = new Input { Type = 1, Union = new InputUnion { Keyboard = new KeyboardInput { VirtualKey = virtualKey, Flags = flags } } };
        SendInput(1, new[] { input }, Marshal.SizeOf<Input>());
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Union;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KeyboardInput Keyboard;
        [FieldOffset(0)] public MouseInput Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort Scan;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X, Y;
        public uint Data, Flags, Time;
        public IntPtr ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);

    /// <summary>A Credential Manager in memory that counts writes and can be made to fail.</summary>
    private sealed class MemoryStore : ISecretStore
    {
        private readonly Dictionary<string, byte[]> _bytes = new(StringComparer.Ordinal);

        public int Sets { get; private set; }

        public bool Broken { get; set; }

        public string? Text(string name) => _bytes.TryGetValue($"{name}:{KeyRing.Alias}", out var b) ? System.Text.Encoding.UTF8.GetString(b) : null;

        public SecretHandle? Get(string providerId, string keyAlias) =>
            _bytes.TryGetValue($"{providerId}:{keyAlias}", out var b) ? SecretHandle.Create(b) : null;

        public void Set(string providerId, string keyAlias, ReadOnlyMemory<byte> secret)
        {
            if (Broken)
            {
                throw new SecretStoreException("store unavailable", 1312);
            }

            Sets++;
            _bytes[$"{providerId}:{keyAlias}"] = secret.ToArray();
        }

        public bool Exists(string providerId, string keyAlias) => _bytes.ContainsKey($"{providerId}:{keyAlias}");

        public void Delete(string providerId, string keyAlias) => _bytes.Remove($"{providerId}:{keyAlias}");

        public void Dispose()
        {
        }
    }
}
