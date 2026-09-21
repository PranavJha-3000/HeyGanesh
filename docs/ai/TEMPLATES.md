# TEMPLATES — Working-Agreement Entry Formats

> Single source of truth for the format of every artifact under `docs/ai/`.
> Change a format here first, then update the affected entries.
> `<angle brackets>` mark placeholders — remove them when filling in.

---

## 1. HANDOVER entry

Appended to [HANDOVER.md](./HANDOVER.md), **newest first** — one entry per work session.

```markdown
## <YYYY-MM-DD> — <model / version> — <headline>

- **Branch / commit:** `<branch>` @ `<short-sha>`
- **Scope:** <one sentence: what this session set out to do>

### Done
- <completed item, with file paths>

### In progress / next
- <the first thing a fresh session should pick up>

### Risks & gotchas
- <anything a future session could trip over — or "none">

### Files touched
- `<path>` — <why>

### Verification run
- `<exact command>` -> <observed result>
```

---

## 2. DECISIONS entry

Appended to [DECISIONS.md](./DECISIONS.md), **newest first** — one decision per entry.
IDs run sequentially from `DEC-001` and are never reused. Entries are append-only: to change
a past decision, add a new entry carrying `Supersedes: DEC-<nnn>` rather than editing the old one.

```markdown
## DEC-<nnn> — <YYYY-MM-DD> — <model / version>

- **Status:** accepted | superseded by DEC-<nnn> | reversed
- **Decision:** <what was decided, in one or two sentences>

### Context / alternatives considered
- <option A — what it is>
- <option B — what it is>

### Chosen because
- <the deciding factor>

### Rejected because
- <option B> — <why not>

### Consequences
- <what this makes easy, what it makes hard, what it locks in>
```

---

## 3. PLAN block

Posted **before** any file is created or modified. No code is written until the plan is
approved. All five sections are expected, not optional — use them in this order.

````markdown
### PLAN — <task title>

**1. Problem restated**
<the request in my own words, including anything explicitly out of scope>

**2. Files / actions**

| # | Path | Purpose (one line) |
|---|------|--------------------|
| 1 | `<path>` | <purpose> |

**3. Alternatives considered and rejected**
<alternative> — rejected because <reason>

**4. What could break**
<blast radius, or "nothing yet — greenfield" plus the reason why>

**5. Verification commands**

```powershell
<exact commands to run, and the output expected from each>
```
````

---

## 4. `BUG-<id>.md`

Created in [bugs/](./bugs/) as `BUG-<nnn>.md` (e.g. `BUG-001.md`). One file per bug.

```markdown
# BUG-<nnn> — <short title>

- **Date found:** <YYYY-MM-DD>
- **Found by:** <name / model>
- **Severity:** blocker | major | minor | cosmetic
- **Status:** open | investigating | fixed | verified | wontfix
- **Module(s):** <canonical name(s) from ARCHITECTURE.md>

## Replication
1. <step>

## Expected vs actual
- **Expected:** <...>
- **Actual:** <...>

## Root cause
<filled in once known>

## Fix
- `<path>` — <what changed>

## Verification
- `<exact command or manual step>` -> <observed result>

## Rollback
<how to undo this fix — see ROLLBACK.md>
```

---

## 5. `FEATURE-<id>.md`

Created in [features/](./features/) as `FEATURE-<nnn>.md` (e.g. `FEATURE-001.md`).
One file per feature.

```markdown
# FEATURE-<nnn> — <short title>

- **Date:** <YYYY-MM-DD>
- **Status:** planned | in-progress | done | rolled-back
- **Module(s):** <canonical name(s) from ARCHITECTURE.md>

## Description
<what the user can do once this exists>

## Out of scope
- <explicitly excluded>

## Acceptance criteria
- [ ] <observable, testable statement>

## Test checklist
<link to the relevant items in TEST-CHECKLIST.md>

## Rollback
<what to revert, and which ROLLBACK.md entry covers it>
```

---

## 6. File ownership quick reference

| File / dir | Owner | Cline may |
|------------|-------|-----------|
| `HANDOVER.md` | Cline | append entries |
| `DECISIONS.md` | Cline | append entries |
| `FLOW.md` | shared | edit once the flow is defined |
| `ARCHITECTURE.md` | shared | edit, backed by a DECISIONS entry |
| `CONSTRAINTS.md` | **user only** | **read only — never write** |
| `TEST-CHECKLIST.md` | shared | append checks |
| `ROLLBACK.md` | Cline | append entries |
| `bugs/`, `features/` | shared | create ticket files |
| `TEMPLATES.md` | Cline | edit, then update affected entries |
