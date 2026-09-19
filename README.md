# LatencyPilot

**Evidence-driven automatic interrupt-affinity tuning for Windows 11.**

LatencyPilot follows one rule:

> **Measure → Apply one supported change → Verify → Keep or Revert**

It is not a registry-tweak pack, debloater, generic FPS booster or a list of settings assumed to be universally better. The current v1 goal is deliberately narrow: automate the useful manual GPU + input/USB interrupt-affinity workflow while preserving exact rollback and proving effective runtime placement.

> [!IMPORTANT]
> LatencyPilot is **0.0.2 pre-alpha**. Public protocol v6 remains observation-only and user-reachable mutation is unavailable. The simplified GPU search source exists, but exact-head CI and physical GPU Gate A must pass before product mutation can be armed; automatic USB/xHCI mutation follows only after that shared substrate is physically proven.

## v1 workflow

```text
preflight / quiet check
→ deep ETW baseline
→ benchmark GPU interrupt affinity on every eligible logical CPU
→ re-test the best up to three
→ choose by 1% low → AVG FPS → p99 (0.1% low rare-tail context)
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
- Simplified GPU auto-affinity search: **source implemented; hosted critical-test CI green; physical Gate A open**
- Final GPU Keep: **internal source requires hard ETW target-only ISR proof**
- USB/input route + xHCI read-only evidence: **source exists**
- Automatic USB CPU selection: **read-only post-GPU recommendation implemented; physical evidence/UI integration open**
- Reversible xHCI mutation: **open; gated behind physical GPU mutation proof**
- Combined reboot verification / before-after UX: **open**
- Public protocol: **v6**, `GetStatus` + `CaptureKernelLatency` only
- `ServiceBoundary.MutationAvailable`: **false**
- Hosted CI: **test-only**

[`PROJECT_STATUS.md`](PROJECT_STATUS.md) is the live execution ledger. [`ROADMAP.md`](ROADMAP.md) is the authoritative v1 phase plan.

## What v1 intentionally does not automate

NIC/RSS mutation, audio affinity, BIOS changes, HAGS changes, MSI-mode toggles, power-plan tuning, generic debloating and a cross-subsystem/Pareto auto-optimizer are outside the v1 automatic path. Existing read-only/future source can remain without gating the v1 product.

## GPU search semantics

The built-in normal-user `LatencyPilot.GpuBenchmark` process performs deterministic D3D12 work. One startup calibration freezes worker mapping, simulation work, command workload, seed and resolution.

Every eligible logical CPU receives:

```text
apply/restart/verify
→ 5 s non-scored warm-up
→ 1 scored 30 s screening run
→ exact rollback
```

After the full logical-CPU sweep, one fresh Original control must remain inside the Original repeatability band or the sweep is discarded. The best three candidates, plus any additional core whose screening 1% low is within max(1%, observed Original noise) of the third-place cutoff, enter two independent re-test rounds. Each round deterministically shuffles finalist order; every candidate receives a fresh apply/restart/warm-up, one 30 s scored run, and exact rollback. A single replacement score is allowed only when needed to form a stable 3-of-up-to-4 cluster; at most one score may be rejected. After finalist re-tests, a second Original control must remain comparable in 1% low, AVG and frame-p99 before any candidate can be kept. Finalists are ranked transparently by:

1. median **1% low**, with <=1% relative differences treated as ties;
2. median **AVG FPS**, also with a 1% tie margin;
3. lower median **frame-p99**, also with a 1% tie margin;
4. median **0.1% low** only when the rare-tail difference exceeds 5%;
5. deterministic passive fallback only if measured metrics remain practically tied.

There is no fixed “must beat Windows default by 3%” rule and no separate SMT/hyperthread refinement phase: eligible SMT siblings are first-class screening candidates. There is no ABBA/BAAB confirmation loop in v1. Windows default is the exact reference/recovery state. Before Keep, the ranked finalists are checked in order against Original and frame guardrails. When Original and finalist each provide at least three usable interrupt-tail runs, GPU-driver DPC/ISR p99 is compared per run using median values and a noise-aware threshold, so a bad top finalist can fall through to the next clean improvement without overreacting to normal tail variance.

The benchmark process intentionally stays alive for the whole search. The renderer uses a three-buffer flip chain with two frame contexts, so it no longer waits for the entire GPU after every Present. After each GPU configuration restart, the D3D12 renderer/device is recreated once. Warm-up and the following scored run reuse that same recreated renderer/device instance, while process identity, frozen workload, seed and worker map remain constant so process-start/JIT/cold-cache effects are not reintroduced for every CPU.

The benchmark records its own controlled wall-clock loop periods. They are a deterministic comparison signal for this workload; they are not claimed to be identical to arbitrary game end-to-end frametime.

## Screening versus final Keep

Screening is resilient: standalone PresentMon and kernel ETW are independent cross-checks/guardrails. If they are unavailable, the absence is explicit and valid controlled benchmark ranking can continue under verified stored state. If healthy ETW proves an off-target candidate, that candidate is invalid.

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

Gate A uses the pinned official standalone PresentMon 2.5.1 collector; users do not need a separately installed PresentMon Service/API for this path.

PresentMon remains an independent frame-cadence cross-check rather than the primary ranking dependency. CSV parsing keeps timing semantics distinct: current `FrameTime` or legacy `MsBetweenPresents` may provide present cadence; `MsBetweenAppStart` is not silently substituted as the same metric. Failed diagnostic CSV retention is bounded.

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

After the GPU winner is fixed, v1 will choose a separate CPU from remaining interrupt headroom using DPC duration + ISR duration + tail spikes, with counts as context. The selected target is the interrupt-owning xHCI/controller, not blindly the leaf mouse.

System-changing xHCI affinity is part of v1 but remains unarmed until GPU Gate A proves the shared privileged mutation/recovery substrate physically.

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

The final one-button v1 workflow will use the ETW engine for a long quiet before/after baseline comparable to the manual 10-minute LatencyMon step. LatencyMon itself is not a dependency.

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

Hosted CI runs the critical test project only. It does not prove WinUI rendering, LocalSystem behavior, physical interrupt placement, GPU restart timing, installer/package behavior, signing or accessibility. Those require owner-local physical evidence.

No new permanent test method should be added for every implementation detail. Temporary TDD characterization tests are removed once their behavior is represented in canonical tests.

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
→ typed mutation-specific IPC
→ physical App → Service mutation proof
→ normal-user GPU arming
→ automatic USB/xHCI selection + reversible mutation + physical proof
→ combined reboot/verification/before-after UX
→ signed package/accessibility/recovery closure
```

See [`PROJECT_STATUS.md`](PROJECT_STATUS.md) for current blockers.

## What “done” means

Repository/source completion and true v1 completion are separate claims. True v1 requires physical GPU Gate A, automatic USB/xHCI apply/verify, reboot/recovery, final before/after UX, accessibility/runtime validation and signed package/install/upgrade/uninstall evidence on recorded artifacts.

## Contributing / license / security

Read [`AGENTS.md`](AGENTS.md), [`CONTRIBUTING.md`](CONTRIBUTING.md), [`CLA.md`](CLA.md), [`LICENSE`](LICENSE) and [`SECURITY.md`](SECURITY.md) before making changes.

## 2026-09-19 audit-closure semantics

LatencyPilot now treats automatic optimization as a one-at-a-time experiment pipeline: **Original measurement → GPU affinity → conservative MSI → primary-input/xHCI → final verification → report**. A stage that is NotReady, inconclusive, or requires reboot stops the pipeline; intent is never treated as activation proof.

MSI mutation is deliberately narrow: `MSISupported` may be enabled only when authoritative stored state makes the target applicable. `MessageNumberLimit` and interrupt priority are preserved/observed, not tuned automatically. xHCI affinity requires one explicit primary Raw Input identity, one exact USB route, clean capture evidence, and reversible journal ownership.

`Restore original settings` replays retained LatencyPilot changes newest-first from exact snapshots. It does **not** claim to restore Windows defaults. Public mutation remains fail-closed (`MutationAvailable = false`) until exact-revision physical GPU/MSI/xHCI validation is recorded; hosted CI proves source contracts only.
