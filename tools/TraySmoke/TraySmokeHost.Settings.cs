using HeyGanesh.App;
using HeyGanesh.Security;
using HeyGanesh.Tray;

namespace HeyGanesh.Tools.TraySmoke;

internal static partial class TraySmokeHost
{
    private static readonly string ClaudeExe = @"C:\fake\claude.exe";
    private static readonly string CodexExe = @"C:\fake\codex.cmd";

    // PS-1 (DEC-082): which teacher is active and what each provider row says, with no UI, no CLI and no real key:
    // fake locators, a fake credential-file check, an in-memory key store and a temporary preference file.
    private static int RunAssertProviderModel()
    {
        var failures = 0;
        var pref = Path.Combine(TempFolder("pref"), "teacher.txt");
        var store = new MemoryStore();
        using var ring = new KeyRing(store, _ => null);
        var claudeInstalled = true;
        var codexInstalled = false;
        var credentials = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        ProviderStatusModel Model() => new(ring, () => claudeInstalled ? ClaudeExe : null, () => codexInstalled ? CodexExe : null,
            credentials.Contains, @"C:\home", pref);

        var rows = Model().Rows();
        failures = Check(rows.Select(r => r.Id).SequenceEqual(new[] { ShellHost.Teacher.DeepSeek, ShellHost.Teacher.ClaudePlan, ShellHost.Teacher.CodexPlan }), "three rows: DeepSeek, Claude plan, Codex plan", failures);
        failures = Check(rows[0].State == ProviderState.NeedsKey && rows[1].State == ProviderState.NotSignedIn && rows[2].State == ProviderState.NotInstalled,
            $"no key, claude installed but no credential file, codex missing ({rows[0].State}/{rows[1].State}/{rows[2].State})", failures);
        failures = Check(Model().Headline() == "Claude Code plan" && rows[1].Active, "with no key the installed Claude plan is the headline, and active even though signed out", failures);

        credentials.Add(@"C:\home\.claude\.credentials.json");
        failures = Check(Model().Rows()[1] is { State: ProviderState.Ready, Detail: "Signed in, no key needed", Active: true }, "the credential file makes it Ready and active", failures);

        Model().SaveKey(MadeUp);
        rows = Model().Rows();
        failures = Check(rows[0].State == ProviderState.Ready && rows[0].Active && !rows[1].Active, "a saved DeepSeek key becomes the active teacher (the automatic order)", failures);

        var said = Model().Choose(ShellHost.Teacher.ClaudePlan);
        rows = Model().Rows();
        failures = Check(File.ReadAllText(pref) == "claude" && rows[1].Active && !rows[0].Active && said.Contains("next time", StringComparison.Ordinal), $"choosing Claude is saved and wins over the key ({said})", failures);

        claudeInstalled = false;
        failures = Check(Model().Rows()[0].Active, "a saved choice that is no longer available falls back to the automatic order", failures);
        failures = Check(ShellHost.Resolve(ShellHost.Teacher.None, false, false, false) == ShellHost.Teacher.None, "Resolve with nothing available is None", failures);

        File.WriteAllText(pref, "gibberish");
        failures = Check(TeacherPreference.Load(pref) == ShellHost.Teacher.None && TeacherPreference.Load(Path.Combine(pref, "no")) == ShellHost.Teacher.None, "an unknown or unreadable preference reads as no choice", failures);
        failures = Check(!TeacherPreference.Save(ShellHost.Teacher.DeepSeek, Path.Combine(pref, "x", "teacher.txt")), "an unwritable preference path is said (false), not thrown", failures);

        failures = Check(ProviderStatusModel.SignIn(ShellHost.Teacher.ClaudePlan) is { Arguments: var a } && a.Contains("claude auth login", StringComparison.Ordinal)
            && ProviderStatusModel.SignIn(ShellHost.Teacher.CodexPlan)!.Value.Arguments.Contains("codex login", StringComparison.Ordinal) && ProviderStatusModel.SignIn(ShellHost.Teacher.DeepSeek) is null,
            "sign-in runs each CLI's own login command; DeepSeek has none", failures);

        var (okEmpty, hintEmpty) = ProviderStatusModel.CheckKey("  ");
        var (okSpace, hintSpace) = ProviderStatusModel.CheckKey("abcdefghij klmnopqrstuv");
        var (okShort, hintShort) = ProviderStatusModel.CheckKey("abc123");
        var (okGood, hintGood) = ProviderStatusModel.CheckKey("  " + MadeUp + "\r\n");
        failures = Check(!okEmpty && hintEmpty is null && !okSpace && hintSpace == "A key has no spaces or line breaks." && okShort && hintShort!.Contains("short", StringComparison.Ordinal) && okGood && hintGood is null,
            "inline check: empty and a space inside cannot be saved (and say why), short can with a nudge, a good key (trimmed) is clean", failures);
        failures = Check(!(hintSpace + hintShort).Contains("abc123", StringComparison.Ordinal) && !(hintSpace + hintShort).Contains("klmno", StringComparison.Ordinal), "a hint never repeats what was typed", failures);

        var texts = Model().Rows().SelectMany(r => new[] { r.Name, r.Detail }).Append(Model().Headline()).Append(said);
        failures = Check(texts.All(t => !t.Contains("hgmadeup", StringComparison.Ordinal)), "no text the window would show contains a key value", failures);
        return Summarize(failures, "PROVIDER MODEL OK");
    }

    // PS-2: the real notch window. Local: it shows a window and takes focus, by design. Fake CLIs, in-memory keys.
    private static int RunAssertSettingsWindow()
    {
        var failures = 0;
        var results = new List<(bool Ok, string Label)>();
        var pref = Path.Combine(TempFolder("pref"), "teacher.txt");
        var shots = TempFolder("settings-shot");
        var store = new MemoryStore();
        using var ring = new KeyRing(store, _ => null);

        RunSta(() =>
        {
            var model = new ProviderStatusModel(ring, () => ClaudeExe, () => null, _ => true, @"C:\home", pref);
            using var pill = new SettingsPill(model);
            pill.Show();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (clock.ElapsedMilliseconds < 2500 && Math.Abs(pill.Height - pill.TargetHeight) > 1)
            {
                Application.DoEvents();
                Thread.Sleep(15);
            }

            results.Add((Math.Abs(pill.Height - pill.TargetHeight) <= 1 && pill.Height > 150, $"the spring settles on its target height ({pill.Height} of {pill.TargetHeight})"));
            results.Add((pill.Top == Screen.FromPoint(Cursor.Position).Bounds.Top && pill.Region is not null, "it hangs flush from the top edge and is shaped (a region)"));
            results.Add((pill.Headline == "Claude Code plan" && pill.ActionTexts.SequenceEqual(new[] { "Add key", "Active", "Get" }), $"headline and buttons ({pill.Headline}: {string.Join("/", pill.ActionTexts)})"));
            // For a human to look at: the window's own pixels (it is excluded from screen capture, so a screenshot would be blank).
            using (var bitmap = new Bitmap(pill.Width, pill.Height))
            {
                pill.DrawToBitmap(bitmap, new Rectangle(0, 0, pill.Width, pill.Height));
                var shot = Path.Combine(shots, "settings-closed.png");
                bitmap.Save(shot);
                Console.WriteLine($"snapshot    : {shot}");
            }

            results.Add((!pill.KeyPanelOpen, "the key box starts closed"));
            pill.Press(0);
            Application.DoEvents();
            Thread.Sleep(600);
            Application.DoEvents();
            using (var bitmap = new Bitmap(pill.Width, pill.Height))
            {
                pill.DrawToBitmap(bitmap, new Rectangle(0, 0, pill.Width, pill.Height));
                bitmap.Save(Path.Combine(shots, "settings-key.png"));
            }

            results.Add((pill.KeyPanelOpen && pill.KeyBox.UseSystemPasswordChar, "Add key opens a masked box"));
            results.Add((pill.TabStops().SequenceEqual(new[] { "DeepSeek key: Close", "DeepSeek key", "Save", "Claude Code plan: Active", "ChatGPT plan (Codex): Get", "Recheck", "Done" }), $"Tab order, with the key box open ({string.Join(" > ", pill.TabStops())})"));
            pill.KeyBox.Text = "has a space inside it";
            Application.DoEvents();
            pill.KeySaveButton.PerformClick();
            results.Add((!pill.CanSave && pill.Message == "A key has no spaces or line breaks." && store.Sets == 0, "a key with a space says why inline, and Save writes nothing"));
            pill.KeyBox.Text = "short1";
            Application.DoEvents();
            results.Add((pill.CanSave && pill.Message.Contains("short", StringComparison.Ordinal), "a short key can be saved, with a nudge"));
            pill.KeyBox.Text = MadeUp;
            pill.KeySaveButton.PerformClick();
            Application.DoEvents();
            var justChanged = pill.MessageColor;
            for (var i = 0; i < 30; i++)
            {
                Application.DoEvents();
                Thread.Sleep(10);
            }

            results.Add((!SystemInformation.UIEffectsEnabled || (justChanged != pill.MessageColor && justChanged.R < pill.MessageColor.R), $"a changed message fades in from the background ({justChanged.R} then {pill.MessageColor.R})"));
            results.Add((store.Sets == 1 && pill.KeyBox.Text.Length == 0 && !pill.KeyPanelOpen, "Save writes through the key ring once, clears the box and closes it"));
            results.Add((!pill.AllLabelText().Contains("hgmadeup", StringComparison.Ordinal) && pill.Message.Contains("Saved", StringComparison.Ordinal), "the window says it saved and shows no value"));
            results.Add((pill.Headline == "DeepSeek key" && pill.ActionTexts.SequenceEqual(new[] { "Active", "Use", "Get" }), $"a saved key becomes the active teacher ({pill.Headline}: {string.Join("/", pill.ActionTexts)})"));
            pill.Press(1);
            Application.DoEvents();
            results.Add((File.Exists(pref) && File.ReadAllText(pref) == "claude" && pill.Headline == "Claude Code plan", "Use on the Claude row saves the choice and moves the headline"));
            pill.Close();
        });

        foreach (var (ok, label) in results)
        {
            failures = Check(ok, label, failures);
        }

        return Summarize(failures, "SETTINGS WINDOW OK");
    }
}
