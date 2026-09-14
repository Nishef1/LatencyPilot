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
- Hosted CI: **test-only**; App/Service compile/runtime/release evidence remains owner-local
- Current source handoff: **Phase 3 recovery/restart/transaction substrate is implemented but not exposed through IPC and not physically mutation-tested**

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
- recovery planning is fail-closed: unknown or externally diverged state requires manual intervention rather than a blind write;
- public observation protocol still has only `GetStatus` and `CaptureKernelLatency`; no mutation command is reachable.

### GPU affinity applicability and candidate generation

Implemented in source:

- mutation target restricted to a present SetupAPI display adapter;
- only documented `Interrupt Management\Affinity Policy` values are touched;
- one processor group only for v1 KAFFINITY writes;
- CPU0 is not hard-excluded;
- one logical sibling per physical core is selected using measured pressure plus CPU-set availability;
- hybrid efficiency classes are represented in the bounded screening set;
- candidate count defaults to four;
- baseline per-CPU DPC+ISR event shares are converted into per-window-normalized pressure evidence and fed into candidate planning.

### Apply/restart/revert source path

Implemented but **unarmed and not yet physically validated**:

- `Prepare` captures exact original state and durably journals it before apply;
- no-op candidate requests are rejected;
- candidate write is verified from stored state;
- device refresh uses SetupAPI `DIF_PROPERTYCHANGE` + `DICS_PROPCHANGE` for the exact display adapter;
- post-change install flags are inspected for `DI_NEEDRESTART` / `DI_NEEDREBOOT`;
- devnode state is checked through `CM_Get_DevNode_Status`, including restart-needed problem code 14;
- inability to establish a healthy in-place restart leaves the experiment unresolved in `RecoveryRequired`;
- rollback restores exact original values/key absence, verifies stored state, restarts the device and reaches `Reverted` only after the active original state is trusted;
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

- Hosted Tests for pre-transaction restart head `8086d6bf38c12a2bcb01528355f84c2128723368` completed successfully.
- Hosted Tests for mutation/recovery source head `cff657aaea4281a54717c7199bbdec105d5a141a` completed successfully.
- Subsequent recovery-planning source commits require their own exact-head CI result before being called deterministic-green.
- Hosted Tests remain insufficient evidence for the Service/WinUI runtime because that workflow intentionally does not build or run them.

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
- Service logs show successful mutation-journal startup inspection with zero unresolved experiments on a clean machine state.

If current Service source does not compile or launch, fix that before any further optimizer work.

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
