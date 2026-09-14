# LatencyPilot Project Status

This file is the live execution ledger for `ROADMAP.md`. Current source/runtime evidence owns actual state; plans and historical chat do not.

Last updated: 2026-09-14

## Overall

- Product version: **0.0.2 pre-alpha**
- Product completion: **Phases 0–1 closed; Phase 2 physical closure open; Phase 3 safety/candidate source implementation in progress under ADR 0004**
- User-visible mutation capability: **Unavailable / unarmed**
- Supported target: **Windows 11 x64, active local interactive desktop session**
- Desktop UI: **WinUI 3 / Windows App SDK 2.4 Stable / unpackaged self-contained**
- Privileged boundary: **Windows Service; public protocol remains read-only**
- Observation protocol: **v6** (`LatencyPilot.Observation.v6`)
- Evidence schema: **`latencypilot-evidence-v8`**
- Baseline method: **`baseline-quality-v2`**
- Permanent automated tests: **9 / hard maximum 10**
- Normal hosted CI: **test-only**; a one-time temporary Windows Service compile smoke has passed, while App/Service install/runtime/release evidence remains owner-local
- Current source handoff: **Phase 3 recovery/restart/transaction substrate is implemented and Service-compile-verified, but not exposed through IPC and not physically mutation-tested**

## Measurement authority

The current measurement contract remains:

```text
Quick diagnostic snapshot
  1 × 5 s
  integrity / attribution / concentration / hypothesis generation
  never a health or optimizer verdict

Repeated decision baseline — baseline-quality-v2
  workload already warmed/repeatable when applicable
  5 s LatencyPilot/service settle
  5 × 20 s authoritative windows
  750 ms inter-window settle
  >=95% actual/request duration
  >=1,000 DPC and >=1,000 ISR events/window
  clean capture integrity
  noise + drift gates

p99.9
  shown only with >=10,000 samples for that distribution
```

`Valid` means repeatable enough for the current comparison method. It does not mean the machine is globally healthy or optimally configured.

## Phase 2 — physical closure remains open

Source-complete read-only capabilities include:

- processor-group-aware topology and CPU sets;
- present PnP inventory with stable IDs and driver metadata;
- stored interrupt configuration kept distinct from allocated IRQ/resources and runtime DPC/ISR behavior;
- representative GPU/display, NIC and actual `USBXHCI` evidence surfaces;
- protected local Windows Service observation host;
- bounded/fail-closed Named Pipe protocol v6 with active-console-session authorization;
- ETW DPC/ISR collection, per-processor aggregation and image-lifetime-aware module attribution;
- explicit unresolved attribution rather than guessed ownership;
- Real-world / Controlled idle / Before-after scenario provenance;
- evidence-v8 with RequestIds, source revision, runtime CPU/power context and SHA-256;
- keyboard/high-contrast/adaptive evidence UX.

Physical evidence already includes a valid Real-world five-window decision baseline on clean revision `a4b4ff36c875982d5a263665860853462d0b055b`. Under ADR 0004 this is sufficient to allow targeted Phase 3 source work, but it does **not** close Phase 2.

Remaining Phase 2 physical obligations include:

1. valid Controlled-idle five-window baseline;
2. representative GPU/NIC/xHCI inspector sanity;
3. attribution plausibility against an independent observer where practical;
4. App-close/Service-restart/stale-ETW cleanup and active-session rejection checks;
5. Light/Dark/High Contrast, narrow/text-scaling, keyboard and UI Automation/screen-reader sanity;
6. JSON-visible-data/SHA/source-revision reconciliation;
7. proof that read-only validation performs zero unrelated system mutation.

## Phase 3 — current source state

ADR 0004 permits safety/candidate **source implementation** to overlap the remaining Phase 2 physical record. Mutation stays unavailable until the arming gate is physically proven.

### Durable safety substrate

Implemented in source:

- concrete SQLite persistence project;
- schema-v1 mutation journal with atomic transactions and compare-and-swap revisions;
- unresolved journal blocks another experiment;
- explicit `Prepared → Applying → Applied → Measuring → AwaitingDecision` and rollback/recovery states;
- recovery from `RecoveryRequired` is biased toward rollback, not forward resume;
- journal payloads are bounded valid JSON objects;
- versioned GPU-affinity journal payload codec stores exact original snapshot and candidate;
- exact original registry value existence/kind/raw bytes are retained;
- startup Service initializes the journal and re-reads actual stored GPU affinity state for unresolved entries;
- unresolved stored state is classified as original / candidate / both / diverged / unknown;
- recovery planning is fail-closed: unknown, externally diverged state, or a changed target driver environment requires manual intervention rather than a blind write;
- a pre-write abort is explicitly marked so later recovery does not claim an external candidate-looking state as LatencyPilot-owned;
- public observation protocol still has only `GetStatus` and `CaptureKernelLatency`; no mutation command is reachable.

### GPU affinity applicability and candidate generation

Implemented in source:

- mutation target restricted to a present SetupAPI display adapter;
- only documented `Interrupt Management\Affinity Policy` values are touched;
- existing `DevicePolicy`, when present, must use the documented `REG_DWORD` shape;
- existing `AssignmentSetOverride`, when present, must use documented DWORD/QWORD or <=64-bit binary shape;
- one processor group only for v1 KAFFINITY writes;
- CPU0 is not hard-excluded;
- one logical sibling per physical core is selected using measured pressure plus CPU-set availability;
- hybrid efficiency classes are represented in the bounded screening set;
- candidate count defaults to four;
- baseline per-CPU DPC+ISR event shares are converted into per-window-normalized pressure evidence and fed into candidate planning.

### Apply/restart/revert source path

Implemented but **unarmed and not yet physically validated**:

- `Prepare` validates current topology/applicability, captures exact original state and durably journals it before apply;
- no-op candidate requests are rejected;
- before any write, the exact stored original, driver version and current processor topology are revalidated;
- after journal transition to `Applying`, the same state is re-read immediately before the registry write to narrow the external-change race;
- a change detected before LatencyPilot writes is recorded as a pre-write abort and is **not** automatically rolled back as though LatencyPilot owned that external change;
- candidate write is verified from stored state;
- device refresh uses SetupAPI `DIF_PROPERTYCHANGE` + `DICS_PROPCHANGE` for the exact display adapter;
- post-change install flags are inspected for `DI_NEEDRESTART` / `DI_NEEDREBOOT`;
- devnode state is checked through `CM_Get_DevNode_Status`, including restart-needed problem code 14;
- inability to establish a healthy in-place restart leaves the experiment unresolved in `RecoveryRequired`;
- rollback refuses a blind write if the display-driver version changed or current stored state matches neither the captured original nor the experiment candidate;
- otherwise rollback restores exact original values/key absence, verifies stored state, restarts the device and reaches `Reverted` only after the active original state is trusted;
- failed/incomplete rollback remains unresolved instead of being reported as success.

The code deliberately does **not** use a broad device-restart primitive that could restart unrelated devices sharing function/filter drivers.

### Runtime-effect verification

Implemented as source evidence, not yet integrated into an armed experiment:

- stored registry equality is not treated as runtime proof;
- the raw ETW capture can resolve GPU-driver ISR events by the display adapter's driver service/module name;
- observed ISR execution is counted on the candidate CPU versus off-target CPUs;
- unresolved attribution stays explicitly unresolved;
- no arbitrary pass threshold has been invented before physical data establishes an adequate rule.

### PresentMon

PresentMon API discovery, graphics-device correlation and workload metric capture exist in source. Available workload metrics include frame/FPS plus optional CPU/GPU busy/wait, GPU/display latency and dropped-frame evidence where the installed PresentMon API exposes them. These are not yet wired into the candidate screening/finalist orchestration.

## Current verification evidence

- A one-time temporary Windows-hosted Service compile smoke on the current Phase 3 Service source succeeded in Tests run `34839998553` / run #535 at workflow commit `3abe4b1678de80a309f1b17acf7c63ec34c49438`, after fixing the `CA1859` analyzer failure in `MutationRecoveryInspector` at source commit `5292dd504cb426bafd714510106eef79104ebb66`.
- The same run #535 completed the permanent critical suite successfully; the suite remains **9/10**.
- The temporary Service compile step was removed immediately afterward; normal hosted CI is again test-only at `f96081f5ba5e9dd6b2ec6041e3ec1d6b38a03a63`.
- Hosted compile evidence proves the Service source compiles on the Windows runner; it does **not** prove WinUI App build, protected Service installation/startup, LocalSystem behavior, named-pipe runtime authorization, journal startup on the target PC, or any physical mutation behavior.
- Owner-local `./run.ps1` build/install/launch therefore remains mandatory before the physical recovery/restart gate can advance.

No physical GPU mutation, GPU restart, forced-failure rollback or reboot recovery has been performed by this source work.

## Mutation arming gate — still CLOSED

Do not add/enable user-reachable mutation commands until all of the following are true on the supported owner-local Windows path:

1. current exact clean `main` builds and launches App + Service successfully;
2. startup journal/recovery inspection works with no unresolved entry;
3. a deliberately constructed unresolved test entry survives Service restart and is correctly classified from actual machine state;
4. exact-target `DICS_PROPCHANGE` restart behavior is physically verified and reboot-required behavior is handled without pretending activation succeeded;
5. candidate apply → stored verification → restart → runtime evidence → exact rollback is proven on supported physical hardware;
6. a forced-failure exercise proves rollback/recovery rather than only the happy path;
7. only then may mutation-specific typed/allowlisted IPC be introduced and protocol/readiness semantics versioned.

## Exact next owner-local sequence

After the latest exact `main` revision has a completed green Tests run:

```powershell
.\run.ps1
```

First objective is **compile/install/launch validation only**, not mutation. Confirm:

- App header shows the exact clean source revision;
- Service starts and remains read-only over protocol v6;
- observation and evidence export still work;
- `run.ps1` reports the protected Service as `Running`;
- `run.ps1`/Service logs prove mutation-journal startup inspection with zero unresolved experiments on a clean machine state.

If current App/Service source does not compile, install or launch on the supported owner-local Windows path, fix that before any further optimizer work.

## After owner-local compile/launch passes

Proceed in this order:

```text
physical startup/recovery inspection
→ controlled unresolved-journal recovery classification
→ physical exact-target device restart/reboot-required validation
→ forced apply/rollback failure exercise while IPC remains unarmed
→ reconcile runtime ISR placement evidence
→ only then design typed mutation IPC/authorization
→ bounded candidate screening
→ PresentMon + ETW target/guardrail integration
→ balanced finalist confirmation (for example ABBA/BAAB)
→ Keep best or restore exact original state
```

Later phases remain USB/xHCI, NIC/RSS, bounded cross-subsystem optimization/profiles/Pareto/Restore Baseline, then release hardening.
