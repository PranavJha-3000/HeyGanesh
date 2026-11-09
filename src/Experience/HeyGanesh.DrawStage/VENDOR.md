# Vendored drawing stage (FEATURE-011 step 1, DEC-067 / DEC-068)

The lesson drawing stage from openclicky (https://github.com/prasanthsasikumar/openclicky, MIT, commit
`5b838add23bec28fbddf80f26eadabe98a1df4ff`), copied byte for byte with `git -c core.autocrlf=false archive`,
so line endings are the repository's (LF). Nothing here is fetched at runtime. `DrawStageSmoke --assert-source`
(DS-1) re-hashes every file under `Page/` against this table and fails on any file that is missing or not listed.
`Page/**` is marked `-text` in `.gitattributes` so checkouts never rewrite line endings.

- **Page files** come from the Windows app's copy (`windows/src/OpenClicky.Windows/Assets/DrawStage/`), not the
  shared `draw-stage/` folder. The Windows copy is newer and stricter: its worker fixes each fenced global with
  `defineProperty`, also fences `WebTransport`, `File`, `FileReader`, `Request` and `Response`, and runs nothing
  if a fence fails.
- **Prompts** come from `draw-stage/prompt/` (the Windows app builds its prompt in C#).
- **three.js r172** (0.172.0, MIT, DEC-068): `three.module.min.js`, `three.core.min.js` and `LICENSE` match
  the hashes jsdelivr publishes for npm `three@0.172.0` (checked 2026-11-07). `RoomEnvironment.js` is
  `examples/jsm/environments/RoomEnvironment.js` with its `three` import pointed at `./three.module.min.js`
  (openclicky's one-line change, see `vendor/three/README.txt`).
- **Licences:** openclicky's MIT licence ships as `Page/LICENSE-openclicky`, and three.js's as
  `Page/vendor/three/LICENSE`.

Every file is byte-exact from upstream except those listed under "Local edits", which the table pins at their
edited hash.

| File | Bytes | SHA-256 | Source path at `5b838ad` |
|---|---|---|---|
| `LICENSE-openclicky` | 1103 | `c065b1d7d900775046efaa294558172f3d2c16f72115923c57695965de86024d` | `LICENSE` |
| `figure3d.js` | 66148 | `bed3bfce35ea58ef15d0f4e7eff3ba1b32fb291161a7b7157b8ae929ba35eee2` | `windows/src/OpenClicky.Windows/Assets/DrawStage/figure3d.js` |
| `index.html` | 1980 | `fa856d28d0297e816423e0ed70bd8e7f44b5a212ba290a85fc5fa6bdf00153b6` | `windows/src/OpenClicky.Windows/Assets/DrawStage/index.html` |
| `prompt/drawing-language.md` | 12299 | `4027377aea981d527c3caf6e725c3375070e34fed8f5baee60e5c532fd18fa85` | `draw-stage/prompt/drawing-language.md` |
| `prompt/follow-up.md` | 980 | `da18075d450bdb94fa5ac5bff1b774d0755e3e5ff21b4f3b1388edc0b33063c7` | `draw-stage/prompt/follow-up.md` |
| `prompt/question.md` | 427 | `4b6bda73dbe87148ed5dd2a2e6e3fe60b1261cd10431ed0f876fef82f0e295f6` | `draw-stage/prompt/question.md` |
| `samples/board-3d.js` | 2019 | `ae455c9e893849c62b26e72b46fdc4949d03b7d49f3a8cc935e9b08d9273ef0f` | `windows/src/OpenClicky.Windows/Assets/DrawStage/samples/board-3d.js` |
| `samples/heart-blob-3d.js` | 1976 | `c0f9d2a7b428c8a9b53dc1db58407732ff22e86e804e3089c9e9eeeda9f17350` | `windows/src/OpenClicky.Windows/Assets/DrawStage/samples/heart-blob-3d.js` |
| `samples/heart-photo-3d.js` | 1300 | `c1124fd50a4a1d91a15bc8ebbcbab64675eabb9bf58c768b942cee6f2704a09f` | `windows/src/OpenClicky.Windows/Assets/DrawStage/samples/heart-photo-3d.js` |
| `samples/heart-reply.md` | 2355 | `fa65b15c15cc0652d327daea366cd00dc50374c8a80612362a5c939987af059f` | `windows/src/OpenClicky.Windows/Assets/DrawStage/samples/heart-reply.md` |
| `samples/jet-engine-cutaway-3d.js` | 2255 | `801fc1590a7ad7a1709fe5da71207275a459f306653cf2de9f294f71c1d31b6c` | `windows/src/OpenClicky.Windows/Assets/DrawStage/samples/jet-engine-cutaway-3d.js` |
| `samples/jet-engine-followup-reply.md` | 608 | `ec6954fdb28304494837503cfb96f3cf97e1c75b4b4d7903bf2a1e0a62a4c1fe` | `windows/src/OpenClicky.Windows/Assets/DrawStage/samples/jet-engine-followup-reply.md` |
| `samples/printer-3d.js` | 1921 | `57b74420f095baac5902789a69299fa6ecaa8c64ce65c65aff4fe5a6d2608112` | `windows/src/OpenClicky.Windows/Assets/DrawStage/samples/printer-3d.js` |
| `samples/rocket-engine-3d.js` | 1861 | `88cc11bd1a435c2c0de25c0045f6e3b6229c7875b827ec5f1761d1df3065b827` | `windows/src/OpenClicky.Windows/Assets/DrawStage/samples/rocket-engine-3d.js` |
| `samples/rocket.js` | 1796 | `1f69aecba6f8ae4bfd22eaa86b996535a65b190bb628c6684ce1c40074b30cf7` | `windows/src/OpenClicky.Windows/Assets/DrawStage/samples/rocket.js` |
| `stage-worker.js` | 11131 | `3c0ae3e56d56573e3ae3146195fd0d12b3d68eb43d011e67d6173b10247db8a0` | `windows/src/OpenClicky.Windows/Assets/DrawStage/stage-worker.js` |
| `stage.css` | 7131 | `63531d35d8b6a2d0cdbf4c1589f7c840a3853bededc5ad8c248245027720e98e` | `windows/src/OpenClicky.Windows/Assets/DrawStage/stage.css` |
| `stage.js` | 47564 | `7795b7eb1cc1d699dab29590f03c7a8e6b5f9fa898d4650b9d1889681a31472f` | `windows/src/OpenClicky.Windows/Assets/DrawStage/stage.js` |
| `vendor/three/LICENSE` | 1081 | `4c40a1ef62450b857c3b2aaf294936304cd552d965fbcd9d32d4c5bcf4ba4454` | `windows/src/OpenClicky.Windows/Assets/DrawStage/vendor/three/LICENSE` |
| `vendor/three/README.txt` | 241 | `4c3b9deaa72b1c3a588c77eed1cc8c42c2dcecfb553ef44163c8e045894f88bc` | `windows/src/OpenClicky.Windows/Assets/DrawStage/vendor/three/README.txt` |
| `vendor/three/RoomEnvironment.js` | 3622 | `625d0d8ac8690a73799ea0e2c577d6260162e0da97698c4fe9b03715909ac214` | `windows/src/OpenClicky.Windows/Assets/DrawStage/vendor/three/RoomEnvironment.js` |
| `vendor/three/three.core.min.js` | 366322 | `85c74c0fe8f35a348282bc1b2c099aa1e29a0ebf453c9df1c5ba68ea7d6d43f5` | `windows/src/OpenClicky.Windows/Assets/DrawStage/vendor/three/three.core.min.js` |
| `vendor/three/three.module.min.js` | 339437 | `7700815c10cddde77e717bc9186f3f9b0fa64034c76622efc5f895459d2c305e` | `windows/src/OpenClicky.Windows/Assets/DrawStage/vendor/three/three.module.min.js` |

## Local edits

- `stage.js` (2026-11-07 · claude-opus-5-5, FEATURE-011 step 3, DEC-070). Upstream 42576 bytes,
  `bde25b57dc7a058e21d842c3ada25a2b1e718e27b38ba6c3966536ffc1bea340`. Added: `PROGRAM_SECONDS = 20` (the worker time a
  whole program may use, checked after each statement); the "checking marks" section (`markProblem`, `soundSay`):
  every mark the worker returns is checked before it is painted (known kind, finite numbers near the canvas, bounded
  sizes and text, at most 4 figures and 300 parts, `photo()` and `f.piece()` refused); `ran()` reads the result only in
  the shape expected and skips a refused mark with a `log` line. `reset()`/`restartStream()` zero the new counters.
  Nothing upstream was removed.
  Then (FEATURE-011 step 6, DEC-073): `window.stageMarks()`, after the state line, returns the accepted marks as JSON
  for PlannerEval's run checks, read by a host script. The program runs in the worker and cannot call it.
- `stage.css` (2026-11-07 · claude-opus-5-5, FEATURE-011 step 4, DEC-071). Upstream 6916 bytes,
  `7893b2a1e2a27f33b0044c0f277df84f7410aeb1f1d68bc50d2d9e52316838e8`. Added one rule after the `[hidden]` rule:
  `#tools button.picture, #tools button.video, #tools button.pip { display: none !important; }`, so only the close
  button shows and the page never sends `save`. Nothing upstream was removed.

Vendored 2026-11-07 · claude-opus-5-5 with the owner's approval ("Approve, use three.js").
