# Vendored third-party files (FEATURE-007 §1.5, DEC-030)

Served only from the lesson page's virtual host; never fetched at runtime. Pinned by SHA-256 below —
`LessonSurfaceSmoke --assert-source` (LSU-7) re-hashes every file against this table. The folder is
marked `-text` in `.gitattributes` so checkouts never rewrite line endings (which would change the hashes).

| File | Version | Source | SHA-256 | Licence |
|---|---|---|---|---|
| `babylonjs-9.30.0/babylon.js` | babylonjs 9.30.0 (2026-10-08) | https://cdn.jsdelivr.net/npm/babylonjs@9.30.0/babylon.js | `ff331103574d58a94e68353087721260628a4a6dd5521aa98949127bde73bbff` | Apache-2.0 |
| `babylonjs-9.30.0/babylon.gui.min.js` | babylonjs-gui 9.30.0 | https://cdn.jsdelivr.net/npm/babylonjs-gui@9.30.0/babylon.gui.min.js | `4f15fb417cd40d8fad425ef3b0cec967d815565168ee736a0c8149f1e700a45c` | Apache-2.0 |
| `babylonjs-9.30.0/LICENSE.md` | babylonjs 9.30.0 `license.md` | https://cdn.jsdelivr.net/npm/babylonjs@9.30.0/license.md | `9362ea9ea17cb221a20cbbd63f404710834cc273d2a9a7b60314fd68ab702cd6` | — |
| `babylonjs-9.30.0/NOTICE.md` | babylonjs 9.30.0 `NOTICE.md` (46 bytes) | https://cdn.jsdelivr.net/npm/babylonjs@9.30.0/NOTICE.md | `225463efd81558f120a6e0cdd7f4c1007ed486006d1228c4849fee0c49715823` | — |

Every hash above equals the one jsdelivr publishes for that file (checked 2026-10-29).

`NOTICE.md` is the Apache-2.0 §4(d) notice; it ships alongside the licence. Vendored 2026-10-29 · claude-opus-5-5 with the
owner's approval; its hash equals jsdelivr's published one.
