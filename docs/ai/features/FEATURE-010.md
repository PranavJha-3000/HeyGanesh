# FEATURE-010 — Plan providers (Claude Code, Codex) and openclicky's drawing system against our eval set

- **Date:** 2026-11-07 · claude-opus-5-5
- **Status:** step 1 done, live rows included (CC-4, PE-8). Step 2 done offline (owner: "Yes, build step 2"); CX-4 waits for Codex to be installed and signed in. Step 3 next.
- **Decisions:** [DEC-065](../DECISIONS.md#dec-065--2026-11-07--claude-opus-5-5), [DEC-066](../DECISIONS.md#dec-066--2026-11-07--claude-opus-5-5).
- **Source:** https://github.com/prasanthsasikumar/openclicky (MIT), assessed at `5b838ad` (2026-11-07).

## Why

The owner wants to run lessons on the Claude Code and ChatGPT subscriptions they already pay for, not only on API keys. They also want to know whether openclicky's drawing system is better, faster and cheaper than ours on our eval set.

## Assessment (2026-11-07)

- **Plan login:** openclicky's Windows app runs `claude -p` (stream-json, images, no tools) and `codex exec -i` (read-only, not streamed). Both fit our `ILlmClient` seam directly.
- **Drawing:** the model writes a small JavaScript program (`step`, `say`, pen strokes, `figure()` with lathe, extrude and blob parts, `photo()`, `keep()` follow-ups). It runs in a locked-down Web Worker and is drawn stroke by stroke in time with each spoken line, on three.js. Ours is validated VisualPlan JSON (DEC-023) plus the Part Library and Babylon. Theirs is freer and looks hand-drawn; ours is checked before it is shown.

## Steps

1. **Claude Code provider** — done. `HeyGanesh.Providers.ClaudeCode`, `tools/ClaudeCodeSmoke` (CC-1..CC-4), PlannerEval `--record --provider claude-code` (PE-8).
2. **Codex provider** — done offline. `CodexLlmClient` in the same assembly, the `.cmd` launcher in `PlanCli`, CX-1..CX-5, PlannerEval `--provider codex`. The owner installs `npm i -g @openai/codex` and runs `codex login` for CX-4.
3. **Recording openclicky's drawing** — the same cases sent with their drawing prompt. Saves the programs, time to first drawing, total time, tokens and cost, for {ours, theirs} × {DeepSeek, Claude Code}.
4. **Side by side** — render both as PNGs and show them on a blind A/B page for the owner; Claude as an optional blind second opinion. Needs `draw-stage/` (with three.js) copied into `tools/` for testing only, which is a dependency question for the owner.

## Step 1 notes

- Each call starts the CLI cold, so expect a slower first beat than DeepSeek. Step 3 measures it.
- Temperature and MaxOutputTokens are ignored (the CLI has no flags for them).
- ~~Only a real `claude.exe` is found.~~ Paid off in step 2: `claude.cmd` npm installs are found and launched too (DEC-066).
- On this machine the standalone CLI was signed out (`claude auth status`: `loggedIn: false`; the live error was "OAuth session expired"). The Claude desktop app's sign-in is separate from it.

## First live numbers (owner's machine, 2026-11-07)

- **Claude plan, our VisualPlan, E-01 + E-03:** 12 of 14 checks (both fail K-2: 9 and 10 beats, lines up to 26 words); first beat p50 3.1 s / p90 5.3 s; whole lesson p50 9.4 s; 7 097 in / 2 721 out tokens. Recording: `%LOCALAPPDATA%\HeyGanesh\eval\cc-E01-E03-20261107`.
- **Claude plan, one short question (CC-4):** first text 2.2 s, total 3.9 s, $0.0033 API-equivalent.

## Step 2 notes

- The system prompt goes on stdin (cmd.exe's 8 191-character cap); `shortcut:` in `CodexLlmClient`.
- Assumed, not verified: Codex's `cached_input_tokens` is part of `input_tokens`.
- `--ignore-user-config` / `--ignore-rules` were verified by openclicky on codex-cli 0.152.1; a newer Codex may rename them. CX-4 is the check.
