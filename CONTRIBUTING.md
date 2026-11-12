# Contributing to Hey Ganesh

Thanks for looking. Hey Ganesh is a Windows desktop app (.NET 8, Windows 11) that listens for Ctrl+Alt, looks at your
screen and teaches with speech and drawings. It never clicks or types for you, and that is a rule of the project, not a
gap.

## Build and check

```bash
dotnet build HeyGanesh.sln -c Debug -warnaserror
```

Most verification rows run offline and need no keys. Each module has a small host under `tools/`; the commands and the
output each one should print are listed in [`docs/ai/TEST-CHECKLIST.md`](docs/ai/TEST-CHECKLIST.md). For example:

```bash
dotnet build tools/TraySmoke -c Debug -warnaserror
tools/TraySmoke/bin/Debug/net8.0-windows10.0.22621.0/HeyGanesh.Tools.TraySmoke.exe --assert-provider-model
```

Rows marked local-only open a window or use the real keyboard hook and need a desktop session.

## Rules that CI enforces

- **No external NuGet packages in the core modules.** Win32 P/Invoke and the Windows SDK only.
- **No actuation.** Nothing may synthesize input, move the cursor or take focus (`scan-forbidden.ps1`).
- **No secrets.** Keys live in Windows Credential Manager through the key ring and are never logged, printed or put in
  a test (`scan-secrets.ps1`). Use made-up values in tests.
- Overlay windows must be excluded from screen capture, and coordinates are physical pixels. See
  [`AGENTS.md`](AGENTS.md) for the invariants and the reasons.

## Sending a change

1. One logical change per pull request. If it grows, split it.
2. Match the style of the file you are in. No drive-by renames or formatting sweeps.
3. Say what you ran and what you did not. "Builds, and row X passes" is a fine description.
4. If you made a non-obvious choice, add a line to [`docs/ai/DECISIONS.md`](docs/ai/DECISIONS.md); if a verification
   step is new, add a row to `TEST-CHECKLIST.md`.

## Good places to start

Issues labelled [`good first issue`](https://github.com/PranavJha-3000/HeyGanesh/labels/good%20first%20issue) are
small and have a clear finish line. Run a local-only row on your own setup (another monitor, 100% scale, another
Windows language) and report what you see: that is a real contribution.

## Questions and bugs

Ask in [Discussions](https://github.com/PranavJha-3000/HeyGanesh/discussions). Use an
[issue](https://github.com/PranavJha-3000/HeyGanesh/issues/new/choose) for a bug or a concrete request.
Security problems: please do not open a public issue; email the maintainer through the address on the GitHub profile.

## How fast you will hear back

The aim is a first reply to every new issue, pull request or discussion within one day. A first reply is a human
saying one of: "reproduced, here is what I think", "I need your Windows version, scale and the log lines", "this is
a duplicate of …", or "out of scope, because …". It is not a promise to fix it that day. Maintainer routine: watch the
repository for all activity, label each new issue (`bug`, `enhancement`, `question`) when replying, ask for what the
issue form did not get, and close duplicates with a link. If a day was missed, the next reply says so.

By contributing you agree your work is released under the [MIT License](LICENSE).
