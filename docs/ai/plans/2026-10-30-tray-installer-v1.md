# Plan: a tray app, a first-run key screen, start at login, and an installer (build sequence step 11)

2026-10-30 · claude-opus-5-5 · STATUS: 11a BUILT 2026-10-30 (owner: "Yes, build 11a"): TY-1 6, TY-2 7, TY-3 5, TY-4 9, TY-5 8; DEC-052.
Changed while building: the rows are TY-1..TY-5 (TR-* is the Turn Router's); the menu has no Keys… or Start at login item
until 11b/11c build them; the log row (TY-2) is offline and the planted-key check moved into TY-5, on the real exe.
11b BUILT 2026-10-30 (owner: "Yes, build 11b"): TK-1 13, TK-2 11, TK-3 3; DEC-053. It opens by itself for a missing DeepSeek or
TypeSafe key (decision 3, as recommended). 11c BUILT 2026-10-30 (owner: "Yes, build 11c"): TS-1 10, TS-2 7; DEC-054. The
throwaway key is `HKCU\Software\HeyGanesh\Test-<id>\Run`. 11d BUILT 2026-10-30 (owner: "A: script installer", self-contained with
the download): IN-1 PACKAGE OK (513 files, 198 MB), IN-2 15; DEC-055. Step 11 is complete.

## Problem, restated

Today Hey Ganesh only runs as `HeyGanesh.App.exe` in a console window. You start it from a terminal and stop it
with Ctrl+C. Keys are set with `--set-key`, also in a terminal. Nothing starts it at login, and there is no
install. A student would need a terminal for all of that. Step 11 makes it an ordinary desktop app:

- an icon in the notification area that runs the hotkey pipeline in the background;
- a small window to enter keys the first time, and later;
- an option to start at login;
- a way to install and uninstall it.

The console exe stays exactly as it is. Every SH row, every exit code and the CI gates depend on it.

## Design

**A new thin host, `src/Experience/HeyGanesh.Tray` (`OutputType=WinExe`, so no console window).** It references
`HeyGanesh.App` and runs the same composition through `ShellHost.RunAsync`. The pipeline is not built twice.

- **Stop seam (the one change to the App):** `ShellHost.RunAsync(string[] args, CancellationToken stop)`. Hooked
  mode's wait (`stop.Wait()` today: Ctrl+C or `--seconds`) also ends when this token fires. The existing
  `RunAsync(args)` passes `CancellationToken.None`, so console behaviour is unchanged.
- **Threads:** the tray icon lives on its own STA thread with a WinForms message loop. The pipeline runs where it
  runs today. DPI is set to PerMonitorV2 before anything else, as `Program.cs` does.
- **Icon:** WinForms `NotifyIcon`. WinForms is part of the .NET Desktop runtime (a framework reference, not
  NuGet), and LessonSurface already uses it, so there is no new package.
- **Menu:**
  - status lines: listening (hook live), and each key present or missing (presence only, never a value);
  - "Keys…";
  - "Start at login" (a check mark);
  - "Open log folder";
  - "Quit".

  Quit fires the stop token and waits for the pipeline to drain, so the keyboard hook is released in order,
  just as after Ctrl+C.
- **Output:** the shell's console lines go to `%LOCALAPPDATA%/HeyGanesh/logs/shell-<stamp>.log` (honouring
  `HEYGANESH_DATA_DIR`); only the last 5 logs are kept. The shell never prints a key value today, and the key
  rows check that, so the log carries none. It does carry the user's transcribed questions, as the console
  does today (see *Your decisions*).
- **One instance:** a named mutex `Local\HeyGanesh.Tray`. Two hooks would answer every Ctrl+Alt twice. A second
  launch shows a balloon and exits.

**Steps 11b–11d** add the key screen, start at login and the installer. They are described in the table below.

## Steps (each approved on its own)

| Step | What | Where | Rows |
|---|---|---|---|
| **11a** | The tray host: `RunAsync(args, stop)`, the icon and menu, Quit drains the pipeline, the log file, one instance | `HeyGanesh.App/ShellHost.cs` (stop seam only), new `src/Experience/HeyGanesh.Tray`, new `tools/TraySmoke` | TR-A1 `--assert-source` (WinExe, no PackageReference, no key reads except through KeyRing); TR-A2 `--assert-stop` (offline: the stop token ends hooked mode within 2 s and drains; no Ctrl+C); TR-A3 `--assert-log` (rotation keeps 5; a planted fake key value given to the shell never reaches the file); TR-A4 `--assert-tray` (local-only: the real icon appears, its menu has the five items, Quit exits 0 with the hook released); TR-A5 `--assert-single` (a second instance exits at once) |
| **11b** | The first-run key screen: a small WinForms window, one masked box per key (DeepSeek, TypeSafe, Fish), Save / Remove. It shows only "present (N chars, source)" or "missing". It opens by itself when the DeepSeek or TypeSafe key is missing at start, and from "Keys…". While it is open, the pipeline ignores Ctrl+Alt (AltGr is Ctrl+Alt on many keyboards) | `HeyGanesh.Tray` (`KeyScreen`, plus a view-model with no UI so it can be tested) | TK-1 `--assert-keys-model` (offline, in-memory `ISecretStore`: save, remove, statuses, a value is never in any label or log line); TK-2 `--assert-keys-window` (local-only: the real window, masked boxes, nothing written unless Save is clicked); the real Credential Manager write stays **your** row, like SH-25 |
| **11c** | Start at login: the menu's check mark writes or removes `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` value `HeyGanesh` (the quoted path of `HeyGanesh.Tray.exe`). Nothing is turned on by default | `HeyGanesh.Tray` (`StartupEntry`, its key path injectable) | TS-1 `--assert-startup` (against a throwaway `HKCU\Software\HeyGanesh\Test\Run` key, cleaned up after: on, off, a path with spaces is quoted, a stale path is reported); turning on the real Run entry and signing out and back in is **your** row |
| **11d** | The installer (see the choice below) | new `installer/` | IN-1 the build script produces the package offline; IN-2 install, start, quit, uninstall on this PC is **your** row |

**Installer choice (decide before 11d):**

- **A, recommended for now: a per-user script installer.** `installer/build.ps1` runs `dotnet publish` (Release,
  self-contained win-x64, so no .NET runtime is needed). `install.ps1` copies the output to
  `%LOCALAPPDATA%\Programs\HeyGanesh` and adds a Start-menu shortcut and an "Apps & features" uninstall entry
  under HKCU. `uninstall.ps1` removes all of it; the keys in Credential Manager stay unless you remove them.
  - no admin rights, no new tools, no signing;
  - the cost: unsigned scripts need `-ExecutionPolicy Bypass`, and the download is about 150 MB.
- **B, later: MSIX.**
  - gains: a clean install and uninstall; start at login through the package's StartupTask; and the
    `graphicsCaptureWithoutBorder` capability, which removes the yellow capture border (DEC-011 noted that
    removing it needs a packaged app);
  - what it needs:
    - MakeAppx and SignTool, which are not installed here: only `Windows Kits\10\UnionMetadata` exists. Either
      the Windows SDK install or the `Microsoft.Windows.SDK.BuildTools` NuGet package would be a new
      dependency, which I must ask you about;
    - a signing certificate you trust on this PC, which is a security setting and so yours;
  - its 11c would be the StartupTask, not the Run key.

## Execution path

`FLOW.md` has the console shell path (`ShellHost.RunAsync → RunHookedAsync → TriggerPump`). The tray adds:

```
HeyGanesh.Tray.exe → DPI PerMonitorV2 → mutex (held? balloon, exit)
  → log writer as Console.Out → [11b: keys missing? KeyScreen]
  → Task.Run(ShellHost.RunAsync(args, stop)) ┐
  → STA thread: NotifyIcon + menu + Application.Run   │
       Quit → stop.Cancel() → RunHookedAsync's wait ends → hotkey.Stop → pump.DrainAsync → exit code
```

That path is not documented in FLOW.md yet; 11a adds it.

## Alternatives considered

- **A `--tray` flag on the console exe.** Rejected. A console exe always opens a console window; `FreeConsole`
  hides it after a flash, and the build would still be a console app. A separate WinExe keeps the console
  contract untouched.
- **Raw `Shell_NotifyIcon` P/Invoke instead of WinForms.** No dependency either, but about 300 lines of window
  class, message and menu plumbing the framework already has. WinForms is already in the repo through
  LessonSurface.
- **WinUI 3 / Windows App SDK.** Rejected again for the reasons DEC-012 gives: the repo's first NuGet package for
  the shell, plus a packaging decision. It would buy a nicer key screen and nothing the pipeline needs.
- **A Scheduled Task for start at login.** Rejected. It needs more privileges and is harder to remove; the HKCU
  Run value is what per-user apps use.

## What could break, and how we'd know

- **The stop seam changes how hooked mode ends.** If it is wrong, `--seconds 4` and Ctrl+C stop working. SH rows
  `--seconds 4` (exit 0, hook released) and the hook-health block would show it; TR-A2 covers the token itself.
- **A WinForms message loop next to the overlay.** The overlay's windows must still never take focus.
  TR-A4 checks that focus stays where it was while the tray runs; OverlaySmoke `--assert-clickthrough` stays
  unchanged.
- **The log could leak something.** TR-A3 plants a fake key and checks the file. The transcript-in-log question
  is yours (below).
- **AltGr typing in the key screen could trigger listening.** 11b pauses the trigger while the screen is open;
  TK-2 checks it.
- **The Exe-to-Exe project reference.** It builds in .NET 8, but publishing copies `HeyGanesh.App.dll` as a
  library. IN-1 checks the published folder runs.

## Verification (11a, the first step)

```
dotnet build src/Experience/HeyGanesh.App -c Debug -warnaserror      -> 0 warnings, 0 errors
dotnet build src/Experience/HeyGanesh.Tray -c Debug -warnaserror     -> 0 warnings, 0 errors
tools/TraySmoke/bin/Debug/net8.0-windows10.0.22621.0/HeyGanesh.Tools.TraySmoke.exe --assert-source   -> PASS, exit 0
                                                                     ... --assert-stop   -> PASS, exit 0
                                                                     ... --assert-log    -> PASS, exit 0
                                                                     ... --assert-tray   -> PASS, exit 0 (local-only)
                                                                     ... --assert-single -> PASS, exit 0
HeyGanesh.App.exe --seconds 4                                        -> hook live, auto-stop, exit 0 (unchanged)
HeyGanesh.App.exe --once --question "x"                              -> exit 12 (unchanged)
local CI runner                                                      -> 19 gates + the new offline Tray gate, all exit 0
```

## Your decisions

1. **Installer A or B.** A now and B later is my recommendation. B needs a new tool and a certificate.
2. **Should the log file keep the transcribed questions?** The console shows them today. In a file they persist
   on disk; keeping only 5 logs limits that. The other option is to replace them with their word count.
3. **Should the key screen open by itself** when a key is missing at start? I'd say yes for DeepSeek and
   TypeSafe; Fish only affects the spoken voice, so I would not force it (I have not checked what the shell does without it beyond the header line).
