# LatencyPilot System Design

Status: **Authoritative architecture baseline**  
Last updated: 2026-09-18

`ROADMAP.md` defines required outcomes. `PROJECT_STATUS.md` records current evidence. ADR 0006 owns the current v1 interrupt-affinity product direction.

## 1. Product model

LatencyPilot v1 is a narrow Windows 11 interrupt-affinity automation product built around:

```text
Measure → Apply one supported change → Verify effective state → Keep or Revert
```

The normal-user v1 workflow is:

```text
preflight / quiet check
→ baseline DPC/ISR evidence
→ GPU logical-CPU benchmark search
→ final runtime GPU placement proof
→ input/xHCI CPU-headroom selection
→ reversible xHCI/controller affinity
→ one reboot when required
→ runtime verification
→ before/after evidence + Restore original settings
```

A mutation feature is incomplete unless it snapshots exact original state, journals ownership, applies one allowlisted change, verifies stored and runtime state, and can restore the exact baseline after cancellation/failure.

NIC/RSS mutation, audio affinity, BIOS/HAGS/MSI/power-plan changes and cross-subsystem/Pareto auto-tuning are outside the v1 automatic path. Existing read-only/future source may remain but must not complicate the critical path.

## 2. Supported target

Initial target:

- Windows 11 x64;
- active local interactive desktop session;
- local machine only;
- non-elevated WinUI App;
- self-contained .NET / Windows App SDK deployment;
- narrow privileged Service/helper boundary only for supported observation and physically authorized mutation.

RDP/multi-session, ARM64 and arbitrary multi-processor-group mutation are not implied v1 support.

## 3. Technology baseline

- C# 14 / .NET 10 LTS;
- WinUI 3 / Windows App SDK;
- typed/versioned local Named Pipes;
- ETW through `Microsoft.Diagnostics.Tracing.TraceEvent`;
- managed D3D12/DXGI through Vortice;
- pinned standalone PresentMon 2.5.1 as an independent frame-cadence cross-check;
- `Sylvan.Data.Csv` for PresentMon CSV parsing;
- SetupAPI + Configuration Manager;
- documented processor-topology / CPU-set APIs;
- documented USB hub interfaces/IOCTLs;
- Raw Input for host-observable input timing;
- SQLite mutation journal through `Microsoft.Data.Sqlite`;
- MSTest + Microsoft.Testing.Platform;
- Inno Setup + owner-local release tooling.

There is no native Vulkan/C++ baseline dependency and no separately installed PresentMon Service prerequisite for GPU Gate A.

## 4. High-level architecture

```text
┌────────────────────────────────────────────┐
│ LatencyPilot.App                           │
│ normal user / WinUI                        │
│ preflight, evidence, progress, result UX   │
└───────────────┬─────────────────┬──────────┘
                │                 │ launches normal-user workload
                │                 ▼
                │   ┌──────────────────────────────┐
                │   │ LatencyPilot.GpuBenchmark    │
                │   │ deterministic D3D12 workload│
                │   │ frozen work + frame periods │
                │   │ never mutates device state  │
                │   └──────────────────────────────┘
                │
                │ typed/versioned local IPC
                ▼
┌────────────────────────────────────────────┐
│ LatencyPilot.Service / owner Gate helper   │
│ privileged narrow boundary                │
│ public v6 still observation-only          │
│ mutation/recovery remains gated           │
└──────────────┬─────────────────┬───────────┘
               │                 │
               ▼                 ▼
┌──────────────────────────┐  ┌──────────────────────────┐
│ Platform.Windows         │  │ Persistence              │
│ ETW / SetupAPI / DXGI    │  │ SQLite mutation journal │
│ PresentMon / USB / Input │  │ + recovery ownership    │
└──────────────┬───────────┘  └──────────────────────────┘
               ▼
         Windows 11 / hardware
```

The development Gate A helper is explicit owner validation, not product mutation IPC. Public mutation stays off until the physical safety gates pass.

There is intentionally no generic tweak engine, generic privileged registry writer, shell execution primitive or repository-abstraction layer.

## 5. Project responsibilities

### `LatencyPilot.Core`

Stable domain records/invariants only: topology/device evidence, benchmark/report DTOs, input/USB route evidence, metrics/results and system context. No UI, ETW implementation, P/Invoke, registry paths or SQLite.

### `LatencyPilot.Benchmarking`

Hardware-independent interpretation/orchestration:

- percentile/statistical primitives;
- steady `baseline-quality-v2` / `workload-stability-v1`;
- GPU candidate generation from actual topology;
- `gpu-affinity-benchmark-v1` evidence interpretation/readiness;
- v1 GPU search policy: one scored screen/logical CPU, noise-aware finalist re-test, low-FPS ranking and bounded repeatability;
- progress planning;
- input/xHCI timing/headroom interpretation;
- future read-only/policy components that do not enter the v1 critical path.

Current GPU ranking is lexicographic and transparent:

```text
median 1% low (<=1% relative difference = tie)
→ median AVG FPS (<=1% = tie)
→ lower median frame-p99 (<=1% = tie)
→ median 0.1% low only for >5% rare-tail separation
→ deterministic passive fallback
```

No weighted score or ABBA/BAAB confirmation exists in the v1 GPU session. SMT siblings are screened directly as logical-CPU candidates, so there is no separate SMT-refinement phase.

### `LatencyPilot.Protocol`

Typed/versioned IPC DTOs/errors only. Current public contract remains:

```text
ProtocolVersion.Current = 6
Pipe = LatencyPilot.Observation.v6
Commands = GetStatus, CaptureKernelLatency
MutationAvailable = false
```

### `LatencyPilot.Platform.Windows`

Windows mechanisms:

- topology/CPU sets;
- PnP/driver/stored/allocated interrupt evidence;
- ETW capture/module/runtime attribution;
- GPU affinity state/applicability/restart;
- DXGI adapter identity;
- pinned standalone PresentMon resolution/hash verification/CSV parsing;
- USB hub topology and exact port/controller correlation;
- Raw Input discovery/timing;
- xHCI interrupt attribution;
- retained NIC/RSS read-only source outside the v1 automatic path.

Ambiguous evidence stays ambiguous.

### `LatencyPilot.GpuBenchmark`

Normal-user deterministic workload process:

- hardware D3D12 adapter + flip-model swap chain;
- fixed multi-core worker map;
- adaptive startup calibration then frozen workload;
- direct D3D12 timestamp evidence;
- controlled wall-clock loop periods used for AVG / 1% / 0.1% lows and p99 context;
- renderer recreation after GPU restart;
- no registry/device mutation and no elevation.

The controlled loop period is a deterministic candidate-comparison signal; it is not claimed to equal arbitrary game end-to-end frametime.

### `LatencyPilot.Persistence`

Concrete SQLite recovery boundary: schema/CAS revisions, unresolved-state blocking, retained Keep ownership, safe restore/upgrade/uninstall checks. It is not a generic repository abstraction.

### `LatencyPilot.Service`

Privileged boundary. Public v6 is observation-only. Future product mutations must be typed/allowlisted and reuse the same journal/recovery discipline. Never a generic scripting host.

### `LatencyPilot.App`

Normal-user orchestration and presentation. A development checkout may expose `Run GPU Gate A`; normal-user v1 ultimately exposes `Optimize Interrupt Affinity` and `Restore original settings` after arming gates pass.

## 6. Evidence semantics

Never collapse:

```text
stored interrupt configuration
≠ allocated IRQ/resource assignment
≠ runtime DPC/ISR placement
```

Likewise:

```text
Raw Input host report timing
≠ physical device latency
≠ click-to-photon latency
```

Unavailable evidence remains unavailable.

## 7. Measurement products

### Quick diagnostic

`1 × 5 s` ETW capture for integrity/attribution/concentration only.

### Steady baseline

`baseline-quality-v2` and `workload-stability-v1` remain the repeated RealWorld/manual evidence product. They are not forced into the synthetic GPU-search method.

### Automatic GPU search

The benchmark process remains stable across the complete search. The D3D12 renderer uses a three-buffer flip chain with two frame contexts/fences, avoiding the old full GPU wait after every Present while bounding queue depth. Every GPU configuration restart—candidate activation **and rollback to Original**—is followed by renderer/device recreation before the next measurement block. A `DXGI_ERROR_DEVICE_REMOVED`/RESET trial has one bounded recreate-and-retry path; repeated failure remains terminal. Workload calibration, process identity, seed and worker map remain frozen.

```text
one adaptive calibration
→ frozen workload
→ 5 s original non-scored warm-up/reference (benchmark only; no PresentMon/ETW)
→ each eligible logical CPU:
     apply/restart/verify
     5 s non-scored warm-up (benchmark only; no PresentMon/ETW)
     1 scored run
     exact rollback
→ 5 s Original warm-up → fresh scored Original control after the sweep; stop if Original drift exceeds its repeatability band
→ rank by 1% low → AVG → p99 → 0.1% low rare-tail context
→ best three + all candidates within max(1%, observed Original noise) of the third-place screening cutoff:
     two independent deterministically shuffled re-test rounds
     fresh apply/restart/warm-up + 1 scored 30 s run + exact rollback per round
     at most one adaptive replacement score
→ prefer stable 3-of-up-to-4 evidence; otherwise retain all four valid runs and make their measured variance part of ranking/guardrail thresholds
→ 5 s Original warm-up → fresh scored Original control after finalist re-tests; stop if 1%/AVG/p99 drift
→ walk finalists in rank order through Original/frame/noise-aware DPC/ISR guardrails
→ apply highest-ranked clean winner
→ final benchmark-only warm-up → ETW placement-verification capture
→ Keep only with clean target-only GPU ISR proof
   else exact RestoreOriginal
```

Windows default is reference/recovery, not a minimum-improvement gate.

## 8. Screening versus Keep

Screening prioritizes completing the bounded comparison safely:

- controlled benchmark artifact + frozen-workload/stored-state continuity are required;
- PresentMon is an independent best-effort cadence cross-check;
- ETW is a best-effort screening guardrail when unavailable;
- healthy ETW proving wrong/off-target placement invalidates that candidate;
- system CPU-busy drift is measured from Windows system-time snapshots and surfaced as trial context;
- when Original and finalist each have at least three usable attributable GPU-driver runs, DPC/ISR p99 tails are evaluated per run and compared by median against a noise-aware Keep threshold rather than used as ranking inputs.

Final Keep is stricter. It requires:

- exact stored winner state verified before/after final capture;
- clean kernel ETW with zero event loss;
- attributable GPU ISR samples;
- requested CPU target ISR > 0;
- resolved off-target ISR = 0.

No ETW proof means no Keep.

## 9. PresentMon boundary

PresentMon is the pinned official standalone 2.5.1 executable resolved only from LatencyPilot-controlled packaged/cache locations with hash verification.

CSV timing semantics are schema-aware:

- current `FrameTime` or legacy `MsBetweenPresents` may provide Present cadence;
- `MsBetweenAppStart` is a different CPU-frame boundary and is not substituted as the same metric.

Failed raw CSVs may be retained for owner diagnostics, but retention is bounded.

## 10. GPU recovery/cancellation

Once candidate mutation is journal-owned:

```text
apply failure / capture failure / cancel / failed final proof
→ rollback exact original
→ verify actual original state
→ terminalize journal
```

Unknown/diverged state stays recovery-owned. A failed rollback verification is never hidden behind the original failure.

## 11. USB/xHCI v1 architecture

Read-only route source already follows:

```text
Raw Input interface
→ PnP instance/ancestry
→ USB hub enumeration + driver-key/port correlation
→ exact xHCI controller
```

No VID/PID/name heuristic is accepted as exact route proof.

After GPU winner is fixed, automatic v1 selection will:

```text
post-GPU quiet ETW capture
→ exclude GPU winner CPU by default
→ rank CPU headroom from DPC duration + ISR duration + tail spikes
   (counts remain visible context)
→ select exact interrupt-owning xHCI controller target
```

System-changing xHCI affinity is part of v1 but remains unarmed until GPU Gate A physically proves the shared privileged mutation/recovery substrate. This is sequencing, not scope deferral.

## 12. Reboot and verification

The combined product session must persist enough journal state to survive one required reboot, then re-read actual stored/runtime GPU+xHCI state after login. If either managed placement cannot be verified, the supported baseline restore path owns recovery.

## 13. IPC and authorization gates

```text
Gate A  owner-only physical GPU search + mutation/recovery proof; public v6 stays read-only
Gate B  typed allowlisted mutation-specific IPC + authorization
Gate C  physical App/client → Service mutation proof
Gate D  normal-user mutation arming
```

Observation authorization is not mutation authorization.

## 14. Release and recovery

Hosted CI is test-only. Owner-local release owns Release build/publish, launch smoke, payload manifest/hashes, signing/timestamp, installer/portable packaging and clean-machine/recovery validation.

Install/upgrade/uninstall must not remove recovery tools while LatencyPilot still owns a retained or unresolved system change.

## 15. Future/non-v1 source

NIC/RSS mutation, audio affinity, profile/Pareto and multi-subsystem automatic optimization are post-v1. Existing read-only/policy source may remain but cannot silently participate in the v1 decision path.

## 16. Verification discipline

Hosted Tests prove only deterministic/source contracts and compile referenced source. They do not prove physical device restart/interrupt placement, LocalSystem behavior, rendered accessibility, PresentMon runtime on owner hardware, installer/signing or reboot recovery.

Physical progression is owned by `ROADMAP.md` / `PROJECT_STATUS.md`: exact-head CI → GPU Gate A → product mutation boundary → automatic USB/xHCI mutation/verification → combined reboot/before-after UX → release/accessibility closure.

## Audit-closure optimizer transaction model (2026-09-19)

LatencyPilot now treats automatic optimization as a one-at-a-time experiment pipeline: **Original measurement → GPU affinity → conservative MSI → primary-input/xHCI → final verification → report**. A stage that is NotReady, inconclusive, or requires reboot stops the pipeline; intent is never treated as activation proof.

MSI mutation is deliberately narrow: `MSISupported` may be enabled only when authoritative stored state makes the target applicable. `MessageNumberLimit` and interrupt priority are preserved/observed, not tuned automatically. xHCI affinity requires one explicit primary Raw Input identity, one exact USB route, clean capture evidence, and reversible journal ownership.

`Restore original settings` replays retained LatencyPilot changes newest-first from exact snapshots. It does **not** claim to restore Windows defaults. Public mutation remains fail-closed (`MutationAvailable = false`) until exact-revision physical GPU/MSI/xHCI validation is recorded; hosted CI proves source contracts only.
