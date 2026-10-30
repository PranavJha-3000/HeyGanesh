# HeyGanesh - Build Playbook

**Status:** Authoritative. **Audience:** the junior developer and Cline (AI coding agent). **Owner:** the Lead (repo owner).
**Baseline:** `docs/ai/PLATFORM.md`. Everything this playbook calls "current" is taken from it. Where this playbook and the code disagree about *current state*, the code wins and the gap is logged in `docs/ai/HANDOVER.md` Open Items. Where they disagree about *intended direction*, this playbook wins until the Lead says otherwise.

Place this file at `docs/ai/BUILD-PLAYBOOK.md`.

---

## 0. Authority and Precedence

When two documents conflict, obey the higher one and **stop and ask** before proceeding:

1. `docs/ai/CONSTRAINTS.md` (Lead-owned; read-only to everyone else)
2. This playbook
3. `docs/ai/DECISIONS.md` (append-only; superseded entries lose force)
4. `docs/ai/ARCHITECTURE.md`, `FLOW.md`
5. `AGENTS.md`, `TEST-CHECKLIST.md`, feature and bug files

"Lead authorization" means a written instruction from the Lead in the current chat. Silence is not authorization. Marker used below: **[LEAD-AUTH]** = may not be done without it.

---

## 1. Project Vision and Strategic Intent

### 1.1 Product statement
HeyGanesh is a Windows AI tutor for academic and technical learning. The user holds a hotkey and asks out loud. HeyGanesh sees the screen, **points at the exact element** the question is about, explains it aloud, opens a visual teaching surface when a picture or simulation teaches better, checks understanding with practice, and remembers what the learner has and has not mastered.

### 1.2 Why this project exists
It is a portfolio flagship. It must prove, with evidence a hiring manager can verify in ten minutes, that its author can:

- integrate multiple AI providers behind one clean abstraction, safely, with user-owned keys (BYOK);
- orchestrate several models under a latency budget, with fallbacks and honest failure;
- design grounded, pedagogically sound prompts and retrieval, and measure them;
- shape an AI product: onboarding, trust, explainability, a coherent UX, success metrics.

### 1.3 Positioning (competitive reference)
| Product | What it shows us | Our stance |
|---|---|---|
| DuckyHelper (Mac only) | Screen-aware voice tutor, draws on screen, blackboard, quizzes, simulations, dashboard and study planner, hint-first teaching | Closest match. **We are Windows-first**, resolve targets through UI Automation (exact element boxes, not drawn guesses), are strictly guide-only, and are BYOK. |
| Penseum (web) | Notes to flashcards/quizzes, canvas the tutor can see, weak-spot tracking | Study layer to learn from: mastery tracking and practice loops. |
| Look At It (Chrome extension) | Point-to-explain in a small in-place window, on-device AI | Interaction idea only. We work across all Windows apps, not one browser. |

Do **not** chase a feature checklist. Differentiators we must make visible: Windows-native, UIA-exact pointing, guide-only trust model, BYOK privacy, learner model where *exposure is not mastery*.

### 1.4 Non-goals
Not a children's product. Not an answer-dumping homework solver. Never acts on the user's behalf (no clicking, typing, or focus changes). No hosted backend that holds user keys or screen data. No telemetry upload by default.

### 1.5 Definition of "resume-worthy"
Done means all of: installable package; first-run onboarding that gets a user from zero to a first taught answer using their own key in under 3 minutes; the eval report (section 7) published in the repo; a recorded demo; a case-study document; green CI; every claim in the README backed by a verification row.

---

## 2. Technical Constraints and Security Protocol

### 2.1 Bring Your Own Key (BYOK) - mandatory rules

1. **The product ships with zero credentials.** Every AI call uses a key the end user supplied.
2. **No API key may be hardcoded, committed, placed in a config file, test fixture, log, prompt, crash report, or screenshot.** CI includes a secret-pattern scan; a hit fails the build.
3. **During development, no key is provided to you.** Do not ask for one, do not generate placeholders that look real, do not read keys from the environment. All automated verification uses fakes and recorded fixtures. Live provider rows stay **human-gated**: the Lead runs them with their own key, entered through the app's key-management path, outside Cline.
4. **Keys live only in Windows Credential Manager** (via `advapi32` `CredWrite`/`CredRead`/`CredDelete` P/Invoke, preserving the zero-NuGet rule) behind `ISecretStore`. In memory they are held as `byte[]`, passed only to the HTTP layer, and zeroed after use. No `string` copies are retained, none are logged, none are serialized.
5. **Existing environment-variable key reads** (Vision, Element Selection, Tutor, Voice, per `PLATFORM.md` section 5) are a stopgap and are **removed** in Milestone 3. After M3 no module reads a key from the environment.
6. **Keys leave the machine only in the `Authorization`/key header of a TLS request to the provider the user selected.** No proxy, no relay, no analytics, no "validation" server of ours.
7. **Logging is redacting by construction.** The logger rejects any value tagged secret; request/response bodies are never logged at default level; screenshots are never logged or persisted unless the user saves a lesson that includes one.
8. **Screen data is sensitive.** First-run consent states plainly what is sent (a screen frame, the transcript) and to whom (the provider the user chose). Provide: per-provider enable switches, an "exclude this window" list, and a visible indicator whenever a capture is in flight.

### 2.2 Provider coverage requirement
The system must support the major LLM providers through one abstraction. Capabilities are declared per provider/model, never assumed.

| Provider family | Adapter strategy | Notes |
|---|---|---|
| OpenAI, Azure OpenAI | OpenAI-compatible adapter (Azure: endpoint + deployment + api-version variant) | |
| Anthropic | Dedicated Messages-API adapter | |
| Google Gemini | Dedicated adapter | 2026-10-29 · claude-sonnet-5-5: Gemini adapter removed - it never compiled and nothing referenced it; if Gemini is wanted, point Providers.OpenAiCompat at a different BaseUrl. |
| DeepSeek, Mistral, xAI, Groq, Together, OpenRouter | OpenAI-compatible adapter with per-provider base URL and quirk flags | |
| Cohere | Dedicated adapter if chat+embeddings are needed | Optional until needed |
| Ollama / LM Studio (local) | OpenAI-compatible adapter, **no key** | Enables a fully offline demo path |
| TypeSafe (Jev) | Stays behind `IJevClient`; its key also goes through `ISecretStore` | Not an LLM adapter |

Rules:
- **Model IDs are user data, not constants.** Ship a `config/providers.catalog.json` with display names, base URLs, capability flags and *suggested* models; the user may type any model ID. Verify suggested IDs against the provider's current documentation when authoring the catalog, and never hardcode a model name in code.
- Capability flags: `Text`, `Vision`, `StructuredOutput`, `Streaming`, `ToolCalls`, `Embeddings`, `Stt`, `Tts`. A request needing a capability the selected model lacks fails fast with a typed, user-readable error (never a silent downgrade).
- **Role assignment:** the user assigns a provider+model to each *role*: `Tutor`, `Vision`, `Embedder`, `Stt`, `Tts`, `Jev`. Mix-and-match is a first-class feature. Provide a keyless default for speech (Windows built-in speech) so voice works before any speech key exists.
- A **Test key** action performs one minimal, user-initiated call and reports the result. It is the only "validation" and runs on the user's machine with the user's key.

### 2.3 Core types (names are binding)
```
ISecretStore            Get/Set/Delete/Exists by (ProviderId, KeyAlias); never returns a string
IProviderCatalog        loads providers.catalog.json; capability queries
IProviderRegistry       resolves role -> (provider, model, credential handle)
ILlmClient              GenerateAsync / StreamAsync (text, image parts, JSON-schema output, tool calls)
IEmbeddingClient        EmbedAsync
ISttClient / ITtsClient speech in / out
ProviderError           typed: AuthFailed, RateLimited, ModelNotFound, CapabilityMissing, Timeout, Transport, ContentBlocked
```
All provider-specific code lives in `HeyGanesh.Providers.*` assemblies. No other assembly may reference a provider SDK, URL, or model name.

### 2.4 Standing product invariants (CI-enforced)
- **Guide-only.** A repository scan test fails if product code (outside `tools/` verification hosts) references `SendInput`, `keybd_event`, `mouse_event`, `SetCursorPos`, `SetForegroundWindow`, UIA `Invoke`/`SetValue`/`Toggle` patterns, or calls the UIA `SetFocus` slot.
- **No silent failure.** A no-match or a failed provider call is reported honestly; the overlay draws nothing for it.
- **Sibling rule.** `ElementSelection` and `Vision` never reference each other. Only `ContextOrchestrator` sees both.
- **Capture exclusion.** The overlay is excluded from capture; a regression test proves it.
- **Untrusted content.** Screen text, OCR text and uploaded documents are *data*, never instructions. Prompts delimit them and instruct the model accordingly; an injection test set (section 7) must pass.

---

## 3. Repository Restructuring

### 3.1 Current state (from the repo and `PLATFORM.md`)
```
/docs/ai/        process docs (HANDOVER, DECISIONS, FLOW, ARCHITECTURE, CONSTRAINTS, TEST-CHECKLIST, ROLLBACK, TEMPLATES, features/, bugs/, PLATFORM)
/src/            App, ContextOrchestrator, ElementSelection, GlobalHotkeyMonitor, ScreenCapture, ScreenOverlay, TutorEngine, Vision, Voice  (flat)
/tools/          smoke and verification hosts
AGENTS.md  README.md  .gitignore  .gitattributes
```
Problems: flat `src/` hides layering; no solution-wide build settings; no CI; no test home; prompts, provider config and eval data have nowhere to live; no product documentation.

### 3.2 Target layout (binding)
```
HeyGanesh/
├─ README.md                      product pitch, demo GIF, quick start, architecture diagram, link to case study
├─ AGENTS.md                      onboarding summary (kept in sync with PLATFORM.md)
├─ HeyGanesh.sln
├─ global.json                    pinned SDK
├─ Directory.Build.props          TFMs, Nullable, TreatWarningsAsErrors, analyzers, deterministic builds
├─ .editorconfig
├─ .github/
│  ├─ workflows/ci.yml            build, key-free verification rows, forbidden-API scan, secret scan
│  ├─ ISSUE_TEMPLATE/
│  └─ pull_request_template.md
├─ config/
│  ├─ providers.catalog.json      provider metadata and suggested models. NEVER secrets
│  └─ defaults.json               non-secret defaults (budgets, thresholds)
├─ prompts/                       versioned prompt files (see 6.1); one folder per role
│  ├─ tutor/  vision/  jev/  lesson/  eval-judge/
├─ eval/
│  ├─ datasets/                   golden sets (element selection, tutoring, injection, retrieval)
│  ├─ runners/                    eval host sources (live runs are Lead-run, human-gated)
│  └─ reports/                    published results, dated
├─ docs/
│  ├─ ai/                         UNCHANGED location and format (agent workflow docs)
│  ├─ product/                    PRD, personas, journey maps, UX principles, metrics, roadmap
│  ├─ architecture/               diagrams (Mermaid), threat model, latency budget
│  └─ case-study/                 the portfolio write-up
├─ src/
│  ├─ Foundation/
│  │  ├─ HeyGanesh.Abstractions/        CursorTarget, GuidanceResult, shared contracts
│  │  ├─ HeyGanesh.Security/            ISecretStore, Credential Manager, redaction
│  │  └─ HeyGanesh.Telemetry/           local-only tracing, latency timers, structured logs
│  ├─ Platform/                         Win32-facing; no AI logic
│  │  ├─ HeyGanesh.GlobalHotkeyMonitor/
│  │  ├─ HeyGanesh.ScreenCapture/
│  │  └─ HeyGanesh.ScreenOverlay/
│  ├─ Providers/
│  │  ├─ HeyGanesh.Providers.Core/      ILlmClient & friends, catalog, registry, resilience
│  │  ├─ HeyGanesh.Providers.OpenAiCompat/
│  │  ├─ HeyGanesh.Providers.Anthropic/
│  │  ├─ HeyGanesh.Providers.Gemini/
│  │  └─ HeyGanesh.Providers.Speech/    keyless Windows speech + cloud STT/TTS adapters
│  ├─ Perception/
│  │  ├─ HeyGanesh.ElementSelection/    PRIMARY (UIA -> rank -> Jev choice)
│  │  └─ HeyGanesh.Vision/              FALLBACK
│  ├─ Intelligence/
│  │  ├─ HeyGanesh.Jev/                 typed fast-gate: intent, confidence gate, element choice
│  │  ├─ HeyGanesh.TutorEngine/
│  │  ├─ HeyGanesh.Prompts/             loader/renderer/versioning for /prompts
│  │  ├─ HeyGanesh.Knowledge/           notes store, ingestion, chunking, retrieval, learner model
│  │  └─ HeyGanesh.ContextOrchestrator/ the only place that composes legs
│  ├─ Experience/
│  │  ├─ HeyGanesh.Voice/
│  │  ├─ HeyGanesh.LessonEngine/        lesson JSON schema, validator, widget runtime contracts
│  │  └─ HeyGanesh.App/                 process host (and, after Decision D1, the UI shell)
├─ tests/                           per-assembly test or verification projects (see D2)
└─ tools/                           smoke hosts and verification hosts (existing pattern, kept)
```

### 3.3 Dependency rules (enforced by an architecture test)
Allowed direction only: `Foundation <- Platform, Providers <- Perception <- Intelligence <- Experience`.
- `Platform` references nothing above `Foundation`.
- `Providers.*` reference `Foundation` and `Providers.Core` only; no other assembly references a concrete provider.
- `Perception` references `Foundation` and `Providers.Core` interfaces only. **ES and Vision never reference each other.**
- Only `ContextOrchestrator` and `App` reference more than one Perception/Intelligence leg.
- Circular references fail the build.

### 3.4 Migration procedure (non-negotiable)
Moving files is high-risk in an agent-driven repo. Do it in small, reversible steps:

1. **R1** add `HeyGanesh.sln`, `global.json`, `Directory.Build.props`, `.editorconfig`; include all *existing* projects at their *current* paths; build green. (No moves.)
2. **R2** create new empty folders (`config/`, `prompts/`, `eval/`, `tests/`, `docs/product/`, `docs/architecture/`) each with a README stating its purpose.
   > 2026-10-29 · claude-sonnet-5-5: R2 placeholder folders removed - they held only aspirational READMEs and no content.
3. **R3-R6** one layer per step: `git mv` the assemblies of that layer into `src/<Layer>/`. Rules: **assembly names, namespaces and public APIs do not change**; only paths and `ProjectReference` paths change; the build and every key-free verification row must be green before and after; each step gets a `ROLLBACK.md` entry written *first* (it touches more than 3 files).
4. **R7** update `AGENTS.md`, `ARCHITECTURE.md` path references, and `tools/` host project references.

No new behaviour in any R-step. If a move reveals a bug, log it in HANDOVER Open Items; do not fix it in the same step.

---

## 4. Execution Standards (the operating contract)

### 4.1 Who does what
- **Lead:** owns `CONSTRAINTS.md`, authorizes dependencies and deviations, runs every live provider row with their own key, signs off milestones.
- **Junior developer:** sequences work, reviews every Cline plan against this playbook before approving, runs verification commands themselves, never approves a plan that violates section 2 or 3.
- **Cline:** executes exactly one approved step at a time.

### 4.2 Non-negotiable rules for every step
1. **Session Start Block first:** model, picking up from (latest HANDOVER entry), constraints in force, open risks. Read `HANDOVER`, `CONSTRAINTS`, `ARCHITECTURE`, `FLOW`, and this playbook before acting.
2. **PLAN first, then stop.** The plan has: problem restated, files to touch, execution path affected, one alternative considered and rejected, what could break, exact verification commands. Implementation begins only after the junior developer replies "approved" (or the Lead says "plan and implement").
3. **One logical change per step.** Multi-part asks are split into a numbered sequence; only step 1 is implemented after confirmation.
4. **Report shape after every change:** `CHANGED / WHY / VERIFY (exact commands + expected output) / NOT VERIFIED (honest list)`. Never write "works" without having run and shown the output.
5. **No new dependency [LEAD-AUTH].** This includes NuGet packages, CDN assets, fonts, models, and tools. The core rule is **zero NuGet in core assemblies**; use P/Invoke and in-box Windows components where that is reasonable. Any proposal to break it goes through a Decision (section 8).
6. **No opportunistic refactors, renames, or cleanups.** Log them in `HANDOVER.md` Open Items.
7. **Never delete or rewrite code you do not understand.** Flag it.
8. **`CONSTRAINTS.md` is read-only.**
9. **`DECISIONS.md` is append-only** (ASCII append only; no PowerShell text round-trips; recover via byte-exact `git checkout` if damaged). Add a dated, model-versioned entry for any non-obvious or expensive-to-reverse choice; supersede, never edit.
10. **Large or risky change (more than 3 files, or touching data) requires a `ROLLBACK.md` entry written before the change.**
11. **No deviation from the documented architecture.** If you believe the architecture is wrong, stop, write the case in the plan under "Deviation proposal", and wait for **[LEAD-AUTH]**.
12. **Honesty rule:** an unrun check is listed under NOT VERIFIED. A passing offline row never implies provider acceptance.
13. **Session end ("wrap up"):** append a 5-line `HANDOVER.md` entry: Did / Left / Watch / Next / Verified.

### 4.3 Definition of Done (per step)
Builds with zero warnings; all existing key-free rows still green; new behaviour has a row in `TEST-CHECKLIST.md` with an exact command; invariants in 2.4 intact; docs updated in the same step; report delivered in the required shape.

### 4.4 Escalate (stop and ask) when
A requirement is ambiguous; two documents conflict; a step would touch `CONSTRAINTS.md`; a dependency seems necessary; a live key seems necessary; a step cannot be verified key-free; the plan grows beyond one logical change.

---

## 5. Development Roadmap

Milestones are sequential unless stated. Each bullet is **one Cline step**. Exit criteria must be met and signed off by the Lead before the next milestone starts.

### M0 - Governance baseline (docs and build hygiene)
- S0.1 **Lead** populates `CONSTRAINTS.md` (proposal in Appendix A).
- S0.2 Reconcile `AGENTS.md` with `PLATFORM.md` Discrepancies 1-4 (docs only).
- S0.3 = R1 (solution, `Directory.Build.props`, `global.json`, `.editorconfig`).
- S0.4 CI workflow: build + key-free verification rows + forbidden-API scan + secret scan. Rows needing a desktop session or a second display are marked local-only.
**Exit:** CI green on `main`; `CONSTRAINTS.md` non-empty.

### M1 - Restructure
- R2 through R7 from 3.4.
**Exit:** target layout in place; all rows green; ARCHITECTURE paths updated; architecture-test passing.

### M2 - BYOK foundation
- S2.1 `HeyGanesh.Security`: `ISecretStore` over Credential Manager, plus redacting logger and secret tagging. Tests prove no secret appears in logs or exceptions.
- S2.2 `Providers.Core`: `ILlmClient`, `IEmbeddingClient`, capability model, `ProviderError`, catalog loader, registry, resilience (timeout, bounded retry with jitter, per-provider circuit breaker).
- S2.3 `Providers.OpenAiCompat` with a loopback stub server for wire-contract tests (the pattern already used for the Tutor provider).
- S2.4 `Providers.Anthropic`. S2.5 `Providers.Gemini`. (One step each.)
  > 2026-10-29 · claude-sonnet-5-5: Gemini adapter removed - it never compiled and nothing referenced it; if Gemini is wanted, point Providers.OpenAiCompat at a different BaseUrl.
- S2.6 Keyless local provider path via the OpenAI-compatible adapter pointed at a local server; documented.
- S2.7 Key management CLI in `App` (`heyganesh keys set|test|delete|list`). Keys are read from a masked prompt only; never from arguments or environment.
**Exit:** three adapter families pass wire-contract rows offline; secret-leak rows pass; **no live call has been made by Cline**.

### M3 - Migrate existing modules onto the abstraction
- S3.1 Vision uses `ILlmClient` (role `Vision`), env read removed.
- S3.2 Tutor Engine provider replaces its DeepSeek-specific provider; `CreateTutorClient()` resolves from the registry (this is the "one-line flip", now provider-neutral).
- S3.3 Jev/TypeSafe key via `ISecretStore`; `IJevClient` unchanged.
- S3.4 Voice: `ISttClient`/`ITtsClient` abstractions; keyless Windows speech adapter; existing Gateway and Fish become adapters.
- S3.5 Grep gate: no `Environment.GetEnvironmentVariable` for keys anywhere; add as a CI check.
**Exit:** every AI call in the product goes through `Providers.*`; `PLATFORM.md` Discrepancies updated.

### M4 - Perception and orchestration completion
- S4.1 `CursorTarget` in `HeyGanesh.Abstractions`; ES and Vision results map to it (still no sibling reference).
- S4.2 `HeyGanesh.Jev`: confidence-tier gate (0.8 / 0.5 boundaries preserved) in the orchestrator path.
- S4.3 `HeyGanesh.Jev`: intent classification (typed output only: point-and-explain, concept question, practice request, lesson request).
- S4.4 Orchestrator session policy: bounded timeouts per leg, cancellation on new trigger, partial-result handling.
- S4.5 Overlay: Bezier pointer animation leading to the resolved centre; still bounded, click-through, capture-excluded.
- S4.6 Multi-monitor verification rows (needs a second display; human-run).
**Exit:** latency instrumentation present on every leg; ES-primary and Vision-fallback paths demonstrated with fakes.

### M5 - Tutor intelligence
- S5.1 `HeyGanesh.Prompts`: loader, templating, prompt versioning, hash recorded in every trace.
- S5.2 Tutor system prompt v1: hint-first pedagogy, three modes (Explain, Coach, Check), grounding rules, injection defence (6.1).
- S5.3 Structured tutor output (JSON schema) with validation and repair-or-fail policy.
- S5.4 Streaming: first sentence spoken while the rest generates; barge-in (user speaks, speech stops).
- S5.5 In-session multi-turn memory with a bounded token budget and summarization.
- S5.6 Model router: cheap/fast model for gate and short answers, stronger model for hard explanations; user-visible and overridable.
- S5.7 Latency and cost trace per turn (local only), viewable in a debug panel.
**Exit:** latency budget (section 7) measured by fakes with injected delays; live measurement row defined for the Lead.

### M6 - Product shell and UX  (blocked on Decision D1)
- S6.1 Packaged shell (per D1) with tray, global settings, and Credential-Manager-backed key screen (add/test/remove, per-role provider and model pickers, capability badges, "what is sent" disclosure).
- S6.2 First-run onboarding: consent, hotkey demo, key setup, a guided first question. Time-to-first-answer is a tracked metric.
- S6.3 Teaching window: a compact, dockable window (the "blackboard") with a Visualise tab; renders text, equations, diagrams.
- S6.4 Accessibility pass: keyboard-only operation, screen-reader names, high-contrast, scalable text, reduced motion.
- S6.5 Error UX: every `ProviderError` maps to a plain-language message and a next action.
**Exit:** a stranger can install, add a key, and get a taught answer without help (usability row with 3 observers' notes recorded).

### M7 - Visual Lesson Engine and practice
- S7.1 Lesson JSON schema v1 (versioned, validated); the model emits **structured data**, the app renders **native widgets**, never AI-drawn pixels.
- S7.2 Renderers: text/steps, equation, diagram primitives.
- S7.3 Widgets: `DragAndDropMatch`, `InteractiveSlider`, `NodeWire`; each emits typed practice events.
- S7.4 Practice loop: hint, retry, check; events feed the learner model (M8).
- S7.5 Simulation-style lessons built from the slider and node widgets.
**Exit:** golden lesson fixtures render deterministically; malformed model output degrades to plain text with a notice.

### M8 - Knowledge: notes, RAG, learner model
- S8.1 `HeyGanesh.Knowledge`: SQLite store for lessons, subjects, concepts, attempts. **[LEAD-AUTH]** engine choice (Decision D3).
- S8.2 Save lesson as a note under a subject (user-editable subject).
- S8.3 Document ingestion (PDF/text/markdown): chunking, embeddings via the `Embedder` role, local vector index.
- S8.4 Retrieval-augmented tutoring: top-k with source citations shown in the teaching window; retrieved text is delimited as untrusted data.
- S8.5 Learner model v1: per-concept state (exposed, recalled, practiced, transferred); **exposure never counts as mastery**; decay and spaced review scheduling.
- S8.6 Study planner: next-review suggestions from the learner model.
- Deferred, explicitly: the Graphiti/Neo4j temporal knowledge graph. It adds a server dependency that conflicts with local-first and BYOK simplicity. Revisit only via a Decision after v1 data exists.
**Exit:** retrieval and learner-model rows green offline; eval retrieval metrics baselined.

### M9 - Evaluation, hardening, release
- S9.1 Eval harness (section 7) and first published report.
- S9.2 Threat-model document and prompt-injection suite.
- S9.3 Performance soak (leaks, handles, CPU idle).
- S9.4 Installer/package, signing plan, update plan.
- S9.5 README, demo video, case study, architecture diagrams.
**Exit:** release checklist (section 7.4) fully ticked.

---

## 6. Role-Specific Skill Demonstrations

### 6.1 Applied AI Engineering
- **Prompt engineering as a discipline:** prompts are versioned files in `/prompts`, rendered by `HeyGanesh.Prompts`, hashed into every trace, and covered by tests. Pedagogy is explicit: hint first, ask-back, one step at a time, check the learner's own work, never just the answer. Untrusted content is delimited and declared as data.
- **Structured outputs everywhere a program consumes model output:** JSON-schema outputs with validation; a documented repair-once-or-fail policy.
- **RAG done properly:** chunking strategy rationale, embedding via BYOK role, hybrid lexical+vector retrieval, citations in the UI, a retrieval eval (recall@k) and a groundedness check.
- **Agentic workflow with a typed toolset and a hard safety boundary:** the tutor chooses among *guide-only* tools (point, explain, open lesson, quiz, schedule review) through a typed contract; the actuation tools do not exist. This is the headline safety design to document.
- **Typed fast-gate (Jev) in front of expensive models:** intent and confidence routing that outputs only typed choices, never free text or coordinates.

### 6.2 AI/ML Engineering
- **Model orchestration:** role-based provider registry, router policy (cost, latency, capability), fallbacks with a circuit breaker, per-provider rate-limit handling.
- **Latency management:** a published latency budget (7.1), instrumented on every leg; streaming with first-sentence speech; parallelism where legs are independent (capture while STT finalizes); cancellation on re-trigger; caching of UIA candidate rankings per window state.
- **Evaluation:** offline golden sets with automatic scoring, LLM-as-judge for tutoring quality with a rubric *and* a human-spot-check protocol, regression gates in CI on key-free subsets, results published in `eval/reports`.
- **Failure honesty:** typed errors, no silent fallback to a weaker capability, calibrated "not found" behaviour tested against distractor screens.
- **Cost awareness:** per-turn token and cost estimate shown locally; budget cap setting.

### 6.3 AI Product Management
- **Product artifacts in `docs/product/`:** PRD, personas (e.g. engineering undergraduate, self-taught developer, certification candidate), journey maps, UX principles (trust, control, legibility, no dark patterns), prioritized roadmap with explicit non-goals, and a metrics tree.
- **BYOK as a product story:** onboarding makes key setup feel safe (what is sent, where it goes, how to revoke); provider and model pickers explain capability and cost in plain language.
- **Interaction design for AI:** visible state (listening, looking, thinking, speaking), interruptibility, confidence communicated honestly (a pointer appears only for found targets), undo-free by design because the product never acts.
- **Metrics defined before building:** time-to-first-answer, pointer accuracy, answer helpfulness (thumbs plus rubric), practice completion, 7-day return, cost per session. Instrumentation is local; the user may export their own data.
- **Evidence of decisions:** every trade-off in `DECISIONS.md` with alternatives; the case study explains what was cut and why.

---

## 7. Quality Gates

### 7.1 Latency budget (proposed targets; baseline measured in M5, then ratified by the Lead)
| Segment | p50 | p95 |
|---|---|---|
| Hotkey release to pointer visible, ES path | 1.5 s | 3 s |
| Same, Vision fallback path | 3.5 s | 6 s |
| Verdict to first spoken audio (streaming) | 2 s | 4 s |
| Key test action | 3 s | 8 s |
| Idle CPU | under 1% | under 2% |

### 7.2 Eval sets (all key-free to author; live scoring is Lead-run)
Element-selection accuracy and honest no-match on distractor screens; Vision box IoU; tutoring rubric (correctness, hint-first, brevity, no answer-dumping); prompt-injection suite (screen text and documents attempting to override instructions); retrieval recall@k and citation faithfulness; lesson-schema validity rate.

### 7.3 CI gates
Build zero warnings; architecture dependency test; forbidden-API scan; secret scan; key-free verification rows; lesson fixtures; prompt hash drift check.

### 7.4 Release checklist
All milestone exits signed; live rows run by the Lead and recorded; eval report published; threat model reviewed; accessibility pass recorded; installer tested on a clean machine; README claims each linked to a row.

---

## 8. Decisions Awaiting the Lead

| ID | Decision | Options | Recommendation |
|---|---|---|---|
| D1 | UI technology for the shell and teaching window | WinUI 3 packaged app (new NuGet and tooling); keep Win32 plus a rendering surface; WebView2 hosting for rich lessons | Decide before M6. The Visual Lesson Engine's "native widgets" principle favours WinUI 3. |
| D2 | Test framework | Keep the existing verification-host pattern; add xUnit (NuGet) | Keep hosts for core assemblies; allow a test-only xUnit project that product assemblies never reference. |
| D3 | SQLite access | `winsqlite3.dll` via P/Invoke (zero NuGet; confirm availability on the supported Windows versions); `Microsoft.Data.Sqlite` (NuGet) | Start with P/Invoke if it verifies cleanly; otherwise approve the NuGet. |
| D4 | Embedding source | User-provided cloud embedder only; also an optional local model | Cloud via BYOK first; local later. |
| D5 | Telemetry | Local-only; opt-in export | Local-only, opt-in export. |
| D6 | Distribution | MSIX store-less sideload; installer; winget | MSIX plus winget manifest. |

---

## Appendix A - Proposed `CONSTRAINTS.md` text (for the Lead to adopt; Cline must not edit)

1. BYOK only. No key is ever hardcoded, committed, logged, or supplied to the agent. Keys live only in Windows Credential Manager.
2. The product never acts for the user: no input synthesis, no focus changes, no UIA writes.
3. Element Selection and Vision never reference each other.
4. Core assemblies take zero NuGet dependencies unless the Lead authorizes a Decision.
5. No hosted backend holds user keys or screen data.
6. Screen and document content is untrusted data, never instructions.
7. Live provider checks are run by the Lead only.
8. One logical change per step; plan before implementation.

## Appendix B - Step prompt template (Lead or junior developer to Cline)

```
Read docs/ai/HANDOVER.md, CONSTRAINTS.md, ARCHITECTURE.md, FLOW.md, BUILD-PLAYBOOK.md. Output the Session Start Block.

STEP: <Milestone.Step id and title from BUILD-PLAYBOOK.md section 5>
SCOPE: one logical change. Touch only: <paths>. No new dependencies. No refactors outside scope.
INVARIANTS: sections 2.1 and 2.4 of the playbook apply.

Respond with a PLAN only (problem restated, files, execution path, alternative rejected, what could break, verification commands) and stop.
If more than 3 files or data are touched, include the ROLLBACK.md entry you will write first.
After approval: implement, then report CHANGED / WHY / VERIFY / NOT VERIFIED.
```
