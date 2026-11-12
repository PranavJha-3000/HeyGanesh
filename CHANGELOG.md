# Changelog

All notable changes to Hey Ganesh. Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [1.1.0] - 2026-11-12

### Added
- **Teach on a plan you already pay for.** With no DeepSeek key, lessons and explanations now run on your Claude Code
  plan (the `claude` CLI) or your ChatGPT plan (the `codex` CLI). Order: your saved choice, then a DeepSeek key, then
  Claude, then Codex.
- **Settings window** (tray menu, **Settings…**): a notch-style pill at the top of the screen. It shows who is teaching
  and each provider's state, and offers Use, Add key, Sign in or Get, so nothing needs a terminal. Typing a key is
  checked as you go; it is keyboard-friendly and follows Windows High Contrast.
- MIT `LICENSE`, `CONTRIBUTING.md`, issue templates.
- README rewrite with a launch video, screenshots, architecture and credits.

### Changed
- The tutor and lesson lines in the shell header name the active teacher.

### Known limits
- A changed teacher applies the next time Hey Ganesh starts.
- "Signed in" for a plan CLI means its credential file exists; a stale sign-in fails at the first lesson with the CLI's
  own message.
- Codex answers arrive whole, so a Codex drawing starts later than a DeepSeek one. Codex has not been run live yet.
- Vision (the fallback locator) and turn routing still need their DeepSeek and TypeSafe keys.

## [1.0.0] - 2026-11-09

### Added
- First release: hold Ctrl+Alt, ask out loud, and Hey Ganesh points at the element, explains it in a voice, or draws the
  idea over your screen as a spoken 2D or 3D drawing.
- Gnani Prisma listening and Timbre speaking (optional), Fish Audio as the other voice.
- Local notes with replay of stored drawings, per-user installer, tray app with start at login, landing page.
