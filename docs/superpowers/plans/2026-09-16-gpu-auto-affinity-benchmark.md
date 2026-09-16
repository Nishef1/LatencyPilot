# GPU Auto-Affinity Benchmark Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace passive/steady-scene-only GPU affinity selection with a deterministic one-click Direct3D 12 multi-core benchmark that actively tests every bounded physical-core candidate, refines the winning SMT sibling, verifies real GPU ISR placement, preserves rollback/recovery and exposes live progress.

**Architecture:** Add a normal-user `LatencyPilot.GpuBenchmark` process built on Vortice.Direct3D12/DXGI. The benchmark owns only deterministic workload generation and telemetry; it never mutates hardware. `LatencyPilot.Benchmarking` owns candidate policy and evidence interpretation. Before Gate D, mutation remains owner-only through the Gate A validation harness; after Gate B/C, the App uses typed Service mutation commands. Raw PresentMon + ETW + D3D12 timestamps provide evidence, with the original Windows state retained as the control candidate.

**Tech Stack:** C# 14, .NET 10 LTS, Windows 11 x64, Vortice.Direct3D12 3.8.3, Vortice.DXGI 3.8.3, existing TraceEvent/PresentMon/SQLite/WinUI 3 stack.

**Spec:** `docs/superpowers/specs/2026-09-16-gpu-auto-affinity-benchmark-design.md`

## Global Constraints

- Work directly on `main`; preserve unrelated work.
- Public protocol remains v6/read-only until Gate A physically passes and Gate B begins.
- `ServiceBoundary.MutationAvailable` remains `false` before Gate D.
- `LatencyPilot.GpuBenchmark` must never require elevation or mutate affinity.
- Do not add liblava, Vulkan SDK, OCAT or a C++ project.
- Use raw frame evidence and LatencyPilot's canonical percentile estimator; do not trust external precomputed percentile values as authoritative.
- Do not use PresentMon `msGPUActive` as the sole workload-calibration signal under HWS; use D3D12 timestamp queries.
- CPU0 is eligible; never hard-ban it.
- Original/default Windows affinity is a real control candidate and wins when no tested candidate is measurably better.
- Automatic benchmark readiness must not hard-fail only because system-wide CPU busy drifted.
- Multi-group GPU mutation stays fail-closed until the device-affinity writer becomes group-correct.
- Permanent deterministic tests remain <=20; current count is 18. Prefer extending existing GPU/optimizer tests over adding methods.
- Hosted CI remains test-only; owner-local App/Service/benchmark/hardware behavior requires physical evidence.
- Every mutation-owned failure/cancel path must end in exact rollback/recovery or explicit unresolved state.

---

### Task 1: Reconcile authority and isolate the existing dev Gate A button

**Files:**
- Modify: `src/LatencyPilot.App/GateAValidationExperience.cs`
- Modify: `SYSTEM_DESIGN.md`
- Modify: `ROADMAP.md`
- Modify: `PROJECT_STATUS.md`
- Modify: `docs/BENCHMARK_METHODOLOGY.md`
- Modify: `docs/OPTIMIZER_TARGET_GRAPH.md`
- Modify: `docs/PHASE3_PHYSICAL_VALIDATION.md`
- Modify: `docs/superpowers/plans/2026-09-14-gpu-go-mode-source-completion.md`

**Interfaces:**
- Produces explicit distinction between `Run GPU Gate A` (development-only owner validation) and future `Auto-optimize GPU` (Gate D product feature).
- Does not change mutation availability.

- [ ] **Step 1: Extend an existing source-provenance/Gate A test with the development-surface invariant**

Add assertions in the existing durable test that owns Gate A/provenance so the UI helper remains gated by a development checkout and is not described as a product optimizer. Keep the permanent method count unchanged.

```csharp
Assert.IsFalse(ServiceBoundary.MutationAvailable);
Assert.AreEqual(6, ProtocolVersion.Current);
```

Also assert any new `GateAExperienceMode`/copy helper returns development-only semantics.

- [ ] **Step 2: Run Tests and confirm the new assertion is RED if implementation is missing**

Run only hosted Tests per project policy. Expected failure must be the missing/incorrect development-surface contract, not unrelated code.

- [ ] **Step 3: Rename/reframe the current button**

Change visible copy from `Validate GPU · one click` to an explicit owner/development label such as:

```text
Run GPU Gate A
```

Keep the existing repository-root requirement and make it explicit in code with a small helper such as:

```csharp
internal static bool IsDevelopmentGateAAvailable(string? repositoryRoot) =>
    !string.IsNullOrWhiteSpace(repositoryRoot);
```

Do not add a product `Optimize` button yet.

- [ ] **Step 4: Reconcile canonical docs**

Document the final distinction:

```text
steady RealWorld = existing controlled/manual evidence product
gpu-affinity-benchmark-v1 = automatic synthetic candidate-search method
Run GPU Gate A = dev-only substrate validation
Auto-optimize GPU = blocked until Gate D
```

Mark the 2026-09-14 GPU Go Mode plan as historical where it conflicts with this plan.

- [ ] **Step 5: Verify Tests GREEN and commit**

Commit: `docs(gpu): reconcile auto-affinity authority`

---

### Task 2: Add the benchmark project and managed Direct3D 12 dependencies

**Files:**
- Modify: `Directory.Packages.props`
- Modify: `LatencyPilot.slnx`
- Create: `src/LatencyPilot.GpuBenchmark/LatencyPilot.GpuBenchmark.csproj`
- Create: `src/LatencyPilot.GpuBenchmark/Program.cs`
- Create: `src/LatencyPilot.GpuBenchmark/BenchmarkOptions.cs`
- Create: `src/LatencyPilot.GpuBenchmark/BenchmarkProtocol.cs`

**Interfaces:**
- Produces executable `LatencyPilot.GpuBenchmark.exe`.
- Accepts deterministic session options and emits JSON-lines progress/results to stdout.
- No Service/Persistence reference and no mutation capability.

- [ ] **Step 1: Add centrally managed packages**

```xml
<PackageVersion Include="Vortice.Direct3D12" Version="3.8.3" />
<PackageVersion Include="Vortice.DXGI" Version="3.8.3" />
```

Add `Vortice.Dxc` only if shader build tooling actually requires it; measured runtime must use precompiled shader blobs.

- [ ] **Step 2: Create the executable project**

Project requirements:

```xml
<TargetFramework>net10.0-windows10.0.26100.0</TargetFramework>
<OutputType>Exe</OutputType>
<RuntimeIdentifier>win-x64</RuntimeIdentifier>
<Nullable>enable</Nullable>
<ImplicitUsings>enable</ImplicitUsings>
```

References: `LatencyPilot.Core` only where shared topology/evidence DTOs are needed; avoid referencing Service/Persistence.

- [ ] **Step 3: Define deterministic command-line options**

Create an immutable options record with at least:

```csharp
internal sealed record BenchmarkOptions(
    Guid SessionId,
    int Width,
    int Height,
    TimeSpan Duration,
    int WorkerCount,
    int Seed,
    string OutputPath);
```

Reject invalid sizes/durations/seeds before graphics initialization.

- [ ] **Step 4: Define stdout protocol messages**

Use one JSON object per line:

```csharp
internal sealed record BenchmarkProgress(
    string Schema,
    Guid SessionId,
    string Stage,
    double Fraction,
    string Message);
```

No privileged command input channel is introduced.

- [ ] **Step 5: Verify source build through the existing test graph without adding a permanent test method**

Add the benchmark project as a `ReferenceOutputAssembly=false` build dependency of `CriticalTests` if needed so hosted Tests compile it, following the existing App compile-check pattern.

- [ ] **Step 6: Commit**

Commit: `feat(benchmark): add managed D3D12 benchmark host`

---

### Task 3: Implement deterministic D3D12 rendering, GPU timestamps and multi-core CPU work

**Files:**
- Create: `src/LatencyPilot.GpuBenchmark/D3D12BenchmarkRenderer.cs`
- Create: `src/LatencyPilot.GpuBenchmark/BenchmarkWorkload.cs`
- Create: `src/LatencyPilot.GpuBenchmark/CpuRenderWorker.cs`
- Create: `src/LatencyPilot.GpuBenchmark/GpuTimestampCollector.cs`
- Create: `src/LatencyPilot.GpuBenchmark/Shaders/*` or embedded precompiled shader resources
- Modify: `src/LatencyPilot.GpuBenchmark/Program.cs`

**Interfaces:**
- `BenchmarkWorkload.CalibrateAsync(...)` returns frozen workload parameters.
- `BenchmarkWorkload.RunTrialAsync(...)` returns raw trial telemetry.
- Worker mapping is immutable after calibration.

- [ ] **Step 1: Add a deterministic pure-policy test before renderer code**

Extend an existing optimizer test with a pure calibration/workload-parameter model. The test must prove candidate changes cannot alter worker mapping or calibrated draw/simulation counts.

```csharp
Assert.AreEqual(original.WorkerMap, candidate.WorkerMap);
Assert.AreEqual(original.DrawCountPerWorker, candidate.DrawCountPerWorker);
Assert.AreEqual(original.SimulationIterations, candidate.SimulationIterations);
```

- [ ] **Step 2: Implement D3D12 initialization**

Use DXGI hardware adapter enumeration, exclude software adapters, create one direct queue, swap chain and fixed resources. Prefer 1280×720 windowed rendering. Detect tearing support and record the chosen present mode.

- [ ] **Step 3: Implement multi-threaded command recording**

Create one benchmark render worker per eligible physical core within the v1 bound. Each worker owns its command allocator/list and deterministic scene partition.

Worker mapping is fixed for the session and must not depend on the GPU interrupt candidate.

- [ ] **Step 4: Add bounded CPU simulation work**

Each worker performs deterministic allocation-free math/update work per frame before command-list recording. It must be burst/frame synchronized rather than a permanent 100% busy loop.

- [ ] **Step 5: Implement GPU timestamp queries**

Use D3D12 timestamp query heap + `GetTimestampFrequency` and calculate floating-point durations. Capture per-frame GPU-work duration independently from PresentMon.

- [ ] **Step 6: Implement warm-up and adaptive calibration**

During 10–20 seconds of warm-up, adjust draw/simulation work until CPU workers are materially active and GPU work is non-idle/non-permanently-saturated. Then freeze:

```csharp
public sealed record FrozenBenchmarkWorkload(
    int DrawCountPerWorker,
    int SimulationIterations,
    IReadOnlyList<LogicalProcessorId> WorkerMap,
    int Seed,
    int Width,
    int Height);
```

Never recalibrate between candidates.

- [ ] **Step 7: Enforce observer discipline**

After warm-up: no per-frame file I/O, no high-volume logging, no avoidable allocations. Progress updates are low frequency (for example 4 Hz maximum).

- [ ] **Step 8: Verify Tests GREEN and commit**

Commit: `feat(benchmark): add deterministic multicore GPU workload`

---

### Task 4: Add benchmark evidence artifacts and PresentMon compatibility boundary

**Files:**
- Create: `src/LatencyPilot.Core/Benchmarking/GpuBenchmarkEvidence.cs`
- Create: `src/LatencyPilot.Benchmarking/Optimization/GpuBenchmarkEvidenceInterpreter.cs`
- Modify existing PresentMon integration files under `src/LatencyPilot.Platform.Windows`
- Modify: `docs/BENCHMARK_METHODOLOGY.md`

**Interfaces:**
- Artifact schema: `latencypilot-gpu-benchmark-v1`.
- Interpreter consumes raw trial evidence and produces candidate-validity + named metric series.

- [ ] **Step 1: Audit the exact existing PresentMon native/API dependency**

Record binary/API version and compatibility. Require a known compatible v2.5.1-or-later behavior before this benchmark is authoritative. Do not blindly replace native binaries without checking existing raw-frame API compatibility.

- [ ] **Step 2: Extend an existing optimizer test with raw-frame interpretation assertions**

Prove:

```csharp
frame p99 is derived through LatencyPilot Percentiles
1% low is derived consistently from raw frame intervals
missing external metrics remain missing
HWS-sensitive GPUActive is context, not sole validity/primary evidence
```

- [ ] **Step 3: Define benchmark artifact**

Include exact source revision, Windows/GPU/driver/topology identity, benchmark version, frozen workload, worker map, trial role/order/seed, D3D12 GPU timestamp evidence, PresentMon capture identity/raw representation, ETW capture identity/integrity and validity reasons.

- [ ] **Step 4: Implement interpreter**

Primary/guardrail series must use the existing canonical metric/comparison types. Do not create a hidden score.

- [ ] **Step 5: Update methodology**

Explicitly state that this is repeated whole-run evidence and is not `baseline-quality-v2` five-window RealWorld evidence.

- [ ] **Step 6: Verify GREEN and commit**

Commit: `feat(benchmark): add GPU benchmark evidence contract`

---

### Task 5: Replace passive rank-1 selection with bounded all-core active screening

**Files:**
- Modify: `src/LatencyPilot.Benchmarking/Candidates/GpuAffinityCandidatePlanner.cs`
- Modify: `src/LatencyPilot.Benchmarking/Candidates/ProcessorPressureEvidenceBuilder.cs` only if needed for seed/context semantics
- Modify: `tests/LatencyPilot.CriticalTests/GpuAffinityCandidatePlannerTests.cs`
- Create or modify optimizer policy models under `src/LatencyPilot.Benchmarking/Optimization/`

**Interfaces:**
- Candidate generation produces physical-core screen set plus optional sibling-refinement set.
- Passive pressure is ordering/context only.

- [ ] **Step 1: Write RED assertions in `GpuAffinityCandidatePlannerTests`**

For an 8-core/16-thread synthetic topology require all 8 physical cores to appear in the v1 screening set when eligible.

```csharp
Assert.AreEqual(8, candidates.Count);
CollectionAssert.AreEquivalent(
    Enumerable.Range(0, 8).ToArray(),
    candidates.Select(c => c.PhysicalCoreIndex).ToArray());
```

Also require CPU0 eligibility and heterogeneous efficiency-class representation.

- [ ] **Step 2: Change default screening policy**

Use every eligible physical core when `PhysicalCoreCount <= 16`. Keep `MaximumCandidates = 16` in v1. For larger systems, stratify by efficiency class/NUMA/cache/passive pressure instead of simply taking four lowest-pressure cores.

- [ ] **Step 3: Add SMT refinement**

After a physical core wins screening, return both logical siblings for a dedicated sibling comparison when SMT is present.

- [ ] **Step 4: Preserve availability exclusions**

Real-time/foreign-allocated/parked CPU Set semantics remain explicit; do not force unavailable CPUs into the active set.

- [ ] **Step 5: Verify GREEN and commit**

Commit: `feat(optimizer): screen all bounded GPU affinity cores`

---

### Task 6: Introduce GPU-benchmark-specific readiness and contamination handling

**Files:**
- Create: `src/LatencyPilot.Benchmarking/Optimization/GpuBenchmarkReadiness.cs`
- Modify existing GPU optimizer readiness call sites
- Modify existing workload-readiness tests without adding a new permanent method if possible
- Modify: `docs/BENCHMARK_METHODOLOGY.md`

**Interfaces:**
- `workload-stability-v1` remains unchanged for steady RealWorld evidence.
- New method identity: `gpu-affinity-benchmark-v1`.

- [ ] **Step 1: Add RED policy assertions**

Require these behaviors:

```text
system CPU busy drift alone -> does not reject benchmark
GPU identity change -> reject
benchmark process change -> reject
ETW loss -> reject
worker/workload parameter change -> reject
control-trial drift -> retry/inconclusive
sleep/device reset -> reject
```

- [ ] **Step 2: Implement readiness model**

```csharp
public enum GpuBenchmarkReadinessState
{
    Ready,
    RetryableContamination,
    Inconclusive,
}
```

Return named reasons; do not map every issue to one generic failure.

- [ ] **Step 3: Add one bounded retry**

When a control/trial block is contaminated, repeat it once. A second failure terminalizes as Inconclusive. Browser/background activity remains context unless it breaks GPU/benchmark comparability.

- [ ] **Step 4: Remove GPU auto-affinity dependency on CPU-busy drift hard gate**

Do not weaken `workload-stability-v1` itself. Route automatic benchmark sessions through the new benchmark readiness contract.

- [ ] **Step 5: Verify GREEN and commit**

Commit: `fix(optimizer): use GPU-specific benchmark readiness`

---

### Task 7: Implement candidate trial orchestration, balanced confirmation and report

**Files:**
- Create: `src/LatencyPilot.Benchmarking/Optimization/GpuAutoAffinitySession.cs`
- Create: `src/LatencyPilot.Core/Benchmarking/GpuAutoAffinityReport.cs`
- Modify: `tools/LatencyPilot.GateAValidation/Program.cs`
- Modify existing Service experiment executor only where necessary; do not add public mutation IPC yet

**Interfaces:**
- Dev Gate A can execute the session through internal/owner-only mutation path.
- Artifact schema: `latencypilot-gpu-auto-affinity-report-v1`.

- [ ] **Step 1: Extend `OptimizerSafetyTests` with state-machine assertions**

Require exact sequence ownership:

```text
Original control
→ candidate apply
→ stored verify
→ benchmark trial + ETW/PresentMon
→ runtime ISR-placement proof
→ exact rollback
→ next candidate
```

Cancel/failure after ownership must enter rollback/recovery.

- [ ] **Step 2: Implement deterministic screening order**

Generate a stored deterministic shuffle seed. Run two 15-second trials per physical-core candidate and serialize the order/seed.

- [ ] **Step 3: Interpret screening without hidden score**

Use named metric vector + Pareto/guardrails. Screening can nominate finalists only.

- [ ] **Step 4: Run SMT refinement**

Test both siblings of the winning physical core with identical frozen benchmark parameters.

- [ ] **Step 5: Run final balanced confirmation**

Reuse the existing fixed `ABBA + BAAB` confirmation unless physical validation demonstrates the need for a versioned replacement. Each run stays >=30 seconds.

- [ ] **Step 6: Let original/default win**

If no candidate clears practical + guardrail thresholds, restore original and report `NoMeasurableDifference`/`RestoreOriginal`. Never force a changed affinity.

- [ ] **Step 7: Write full report**

Include every candidate/trial, raw named deltas, placement proof, rollback/recovery outcome and final exact state.

- [ ] **Step 8: Verify GREEN and commit**

Commit: `feat(optimizer): execute full GPU affinity candidate search`

---

### Task 8: Add real progress UI and safe cancellation

**Files:**
- Create: `src/LatencyPilot.App/GpuOptimizationProgressWindow.xaml`
- Create: `src/LatencyPilot.App/GpuOptimizationProgressWindow.xaml.cs`
- Create: `src/LatencyPilot.App/GpuOptimizationProgressViewModel.cs` if the existing App pattern benefits from it
- Modify: `src/LatencyPilot.App/GateAValidationExperience.cs`
- Modify relevant App design resources only by reusing existing styles/tokens

**Interfaces:**
- Progress source is real session state, not elapsed-time interpolation.
- `Stop safely` requests cancellation and displays rollback/recovery until terminal.

- [ ] **Step 1: Define progress DTO/state**

```csharp
internal sealed record GpuOptimizationProgress(
    string Phase,
    int CompletedUnits,
    int TotalUnits,
    LogicalProcessorId? Processor,
    int? PhysicalCore,
    int? CandidateIndex,
    int? CandidateCount,
    string Message,
    TimeSpan Elapsed,
    TimeSpan? EstimatedRemaining);
```

- [ ] **Step 2: Replace `AppWindow.Hide()` behavior**

Main window minimizes rather than disappearing. Keep normal taskbar representation.

- [ ] **Step 3: Implement compact progress window**

Show:

```text
current CPU/core
candidate X/Y
phase/pass
real percentage
frame p99 / 1% low when available
ISR placement state
last completed candidate verdict
elapsed / estimated remaining
Stop safely
```

Use existing design tokens/styles; no parallel design system.

- [ ] **Step 4: Add accessibility semantics**

Automation names/status text must contain the same phase/progress meaning. Do not communicate state by color only.

- [ ] **Step 5: Safe stop semantics**

Stopping prevents future trials but keeps the progress window in `Restoring original state…` until rollback/recovery reaches a terminal state.

- [ ] **Step 6: Owner-local App build/run inspection**

Hosted CI is test-only. Verify actual WinUI render, narrow sizing, keyboard focus and taskbar behavior on Windows.

- [ ] **Step 7: Commit**

Commit: `feat(app): show GPU optimizer candidate progress`

---

### Task 9: Physically validate the complete benchmark-backed Gate A on the RTX 3070 system

**Files:**
- Update generated evidence under owner-local validation directory only; do not commit private machine evidence unless redacted/approved
- Modify docs/status only after evidence exists

**Interfaces:**
- Uses exact current clean `main` and exact-green Tests.

- [ ] **Step 1: Pull exact final implementation and confirm exact-green Tests**

Record full SHA and workflow run.

- [ ] **Step 2: Run the D3D12 benchmark without mutation**

Confirm:

```text
multi-core workers are visibly active
CPU0 is not the only materially active core
GPU load calibrates and freezes
PresentMon/raw frame capture is clean
D3D12 GPU timestamps are finite/stable
```

- [ ] **Step 3: Run full dev Gate A candidate search**

On the Ryzen 7 5700X system expect eight physical-core candidates before SMT refinement, subject only to explicit CPU-set exclusions.

- [ ] **Step 4: Inspect progress behavior**

Verify current processor/candidate/progress changes truthfully; App remains recoverable from taskbar; Stop safely restores original state.

- [ ] **Step 5: Verify every mutation boundary**

For at least one candidate:

```text
stored state correct before capture
runtime NVIDIA ISR appears on requested logical processor
zero resolved NVIDIA ISR off target
exact rollback succeeds
final unresolved journal count = 0
```

- [ ] **Step 6: Repeat the full search**

Run at least twice. Acceptable outcome:

- same/equivalent winner within method thresholds; or
- explicit Inconclusive/NoMeasurableDifference.

Unacceptable outcome: arbitrary different winners with no uncertainty explanation.

- [ ] **Step 7: Exercise supported failure/recovery**

Use the existing safe failure exercise; do not deliberately corrupt registry/device state outside the harness.

- [ ] **Step 8: Reconcile docs and close Gate A only if evidence supports it**

Commit only the source/docs changes justified by evidence.

---

### Task 10: Gate B/C/D productization after physical Gate A succeeds

**Files:**
- Modify: `src/LatencyPilot.Protocol/*`
- Modify: `src/LatencyPilot.Service/*`
- Modify: `src/LatencyPilot.App/*`
- Modify: `SYSTEM_DESIGN.md`, `ROADMAP.md`, `PROJECT_STATUS.md`

**Interfaces:**
- Typed mutation-specific Service API only.
- No arbitrary registry/path/shell/process primitive.

- [ ] **Step 1: Gate B — add typed/allowlisted mutation commands**

Commands must encode the exact supported GPU affinity lifecycle, experiment ID, target device identity and expected state. Authorization remains active-console/local-owner constrained.

- [ ] **Step 2: Keep product mutation feature-flagged/off during Gate B implementation**

`MutationAvailable` does not become true merely because commands compile.

- [ ] **Step 3: Gate C — physically prove App → Service orchestration**

Run the full benchmark/candidate/rollback/recovery sequence through real product IPC.

- [ ] **Step 4: Gate D — arm `Auto-optimize GPU`**

Only after Gate C success expose the normal-user one-click button. Product flow should require at most ordinary UAC/Service authorization already established by installation; it must not require CLI commands.

- [ ] **Step 5: Keep optional real-game confirmation separate**

Built-in benchmark remains the deterministic default. A later `Optimize for this game` mode may confirm the finalist against an identified live game process using PresentMon, but it must not reintroduce a requirement to close every background application.

- [ ] **Step 6: Final exact-head CI + physical product report**

Commit: `feat(gpu): arm benchmark-backed auto affinity`

---

## After this plan

Do not broaden GPU work indefinitely. Once Gate D is physically credible:

```text
GPU Auto Affinity complete
→ implement/validate xHCI mutation using proven journal/recovery substrate
→ implement/validate NIC/RSS mutation
→ bounded multi-subsystem profile session + Restore Baseline
→ accessibility + signed release/install/upgrade/uninstall/reboot/crash recovery
→ representative hardware audit
→ final 1.0 audit/tag
```

The existing `ROADMAP.md` continues to own those downstream product gates.

## Execution Handoff

Start the next chat with:

```text
Use Superpowers and execute docs/superpowers/plans/2026-09-16-gpu-auto-affinity-benchmark.md from current main. Read docs/superpowers/specs/2026-09-16-gpu-auto-affinity-benchmark-design.md first. Preserve the full Gate A/B/C/D safety boundary, work on main, use TDD, CI test-only, and do not stop at recommendations—implement the plan through the next physical owner checkpoint.
```

At execution time, verify current `main` and freshness before touching code. If `main` moved since this plan was written, reconcile the plan with actual files instead of blindly applying stale line assumptions.
