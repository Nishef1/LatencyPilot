# LatencyPilot Project Status

This is the live execution ledger for `ROADMAP.md`. Current source/runtime evidence owns actual state; plans and historical chat do not.

Last updated: 2026-09-17

## Overall

- Product version: **0.0.2 pre-alpha**.
- Supported target: **Windows 11 x64, active local interactive desktop session**.
- Public protocol: **v6 / observation-only** (`LatencyPilot.Observation.v6`).
- Public commands: **`GetStatus`, `CaptureKernelLatency` only**.
- `ServiceBoundary.MutationAvailable`: **false**.
- Steady evidence schema: **`latencypilot-evidence-v9`**.
- Steady decision baseline: **`baseline-quality-v2`**.
- Steady workload readiness: **`workload-stability-v1`**.
- Automatic GPU method: **`gpu-affinity-benchmark-v1`**.
- Automatic GPU evidence/report: **`latencypilot-gpu-benchmark-v1` / `latencypilot-gpu-auto-affinity-report-v1`**.
- Current automatic GPU design authority: `docs/superpowers/specs/2026-09-17-gpu-auto-affinity-ranking-design.md`.
- Gate A frame collector: pinned standalone **PresentMon 2.5.1 console**, official SHA-256 verified; separate PresentMon Service/API installation is not a Gate A prerequisite.
- Permanent deterministic test policy: **target 10, owner-authorized maximum 20 methods**. Exact count is owned by the exact-head hosted run.
- Hosted GitHub Actions: **test-only**. It compile-checks referenced source projects and proves deterministic contracts; it does not prove actual App rendering, LocalSystem behavior, hardware placement/restart, standalone PresentMon operation on the owner machine, installer/signing or accessibility.

## Completion summary

### Repository-verifiable source

The major 1.0 source foundations are implemented across:

- observation-only Protocol v6 and protected Service capture path;
- processor/device/interrupt inventory and ETW DPC/ISR attribution;
- `baseline-quality-v2` and steady `workload-stability-v1` evidence;
- deterministic GPU affinity evidence/guardrail policy and exact rollback journal;
- **ranked benchmark-backed automatic GPU candidate search source** using a managed D3D12 workload, D3D12 timestamps, standalone raw PresentMon evidence, GPU-specific validity, bounded all-core screening, post-transition warm-ups, fresh top-candidate re-screen, SMT refinement, runtime ISR placement proof and balanced confirmation;
- development-only **Run GPU Gate A** App orchestration with real progress, minimize/restore behavior, explicit UAC helper boundary and safe cancellation/recovery ownership;
- USB/xHCI route/input timing and NIC/RSS read-only/readiness source;
- workload profiles/Pareto policy and global Restore Baseline planning;
- recovery-aware install/upgrade/uninstall and release provenance/signing hooks;
- owner-local read-only closure and UI evidence-capture tooling.

Supported USB/NIC mutation implementations remain deliberately unarmed until the shared GPU mutation substrate passes Gate A physically. Product GPU mutation IPC is also still blocked by Gate A/B/C/D ordering.

### True product completion

**Not 100% yet.** Exact-final-HEAD hosted Tests for the current ranked/standalone-collector revision, Phase 2 owner-local closure, GPU Gate A physical proof, product mutation gates and final release/package/hardware validation remain open. Source existence must not be substituted for those claims.

## Measurement authority

```text
Quick diagnostic snapshot
  1 × 5 s
  integrity / attribution / concentration / hypothesis generation only

Steady repeated decision baseline — baseline-quality-v2
  warmed/repeatable RealWorld or Controlled-idle workload
  5 s settle
  5 × 20 s authoritative windows
  750 ms inter-window settle
  clean integrity + duration/sample/noise/drift gates

Steady optimizer readiness — workload-stability-v1
  same five-window sequence
  DPC/ISR activity consistency
  CPU-busy consistency when complete evidence exists
  remains authoritative for the steady/manual evidence product

Automatic GPU affinity — gpu-affinity-benchmark-v1
  deterministic D3D12 benchmark
  one adaptive calibration, then frozen workload/worker mapping
  5 s non-scored original warm-up, then two original reference controls
  every eligible physical core actively screened within v1 bound (max 16)
  each affinity transition: apply/restart → 5 s non-scored warm-up → two scored runs → exact rollback
  candidate run-level frame-p99 spread must remain <=20% for rankability
  Inconclusive/invalid candidates are not ranked
  rank valid physical cores by median run-level frame-p99
  fresh re-screen of the best up-to-three physical cores
  winning physical core receives sibling refinement when applicable
  final ABBA + BAAB confirmation against exact original state, with warm-up after each state transition
  original/default = exact reference/recovery state, not a fixed minimum-improvement winner gate
  one bounded retry for retryable contamination; otherwise Inconclusive
```

Automatic GPU benchmark validity is owned by exact source/GPU/driver/benchmark/frozen-workload identity, ETW integrity, stored-state verification, runtime GPU ISR attribution/placement, standalone PresentMon raw-frame evidence, per-candidate repeatability and control comparability. System-wide CPU-busy drift alone is not a hard failure for this synthetic method.

`Valid` never means globally healthy or optimal.

## Phase 2 — read-only physical closure OPEN

Repository source includes processor-group-aware topology, PnP/driver/interrupt evidence, stored-vs-allocated-vs-runtime separation, protected Service/Named Pipe v6, ETW attribution, repeated baseline gates, evidence-v9 provenance/SHA/readiness verification and adaptive evidence UI.

The owner-local closure path is consolidated in `tools/LatencyPilot.ReadOnlyClosure`, `docs/OWNER_CLOSURE.md` and the UI evidence-capture script. These tools reduce manual work but do not convert unexecuted checks into evidence.

Prior physical evidence includes a valid Real-world five-window baseline on historical clean revision `a4b4ff36c875982d5a263665860853462d0b055b`. It authorized overlapping source work under ADR 0004; it did not close Phase 2.

Remaining owner-local Phase 2 obligations include exact-closure-revision Real-world + Controlled-idle baselines, consolidated read-only audit, current inspector/device sanity, attribution plausibility, App/Service cleanup/session rejection, UI/accessibility review and proof that read-only closure performs no unrelated mutation.

## Phase 3 — ranked benchmark-backed GPU source implemented; exact-head CI + physical arming OPEN

### Durable mutation/recovery substrate

Implemented:

- SQLite journal with compare-and-swap revisions;
- unresolved-state blocking;
- explicit mutation lifecycle states;
- exact original/candidate GPU affinity payloads;
- fail-closed recovery classification from actual state;
- interruption-safe logical affinity write with compensation ownership;
- exact-target restart/reboot-required source;
- owner-only validation harness;
- retained `Kept` discovery and Restore Baseline integration;
- install/upgrade/uninstall protection while managed state is retained/unresolved.

### Automatic benchmark/search source

Implemented source flow:

```text
capture exact original/default GPU affinity
→ launch normal-user LatencyPilot.GpuBenchmark
→ deterministic D3D12 adaptive calibration
→ freeze worker map + workload + seed
→ 5 s non-scored original warm-up
→ two original decision-control trials
→ generate every eligible physical-core candidate within v1 bound (max 16)
→ deterministic shuffled screening
→ each candidate: apply/restart → stored verify → 5 s non-scored warm-up → two scored runs
→ synchronize benchmark artifact + kernel ETW + standalone PresentMon raw frames
→ require resolved single-adapter target-only ISR placement
   (display KMD preferred; labelled dxgkrnl fallback only when unambiguous)
→ require candidate run-level frame-p99 spread <=20% for rankability
→ exact rollback before next candidate
→ rank valid/repeatable physical-core candidates by lower median run-level frame-p99
→ fresh re-screen of best up-to-three physical-core candidates from new apply/restart/warm-up cycles
→ test eligible sibling(s) of fresh physical-core winner
→ fixed eight-run ABBA + BAAB confirmation, >=30 s scored run, each role preceded by a fresh 5 s warm-up
→ final cancellation boundary
→ KeepCandidate only when ranked finalist remains decision-grade/repeatable and final state verifies
   otherwise exact RestoreOriginal / explicit recovery state
```

Original/default affinity is retained as exact recovery/reference state and confirmation side. It is **not** the winner threshold for forced-CPU ranking. A valid finalist can be kept even when the generic Original-vs-candidate delta is inside ±3% or Original measures faster; the comparison remains visible context. Missing/inconsistent attribution, failed placement, dirty evidence or repeatability failure remains `Inconclusive` and cannot enter ranking.

Dedicated source contracts now include:

- `LatencyPilot.GpuBenchmark` normal-user D3D12 host;
- frozen workload and GPU timestamp calibration;
- pinned standalone PresentMon 2.5.1 console locator with official SHA-256 verification and LatencyPilot-controlled packaged/cache provisioning;
- `Sylvan.Data.Csv` 1.4.4 for robust PresentMon CSV parsing rather than a custom parser;
- process-targeted PresentMon capture with unique ETW session name, timed V2 CSV output and crop to the exact benchmark artifact interval;
- DXGI/PnP Gate A graphics identity continuity without a PresentMon Service graphics-device dependency;
- `GpuBenchmarkReadiness` with GPU-specific contamination handling;
- bounded all-core candidate planner + fresh top-three re-screen + sibling refinement;
- `GpuAutoAffinitySession` orchestration/report;
- topology-aware progress plan and real candidate verdict observer;
- late-safe-stop contract: cancellation after final comparison still prevents Keep and forces rollback if candidate state remains owned;
- confirmation/recovery failure handling that preserves the original failure and escalates an unverified exact-original rollback instead of silently discarding it;
- third-party notices for PresentMon and Sylvan.

### Development App experience — source complete, physical UI inspection pending

A development checkout exposes **Run GPU Gate A**, not a product `Optimize GPU` button.

Source behavior:

```text
main App remains non-elevated
→ starts benchmark non-elevated
→ minimizes main window through OverlappedPresenter
→ opens compact progress window
→ obtains explicit UAC only for owner Gate A helper
→ shows real phase/pass/candidate progress, p99, 1% low, ISR state, last verdict and ETA
→ distinguishes non-scored warm-up from scored measurement
→ Stop safely writes cancellation request
→ progress stays stopping/restoring until terminal state verification
→ main window restores after terminalization
```

Dynamic UI Automation `ItemStatus` mirrors live candidate/phase/progress/status meaning; state is not color-only.

Hosted Tests compile-check this source but **do not** prove actual rendered size, keyboard focus, taskbar behavior, screen-reader output, standalone PresentMon runtime, hardware placement/restart or rollback. Those remain owner-local evidence.

### Gate A — internal physical benchmark + mutation proof — OPEN

The historical owner read-only preflight found an NVIDIA GeForce RTX 3070 and a clean journal but returned an ambiguous allocated-resource tuple:

```text
irq=4294967270
group=1
affinity=0x0
flags=0x0002
```

That tuple remains provenance only and is not accepted as effective placement. Current source requires resolved single-adapter runtime ISR evidence: display-KMD attribution is preferred; an explicitly labelled `dxgkrnl.sys` WDDM fallback is accepted only when a single display adapter makes attribution unambiguous under the conservative method.

Current Gate A runbook: `docs/PHASE3_PHYSICAL_VALIDATION.md`.

Gate A now requires physical evidence on one exact clean revision for:

1. exact-final-HEAD green hosted Tests plus normal App + protected Service path and clean journal;
2. D3D12 benchmark smoke without mutation, including multicore activity and finite/stable timestamp evidence;
3. pinned standalone PresentMon 2.5.1 collection without a separately installed Service/API requirement;
4. full bounded ranked candidate search (expected eight physical cores on the owner Ryzen 7 5700X absent explicit CPU-set exclusions), including a 5 s post-transition warm-up before scored candidate evidence;
5. fresh best-up-to-three finalist re-screen and SMT sibling refinement;
6. real progress/taskbar/keyboard/accessibility behavior;
7. exact candidate stored state + resolved target-only single-adapter ISR placement;
8. exact rollback between screening/finalist/refinement candidates;
9. balanced finalist decision with verified Keep or RestoreOriginal and repeatability/integrity gate;
10. **Stop safely** restoring/verifying original with zero unresolved state;
11. repeated whole-search reproducibility/equivalence or explicit evidence-based Inconclusive;
12. one supported failure/recovery exercise;
13. final exact known machine state and `unresolved=0`.

### Historical physical evidence and why it does not select a winner

Historical Gate A reports proved several substrate properties: deterministic benchmark execution, journal-owned candidate apply/rollback, exact original restoration, and later reports proved single-adapter WDDM ISR placement with zero off-target events. They did **not** establish the best CPU under the current ranked method.

The most recent analyzed owner report before this redesign showed a systematic candidate first-pass transient after GPU apply/restart: first scored runs commonly had higher ISR/DPC activity and materially different frame-p99 than the immediately following run. That evidence is why every state transition now has a non-scored five-second warm-up and why the best up-to-three candidates are freshly re-screened before finalist selection.

Any historical `RestoreOriginal` result under the old “candidate must measurably beat Original” rule must remain historical evidence. It cannot be reinterpreted as proof that Windows default or CPU0 was the fastest setting.

### Current execution ladder

1. **Completed now — source/design:** ranked physical-core selection, post-transition warm-ups, fresh top-three re-screen, SMT refinement, integrity-gated ranking, balanced confirmation without a fixed 3% Original winner threshold, standalone PresentMon 2.5.1 capture/provisioning, Sylvan CSV parsing, DXGI/PnP Gate A identity, licensing notices and canonical methodology/runbook/spec/roadmap/system-design reconciliation are on `main`.
2. **Evidence still required now:** obtain a successful hosted **Tests** workflow on the exact final HEAD after the current reconciliation commits. Cancelled/superseded runs do not count.
3. **Next stage — physical Gate A rerun:** on that exact clean green revision, run the full automatic search on the owner machine and inspect per-candidate warm-up/scored trials, PresentMon provenance, ISR attribution/sample counts, target/off-target placement, ranking/finalist re-screen, `finalProcessor`, final stored state and `recoveryStatus=clean-zero-unresolved`.
4. **After that — reproducibility/safety closure:** repeat the whole ranked search and complete Stop safely, supported failure/recovery and rendered/accessibility/taskbar checks.
5. **Then:** only after Gate A passes, advance to Gate B typed mutation IPC; then Gate C physical IPC proof and Gate D product arming.

### Gate B — mutation-specific IPC — BLOCKED BY GATE A

After Gate A only: add typed/allowlisted mutation-specific Service commands and authorization. No arbitrary registry/shell/process primitive. `MutationAvailable` remains false during source implementation until later arming.

### Gate C — physical IPC proof — BLOCKED BY GATE B

Validate the real App/client → Service mutation path, authorization, target identity, journal/recovery and exact rollback.

### Gate D — product arming — BLOCKED BY GATE C

Only then may normal users receive **Auto-optimize GPU**.

## Phase 4 — USB/xHCI/input source frontier

Read-only/readiness source is implemented: Raw Input → PnP route, documented USB hub/port correlation, xHCI identity, bounded host timing metrics and xHCI DPC/ISR attribution. App surfaces exact route/port evidence and on-demand host timing.

Still open: supported reversible xHCI/controller-affinity mutation source after Gate A plus physical high-polling experiment/rollback evidence.

## Phase 5 — NIC/RSS source frontier

Read-only/readiness source is implemented: StandardCimv2 RSS state, conservative provider→PnP correlation, vendor-vs-NDIS attribution, bounded local RTT/jitter/loss/throughput/CPU metric contract and App inspector surface.

Still open: supported reversible RSS/affinity mutation source after Gate A plus physical local-network apply/revert evidence.

## Phase 6 — profile/Pareto/restore source

Implemented: versioned profiles, subsystem opt-out, raw named metrics/guardrails, Pareto policy without hidden score, retained-change discovery and fail-closed global Restore Baseline planning.

Armed multi-subsystem execution remains gated by physically unsupported/unarmed USB/NIC mutation paths.

## Phase 7 — release/recovery source hardening

Implemented source includes exact-main/exact-green release gating, self-contained publish sources, upgrade/uninstall recovery checks, SHA-256 manifests/checksums, Authenticode SHA-256 + RFC3161 hooks and local redacted diagnostics.

Owner-local release closure remains open: actual final Release publish/launch, signing evidence, installer/portable clean-machine checks, safe/blocked upgrade/uninstall, reboot/crash recovery, accessibility and representative hardware.

## Verification discipline

- Hosted CI is test-only.
- Permanent-test target is 10 and owner-authorized maximum is 20 methods.
- Temporary/obsolete tests are removed rather than accumulated.
- Every final source claim requires a successful **Tests** workflow on the exact final HEAD.
- Hardware/UI/package/signing claims require their corresponding owner-local evidence.

## Exact owner-local closure sequence

```text
exact-revision Phase 2 read-only baselines/audit + UI accessibility review
→ pull exact-green current main
→ run non-mutating D3D12 benchmark smoke
→ run complete ranked benchmark-backed Gate A at least twice
→ verify standalone PresentMon + post-transition warm-ups + fresh finalist re-screen
→ verify progress/Stop safely/runtime ISR placement/rollback/recovery + unresolved=0
→ only if Gate A passes: implement Gate B typed mutation IPC
→ Gate C physical App/client → Service proof
→ Gate D user-facing GPU arming
→ supported USB/xHCI mutation + physical proof
→ supported NIC/RSS mutation + physical proof
→ bounded multi-subsystem profile + Restore validation
→ signed package/install/upgrade/uninstall/reboot/crash recovery audit
→ representative supported-hardware audit
→ final 1.0 tag audit
```

Never mark a physical/build/package/signing requirement complete from hosted CI or source existence alone.
