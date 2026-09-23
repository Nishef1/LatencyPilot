# LatencyPilot System Design

Status: **Authoritative architecture baseline**  
Last updated: 2026-09-21

`ROADMAP.md` defines required outcomes. `PROJECT_STATUS.md` records current evidence. ADR 0006 owns the narrow v1 product/safety direction. ADR 0007 owns the current GPU measurement/search/ranking method.

## 1. Product model

LatencyPilot v1 is a narrow Windows 11 interrupt-affinity automation product built around:

```text
Measure → Apply one supported change → Verify effective state → Keep or Revert
```

The normal-user v1 workflow is:

```text
preflight / quiet check
→ baseline DPC/ISR evidence
→ bounded Original qualification
→ paired GPU affinity search
→ final runtime GPU placement proof
→ input/xHCI CPU-headroom selection
→ reversible xHCI/controller affinity
→ one reboot when required
→ runtime verification
→ before/after evidence + Restore original settings
```

A mutation feature is incomplete unless it snapshots exact original state, journals ownership, applies one allowlisted change, verifies stored/runtime state, and can restore the exact baseline after cancellation/failure.

NIC/RSS mutation, audio affinity, BIOS/HAGS/MSI/power-plan changes and generic cross-subsystem/Pareto auto-tuning are outside the v1 automatic path.

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
- pinned standalone PresentMon as an independent frame-cadence cross-check;
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

The development Gate A helper is explicit owner validation, not product mutation IPC. Public mutation stays off until physical safety gates pass.

There is intentionally no generic tweak engine, generic privileged registry writer, shell execution primitive or speculative repository layer.

## 5. Project responsibilities

### `LatencyPilot.Core`

Stable domain records/invariants only:

- processor/device evidence;
- GPU benchmark/report DTOs;
- pair/finalist/terminal-state evidence;
- input/USB route evidence;
- metrics/results/system context.

No UI, ETW implementation, raw P/Invoke, registry paths or SQLite.

### `LatencyPilot.Benchmarking`

Hardware-independent interpretation/orchestration:

- percentile and robust statistical primitives;
- steady `baseline-quality-v2` / `workload-stability-v1`;
- GPU candidate generation from actual topology/current CPU-set evidence;
- `gpu-affinity-benchmark-v3` evidence interpretation/readiness;
- 3–5 Original observations with median/MAD variability;
- direct local pair orchestration and drift retry;
- Stage-A representatives, bounded top-3 Stage-B refinement and top-3 finalists;
- three shuffled 30 s finalist pairs;
- persisted median paired effects, MAD/noise context, raw before/after values and rank;
- best-observed CPU + selection confidence independent from Keep;
- final Keep/Restore orchestration;
- input/xHCI timing/headroom interpretation.

There is no hidden weighted score. Short screens order candidates by paired 1%-low effect; finalists rank by median paired 1%-low effect. Noise changes confidence, not rankability. Keep is a separate guardrail and runtime-placement decision owned by ADR 0008.

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
- direct D3D12 command-queue timestamp evidence;
- controlled wall-clock loop periods used for AVG / 1% / 0.1% lows and p99 context;
- renderer recreation after GPU restart/rollback activation;
- no registry/device mutation and no elevation.

The controlled loop period is a deterministic candidate-comparison signal; it is not claimed to equal arbitrary-game end-to-end frametime.

### `LatencyPilot.Persistence`

Concrete SQLite recovery boundary:

- schema/CAS revisions;
- exact original-state persistence;
- unresolved-state blocking;
- retained Keep ownership;
- safe restore/upgrade/uninstall checks.

It is not a generic repository abstraction.

### `LatencyPilot.Service`

Privileged boundary. Public v6 is observation-only. Future product mutations must be typed/allowlisted and reuse the same journal/recovery discipline. Never a generic scripting host.

### `LatencyPilot.App`

Normal-user orchestration and presentation. A development checkout may expose Gate A and bounded diagnostic scopes; normal-user v1 ultimately exposes `Optimize Interrupt Affinity` and `Restore original settings` after arming gates pass.

For Gate A completion, App consumes `GateAResultPresentation` derived from the validated authoritative report. The renderer must not re-rank candidates from raw/shuffled execution order.

## 6. Evidence semantics

Never collapse:

```text
stored interrupt configuration
≠ allocated IRQ/resource assignment
≠ runtime DPC/ISR placement
```

For GPU evidence also never collapse:

```text
raw scored observation
≠ paired derived effect
≠ persisted decision/finalist authority
≠ verified terminal machine state
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

### Automatic GPU search — noise-tolerant v3

The benchmark process remains stable across the complete search. Workload calibration, process identity, seed and worker map remain frozen. Every affinity-triggered GPU transition is followed by renderer/device recreation before the next relevant warm-up/measurement.

```text
5 s non-scored Original warm-up
→ 3 × 10 s Original observations
→ extend to 4/5 only when robust median/MAD variability is high
→ fresh 10 s O0
→ Stage A: one eligible representative per physical core
→ local pair per candidate:
     OriginalBefore
     apply/restart/verify
     candidate warm-up
     10 s scored candidate
     exact rollback + Original verification
     Original warm-up
     10 s OriginalAfter
→ pair effect from geometric mean of adjacent Original controls
→ one retry for high drift; structurally valid retry remains rankable
→ Stage B: refine at most top 3 physical cores
→ Stage C: at most top 3 logical-CPU finalists
→ 3 shuffled 30 s pairs per finalist
→ rank by median paired 1%-low effect
→ report MAD/noise + High/Medium/Low confidence
→ separate Keep guardrails
→ final target-only ETW ISR-placement proof
→ Keep or exact RestoreOriginal while preserving best-observed rank
```

Raw observations are never replaced by pseudo-normalized FPS.

## 8. Screening versus final Keep

A structurally valid scored trial requires valid artifact/session identity, frozen-workload continuity, finite positive controlled metrics and expected stored affinity around the trial. Ordinary variability is uncertainty evidence, not invalid evidence.

Standalone PresentMon and screening kernel ETW remain independent cross-check/guardrail sources. Missing optional collector data stays explicit. Healthy ETW that proves off-target GPU ISR placement invalidates the candidate.

Ranking and Keep are separate:

```text
valid candidate evidence
→ persisted rank / best-observed CPU
→ confidence from lead + MAD + variability + pair consistency
→ independent Keep guardrails
→ final runtime placement proof
```

Final Keep requires expected stored candidate state, clean ETW integrity, zero lost events, attributable GPU ISR samples, requested logical target, zero resolved off-target ISR and verified terminal state. Failure restores exact Original without deleting the best-observed result.

## 9. GPU result authority

Gate A completion follows:

```text
validate report/session/source
→ package evidence ZIP when possible
→ build GateAResultPresentation
→ render authoritative Overview result
```

The result UX exposes:

- best-observed CPU independently from terminal Keep/Restore;
- High/Medium/Low selection confidence;
- Evidence eligible versus development evidence;
- actual median Original → Candidate FPS/ms;
- absolute FPS/ms improvement and paired percentage effect;
- direct pair evidence, drift/noise guide and attempt;
- persisted rank/finalist authority and practical ties;
- final placement/stored-state evidence;
- ZIP/session/raw-report actions.

`GateAClosureEligible` is source/evidence eligibility only.

## 10. Diagnostic GPU scopes

### Full search

Authoritative machine-wide v3 search, subject to physical Gate A.

### Selected CPUs / Custom

Uses real paired screening for selected logical CPUs, reports the best observed selection with low confidence, skips the machine-wide finalist tournament, never Keeps and restores exact Original.

### Original diagnostics

Captures bounded Original-only observations with no affinity mutation or restart. It measures variability only.

## 11. Input/xHCI path

After a verified GPU Keep, current design:

1. stop the GPU benchmark workload;
2. capture fresh quiet interrupt-headroom evidence;
3. resolve primary Raw Input through USB topology to the exact xHCI controller;
4. exclude the whole physical core containing the GPU winner;
5. rank remaining logical CPUs by interrupt-duration/tail evidence, with counts as context;
6. bind recommendation to the interrupt-owning controller;
7. apply only after the shared mutation/recovery substrate is physically proven;
8. verify controller-specific runtime placement;
9. rollback xHCI safely on verification failure.

The GPU and xHCI choices are sequential, not a generic Pareto optimizer.

## 12. Reboot/resume model

Prefer target-device activation when sufficient. When Windows reports restart/reboot requirements, persist pending ownership and aggregate into one product-level reboot when possible.

Post-login verification must re-read actual machine state. If either managed state cannot be verified, restore safely rather than assuming success from persisted intent.

## 13. Safety/recovery invariants

Every mutation must:

```text
snapshot exact original
→ journal ownership
→ apply narrow supported change
→ verify stored/runtime state
→ Keep or exact Restore
→ verify final state
→ resolve ownership
```

Cancellation is rollback-biased. Unresolved/diverged ownership blocks unsafe follow-on work. Upgrade/uninstall must preserve recovery ability while owned state exists.

## 14. UI model

WinUI remains non-elevated and evidence-first.

Primary surfaces ultimately communicate:

- what exact change is being tested;
- current phase/progress;
- safe-stop state;
- measured evidence and uncertainty;
- final Keep/Restore/practical-tie truth;
- verification/recovery state;
- Restore original settings.

Color never carries decision state alone. Real Windows rendering, keyboard navigation, text scaling and accessibility inspection remain physical requirements.

## 15. Release and arming boundary

Hosted GitHub Actions verifies software contracts only. It cannot prove physical GPU/xHCI placement, device restart behavior, LocalSystem runtime behavior, WinUI rendering/accessibility or installer/signing behavior.

The arming chain is:

```text
exact-head green CI
→ physical noise-tolerant v3 GPU Gate A
→ repeated whole-search / confidence evidence
→ Stop safely + supported recovery exercise
→ real Windows result/accessibility inspection
→ typed allowlisted mutation-specific IPC
→ physical App → Service mutation proof
→ integrated xHCI apply/verify
→ combined reboot/resume/recovery
→ final before/after UX
→ signed package/install/upgrade/uninstall closure
```

Until those gates pass, public product mutation remains unavailable.

## 16. Design rule

Prefer the smallest supported mechanism that produces auditable evidence and exact recovery. Do not add speculative infrastructure, undocumented tuning or broader automatic scope merely because it may affect latency.