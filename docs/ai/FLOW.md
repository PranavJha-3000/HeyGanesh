# FLOW — Interaction Flow

> Placeholder. Will hold the end-to-end interaction flow diagram once it is defined —
> expected shape: hotkey → Screen Capture → Vision/Coordinate Detection → Jev →
> Tutor Engine → Visual Lesson Engine → Screen Overlay / Voice pipeline → Notes persistence.
>
> Screen Capture leg today: `ScreenCaptureService.CaptureAllMonitorsAsync` → one `MonitorCapture`
> per monitor (cursor's monitor first) → `MonitorCapture.Jpeg`, a downscaled B8G8R8A8 JPEG (≤1280 px,
> aspect preserved), plus `NonBlackPixelRatio` so Vision can reject a blank frame. One-shot per call;
> no running capture while idle.
>
> Vision leg today: one `MonitorCapture` + a question → `VisionService.LocateAsync` →
> `VisionResult` carrying source-local and screen-space (`PixelRect`) boxes plus a confidence verdict
> (`Confidence` from `RawConfidence` + cross-sample `Consistency`) — the exact signal shapes the future
> Jev confidence-gate will consume. Transport is behind `IVisionClient` (direct DeepSeek today,
> temporary pending the API key security proxy).
>
> Element Selection leg today (the PRIMARY path — DEC-008): the target window's UIA tree is enumerated
> once (control view, one cross-process cache-request pass) → interactive candidates are ranked by
> lexical overlap with the question and capped at 120 (`c001..`) → Jev returns one option id (or the
> synthetic `none`) → that candidate's UIA BoundingRectangle is the answer, in global physical pixels.
> Element Selection itself consumes no frame — the capture exists for the fallback leg and for the
> overlay's annotation. `SelectionResult` carries the same shape as `VisionResult` (box + 0.8/0.5
> confidence tier) so a consumer can treat the two legs uniformly.
>
> Fallback rule today: a FAILED Element Selection result (no resolvable window / empty interactive
> tree / UIA access denied by UIPI / Jev returned no choice / Jev chose an id never offered) is what
> sends the request down the Vision leg. A valid "nothing matches" answer (`Choice == "none"`) is NOT
> a failure and does not fall back. The two modules must not reference each other (DEC-008/DEC-009).
>
> Module names used here must match [ARCHITECTURE.md](./ARCHITECTURE.md) verbatim.

## Path today (Screen Capture + Element Selection / Vision only)

```
hotkey ─▶ Screen Capture ─▶ Element Selection [enumerate ─▶ rank/cap 120 ─▶ Jev select ─▶ UIA rect] ─▶ unified target
                                │
                                └─ failed result ─▶ Vision [LocateAsync ─▶ box + confidence tier] ─▶ unified target
```

This is the only end-to-end path that exists in code today, and it stops at the unified target
(a global physical-pixel box plus a confidence tier). Downstream of that point — the Jev
confidence-gate that decides ES→Vision fallback, Tutor Engine, Visual Lesson Engine, Screen Overlay,
Voice pipeline, Notes persistence — is still undefined, so it is not drawn here rather than invented.

Both legs are specified in [ARCHITECTURE.md](./ARCHITECTURE.md); Element Selection is primary and
Vision is the fallback (DEC-008). The Vision leg fails fast (`KEY ERROR`, exit 12) when
`DEEPSEEK_API_KEY` is absent, as the Element Selection leg does when `TYPESAFE_API_KEY` is absent.
