# LatencyPilot

**Evidence-driven automatic interrupt-affinity tuning for Windows 11.**

LatencyPilot follows one rule:

> **Measure → Apply one supported change → Verify → Keep or Revert**

It is not a registry-tweak pack, debloater, generic FPS booster or a list of settings assumed to be universally better. The current v1 goal is deliberately narrow: automate the useful manual GPU + input/USB interrupt-affinity workflow while preserving exact rollback and proving effective runtime placement.

> [!IMPORTANT]
> LatencyPilot is **0.0.2 pre-alpha**. Public protocol v6 remains observation-only and user-reachable mutation is unavailable. The GPU search/result source exists, but exact-head CI plus physical GPU Gate A must pass before product mutation can be armed. Automatic USB/xHCI mutation follows only after that shared substrate is physically proven.

## v1 workflow

```text
preflight / quiet check
→ deep ETW baseline
→ benchmark GPU interrupt affinity on every eligible logical CPU
→ use time-local Original controls to normalize gradual background movement
→ bounded finalist confirmation when variability permits
→ choose by 1% low → AVG FPS → lower p99 → 0.1% low rare-tail context
→ final ETW target-only GPU ISR placement proof
→ measure remaining per-CPU interrupt headroom
→ Raw Input → USB → exact xHCI controller
→ choose/apply a separate xHCI interrupt CPU
→ reboot once when required
→ verify GPU + xHCI runtime placement
→ show before/after evidence
→ Restore original settings
```

The workflow is inspired by the manual combination of AutoGpuAffinity, LatencyMon/ETW and Interrupt Affinity Policy Tool, but LatencyPilot replaces manual device matching and blind affinity guesses with Windows topology, controlled measurement, explicit verification and journal-owned rollback.

Current product authority: [`docs/adr/0006-simple-auto-interrupt-affinity-v1.md`](docs/adr/0006-simple-auto-interrupt-affinity-v1.md).

## Current state

- Scope/safety/recovery foundations: **source complete**
- Read-only ETW/topology/device evidence: **source substantially complete; physical closure remains**
- GPU auto-affinity search: **time-local decision method implemented; physical Gate A open**
- Gate A result UX: **validated report → evidence ZIP → Overview result source implemented; physical render/accessibility inspection remains**
- Final GPU Keep: **internal source requires hard ETW target-only ISR proof**
- USB/input route + xHCI read-only evidence: **source exists**
- Automatic USB CPU selection: **read-only post-GPU recommendation implemented; physical evidence/product integration open**
- Reversible xHCI mutation substrate: **internal/product-gated**
- Combined reboot verification / before-after product UX: **open**
- Public protocol: **v6**, `GetStatus` + `CaptureKernelLatency` only
- `ServiceBoundary.MutationAvailable`: **false**
- Hosted CI: **software-contract evidence only**

[`PROJECT_STATUS.md`](PROJECT_STATUS.md) is the live execution ledger. [`ROADMAP.md`](ROADMAP.md) is the authoritative v1 phase plan.

## What v1 intentionally does not automate

NIC/RSS mutation, audio affinity, BIOS changes, HAGS changes, MSI-mode toggles, power-plan tuning, generic debloating and a cross-subsystem/Pareto auto-optimizer are outside the v1 automatic path. Existing read-only/future/recovery source can remain without entering or gating the v1 automatic workflow.

## GPU search semantics

The built-in normal-user `LatencyPilot.GpuBenchmark` process performs deterministic D3D12 work. One startup calibration freezes worker mapping, simulation work, command workload, seed and resolution.

Every eligible logical CPU receives:

```text
apply/restart/verify
→ 5 s non-scored warm-up
→ 1 scored 30 s screening run
→ exact rollback + Original-state verification
```

Original is measured repeatedly before screening. Screening then runs in blocks of at most four candidates, with fresh Original block controls between full blocks and a final Original control after the last block. These controls are not fake candidates and ordinary gradual movement is not automatically a failed experiment. The optimizer:

1. preserves raw scored candidate/control trials unchanged;
2. normalizes rankable candidate **decision aggregates** from their time-local Original level back to the session Original baseline;
3. records the measured local movement as uncertainty;
4. raises shortlist/Keep thresholds using that uncertainty rather than crediting drift as candidate benefit.

If effective 1%-low variability — the larger of Original repeatability noise and time-local control movement — exceeds 15%, the complete screening evidence remains diagnostic but exhaustive finalist confirmation is skipped and exact Original is retained.

Otherwise the best three candidates plus additional candidates inside the bounded noise-aware third-place cutoff enter two independent re-test rounds, capped at five finalists. Each round deterministically shuffles finalist order; every finalist receives a fresh apply/restart/warm-up, one scored run, and exact rollback. A single replacement score is allowed when the preferred ±3% three-run cluster is missing. If four valid runs still do not form that preferred cluster, all four remain usable and their observed per-metric variance becomes part of the decision floor.

Candidate decision evidence is ranked transparently by:

1. median **1% low**, with <=1% relative differences treated as ties;
2. median **AVG FPS**, also with a 1% tie margin;
3. lower median **frame-p99**, also with a 1% tie margin;
4. median **0.1% low** only when the remaining rare-tail difference exceeds 5%;
5. deterministic passive fallback only if measured metrics remain practically tied.

There is no fixed “must beat Windows default by 3%” rule, no separate SMT/hyperthread refinement phase and no ABBA/BAAB confirmation loop. CPU0 and eligible SMT siblings are first-class logical-processor candidates. Windows default is the exact reference/recovery state.

Before Keep, finalists are checked in rank order against Original repeatability noise, time-local uncertainty and frame/interrupt-tail guardrails. A bad top finalist can fall through to the next clean improvement.

The benchmark process intentionally stays alive for the whole search. The renderer uses a three-buffer flip chain with two frame contexts. After each GPU configuration restart, the D3D12 renderer/device is recreated before the next measurement block, while process identity, frozen workload, seed and worker map remain constant so process-start/JIT/cold-cache effects are not reintroduced for every CPU.

The benchmark records its own controlled wall-clock loop periods. They are a deterministic comparison signal for this workload; they are not claimed to be arbitrary-game end-to-end frametime.

## Screening versus final Keep

Screening is resilient: standalone PresentMon and kernel ETW are independent cross-checks/guardrails. If they are unavailable, the absence is explicit and valid benchmark-owned controlled-frame evidence can continue under verified stored state. If healthy ETW proves an off-target candidate, that candidate is invalid.

Structural failures are never normalized away: invalid/mismatched benchmark evidence, state/source divergence, failed mutation/recovery ownership, healthy ETW proving wrong placement, or an unverified terminal state remains fail-closed.

Final Keep is stricter. A ranked GPU winner is kept only after a fresh verification capture proves:

```text
stored winner state verified before/after
ETW integrity clean
ETW lost events = 0
attributable GPU ISR samples > 0
ISR target = selected CPU
resolved off-target ISR = 0
```

If final placement cannot be proved, exact Original is restored.

## PresentMon boundary

Gate A uses a pinned official standalone PresentMon console collector; users do not need a separately installed PresentMon Service/API for this path.

PresentMon remains an independent frame-cadence cross-check rather than the primary ranking dependency. CSV parsing keeps timing semantics distinct: current `FrameTime` or legacy `MsBetweenPresents` may provide cadence; `MsBetweenAppStart` is not silently substituted as the same metric. Failed diagnostic CSV retention is bounded.

## Gate A result evidence

After a report passes schema/session/source validation, LatencyPilot attempts to package the complete session evidence into a shareable ZIP and builds the Overview result from the authoritative report.

The development result surface separates:

```text
raw scored trial history
!= persisted decision aggregates/rank
!= verified terminal machine state
```

It shows Original/candidate decision metrics, candidate comparison, repeatability history, decision evidence and explicit evidence actions. A RestoreOriginal outcome cannot be rendered as a kept winner merely because one candidate measured fastest in a raw or shuffled sample.

Raw JSON remains available through an explicit **Open raw report** action rather than being automatically opened when a run completes.

## ETW / DPC / ISR evidence

LatencyPilot keeps these layers separate:

```text
stored interrupt configuration
≠ allocated IRQ/resource assignment
≠ runtime DPC/ISR behavior
```

DPC/ISR counts are visible but do not represent CPU cost alone; duration and tail behavior also matter. CPU0 is not universally banned.

The built-in ETW engine provides per-CPU and module attribution and replaces a mandatory LatencyMon dependency for the automatic workflow.

## Input / USB direction

Read-only source already resolves:

```text
Raw Input device
→ PnP ancestry
→ USB hub/port
→ exact xHCI controller
```

and can capture host-observable Raw Input timing plus xHCI-attributed interrupt evidence.

After a verified GPU Keep, v1 can choose a separate CPU from remaining interrupt headroom using DPC duration + ISR duration + tail spikes, with counts as context. The selected target is the interrupt-owning xHCI/controller, not blindly the leaf mouse.

System-changing xHCI affinity is part of v1 but remains product-unarmed until GPU Gate A proves the shared privileged mutation/recovery substrate physically and the integrated xHCI runtime-verification path closes.

## Architecture

```text
src/
  LatencyPilot.Core/
  LatencyPilot.Benchmarking/
  LatencyPilot.Protocol/
  LatencyPilot.Platform.Windows/
  LatencyPilot.Persistence/
  LatencyPilot.Service/
  LatencyPilot.App/
  LatencyPilot.GpuBenchmark/

tests/
  LatencyPilot.CriticalTests/
```

Responsibilities remain narrow:

- `Core` — stable domain concepts/invariants;
- `Benchmarking` — measurement interpretation and v1 GPU/input decision policy;
- `Protocol` — typed/versioned local IPC only;
- `Platform.Windows` — ETW, SetupAPI/ConfigMgr, DXGI, PresentMon, USB hub IOCTLs, Raw Input and Windows-specific mechanisms;
- `Persistence` — SQLite mutation journal/recovery ownership;
- `Service` — privileged observation boundary and future typed mutation boundary;
- `GpuBenchmark` — non-elevated deterministic D3D12 workload;
- `App` — non-elevated WinUI orchestration/result UX;
- `CriticalTests` — focused durable high-blast-radius contracts.

There is no generic privileged shell/process/registry execution surface.

## Measurement products

### Quick diagnostic

```text
1 × 5 seconds
```

Used for integrity, attribution, CPU concentration and hypothesis generation only.

### Steady repeated baseline

`baseline-quality-v2` + `workload-stability-v1` remain available for repeated RealWorld/manual evidence. They are intentionally separate from the synthetic GPU-search method.

### Deep v1 baseline

The final one-button v1 workflow will use the ETW engine for a long comparable before/after baseline. LatencyMon itself is not a dependency.

See [`docs/BENCHMARK_METHODOLOGY.md`](docs/BENCHMARK_METHODOLOGY.md).

## Safety and recovery

A system-changing feature owns:

```text
snapshot exact original
→ journal experiment
→ apply narrow supported change
→ verify stored/runtime state
→ Keep or exact Restore
→ verify final state
```

Cancellation is rollback-biased. Upgrade/uninstall must not remove recovery tools while LatencyPilot still owns an unresolved or retained change.

## Testing and evidence policy

Hosted CI runs the critical test project. It does not prove WinUI rendering, LocalSystem behavior, physical interrupt placement, GPU restart timing, installer/package behavior, signing or accessibility. Those require owner-local physical evidence.

No new permanent test method should be added for every implementation detail. Temporary TDD characterization should be folded into canonical contracts or removed once represented.

## Local workflows

Owner-local App + protected Service validation:

```powershell
.\run.ps1
```

Service + App development/log loop:

```powershell
.\live.ps1
```

App-only helpers:

```powershell
.\dev.ps1
```

For XAML Hot Reload/Live Visual Tree, Visual Studio `F5` remains the preferred UI loop.

## Physical arming sequence

```text
exact-head green CI
→ GPU Gate A physical search/restart/placement/rollback proof
→ typed allowlisted mutation-specific IPC
→ physical App → Service mutation proof
→ normal-user GPU arming
→ automatic USB/xHCI selection + reversible mutation + physical proof
→ combined reboot/verification/before-after UX
→ signed package/accessibility/recovery closure
```

MSI-mode mutation is not part of this v1 automatic path.

See [`PROJECT_STATUS.md`](PROJECT_STATUS.md) for current blockers.

## What “done” means

Repository/source completion and true v1 completion are separate claims. True v1 requires physical GPU Gate A, automatic USB/xHCI apply/verify, reboot/recovery, final before/after UX, accessibility/runtime validation and signed package/install/upgrade/uninstall evidence on recorded artifacts.

## Contributing / license / security

Read [`AGENTS.md`](AGENTS.md), [`CONTRIBUTING.md`](CONTRIBUTING.md), [`CLA.md`](CLA.md), [`LICENSE`](LICENSE) and [`SECURITY.md`](SECURITY.md) before making changes.