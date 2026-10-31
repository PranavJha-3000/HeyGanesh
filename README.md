<p align="center"><img src="assets/brand/heyganesh-256.png" width="128" alt="Hey Ganesh"></p>

# Hey Ganesh

A visual AI tutor for Windows. Hold **Ctrl+Alt**, ask a question out loud, and let go. Hey Ganesh looks at your
screen, points at what you asked about, and teaches the answer: spoken aloud, drawn step by step on lesson cards,
and, when it helps, shown as a 3D model you can turn, pull apart and click to ask about any part.

## What it does

- **Points at things on your screen.** "Where is the Save button?" draws a highlight over it. It reads the
  window's own UI tree first (UI Automation, plus on-screen text through Windows' built-in OCR) and uses a vision
  model only as a fallback.
- **Explains.** A tutor model answers academic and technical questions, grounded in what is on your screen, and
  speaks the answer.
- **Teaches with drawings.** For "how does…" questions it plans a short lesson: cards drawn and spoken one sentence
  at a time, with plots and steps.
- **Shows it in 3D.** Lessons can use a library of computed teaching models (a four-stroke engine, a DNA helix, an
  ion pump), or a simplified schematic the lesson draws for itself. Click a part to ask a follow-up about it.
- **Remembers your lessons.** Each taught turn is kept locally, and "Recent lessons" in the tray replays one
  without calling any model. Follow-up questions understand this session's last few turns.
- **Lives in the notification area.** A tray icon with no console window, an optional start at login, and a
  per-user installer that needs no admin rights.

## Requirements

- Windows 11 (built for `net8.0-windows10.0.22621.0`)
- The .NET 8 SDK (`global.json` pins 8.0.425) to build from source
- API keys, stored once in Windows Credential Manager through the tray's **Keys…** screen (or set as environment
  variables):
  - `DEEPSEEK_API_KEY`: the tutor, the lesson planner and the vision fallback
  - `TYPESAFE_API_KEY`: choosing the on-screen element and routing each turn (Jev)
  - `FISH_AUDIO_API_KEY`: the spoken voice
- For speech input, a running qwen-audio-agent Gateway, its address in
  `HEYGANESH_QWEN_GATEWAY_URL` (there is no default)

Keys are only ever reported as present or missing, with their length. They are never logged or shown.

## Install

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File installer\build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File artifacts\installer\install.ps1
```

This installs to `%LOCALAPPDATA%\Programs\HeyGanesh` and adds a Start-menu shortcut and an Apps & features entry.
`uninstall.ps1`, installed beside the app, removes them again and leaves your keys and lessons alone.

## Build and run from source

```powershell
dotnet build HeyGanesh.sln -c Debug
src\Experience\HeyGanesh.Tray\bin\Debug\net8.0-windows10.0.22621.0\HeyGanesh.Tray.exe
```

The console host, `HeyGanesh.App.exe`, has the same pipeline plus flags for testing: `--help`, `--keys`, `--once`,
`--lesson`, `--no-route`, `--no-speak`, `--no-notes`, `--no-context`.

## Privacy

- Lessons are stored on your PC only, in `%LOCALAPPDATA%\HeyGanesh\notes.db`.
- Notes never contain screenshots, screen text or keys.
- Screen captures and on-screen text are sent to the model providers above only to answer the question you asked.
- Hey Ganesh's own overlay and lesson windows are excluded from its screen captures.

## For contributors

The working agreement, module map and verification commands are in [AGENTS.md](AGENTS.md) and
[docs/ai/](docs/ai/): `ARCHITECTURE.md` (modules), `FLOW.md` (call paths), `TEST-CHECKLIST.md` (every
verification row with its command and expected output), and `DECISIONS.md` (why things are the way they are).
Most rows run offline with fakes and need no keys.
