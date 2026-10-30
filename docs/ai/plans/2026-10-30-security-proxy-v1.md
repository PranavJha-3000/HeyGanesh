# Plan: one place for API keys, the "security proxy" (build sequence step 8)

2026-10-30 · claude-opus-5-5 · STATUS: 8a BUILT 2026-10-30 (owner: "Yes, build 8a"): KR-1 11 PASS, KR-2 4 PASS, SH-24 verified; DEC-045.
Changed while building: the header keeps its environment-based text (it still decides which legs run) and only adds a
"stored in Credential Manager" note; the real-slot round trip (SH-25) was refused by this session's guard and is the
owner's to run. 8b is not built.

## The wrap-up sequence (the owner chose must-haves 1–4, 2026-10-30)

Each step is planned and approved on its own, as before:

| Step | What | Why this order |
|---|---|---|
| **8** | Security proxy: one key ring, keys in Windows Credential Manager (this plan) | the tray app's key screen (step 11) needs somewhere safe to write keys |
| **9** | Turn the Tutor on (SH-17, the one-line `CreateTutorClient` change), its key read through the key ring | small; it uses step 8 |
| **10** | 3D for objects with no library model: a "schematic" scene built from simple shapes, clearly tagged | the biggest gap against the reference video ("draw the jet in 3D") |
| **11** | A real app: tray icon, first-run key screen, start at login, installer (MSIX also removes the capture border) | last, because it packages everything above |

## Problem, restated

Five places read API keys from environment variables on their own:
- `TypeSafeJevClient` (Element Selection, `TYPESAFE_API_KEY`);
- `DeepSeekVisionClient` (Vision, `DEEPSEEK_API_KEY`);
- `DeepSeekTutorClient` (Tutor, `DEEPSEEK_API_KEY`);
- `FishTtsClient` (Voice, `FISH_AUDIO_API_KEY`);
- the App's `EnvironmentSecretStore`, which feeds the lesson and router clients (and the eval tool's copy of it).

DEC-007 called this "a temporary stopgap … pending the API key security proxy". The problems:
- **Environment variables are a weak store.** Any process the user starts inherits them, they end up in shell history and
  scripts, and a student can't easily set them.
- **There's no single answer to "which keys does the app have?"** The shell's header re-reads each variable itself.
- **The tray app (step 11) has nowhere to save a key** the student types.

Windows Credential Manager is already wrapped. `HeyGanesh.Security.CredentialManagerSecretStore` stores secrets per user,
encrypted by Windows, and hands them out as `SecretHandle`s that zero their buffer. G-12 (SecretSmoke) already proves a
round trip. Nothing in the app uses it yet.

## Design

**Step 8a: the key ring.**
- `HeyGanesh.Security/KeyRing.cs` (new) knows the three provider keys by name:

  | Name | Environment variable |
  |---|---|
  | `deepseek` | `DEEPSEEK_API_KEY` |
  | `typesafe` | `TYPESAFE_API_KEY` |
  | `fish` | `FISH_AUDIO_API_KEY` |

- It resolves each key in a fixed order: Credential Manager (target `HeyGanesh/<name>`) first, then the environment
  variable. The fallback keeps today's setup and CI working.
- `Describe(name)` returns presence, length and source (`credential manager` / `environment` / `missing`), never the
  value.
- `Reader(name)` returns a `Func<string?>` that reads the key at call time and keeps no copy.
- The App Shell gains three commands:
  - `--keys` prints each key's state;
  - `--set-key <name>` reads the key from the console with no echo, or from piped stdin, stores it in Credential
    Manager, and prints only "stored (N chars)";
  - `--remove-key <name>` deletes it.

  The header's `keys :` line then shows the source.

**Step 8b: route every client through it.**
- Each client's options gain an optional `Func<string?> ApiKey`:
  - `SelectionOptions` (Jev);
  - `VisionOptions`;
  - `DeepSeekTutorOptions`;
  - `FishTtsOptions`.

  Each one defaults to the client's current environment read, so every offline row and every host behaves exactly as
  today.
- The App passes `KeyRing.Reader(...)` everywhere. `EnvironmentSecretStore` becomes a `KeyRing`-backed
  `ISecretStore`.
- The eval tool's `EnvKeyStore` and PE-5's key-variable check follow.
- No module gains a project reference. TutorEngine and Voice deliberately take none, and a delegate needs none.

## Files

**8a**
- `src/Foundation/HeyGanesh.Security/KeyRing.cs` (new).
- `src/Experience/HeyGanesh.App/ShellHost.Args.cs`: the three commands and the `keys :` header.
- `ShellHost.Keys.cs` (new): reading the key with no echo, then store, remove and status.
- `tools/SecretSmoke`: new modes.
  - `--assert-keyring`, offline, over a fake store: resolution order, describing a key without its value,
    the fallback, unknown names refused.
  - `--assert-keyring-cm`: a round trip in the real Credential Manager under a throwaway target
    `HeyGanesh-test/keyring`, deleted afterwards. SecretSmoke's existing self-test already writes and deletes a test
    credential the same way.
- Docs: DEC-045, FLOW, ARCHITECTURE (the planned "API key security proxy" row becomes real), AGENTS.md, TEST-CHECKLIST,
  CI.md, HANDOVER, ROLLBACK (written first).

**8b**
- The four options files and their clients (one line each to call `ApiKey`).
- `ShellHost.Lesson.cs`, `ShellHost.Route.cs` and the client construction in `ShellHost.cs`.
- `EnvironmentSecretStore.cs`; `tools/PlannerEval/EnvKeyStore.cs`, `LiveClient.cs` and PE-5.
- Rows:
  - TU-11 and VO's Fish format row get a case where a given `ApiKey` delegate is the one sent (loopback stubs, a dummy
    key);
  - a new App row checks that `--keys` reports the source without the value.
- More than 3 files, so ROLLBACK comes first.

## Execution path

New (FLOW gains a "keys" section): App start → `KeyRing` → each client's `ApiKey` delegate → read at request time
(Credential Manager, then the environment) → the `Authorization` header. Nothing caches the value. Today's path, an
environment read inside each client, stays as the default for hosts that don't pass a delegate.

## Alternatives considered

- **A separate proxy process** that adds the key to outgoing requests, so the app process never holds it. That is
  stronger isolation, and it's what the name "proxy" suggests. But it adds a second process to start, supervise and
  authenticate (a localhost port needs its own lock, or any program can spend your keys). The realistic risks here are
  a key in a log, a screenshot, a committed file or a shell history, and Credential Manager plus "presence only"
  reporting covers them. It could come later, behind the same `ApiKey` delegates.
- **A new `HeyGanesh.SecurityProxy` assembly**, as older docs imagined. Rejected: `HeyGanesh.Security` already holds the
  store, and one class doesn't need an assembly. The docs will say where the proxy lives.
- **Give the clients an `ISecretStore` instead of a delegate.** That would make TutorEngine, Voice, Element Selection and
  Vision reference `HeyGanesh.Security`, and TutorEngine and Voice deliberately reference nothing. Rejected.
- **Drop the environment fallback.** That would break CI and every documented command today. Rejected; the header shows
  where each key came from, so you can see it.

## What could break, and how we'd know

- **A key printed by `--set-key`.** The new App row and `--assert-keyring` check that the output holds only lengths.
  G-11, the secret scan, still runs.
- **Credential Manager unavailable** (a locked-down machine). `Get` failures fall back to the environment, and the
  source shows it.
- **An offline row changing behaviour.** Every delegate defaults to today's environment read. All 18 CI gates and the
  existing counts must stay identical.
- **The no-echo read misbehaving when input is piped.** `--set-key` takes stdin when input is redirected. A row pipes a
  dummy value, then removes it.

## Verification

- **8a:**
  - Security and App build with `-warnaserror` 0/0;
  - `--assert-keyring` and `--assert-keyring-cm` pass;
  - App `--keys` shows three `MISSING` (environment) here;
  - piping a dummy key into `--set-key deepseek` prints "stored", then `--keys` shows "credential manager", then
    `--remove-key deepseek`. That round trip uses a dummy value and the real store, and I'd run it only with your OK.
  - The local CI runner passes with 18 gates.
- **8b:**
  - every existing row keeps its count;
  - the new delegate rows pass;
  - the App's offline exit codes are unchanged (12, 13, 0).
- **Not verifiable without your keys:** live calls authenticating through Credential Manager.
