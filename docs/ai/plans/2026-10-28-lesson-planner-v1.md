# Lesson Planner v1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Turn a student's question (+ optional screen grounding and screen image) into a validated `VisualPlan` through any `ILlmClient`, offline-verified against recorded model answers — the step that makes lessons come from a model instead of fixtures.

**Architecture:** New assembly `src/Intelligence/HeyGanesh.LessonPlanner/` (references VisualLessonEngine + Providers.Core; VisualLessonEngine stays a pure contract module). `LessonPlanner.PlanAsync(PlanRequest)` builds a service-owned prompt contract (schema, limits and glyph vocabulary interpolated from the code, so the prompt cannot drift from the validator), attaches the screen image only when the model advertises `ProviderCapability.Vision` (DEC-024), parses + validates the answer with grounded-mark rules, and on a failed answer makes **one** repair call that lists the validator's issues. The client comes from the caller or `ProviderRegistry` — never a named provider.

**Tech Stack:** .NET 8, BCL only; `HeyGanesh.Providers.Core` contracts. Zero PackageReference.

**Spec:** FEATURE-007 §2.1–2.4, §3 items 2–6, §4 Phase 4; DEC-021, DEC-022, DEC-023, DEC-024.

## Global Constraints

- Zero PackageReference; ProjectReferences exactly VisualLessonEngine + Providers.Core. No P/Invoke, no file/network/env access in the planner — it reaches a model only through `ILlmClient`, and never sees a key.
- Screen description, screen image and previous lesson are labelled untrusted data in the prompt; the question leads the user text.
- Image attached iff the request has one AND `client.Capabilities` has `Vision`; otherwise none, and `PlanResult.ImageNote` says why. Never a silent downgrade.
- Marks are grounded: with a grounded target box a mark must lie inside it (inflated by `MarkMarginPx` = 24); with no box, any mark is refused (`mark.ungrounded`). A model cannot invent coordinates on the student's screen.
- At most `MaxAttempts` = 2 model calls per plan. The repair call carries the first answer and the issue list (codes + messages; never screen text). A plan that is still bad is returned as validated (PartiallyDegraded / TextFallback / Rejected), never thrown.
- `ProviderException` and `OperationCanceledException` propagate unchanged (the shell maps them).
- **Structured output is not used in v1** (ruling): the Providers adapters disagree on what `LlmResponseFormat.JsonSchema` contains (OpenAI-compatible expects `{name, schema}`, Anthropic the raw schema) and DeepSeek may not support it; the prompt contract + tolerant parser + repair turn work on every provider. Logged as an open item.
- Streaming beats (first stroke ≤ 2.5 s) is deferred: spike 0c cannot be measured without a key.

## Review Focus

1. Model answer is prose with no JSON at all → one repair call, then a `Rejected`/fallback result — no exception.
2. Screen image present but the model has no Vision → no image part sent, `ImageNote` explains, the prompt says the image is not attached.
3. Mark coordinates outside the grounded box (or with no grounding) → `mark.ungrounded`, that beat degrades.
4. Screen description containing "ignore your rules" → it appears only inside the untrusted block, after the question.
5. Empty or whitespace model answer → treated as malformed (repair), not as an empty lesson.

## Tasks

### Task 1: Grounded marks — LessonSmoke `--assert-grounded`
`public sealed record ValidationContext(PixelRect VirtualDesktop) { bool RequireGroundedMarks; PixelRect? GroundedTarget; int GroundedMarginPx = 24; }` and `VisualPlanValidator.Validate(ParseResult, ValidationContext)`; the existing `Validate(ParseResult, PixelRect)` keeps today's behaviour. New code `mark.ungrounded`.
Checks: mark inside box → Valid; inside box+margin → Valid; outside → `mark.ungrounded` on its beat; RequireGroundedMarks with no box → `mark.ungrounded`; old overload unchanged (fixture with mark still Valid).

### Task 2: Planner, prompt, multimodal, plan — LessonPlannerSmoke `--assert-source`, `--assert-prompt`, `--assert-multimodal`, `--assert-plan`
`ScreenGrounding(string? Description, PixelRect? TargetBox, byte[]? Jpeg, int WidthPx, int HeightPx)`, `PlanRequest`, `PlanResult`, `LessonPlannerOptions`, `LessonPlanner(ILlmClient, LessonPlannerOptions?)`, `static LessonPlanner FromRegistry(IProviderRegistry, LessonPlannerOptions?)` (role Tutor), `Task<PlanResult> PlanAsync(PlanRequest, CancellationToken)`.
Checks: source audit; system prompt names schema id, every op, every glyph (= `LessonVocabulary.Glyphs`), the limits from `LessonLimits.Default`, the plot grammar, the grounded-mark rule; user text opens with the question; description inside an UNTRUSTED block after it; box line present iff a box; injected "ignore your rules" text only inside the block; Vision model → one `LlmImagePart` with the exact base64 bytes; no-Vision model → zero images + note; recorded chain-rule answer wrapped in prose → `Valid`, 1 attempt, tokens/model carried.

### Task 3: Repair, errors, follow-up — `--assert-repair`, `--assert-errors`, `--assert-followup`
Checks: invalid first answer → second call contains the first answer + every issue code; valid second → `Valid`, 2 attempts, `FirstAttemptIssues` kept; two bad answers → validated fallback, exactly 2 calls; empty answer → repair; `ProviderException` and OCE propagate; previous plan → "PREVIOUS LESSON" block with its title and says, labelled untrusted.

### Task 4: Docs + review
TEST-CHECKLIST (LS-12, LP-1..LP-7), ARCHITECTURE, FLOW (planning path), DECISIONS (DEC-026), FEATURE-007, HANDOVER (incl. the adapter JsonSchema open item), AGENTS; final review; one fix pass.
