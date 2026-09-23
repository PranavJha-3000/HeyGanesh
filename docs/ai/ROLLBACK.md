# ROLLBACK — Rollback Points & Procedures

> Record of known-good states and the steps to return to them. Add an entry before or
> immediately after any change that is hard to reverse. One entry, newest first.

## Index

| Date | Rollback point (commit / tag) | Applies to | Verified |
|------|-------------------------------|------------|----------|
| — | — | *(no rollback points recorded yet)* | — |

---

<!-- ROLLBACK ENTRIES BELOW THIS LINE — newest first -->

## 2026-09-22 · Cline agent (model id not exposed to the agent)

- **Change:** `docs/ai/DECISIONS.md` restored to a clean 9-entry log (DEC-001–009), LF-only, no BOM, no gamma.
- **Rollback point:** the clean 7-entry recovery file `C:\Users\prana\AppData\Local\Temp\dec_7ent.md` (in the shared `.kilo\worktrees\rainbow-gourd` workspace). `git checkout HEAD -- docs/ai/DECISIONS.md` returns to the committed version at `94a8af1e`; restore `dec_7ent.md` to `docs/ai/DECISIONS.md`, then re-insert the DEC-009/DEC-008 entries before the `## DEC-007 — 2026-09-22 — Laguna S 2.1` header and the DEC-009/008 rows after the index separator to reproduce this exact state.
- **Verified:** `git diff --no-index --stat dec_7ent.md docs/ai/DECISIONS.md` → 48 insertions, 0 deletions; byte probe `bytes=29323, first=35 (no BOM), cr=0 (LF), emdash=106, gamma=0`; DEC-001..009 each once; 9 index rows; all anchors resolve.
