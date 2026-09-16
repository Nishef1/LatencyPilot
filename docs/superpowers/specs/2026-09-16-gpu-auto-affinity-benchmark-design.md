# GPU Auto-Affinity Benchmark and One-Click Optimizer Design

Status: **Approved final design input for the next implementation session**  
Date: 2026-09-16  
Baseline repository state reviewed: `main` at `79c5667b7b269756fd78db52da8a6e3687f5fce9`, Tests #870 successful.

## Purpose

LatencyPilot's GPU optimizer must answer a practical question automatically:

> Which logical processor is the best interrupt-affinity target for the active GPU on this machine under a representative, repeatable graphics workload, and is that target measurably better than the Windows/original state without collateral regression?

The user experience is one action. Internally, the product must still preserve exact provenance, bounded candidate search, real runtime ISR-placement proof, rollback/recovery ownership and a final evidence report.

This design supersedes the passive-only candidate-selection assumptions in the older GPU Go Mode plan. It does not weaken Gate A/B/C/D safety ordering.

---

## Research basis checked on 2026-09-16

### AutoGpuAffinity / liblava prior art

`VladosVladi/AutoGpuAffinity` benchmarks each core with liblava, recommends three trials of roughly 30 seconds and tells users to repeat whole runs to establish reproducibility. Its core product insight is useful: **do not choose GPU interrupt affinity from idle CPU distribution alone; induce a repeatable graphics workload and test candidate processors under load.**

We will reuse that experiment shape, not the dependency.

### Why not ship liblava as the benchmark engine

- liblava is a maintained MIT C++23/Vulkan framework and is suitable for graphics tooling/profiling.
- LatencyPilot is Windows 11-only and already has DirectX/PresentMon/ETW-specific evidence paths.
- PresentMon documents reduced CPU-frame instrumentation accuracy for OpenGL/Vulkan workloads compared with DirectX paths.
- Shipping liblava would add a separate C++23/Vulkan dependency stack while `SYSTEM_DESIGN.md` currently states that native C++ is not a baseline dependency.

Conclusion: **do not add liblava to the product.** Use it as prior-art methodology only.

### Direct3D 12 in 2026

Microsoft's Direct3D 12 Multithreading sample was refreshed in July 2026 and explicitly demonstrates building command lists on multiple CPU threads. Direct3D 12 naturally supports the kind of multi-core CPU-side render workload required here.

Microsoft's D3D12 timing documentation, updated in August 2026, documents command-queue timestamp frequency, GPU timestamp queries and CPU/GPU clock calibration. These timestamps are better suited than PresentMon GPU-active duration as the benchmark's own GPU-work calibration signal.

### Vortice.Windows

`Vortice.Direct3D12` 3.8.3 is a stable MIT .NET binding released in 2026, targets .NET 9/.NET 10 and Windows SDK 10.0.26100.0, matching LatencyPilot's current runtime/SDK direction.

Conclusion: **build the benchmark in C#/.NET 10 using Vortice.Direct3D12 + Vortice.DXGI.** Keep HLSL shader bytecode precompiled/embedded so runtime shader compilation does not become benchmark noise.

### PresentMon in 2026

PresentMon v2.5.1 is the current release reviewed. v2.5.0 included major middleware changes and fixes for CPU busy/wait calculations and percentile calculation, but its binary distribution was withdrawn because of a shared-service compatibility conflict. v2.5.1 restored a supported binary distribution and fixed FPS percentile ordering and API compatibility regressions.

Product consequences:

- audit/pin the existing PresentMon native/API integration to a known compatible v2.5.1-or-later build before the new benchmark becomes authoritative;
- consume raw per-frame evidence and compute LatencyPilot percentiles through `Percentiles` rather than trusting an external precomputed percentile;
- do not use `msGPUActive` as the sole benchmark load/decision metric because PresentMon still documents reduced GPU execution-metric accuracy with Hardware Accelerated GPU Scheduling enabled;
- use D3D12 timestamp queries for internal GPU-work calibration and PresentMon for presentation/frame-delivery evidence.

### Windows topology and interrupt semantics

Microsoft documents interrupt affinity as a **per-device** processor set. `AssignmentSetOverride` is a KAFFINITY mask for the device's interrupt policy. CPU Sets expose physical-core/cache/NUMA/efficiency-class and allocation state. Windows 11 spans application threads across processor groups by default, but the current LatencyPilot device-affinity writer is still a single-group KAFFINITY implementation, so multi-group mutation remains fail-closed until a group-correct device-affinity model exists.

CPU0 is not universally bad. DPC/ISR interference can starve ordinary threads, but which processor is best is machine/workload dependent and must be measured.

---

## Contradiction and debt matrix

### C1 — System-wide CPU-busy drift is a hard GPU eligibility gate

Current behavior: `workload-stability-v1` makes system CPU busy early/late drift a hard blocker. An owner-local Gate A run was rejected solely because CPU busy drift was 32.4% while browser activity changed, before any GPU candidate was prepared.

Problem: general CPU activity is not equivalent to GPU workload invalidity. Normal browser/background activity can vary CPU busy without invalidating the GPU target, and some browser activity can use the GPU. A GPU optimizer should detect **GPU/benchmark-specific** contamination rather than hard-fail from one system-wide percentage.

Decision:

- retain `workload-stability-v1` for the existing steady Real-world evidence product;
- stop using general CPU-busy drift as the hard readiness gate for the new automatic GPU benchmark;
- introduce a separate `gpu-affinity-benchmark-v1` validity contract based on benchmark process continuity, exact GPU identity, ETW integrity, runtime GPU ISR attribution, control-run drift and benchmark self-consistency;
- keep system CPU busy as context and candidate/contamination evidence, not a standalone rejection reason.

### C2 — Passive CPU pressure biases candidate generation

Current behavior: `ProcessorPressureEvidenceBuilder` ranks processors from the observed share of DPC+ISR events in existing workload windows. On ordinary Windows desktops, CPU0 commonly carries more OS/interrupt activity.

Problem: passive idle/light-desktop distribution answers "which CPU is quiet now," not "which CPU produces the best graphics/frame behavior when GPU interrupts are assigned there under load."

Decision: passive pressure becomes **seed/context only**. Candidate winner selection is determined by active candidate-by-candidate benchmark evidence.

### C3 — Default planner tests only four physical cores

Current behavior: `GpuAffinityCandidatePlanner.DefaultMaximumCandidates = 4`, with one logical processor selected per physical core.

Problem: an 8-core/16-thread CPU can have half its physical cores excluded before any active experiment.

Decision:

- automatic benchmark screens every eligible physical core when physical-core count <=16;
- above 16 physical cores, use topology-stratified bounded screening with representatives across efficiency class, NUMA/cache locality and passive pressure, maximum 16 physical-core candidates in v1;
- after the winning physical core is found, if SMT is present, compare both logical siblings on that physical core before final confirmation;
- CPU0 remains eligible; it is never automatically excluded.

### C4 — Existing one-click Gate A helper tests only rank-1

Current behavior: the owner-only helper plans candidates and selects `rank=1`, then performs one candidate apply/placement/rollback exercise.

Problem: this proves the mutation substrate but does not implement Auto GPU Affinity.

Decision: keep this narrow path as **Developer Gate A substrate validation**. The new benchmark/orchestrator is a separate candidate-search layer. Do not pretend Gate A rank-1 validation is the final optimizer.

### C5 — User-facing mutation exists before canonical Gate D wording permits it

Current docs say system-changing UI remains absent until Gate D. Current dev checkout contains `Validate GPU · one click`, which invokes an elevated Gate A helper.

Decision:

- preserve the owner convenience, but explicitly classify it as a **development-only Gate A surface**;
- it must only appear for a development checkout / owner-validation build and must not ship in installer/portable product payload before Gate D;
- rename it to make its role unambiguous (for example `Run GPU Gate A`), not `Optimize GPU`;
- the eventual `Auto-optimize GPU` product button is armed only after Gate B typed IPC + Gate C physical IPC proof + Gate D authorization.

### C6 — App disappears and gives no live progress

Current owner flow calls `AppWindow.Hide()`, removing normal window/taskbar representation while the helper runs.

Decision:

- main App minimizes instead of disappearing;
- show a compact progress window while an optimizer/validation session runs;
- progress is stage/candidate based, never a fake timer;
- show current candidate processor, physical core, candidate X/Y, current phase, elapsed time, last completed result and `Stop safely`;
- Stop must transfer into rollback/recovery ownership before the session can terminalize.

### C7 — One-click product depends on a manually steady game scene

Current `RealWorld` methodology requires the user to prepare and maintain one warmed steady scene. This is valid for controlled A/B work but conflicts with a truly automatic optimizer.

Decision: automatic GPU affinity uses a **built-in deterministic benchmark** as the default candidate-search authority. Real-game evidence becomes an optional/secondary confirmation mode, not the prerequisite for screening every processor.

### C8 — Phase-changing benchmark policy vs new synthetic benchmark

Current methodology says a scripted phase-changing benchmark is not a candidate source under the five-equivalent-window RealWorld method.

Decision: do not force the built-in benchmark into `baseline-quality-v2`/five steady windows. Give it a separate whole-run method identity and compare repeated complete trials under fixed workload parameters.

### C9 — PresentMon version/metric assumptions

New optimizer work must not unknowingly depend on old percentile behavior or HWS-sensitive GPU duration.

Decision: add a concrete PresentMon compatibility audit/pin task, use raw frames + LatencyPilot's own percentile implementation, and use D3D12 GPU timestamps for benchmark workload calibration.

### C10 — Test budget is nearly full

Current permanent suite is 18, owner-authorized maximum 20.

Decision: add at most one durable benchmark-policy test file if existing files cannot safely own the contract. Prefer extending `GpuAffinityCandidatePlannerTests`, `OptimizerSafetyTests` and existing Gate A/provenance tests. Hardware benchmark repetitions are physical validation evidence, not permanent unit-test methods.

---

## Final architecture

```text
LatencyPilot App (normal user)
    │
    ├─ Optimizer progress UI
    │
    ├─ launches LatencyPilot.GpuBenchmark (normal-user workload process)
    │       ├─ D3D12 renderer
    │       ├─ deterministic multi-core command-list workers
    │       ├─ GPU timestamp calibration
    │       └─ machine-readable per-trial telemetry
    │
    ├─ Benchmarking policy/orchestration
    │       ├─ candidate pool
    │       ├─ screen all physical cores (bounded <=16)
    │       ├─ SMT sibling refinement
    │       ├─ Pareto/guardrail interpretation
    │       └─ original-vs-finalist confirmation
    │
    └─ mutation executor
            ├─ TODAY / Gate A: owner-only elevated validation harness
            └─ PRODUCT after Gate B/C: typed mutation-specific Service IPC
```

`LatencyPilot.GpuBenchmark` is a workload generator and telemetry source. It does **not** mutate interrupt affinity and never requires administrator rights in the product architecture.

---

## Built-in D3D12 workload

### Rendering workload

- windowed D3D12 application, default 1280×720 render surface;
- vsync disabled; use supported tearing path when available and record the chosen presentation mode;
- precompiled embedded shaders;
- deterministic scene data and deterministic per-frame sequence;
- fixed resource set after warm-up; no streaming/network dependency;
- no per-frame allocations after warm-up;
- use multiple D3D12 command lists recorded concurrently.

### CPU workload

The workload must deliberately engage multiple physical cores so ordinary CPU0 background ownership does not dominate the experiment.

- one render/command worker per eligible physical core up to the v1 bound;
- workers use stable CPU-set/thread-affinity placement for benchmark repeatability, not to manipulate the GPU interrupt target;
- each worker receives a deterministic command-list partition plus a bounded deterministic CPU simulation/update payload;
- work is frame-synchronized so workers run in game-like bursts rather than a 100% synthetic busy loop;
- worker mapping remains identical across GPU affinity candidates.

The benchmark must never alter its CPU worker layout simply because the GPU interrupt candidate changes. Otherwise candidate identity and workload shape would be confounded.

### Adaptive calibration

Hardware varies too much for a fixed draw count to be meaningful. Before measured trials:

1. warm renderer and pipelines;
2. use D3D12 timestamp queries to measure GPU work;
3. adjust scene/draw workload until it reaches a bounded mid/high GPU load rather than an idle or permanently saturated state;
4. adjust CPU-side command/simulation work to ensure multiple physical cores have meaningful but non-saturated work;
5. freeze all calibrated parameters for the entire candidate session and serialize them in the report.

Calibration may adapt before the experiment. **It may not change between candidates.**

---

## Candidate search

### Stage 0 — original/default control

Capture exact original GPU interrupt state. Original Windows state is always a real candidate/control. If no tested candidate robustly beats it, restore/keep original and report `No measurable improvement`.

### Stage 1 — physical-core screening

- candidate set: one logical representative per eligible physical core;
- <=16 physical cores: test all;
- >16: topology-stratified maximum 16 in v1;
- deterministic shuffled order with the seed serialized in the report;
- two screening trials per candidate;
- each trial is a complete fixed benchmark interval, not a slice of a changing run;
- exact rollback to original/candidate transition semantics remain journal-owned.

Passive pressure is a tie-break/ordering hint only; it cannot decide the winner before active trials.

### Stage 2 — SMT refinement

If the winning physical core exposes two logical siblings, explicitly test both sibling processors under the identical benchmark. This avoids assuming sibling 0 and sibling 1 are equivalent for interrupt contention.

### Stage 3 — finalist confirmation

Reuse the existing balanced original/candidate concept. The final candidate must survive a balanced interleaved confirmation (existing ABBA+BAAB may be retained unless physical evidence justifies a versioned replacement).

A screening run can nominate. Only confirmation can recommend Keep.

---

## Benchmark timing policy v1

Initial physical-validation defaults:

```text
warm-up / adaptive calibration: 10–20 s, until calibration freezes
screening trial:                15 s
screening repetitions:          2 per candidate
SMT sibling refinement:         2 × 15 s per sibling
final confirmation:             existing 8-run ABBA+BAAB, >=30 s/run
inter-trial settle:             2–5 s bounded
```

These are v1 defaults, not immutable truths. Physical repeatability evidence may justify a versioned timing change. The product must show estimated remaining time and real progress because a thorough session can take several minutes.

---

## Metrics and decision policy

No weighted hidden score.

### Validity / hard gates

- exact target GPU identity remains stable (PnP + DXGI/LUID + PresentMon identity where applicable);
- exact requested stored affinity is verified before and after a candidate capture;
- clean ETW integrity;
- benchmark process/session continuity;
- direct GPU-driver ISR attribution exists;
- candidate placement proof: at least one resolved GPU ISR on target processor and zero resolved GPU ISR off target;
- benchmark workload parameters and worker placement unchanged across candidates;
- no sleep/suspend/device reset/driver change;
- original/control runs remain comparable enough for the benchmark method.

### Primary performance vector

Computed from raw frame/trial evidence using LatencyPilot's canonical percentile estimator:

- frame-time p99 — lower is better;
- frame-time p95 — lower is better;
- 1% low FPS / equivalent low-tail throughput derived consistently from raw frames — higher is better;
- GPU-driver DPC/ISR total service time and relevant tail behavior — lower is better when comparable.

### Guardrails

- no frame delivery/drop regression;
- no material total DPC/ISR regression;
- no benchmark CPU-worker regression large enough to explain an apparent graphics win;
- no display/GPU continuity failure;
- no active GPU-backed audio endpoint discontinuity caused by device restart;
- candidate processor must not become pathologically saturated.

### Context only

- system-wide CPU busy;
- browser/Discord/background-process activity;
- original CPU0 interrupt concentration;
- PresentMon GPU-active telemetry under HWS;
- temperatures/clocks/power when available but not authoritative on every vendor.

Context can trigger a repeat when control evidence shows contamination. It cannot independently manufacture a winner or reject a clean GPU-specific experiment.

### Winner semantics

Use Pareto/guardrail semantics. Do not collapse frame pacing, ISR placement and CPU pressure into one arbitrary score.

If several candidates are statistically/practically equivalent, prefer the simplest/safer state (original first; otherwise lower measured contention with deterministic topology tie-breaks). Never force a change merely because the tool was asked to optimize.

---

## Background activity and contamination

Normal desktop activity is expected. The product should not require users to close every program.

Instead:

- benchmark-generated workload is fixed;
- background CPU usage is recorded as context;
- original/control trial drift is the main contamination detector;
- if a trial is clearly contaminated, automatically repeat the block once;
- if control evidence remains unstable after bounded retry, report `Inconclusive` rather than guessing;
- browser activity alone is not a hard failure unless it changes GPU/process identity or makes control evidence non-comparable.

This preserves real-world usability without pretending uncontrolled noise does not matter.

---

## Progress UX

During automatic GPU affinity:

```text
GPU Auto Affinity
Candidate 3 / 8 · CPU 6 · Physical core 3
Screening pass 2 / 2
██████████████░░░░  63%

Frame p99        8.4 ms
1% low           116 FPS
GPU ISR target   confirmed
Candidate CPU    57% busy

Last: CPU 4 — No measurable improvement
Elapsed 04:12 · Estimated remaining 02:35
[ Stop safely ]
```

Requirements:

- main App minimizes rather than disappears;
- compact progress window remains visible and accessible;
- progress derives from actual planned/completed stages and trials;
- show current CPU/core, candidate X/Y and real current phase;
- `Stop safely` cancels future work but first enters owned rollback/recovery;
- final UI shows original vs winner raw deltas and the saved JSON report path.

---

## Evidence artifacts

Do not overload `latencypilot-evidence-v9` with a different experiment shape.

Add dedicated artifacts:

```text
latencypilot-gpu-benchmark-v1
latencypilot-gpu-auto-affinity-report-v1
```

The benchmark artifact records:

- benchmark version/method;
- exact source revision;
- GPU/driver/Windows/topology identity;
- D3D12 feature/presentation mode;
- frozen workload calibration parameters;
- worker-to-processor mapping;
- trial role/order/seed;
- raw-frame reference or bounded raw representation;
- D3D12 GPU timestamp series/aggregates;
- ETW/PresentMon capture IDs;
- validity reasons.

The auto-affinity report records every candidate, every rollback, every result, final recommendation, exact original/final machine state and recovery status.

---

## Safety and Gate A/B/C/D

The benchmark may be implemented before Gate A because it is read-only workload generation. Product mutation still follows the safety ladder.

### Before Gate A closes

- benchmark engine may run;
- candidate policy may be interpreted;
- dev-only `Run GPU Gate A` may use the existing owner helper;
- installer/portable build must not expose product mutation.

### Gate A

Use the built-in benchmark to make the physical substrate proof more representative, but Gate A still fundamentally proves apply/restart/runtime placement/exact rollback/recovery.

### Gate B

Add mutation-specific typed/allowlisted Service commands. No generic registry/shell/process primitive.

### Gate C

Physically prove App → Service benchmark+mutation orchestration and interruption recovery.

### Gate D

Only then expose product `Auto-optimize GPU` to normal users.

---

## Package/dependency decision

Add centrally managed packages only to the benchmark project:

- `Vortice.Direct3D12` stable 3.8.3;
- `Vortice.DXGI` matching 3.8.3;
- add `Vortice.Dxc` only if build-time shader tooling requires it; do not compile shaders during measured runtime.

Do not add liblava, Vulkan SDK, a new C++ project, OCAT, or a second graphics telemetry stack.

PresentMon remains the existing graphics evidence provider, after its current native/API integration is audited and pinned/validated against v2.5.1-or-later behavior.

---

## Required canonical-document reconciliation

Implementation is incomplete until these files agree with this design:

- `SYSTEM_DESIGN.md`
- `ROADMAP.md`
- `PROJECT_STATUS.md`
- `docs/BENCHMARK_METHODOLOGY.md`
- `docs/OPTIMIZER_TARGET_GRAPH.md`
- `docs/PHASE3_PHYSICAL_VALIDATION.md`
- old GPU Go Mode plans/specs must be marked historical/superseded where they conflict.

The main semantic changes are:

1. steady RealWorld readiness remains valid, but is no longer the default automatic GPU candidate-search mechanism;
2. automatic GPU affinity receives a dedicated deterministic benchmark method;
3. dev-only Gate A one-click is explicitly separated from product one-click arming;
4. passive processor pressure seeds candidate order but does not choose the winner;
5. all eligible physical cores are actively screened within the v1 bound, followed by SMT sibling refinement;
6. progress is first-class product evidence/UX, not a hidden helper process.

---

## Definition of done for this tranche

Source tranche is complete only when:

- deterministic D3D12 multi-core benchmark exists and produces auditable artifacts;
- workload calibration is frozen before candidate comparison;
- all eligible physical cores are screened within the bound;
- SMT sibling refinement works;
- original/default remains a control and can win;
- GPU ISR placement is verified per candidate;
- candidate decision uses raw-frame/ETW evidence and guardrails without hidden score;
- background CPU drift alone no longer hard-blocks clean GPU benchmark evidence;
- progress UI shows real candidate/stage/progress and safe cancellation;
- dev Gate A surface cannot ship as product mutation before Gate D;
- exact-head test-only CI is green;
- owner-local RTX 3070 run completes a full candidate search and returns exact original state;
- a repeated owner-local run selects an equivalent winner or reports a defensible Inconclusive rather than producing unstable arbitrary choices.

Product one-click is complete only after Gate B/C/D additionally pass physically.

---

## References reviewed

- Microsoft Learn — Interrupt Affinity and Priority
- Microsoft Learn — CPU Sets / SYSTEM_CPU_SET_INFORMATION / Processor Groups
- Microsoft Learn — CPU Analysis / DPC-ISR Interference
- Microsoft Learn — Direct3D 12 Multithreading sample, refreshed July 2026
- Microsoft Learn — Direct3D 12 Timing, updated August 2026
- Microsoft DirectX-Graphics-Samples / DirectX-Specs
- Vortice.Windows / Vortice.Direct3D12 3.8.3 (2026)
- GameTechDev PresentMon v2.5.1 release notes (June 2026)
- GameTechDev PresentMon README, including Vulkan/OpenGL and HWS measurement caveats
- VladosVladi/AutoGpuAffinity methodology
- liblava/liblava project and license
- Microsoft Windows Performance Lab testing methodology
