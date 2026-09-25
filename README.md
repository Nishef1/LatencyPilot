# LatencyPilot

**Evidence-driven automatic interrupt-affinity tuning for Windows 11.**

LatencyPilot follows one rule:

> **Measure → Apply one supported change → Verify → Keep or Revert**

It is not a registry-tweak pack, debloater, generic FPS booster, or a list of settings assumed to be universally better. The current v1 product goal is deliberately narrow: automate a reversible GPU + input/USB interrupt-affinity workflow while preserving exact rollback and proving effective runtime placement.

> [!IMPORTANT]
> LatencyPilot is **0.0.2 pre-alpha**. Public protocol v6 remains observation-only and user-reachable mutation is unavailable. The current GPU search method is implemented in source, but physical GPU Gate A must pass before product mutation can be armed. Automatic USB/xHCI mutation follows only after the shared mutation/recovery substrate is physically proven.

## Current authority

- Product scope, mutation ownership, recovery and v1 sequencing: [`docs/adr/0006-simple-auto-interrupt-affinity-v1.md`](docs/adr/0006-simple-auto-interrupt-affinity-v1.md)
- GPU measurement/search/ranking: [`docs/adr/0011-restart-canonicalized-gpu-affinity-v6.md`](docs/adr/0011-restart-canonicalized-gpu-affinity-v6.md)
- Live execution/evidence state: [`PROJECT_STATUS.md`](PROJECT_STATUS.md)
- Required completion outcomes: [`ROADMAP.md`](ROADMAP.md)

ADR 0011 supersedes ADR 0010 for new GPU measurement/ranking evidence. ADR 0010 remains historical v5 evidence, ADR 0009 remains historical v4 evidence, and ADR 0006 still owns the broader product/safety contract.

## v1 workflow

```text
preflight / quiet check
→ deep ETW baseline
→ robust Original variability estimate
→ paired GPU screening: Original before → Candidate → Original after
→ physical-core representatives → uncertainty-aware SMT refinement
→ observed top 4 logical CPUs get one 10 s recheck; one fifth may join only when uncertainty overlaps
→ top 2 → two 15 s confirmation pairs
→ optional third 15 s pair only if the top two remain uncertain
→ final ETW target-only GPU ISR placement proof
→ measure remaining per-CPU interrupt headroom
→ Raw Input → USB → exact xHCI controller
→ choose/apply a separate xHCI interrupt CPU
→ reboot once when required
→ verify GPU + xHCI runtime placement
→ show before/after evidence
→ Restore original settings
```

NIC/RSS mutation, audio/storage affinity, BIOS changes, HAGS changes, MSI-mode forcing, power-plan/timer/HPET/processor-performance tweaks, mouse/keyboard queue-size tuning, automatic polling-rate changes, generic debloating and a generic cross-subsystem optimizer are outside the v1 automatic path.

## Current state

- Scope/safety/recovery foundations: **source complete**
- Read-only ETW/topology/device evidence: **source substantially complete; physical closure remains**
- GPU restart-canonicalized observer-isolated adaptive v6 search: **implemented in source; physical Gate A open**
- Gate A result UX: **authoritative report → evidence ZIP → Overview result implemented; real render/accessibility inspection remains**
- Final GPU Keep: **internal source requires hard ETW target-only ISR proof**
- USB/input route + xHCI read-only evidence: **source exists**
- Automatic USB CPU selection: **read-only recommendation source exists; physical/product integration remains**
- Reversible xHCI mutation substrate: **internal/product-gated**
- Combined reboot verification / before-after product UX: **open**
- Public protocol: **v6**, `GetStatus` + `CaptureKernelLatency` only
- `ServiceBoundary.MutationAvailable`: **false**
- Hosted CI: **software-contract evidence only**

## GPU restart-canonicalized observer-isolated adaptive v6 measurement

New GPU evidence uses method id `gpu-affinity-benchmark-v6` and report schema `latencypilot-gpu-auto-affinity-report-v3`. Historical v1/v2/v3/v4/v5 evidence remains historical and is never reinterpreted as v6. `Original` means the exact pre-test Windows/driver affinity policy; it is not CPU 0.

### 1. Original variability estimate

Before any candidate mutation in Full/Selected-CPU paired search:

```text
verify exact Original
→ one in-place GPU restart with Original unchanged
→ re-verify Original + driver and recreate renderer
→ 5 s non-scored Original warm-up
→ 3 × 10 s scored Original observations
→ median + relative MAD noise estimate
→ extend to observation 4/5 only when variability is elevated
→ ordinary noise lowers confidence; it does not block candidate search
```

Structurally valid observations stay in the estimate and audit trail. Candidate search is stopped only when the evidence itself is unusable (for example invalid identity/state or non-finite required metrics), not merely because a normal Windows system is noisy.

### 2. Direct local pairs

LatencyPilot then captures a fresh Original control and screens candidates with chained local pairs:

```text
O0 → C1 → O1 → C2 → O2 → ...
```

For higher-is-better metrics such as 1% low and AVG:

```text
reference = sqrt(originalBefore * originalAfter)
effect    = candidate / reference - 1
```

For lower-is-better frame p99:

```text
effect = reference / candidate - 1
```

This local reference compensates for time-local drift without replacing the raw measurements. The result report keeps both the real Original/Candidate FPS or ms values and the drift-adjusted paired effect.

### 3. Pair drift and retry

High local Original movement gets one fresh retry. If the retry is still noisy **but structurally valid**, the candidate remains rankable and the larger movement is retained as uncertainty evidence. There is no noise-only consecutive-candidate early stop.

Structural failures still fail closed: wrong stored state, invalid artifact/session identity, non-finite required metrics, healthy contradictory placement evidence, failed rollback/recovery, or equivalent evidence-contract failures.

### 4. Full-search strategy

Full search avoids expensive full confirmation of every SMT sibling:

1. **Stage A — physical-core representatives:** screen one eligible logical processor per physical core for 10 seconds.
2. **Stage B — uncertainty-aware sibling refinement:** retain at most four physical-core hypotheses that can still overlap the leader after bounded uncertainty.
3. **Stage C — adaptive shortlist:** retain the observed top four logical CPUs whenever available, admit at most one additional uncertainty-overlapping challenger, and give every shortlisted CPU one additional 10-second local pair.
4. **Stage D — finalists:** rank the repeated short evidence by median and advance only the top two CPUs.

Bounded shortlist uncertainty uses the larger of the 1-point practical margin, effect MAD, and local-control movement capped at that pair's drift budget. The top four short-screen leaders are rechecked when available; uncertainty may admit only one additional challenger. This prevents a single noisy screen from excluding CPU4/CPU14-like candidates while keeping the expensive finalist stage bounded. Paired 1% low remains the primary ranking signal; AVG and frame p99 are guardrail/context metrics and 0.1% low remains diagnostic.

### 5. Finalist ranking and confidence

The top two finalists run **two shuffled 15-second local pairs**. A third 15-second round is added only when their lead remains inside measured uncertainty. Every structurally valid finalist is ranked by median paired 1% low effect.

LatencyPilot also records:

- effect MAD;
- positive-pair count;
- Original variability and local control movement;
- raw median Original/Candidate FPS/ms;
- lead over the runner-up;
- practical-tie state.

Rank 1 is always the **best observed CPU** when valid ranked evidence exists. Noise changes `High` / `Medium` / `Low` confidence; it does not erase rank 1. A one-percentage-point practical tie is disclosed and lowers confidence while preserving the deterministic best-observed choice.

Scored trials with external ETW/PresentMon observers use a bounded 2–4 s unscored startup settle and require a 500 ms quiet tail before the scored QPC boundary. Non-observer warm-ups skip that cost. Benchmark workers use the complete affinity mask of their physical core, and those masks are persisted/verified as frozen workload provenance.

No Bayesian model, bootstrap simulation, scored-frame outlier deletion or hidden weighted score is used in v6.

## Final Keep is stricter than ranking

The best observed CPU and the terminal machine state are separate facts. Automatic Keep additionally requires positive median benefit, bounded AVG/frame-p99/interrupt-tail guardrails, exact stored-state verification, and final clean target-only GPU ISR placement proof:

```text
stored selected state verified
clean kernel ETW
lost events = 0
attributable GPU ISR samples > 0
ISR target = selected logical CPU
resolved off-target ISR = 0
terminal stored state verified
```

If Keep is not recommended or final runtime placement cannot be proved, exact Original is restored **without deleting the best-observed CPU from the report**.

Microsoft documents interrupt affinity as a device affinity policy and `AssignmentSetOverride` as a `KAFFINITY` processor mask when the specified-processors policy is used. LatencyPilot treats that stored policy as configuration evidence only; runtime ETW placement remains a separate verification layer.

## Diagnostic GPU scopes

The developer UI also supports bounded diagnostic paths:

- **Selected CPUs · restore Original** — real paired screening only for the selected CPUs; reports the best observed option in that subset, never Keeps, always restores Original and cannot close Gate A.
- **Original only · no system changes** — captures Original-only observations with no affinity mutation or device restart.

These are methodology/debugging tools, not shortcuts around the full physical gate.

## Gate A result evidence

After a report passes schema/session/source validation, LatencyPilot attempts to package the complete session into a shareable ZIP and builds the Overview result from the authoritative report.

The result surface keeps these layers distinct:

```text
raw scored trials / local pairs
!= persisted best-observed rank
!= selection confidence
!= Keep recommendation
!= verified terminal machine state
```

For the best observed CPU it shows, where evidence exists:

- actual median Original → Candidate 1% low / AVG FPS and frame-p99 ms;
- absolute improvement, such as `+12.6 FPS`;
- direct percentage change from those displayed raw medians, such as `+6.8%`;
- the separate drift-adjusted paired effect used by ranking;
- `High` / `Medium` / `Low` confidence and MAD/noise detail;
- practical-tie state;
- whether the CPU was kept or exact Original was restored;
- direct pair evidence and evidence ZIP/session/raw-report actions.

Presentation code consumes the persisted rank. It must not re-rank shuffled execution data or confuse “best observed” with “kept”.

## PresentMon and ETW boundaries

The built-in D3D12 benchmark owns the primary controlled frame-period signal for GPU search. Standalone PresentMon remains an independent best-effort frame-cadence cross-check. Screening can continue when optional external collectors are unavailable if benchmark evidence and stored state remain valid; healthy ETW that contradicts the requested placement invalidates the candidate.

Final Keep is different: clean attributable target-only ISR evidence is mandatory.

LatencyPilot keeps these layers separate:

```text
stored interrupt configuration
≠ allocated IRQ/resource assignment
≠ runtime DPC/ISR behavior
```

DPC/ISR counts are context, not CPU cost by themselves; duration and tail behavior also matter.

## Input / USB direction

Read-only source resolves:

```text
Raw Input device
→ PnP ancestry
→ USB hub/port
→ exact xHCI controller
```

After the GPU reaches a verified terminal state, v1 stops the GPU workload and takes one fresh bounded quiet ETW headroom capture. It chooses a separate CPU from remaining interrupt headroom using DPC duration + ISR duration + tail spikes, with counts as context. GPU rank is not reused as a USB rank, and there is no second per-CPU USB benchmark or joint GPU/xHCI score. The selected mutation target is the interrupt-owning xHCI/controller rather than blindly the leaf mouse.

System-changing xHCI affinity remains product-unarmed until GPU Gate A physically proves the shared mutation/recovery substrate and the integrated xHCI runtime-verification path closes. Controller-specific Keep fails closed when multiple present controllers share `USBXHCI.sys` and runtime evidence cannot be attributed to one controller. Raw Input timing remains host-observed consistency context, not click-to-photon latency.

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

tools/
  LatencyPilot.GateAValidation/
  LatencyPilot.PhysicalValidation/

tests/
  LatencyPilot.CriticalTests/
```

Responsibilities stay narrow: Core owns stable domain invariants, Benchmarking owns measurement/decision policy, Platform.Windows owns Windows mechanisms, Persistence owns durable recovery state, Service owns the privileged boundary, GpuBenchmark owns the non-elevated deterministic workload, App owns orchestration/result UX, and CriticalTests owns a deliberately small set of high-blast-radius contracts.

There is no generic privileged shell/process/registry execution surface.

## Safety and recovery

A system-changing feature owns:

```text
snapshot exact original
→ journal experiment
→ apply narrow supported change
→ verify stored/runtime state
→ Keep or exact Restore
→ verify terminal state
```

Cancellation is rollback-biased. Upgrade/uninstall must not remove recovery tools while LatencyPilot still owns an unresolved or retained change.

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
→ restart-canonicalized observer-isolated adaptive v6 GPU Gate A physical search/restart/placement/rollback proof
→ repeat whole search
→ Stop safely + supported recovery exercise
→ real Windows result/accessibility inspection
→ typed allowlisted mutation-specific IPC
→ physical App → Service mutation proof
→ normal-user GPU arming
→ automatic USB/xHCI selection + reversible mutation + physical proof
→ combined reboot/verification/before-after UX
→ signed package/accessibility/recovery closure
```

Hosted CI proves software contracts only. It cannot prove physical interrupt placement, device restart behavior, real WinUI rendering/accessibility, LocalSystem behavior or package/signing behavior.

## What “done” means

Repository/source completion and true v1 completion are different claims. True v1 completion requires physical GPU Gate A, automatic USB/xHCI apply/verify, combined reboot/recovery, final before/after UX, accessibility/runtime validation and signed package/install/upgrade/uninstall evidence on recorded artifacts.

## Contributing / license / security

Read [`AGENTS.md`](AGENTS.md), [`CONTRIBUTING.md`](CONTRIBUTING.md), [`CLA.md`](CLA.md), [`LICENSE`](LICENSE) and [`SECURITY.md`](SECURITY.md) before making changes.