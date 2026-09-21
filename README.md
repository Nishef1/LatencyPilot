# LatencyPilot

**Evidence-driven automatic interrupt-affinity tuning for Windows 11.**

LatencyPilot follows one rule:

> **Measure → Apply one supported change → Verify → Keep or Revert**

It is not a registry-tweak pack, debloater, generic FPS booster, or a list of settings assumed to be universally better. The current v1 product goal is deliberately narrow: automate a reversible GPU + input/USB interrupt-affinity workflow while preserving exact rollback and proving effective runtime placement.

> [!IMPORTANT]
> LatencyPilot is **0.0.2 pre-alpha**. Public protocol v6 remains observation-only and user-reachable mutation is unavailable. The current GPU search method is implemented in source, but physical GPU Gate A must pass before product mutation can be armed. Automatic USB/xHCI mutation follows only after the shared mutation/recovery substrate is physically proven.

## Current authority

- Product scope, mutation ownership, recovery and v1 sequencing: [`docs/adr/0006-simple-auto-interrupt-affinity-v1.md`](docs/adr/0006-simple-auto-interrupt-affinity-v1.md)
- GPU measurement/search/ranking: [`docs/adr/0007-paired-local-control-gpu-affinity-v2.md`](docs/adr/0007-paired-local-control-gpu-affinity-v2.md)
- Live execution/evidence state: [`PROJECT_STATUS.md`](PROJECT_STATUS.md)
- Required completion outcomes: [`ROADMAP.md`](ROADMAP.md)

ADR 0007 supersedes only the GPU measurement, screening and ranking portions of ADR 0006.

## v1 workflow

```text
preflight / quiet check
→ deep ETW baseline
→ bounded Original qualification
→ paired GPU screening: Original before → Candidate → Original after
→ physical-core representatives → promising SMT siblings → up to 3 finalists
→ three independent 30 s local pairs per finalist
→ final ETW target-only GPU ISR placement proof
→ measure remaining per-CPU interrupt headroom
→ Raw Input → USB → exact xHCI controller
→ choose/apply a separate xHCI interrupt CPU
→ reboot once when required
→ verify GPU + xHCI runtime placement
→ show before/after evidence
→ Restore original settings
```

NIC/RSS mutation, audio affinity, BIOS changes, HAGS changes, MSI-mode toggles, power-plan tuning, generic debloating and a generic cross-subsystem optimizer are outside the v1 automatic path.

## Current state

- Scope/safety/recovery foundations: **source complete**
- Read-only ETW/topology/device evidence: **source substantially complete; physical closure remains**
- GPU paired-v2 search: **implemented in source; physical Gate A open**
- Gate A result UX: **authoritative report → evidence ZIP → Overview result implemented; real render/accessibility inspection remains**
- Final GPU Keep: **internal source requires hard ETW target-only ISR proof**
- USB/input route + xHCI read-only evidence: **source exists**
- Automatic USB CPU selection: **read-only recommendation source exists; physical/product integration remains**
- Reversible xHCI mutation substrate: **internal/product-gated**
- Combined reboot verification / before-after product UX: **open**
- Public protocol: **v6**, `GetStatus` + `CaptureKernelLatency` only
- `ServiceBoundary.MutationAvailable`: **false**
- Hosted CI: **software-contract evidence only**

## GPU paired-v2 measurement

New GPU evidence uses method id `gpu-affinity-benchmark-v2` and report schema `latencypilot-gpu-auto-affinity-report-v2`. Historical v1 reports remain historical and are never reinterpreted as v2.

### 1. Original qualification

Before any candidate mutation:

```text
5 s non-scored Original warm-up
→ 10 s scored Original observations
→ require a 3-observation 1%-low cluster
   preferred band: ±3% of cluster median
   bounded recovery after observations 4/5: up to ±6%
→ no valid cluster after 5 scored observations = retain Original, no candidate mutation
```

Every observation remains in the audit trail. The accepted qualification cluster establishes a usable measurement substrate; it is not reused as a candidate pair control.

### 2. Direct local pairs

After qualification, LatencyPilot captures a fresh Original control and screens with chained local pairs:

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

Positive effect always means improvement-directed movement. Raw observations are preserved unchanged; the optimizer does not manufacture pseudo-normalized FPS values.

### 3. Pair stability

The pair drift budget is:

```text
clamp(max(6%, 2 × accepted Original 1%-low noise), 6%, 10%)
```

A pair above that budget is unstable and cannot rank the candidate. One fresh retry is allowed. A second unstable attempt makes that candidate inconclusive. Two consecutive candidates that exhaust the retry stop the search safely and retain exact Original.

Structural evidence failures remain fail-closed immediately.

### 4. Full-search strategy

Full search does **not** blindly score every SMT sibling for the full tournament:

1. **Stage A — physical-core representatives:** screen one eligible logical processor per physical core using current CPU-set eligibility, lower observed pressure, then deterministic processor-number fallback.
2. **Stage B — sibling refinement:** keep the best two physical-core hypotheses, plus a third only when it is within the 1% practical-equivalence margin; screen any still-untested eligible siblings on those cores.
3. **Stage C — finalists:** advance the best two logical CPUs, plus one additional CPU only when it is within the same 1% margin; hard cap three finalists.

CPU0 is not globally banned and no even/odd SMT assumption is permitted.

Short-screen scored windows are exactly **10 seconds**. The primary ranking signal is paired 1%-low effect; AVG and frame p99 are guardrail/context metrics and 0.1% low remains diagnostic.

### 5. Finalist confirmation

Each finalist must obtain **three independent valid 30-second local pairs**. Finalist order is deterministically shuffled per round.

A finalist is improvement-capable only when:

- at least two of three 1%-low pair effects are positive;
- median paired 1%-low effect exceeds `max(1%, median finalist pair movement)`;
- no valid pair shows a material primary regression beyond that floor;
- AVG, frame-p99 and sufficiently supported GPU-driver interrupt-tail guardrails do not materially regress.

Finalists within one percentage point are a **practical tie**. Passive pressure/topology ordering may choose the operational target inside a tie, but the UI must not claim that target proved faster than tied peers.

## Final Keep is stricter than ranking

A measured performance winner is not enough. Before Keep, LatencyPilot applies the selected candidate once more and requires:

```text
stored winner state verified
clean kernel ETW
lost events = 0
attributable GPU ISR samples > 0
ISR target = selected logical CPU
resolved off-target ISR = 0
terminal stored state verified
```

If final runtime placement cannot be proved, exact Original is restored.

Microsoft documents interrupt affinity as a device affinity policy and `AssignmentSetOverride` as a `KAFFINITY` processor mask when the specified-processors policy is used. LatencyPilot treats that stored policy as configuration evidence only; runtime ETW placement remains a separate verification layer.

## Diagnostic GPU scopes

The developer UI also supports bounded diagnostic paths:

- **Selected CPUs · restore Original** — runs real paired screening only on the selected CPUs, skips the machine-wide finalist tournament, never Keeps, always restores Original and cannot close Gate A.
- **Original only · no system changes** — captures the bounded Original-only sample set with no affinity mutation or device restart.

These are methodology/debugging tools, not shortcuts around the full physical gate.

## Gate A result evidence

After a report passes schema/session/source validation, LatencyPilot attempts to package the complete session into a shareable ZIP and builds the Overview result from the authoritative report.

The result surface keeps these layers distinct:

```text
raw scored trials / local pairs
!= persisted decision rank/finalist authority
!= verified terminal machine state
```

It shows:

- terminal Keep/Restore/practical-tie outcome;
- `Evidence eligible` versus development evidence without implying physical Gate A is already closed;
- direct `Original before → Candidate → Original after` pair evidence;
- paired effect, local control movement, drift budget and pair verdict;
- candidate/finalist comparison using persisted decision authority;
- runtime placement/final-state evidence;
- evidence ZIP/session/raw-report actions.

Presentation code must never re-rank shuffled execution data or turn a non-Keep candidate into a winner.

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

After a verified GPU Keep, v1 can choose a separate CPU from remaining interrupt headroom using DPC duration + ISR duration + tail spikes, with counts as context. The selected mutation target is the interrupt-owning xHCI/controller rather than blindly the leaf mouse.

System-changing xHCI affinity remains product-unarmed until GPU Gate A physically proves the shared mutation/recovery substrate and the integrated xHCI runtime-verification path closes.

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
→ paired-v2 GPU Gate A physical search/restart/placement/rollback proof
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