# Lesson Runtime v1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Play a validated lesson — draw each beat and speak its sentence in step, with pause, skip, stop and replay — against fake speech and a fake canvas, offline.

**Architecture:** `Runtime/` folder inside `HeyGanesh.VisualLessonEngine` (FEATURE-007 §2.2). `LessonPlayer` consumes a `LessonValidationResult` (DEC-023) and drives two seams it defines: `ILessonCanvas` (the renderer — WebView2 cards in Phase 3, native marks via the overlay) and `ILessonSpeaker` (Voice TTS, adapted in the App Shell later). A beat ends when its speech ends — speech duration is the clock. Every play returns a `PlaybackReport`, the replay log.

**Tech Stack:** .NET 8, BCL only (`TimeProvider`, `CancellationTokenSource`, `Task`). Zero PackageReference; no new ProjectReference.

**Spec:** [docs/ai/features/FEATURE-007.md](../features/FEATURE-007.md) §2.1–2.2, §2.5, §3 item 7 (temporal contiguity, segmenting); §4 Phase 2. DEC-020/022/023/024.

## Global Constraints

- Same assembly constraints as Phase 1 (LS-1 audit covers `Runtime/` because it scans all subfolders): no I/O, no P/Invoke, no provider, no new references. Names must not contain the audit needles (e.g. no `Process…` identifiers).
- Only `Valid` and `PartiallyDegraded` plans reach `ILessonCanvas.ApplyBeatAsync`. A beat with no ops is never sent to the canvas.
- Per beat: draw starts first, speech starts immediately after, beat ends when BOTH finished (draw is ms, speech is seconds).
- `TextFallback`: canvas gets one `ShowNoticeAsync(LessonPlayer.FallbackNotice)` call, speaker gets `FallbackText`. `Rejected`: neither seam is called.
- One play at a time per player: a second `PlayAsync` while playing returns a report with `Status == Refused`, never queues.
- External cancellation (the caller's token) propagates as `OperationCanceledException` (house exit 13). `Stop()` is a user action: returns `Status == Stopped`, no exception.
- `FallbackNotice` exact text: `"I couldn't draw this one, so I'll just explain it."`
- Read exit codes from the built EXE; build library then host sequentially (CS2012).

## Review Focus

1. **Speaker throws mid-lesson (TTS outage)** — the exception propagates out of `PlayAsync` (the shell maps it), state returns to Idle, and a later `PlayAsync` works. Test in Task 3.
2. **`SkipBeat()` / `Pause()` called while idle** — no effect, no exception, the next play is unaffected. Test in Task 3.
3. **`Stop()` while paused** — the pause is released and play ends `Stopped`, not hung. Test in Task 3.
4. **PartiallyDegraded plan with a dropped beat** — the report has one record per beat actually in the validated plan, degraded beats marked. Test in Task 2.
5. **Replay** — playing the same validated result twice gives two reports with identical beat order and identical canvas/speaker call sequences. Test in Task 2.

## Files

```
src/Intelligence/HeyGanesh.VisualLessonEngine/Runtime/
  LessonSeams.cs        ILessonCanvas, ILessonSpeaker
  PlaybackReport.cs     PlaybackStatus, BeatRecord, PlaybackReport
  LessonPlayer.cs       LessonPlayer, PlayerState
tools/LessonSmoke/
  Fakes.cs              RecordingCanvas, RecordingSpeaker (shared event log, gated speech)
  LessonSmokeHost.Play.cs       --assert-play
  LessonSmokeHost.Fallback.cs   --assert-play-degraded
  LessonSmokeHost.Controls.cs   --assert-controls
```

## Interfaces (all tasks)

```csharp
namespace HeyGanesh.VisualLessonEngine.Runtime;
public interface ILessonCanvas
{
    Task ApplyBeatAsync(LessonBeat beat, CancellationToken cancellationToken);
    Task ShowNoticeAsync(string notice, CancellationToken cancellationToken);
    Task ClearAsync(CancellationToken cancellationToken);
}
public interface ILessonSpeaker { Task SpeakAsync(string text, CancellationToken cancellationToken); }
public enum PlaybackStatus { Completed, Stopped, Refused, Rejected }
public enum PlayerState { Idle, Playing, Paused }
public sealed record BeatRecord(int Index, string BeatId, bool Degraded, bool Skipped, DateTimeOffset StartedAt, DateTimeOffset EndedAt);
public sealed record PlaybackReport(PlaybackStatus Status, LessonOutcome Outcome, IReadOnlyList<BeatRecord> Beats, bool SpokeFallback);
public sealed class LessonPlayer
{
    public const string FallbackNotice = "I couldn't draw this one, so I'll just explain it.";
    public LessonPlayer(ILessonCanvas canvas, ILessonSpeaker speaker, TimeProvider? time = null);
    public PlayerState State { get; }
    public Task<PlaybackReport> PlayAsync(LessonValidationResult lesson, CancellationToken cancellationToken = default);
    public void Pause();     // takes effect at the next beat boundary
    public void Resume();
    public void SkipBeat();  // cancels the current beat's draw/speech; play continues with the next beat
    public void Stop();      // ends play: Status = Stopped
}
```

`PlayAsync` calls `ClearAsync` once before the first beat of a drawn plan (replay starts from a blank card).

---

### Task 1: Seams + happy-path playback — `--assert-play`
- [ ] RED: `RunAssertPlay()` with chain-rule validated `Valid`, gated speaker released per beat by the test:
  - event log is exactly `clear, draw:b1, say:b1, draw:b2, say:b2, …, draw:b5, say:b5`
  - `draw:b(n+1)` is not logged until `say:b(n)` speech is released (speech is the clock)
  - report `Status == Completed`, `Outcome == Valid`, 5 records, indexes 0..4, `EndedAt >= StartedAt`, none degraded/skipped, `SpokeFallback == false`
  - `State` is `Playing` mid-lesson and `Idle` after
- [ ] Build → fails (types missing). Implement seams, report types, `LessonPlayer` play loop. Run → PASS. Commit.

### Task 2: Degraded, fallback, rejected, replay — `--assert-play-degraded`
- [ ] RED:
  - PartiallyDegraded (chain-rule, beat 2 highlight → `nope`): log has `say:b2` but no `draw:b2`; record 2 `Degraded == true`
  - PartiallyDegraded with a dropped beat (beat 5 say 5000 chars): 4 records (Review Focus 4)
  - TextFallback: log exactly `notice:<FallbackNotice>, say:<FallbackText>`; `SpokeFallback`; zero beat records; `Status == Completed`
  - Rejected: empty log, `Status == Rejected`
  - Replay: same `Valid` result played twice → identical logs and identical record id sequences (Review Focus 5)
- [ ] Run → FAIL. Implement outcome branches. Run → PASS. Commit.

### Task 3: Controls, burst, cancel, faults — `--assert-controls`
- [ ] RED:
  - `Pause()` during beat 1's speech → beat 2's draw not logged while paused (100 ms window); `State == Paused` at the boundary; `Resume()` → completes
  - `SkipBeat()` during beat 2's speech → record 2 `Skipped`, beat 3 draws next, `Status == Completed`
  - `Stop()` during beat 3 → `Status == Stopped`, beats 4–5 never drawn, `State == Idle`
  - `Stop()` while paused → returns `Stopped` within 1 s (Review Focus 3)
  - second `PlayAsync` during play → `Status == Refused`, first play unaffected
  - caller token cancelled mid-speech → `OperationCanceledException`, `State == Idle`
  - speaker throws `InvalidOperationException("tts down")` on beat 2 → propagates; `State == Idle`; next play `Completed` (Review Focus 1)
  - `SkipBeat()`, `Pause()`, `Stop()` while idle → no exception; following play `Completed` (Review Focus 2)
- [ ] Run → FAIL. Implement controls. Run → PASS. Commit.

### Task 4: Docs
- [ ] TEST-CHECKLIST rows LS-8 (`--assert-play`), LS-9 (`--assert-play-degraded`), LS-10 (`--assert-controls`) with counts and dates; FEATURE-007 Phase 2 status; FLOW.md: lesson playback path (not yet wired into the App Shell); DECISIONS DEC-024 (planner is provider-agnostic and multimodal); HANDOVER entry; AGENTS.md commands. Re-run LS-1 (Runtime/ now scanned) and every row. Commit.
