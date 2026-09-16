# LatencyPilot Project Status

This is the live execution ledger for `ROADMAP.md`. Current source/runtime evidence owns actual state; plans and historical chat do not.

Last updated: 2026-09-16

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
- Permanent deterministic test policy: **target 10, owner-authorized maximum 20 methods**. Temporary all-core coverage has been folded into the canonical GPU session test and the tiny core/protocol assertions were consolidated without dropping coverage; exact count is verified by the exact-head hosted run.
- Hosted GitHub Actions: **test-only**. It compile-checks referenced source projects and proves deterministic contracts; it does not prove actual App rendering, LocalSystem behavior, hardware placement/restart, installer/signing or accessibility.

## Completion summary

### Repository-verifiable source

The major 1.0 source foundations are implemented across:

- observation-only Protocol v6 and protected Service capture path;
- processor/device/interrupt inventory and ETW DPC/ISR attribution;
- `baseline-quality-v2` and steady `workload-stability-v1` evidence;
- deterministic GPU affinity comparison/guardrail policy and exact rollback journal;
- **benchmark-backed automatic GPU candidate search source** using a managed D3D12 workload, D3D12 timestamps, raw PresentMon evidence, GPU-specific readiness, bounded all-core screening, SMT refinement, runtime ISR placement proof and balanced confirmation;
- development-only **Run GPU Gate A** App orchestration with real progress, minimize/restore behavior, explicit UAC helper boundary and safe cancellation/recovery ownership;
- USB/xHCI route/input timing and NIC/RSS read-only/readiness source;
- workload profiles/Pareto policy and global Restore Baseline planning;
- recovery-aware install/upgrade/uninstall and release provenance/signing hooks;
- owner-local read-only closure and UI evidence-capture tooling.

Supported USB/NIC mutation implementations remain deliberately unarmed until the shared GPU mutation substrate passes Gate A physically. Product GPU mutation IPC is also still blocked by Gate A/B/C/D ordering.

### True product completion

**Not 100% yet.** Phase 2 owner-local closure, GPU Gate A physical proof, product mutation gates and final release/package/hardware validation remain open. Source existence or hosted CI must not be substituted for those physical claims.

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
  repeated original controls
  every eligible physical core actively screened within v1 bound (max 16)
  CPU0 remains eligible
  passive processor pressure = ordering/context only
  winning physical core receives sibling refinement when applicable
  final ABBA + BAAB confirmation against exact original state
  original/default wins when no candidate establishes safe measurable improvement
  one bounded retry for retryable contamination; otherwise Inconclusive
```

Automatic GPU benchmark validity is owned by exact source/GPU/driver/benchmark/frozen-workload identity, ETW integrity, stored-state verification, runtime GPU ISR placement and control comparability. System-wide CPU-busy drift alone is not a hard failure for this synthetic method.

`Valid` never means globally healthy or optimal.

## Phase 2 — read-only physical closure OPEN

Repository source includes processor-group-aware topology, PnP/driver/interrupt evidence, stored-vs-allocated-vs-runtime separation, protected Service/Named Pipe v6, ETW attribution, repeated baseline gates, evidence-v9 provenance/SHA/readiness verification and adaptive evidence UI.

The owner-local closure path is consolidated in `tools/LatencyPilot.ReadOnlyClosure`, `docs/OWNER_CLOSURE.md` and the UI evidence-capture script. These tools reduce manual work but do not convert unexecuted checks into evidence.

Prior physical evidence includes a valid Real-world five-window baseline on historical clean revision `a4b4ff36c875982d5a263665860853462d0b055b`. It authorized overlapping source work under ADR 0004; it did not close Phase 2.

Remaining owner-local Phase 2 obligations include exact-closure-revision Real-world + Controlled-idle baselines, consolidated read-only audit, current inspector/device sanity, attribution plausibility, App/Service cleanup/session rejection, UI/accessibility review and proof that read-only closure performs no unrelated mutation.

## Phase 3 — benchmark-backed GPU source implemented; physical arming OPEN

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
→ deterministic D3D12 warm-up/adaptive calibration
→ freeze worker map + workload + seed
→ two original control trials
→ generate every eligible physical-core candidate within v1 bound (max 16)
→ deterministic shuffled screening, 2 × 15 s per candidate
→ verify stored candidate + synchronized benchmark/ETW/raw PresentMon evidence
→ require direct GPU-driver ISR placement on requested logical processor
→ exact rollback before next screening candidate
→ nominate physical-core finalist only from measurable improvement
→ test eligible sibling(s) of finalist physical core
→ fixed eight-run ABBA + BAAB confirmation, >=30 s/run
→ final cancellation boundary
→ KeepCandidate only for confirmed safe improvement
   otherwise exact RestoreOriginal / explicit recovery state
```

Dedicated source contracts now include:

- `LatencyPilot.GpuBenchmark` normal-user D3D12 host;
- frozen workload and GPU timestamp calibration;
- PresentMon 2.5.1+/API compatibility boundary and raw-frame interpretation;
- `GpuBenchmarkReadiness` with GPU-specific contamination handling;
- bounded all-core candidate planner + sibling refinement;
- `GpuAutoAffinitySession` orchestration/report;
- topology-aware progress plan and real candidate verdict observer;
- late-safe-stop contract: cancellation after final comparison still prevents Keep and forces rollback if candidate state remains owned.

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
→ Stop safely writes cancellation request
→ progress stays stopping/restoring until terminal state verification
→ main window restores after terminalization
```

Dynamic UI Automation `ItemStatus` mirrors live candidate/phase/progress/status meaning; state is not color-only.

Hosted Tests compile-check this source but **do not** prove actual rendered size, keyboard focus, taskbar behavior, screen-reader output or hardware rollback. Those remain Task 8/9 owner-local evidence.

### Gate A — internal physical benchmark + mutation proof — OPEN

The historical owner read-only preflight found an NVIDIA GeForce RTX 3070 and a clean journal but returned an ambiguous allocated-resource tuple:

```text
irq=4294967270
group=1
affinity=0x0
flags=0x0002
```

That tuple remains provenance only and is not accepted as effective placement. Current source requires direct runtime GPU-driver ISR evidence.

Current Gate A runbook: `docs/PHASE3_PHYSICAL_VALIDATION.md`.

Gate A now requires physical evidence on one exact clean revision for:

1. normal App + protected Service path and clean journal;
2. D3D12 benchmark smoke without mutation, including multicore activity and finite/stable timestamp evidence;
3. full bounded candidate search (expected eight physical cores on the owner Ryzen 7 5700X absent explicit CPU-set exclusions);
4. real progress/taskbar/keyboard/accessibility behavior;
5. exact candidate stored state + direct target-only GPU ISR placement;
6. exact rollback between screening candidates;
7. balanced finalist decision with verified Keep or RestoreOriginal;
8. **Stop safely** restoring/verifying original with zero unresolved state;
9. repeated whole-search reproducibility/equivalence or explicit Inconclusive/NoMeasurableDifference;
10. one supported failure/recovery exercise;
11. final exact known machine state and `unresolved=0`.

Latest owner-local Gate A benchmark evidence sampled on 2026-09-16 at source
revision `245ea18c893996451f1fec3e4005e24eba3c4d3b`:

- the normal-user App, protected LocalSystem Service, explicit UAC helper and
  deterministic D3D12 benchmark completed one full 31/31 run;
- the run screened all 8 eligible physical-core candidates with 2 × 15 s per
  candidate, producing 19 valid trials and 19 raw benchmark artifacts;
- all 16 candidate trials produced direct WDDM placement proof on the requested
  processor with 192,030 target ISR events and 0 off-target ISR events;
- no candidate established a measurable frame-tail improvement without a
  guardrail regression, so the authoritative recommendation was
  `RestoreOriginal`;
- exact original state was verified after the run and the mutation journal
  reported `clean-zero-unresolved`.

Evidence file:
`C:\Users\PC\Documents\LatencyPilot\validation\gpu-auto-affinity-20260916T211332640Z-9100c66d2e2f441594765d5c3b88db3c\gpu-auto-affinity-report.json`.

This is a successful full benchmark/search sample on one RTX 3070 system, not
the complete physical exit gate: the separate Stop-safely interaction,
rendered keyboard/accessibility/taskbar inspection, and an independent
reproducibility/failure-recovery exercise remain owner-local closure items.

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
- Coverage was consolidated back into canonical owners rather than deleting the bounded all-core contract.
- Every final source claim requires a successful **Tests** workflow on the exact final HEAD.
- Hardware/UI/package/signing claims require their corresponding owner-local evidence.

## Exact owner-local closure sequence

```text
exact-revision Phase 2 read-only baselines/audit + UI accessibility review
→ pull exact-green current main
→ run non-mutating D3D12 benchmark smoke
→ run complete benchmark-backed Gate A at least twice
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
