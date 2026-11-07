# FEATURE-010 — Plan providers (Claude Code, Codex) and openclicky's drawing system against our eval set

- **Date:** 2026-11-07 · claude-opus-5-5
- **Status:** step 1 done (owner: "Yes, build step 1"), except its live rows CC-4 and PE-8, which wait for the `claude` CLI to be signed in. Step 2 next.
- **Decision:** [DEC-065](../DECISIONS.md#dec-065--2026-11-07--claude-opus-5-5).
- **Source:** https://github.com/prasanthsasikumar/openclicky (MIT), assessed at `5b838ad` (2026-11-07).

## Why

The owner wants to run lessons on the Claude Code and ChatGPT subscriptions they already pay for, not only on API keys. They also want to know whether openclicky's drawing system is better, faster and cheaper than ours on our eval set.

## Assessment (2026-11-07)

- **Plan login:** openclicky's Windows app runs `claude -p` (stream-json, images, no tools) and `codex exec -i` (read-only, not streamed). Both fit our `ILlmClient` seam directly.
- **Drawing:** the model writes a small JavaScript program (`step`, `say`, pen strokes, `figure()` with lathe, extrude and blob parts, `photo()`, `keep()` follow-ups). It runs in a locked-down Web Worker and is drawn stroke by stroke in time with each spoken line, on three.js. Ours is validated VisualPlan JSON (DEC-023) plus the Part Library and Babylon. Theirs is freer and looks hand-drawn; ours is checked before it is shown.

## Steps

1. **Claude Code provider** — done. `HeyGanesh.Providers.ClaudeCode`, `tools/ClaudeCodeSmoke` (CC-1..CC-4), PlannerEval `--record --provider claude-code` (PE-8).
2. **Codex provider** — the same shape, in the same assembly or a sibling one. The owner installs `npm i -g @openai/codex` and runs `codex login`.
3. **Recording openclicky's drawing** — the same cases sent with their drawing prompt. Saves the programs, time to first drawing, total time, tokens and cost, for {ours, theirs} × {DeepSeek, Claude Code}.
4. **Side by side** — render both as PNGs and show them on a blind A/B page for the owner; Claude as an optional blind second opinion. Needs `draw-stage/` (with three.js) copied into `tools/` for testing only, which is a dependency question for the owner.

## Step 1 notes

- Each call starts the CLI cold, so expect a slower first beat than DeepSeek. Step 3 measures it.
- Temperature and MaxOutputTokens are ignored (the CLI has no flags for them).
- Only a real `claude.exe` is found (native installer at `%USERPROFILE%\.local\bin`, or PATH). An npm `claude.cmd` shim is not supported yet (`shortcut:` in `PlanCli.LocateClaude`).
- On this machine the standalone CLI was signed out (`claude auth status`: `loggedIn: false`; the live error was "OAuth session expired"). The Claude desktop app's sign-in is separate from it.
