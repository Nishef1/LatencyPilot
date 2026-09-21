# GPU Paired Local-Control Benchmark v2 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the v1 block-normalized GPU-affinity search with direct paired `Original -> Candidate -> Original` evidence, add a safe custom-CPU diagnostic selector beside Gate A, and preserve the existing mutation/recovery/ETW safety substrate.

**Architecture:** `GpuAutoAffinitySession` remains the orchestration owner. Full scope screens one representative logical CPU per physical core, refines siblings only on promising cores, and confirms at most three finalists with three independent 30-second pairs. Custom scope runs only owner-selected logical CPUs through the same 10-second paired screening path and always restores Original; the elevated helper freshly revalidates the requested subset before any mutation.

**Tech Stack:** C# 14, .NET 10, WinUI 3 / Windows App SDK 2.4, existing Benchmarking/Core/Platform.Windows/Persistence boundaries, ETW/TraceEvent, PresentMon cross-check, MSTest/Microsoft.Testing.Platform. No new package or native dependency.

**Spec:** `docs/superpowers/specs/2026-09-21-gpu-paired-local-control-v2-design.md`

## Global Constraints

- Work on `main`; preserve unrelated work.
- Keep `ServiceBoundary.MutationAvailable = false` until physical arming criteria are satisfied.
- Preserve exact journal-owned snapshot/apply/rollback/recovery semantics and final-state verification.
- Keep the current single-Windows-processor-group limitation for the KAFFINITY mutation path.
- Do not add global `ReservedCpuSets`, `SetRTCores`, undocumented `NtSetSystemInformation`, MSI-mode, HAGS, power, NIC/RSS, audio or BIOS mutation.
- Do not add a dependency for CPU selection or statistics.
- Custom CPU scope is development-only, ephemeral, always restores Original, never auto-Keeps, never qualifies for Gate A closure, and never arms product mutation.
- Full and custom scope use raw paired evidence; do not emit normalized pseudo-FPS.
- CPU0 remains an ordinary eligible processor; do not assume even/odd SMT numbering.
- Keep the permanent-test suite within the owner-approved cap; modify existing durable tests rather than adding new permanent methods where possible.
- Hosted CI is software-contract evidence only; local Windows render/build and hardware validation remain separate evidence.

## Review Focus

1. **Selection goes stale before elevation:** helper rebuilds fresh topology/CPU-set eligibility and rejects the whole custom subset before mutation if any requested CPU is invalid.
2. **Retry leaves candidate applied:** every unstable/failed pair must complete rollback and exact Original verification before retry or continuation.
3. **Invalid control becomes chain anchor:** only a valid `OriginalAfter` may become the next pair's `OriginalBefore`; otherwise reacquire a fresh Original or abort.
4. **Representative logic assumes parity:** representative/sibling selection must use physical-core topology, not CPU-number even/odd patterns.
5. **Custom result becomes machine-wide authority:** custom scope must persist its scope, force closure ineligible, suppress Keep, restore Original and render `Best within selected CPUs`.

---

### Task 1: Version the v2 evidence model

**Files:**
- Modify: `src/LatencyPilot.Core/Benchmarking/GpuAutoAffinityReport.cs`
- Modify existing test: `tests/LatencyPilot.CriticalTests/GpuMeasurementBootstrapContractTests.cs`

**Interfaces:**
- Produces: `GpuAutoAffinitySearchScope`, `GpuAutoAffinityPairVerdict`, `GpuAutoAffinityPairReport`, `GpuAutoAffinityFinalistReport`, and v2 scope/coverage properties on `GpuAutoAffinityReport`.
- Consumes: existing `GpuAutoAffinityTrialReport`, `LogicalProcessorId`, placement/audit evidence.

- [ ] **Step 1: Make the existing report-contract test fail for missing v2 pair/scope fields.**

Require these durable concepts in the existing test surface:

```csharp
public enum GpuAutoAffinitySearchScope { Full, Custom }
public enum GpuAutoAffinityPairVerdict { Valid, Unstable, Inconclusive }
```

and explicit persisted pair evidence rather than normalized decision FPS.

- [ ] **Step 2: Run the focused test and verify red.**

```powershell
dotnet test tests/LatencyPilot.CriticalTests/LatencyPilot.CriticalTests.csproj --configuration Release --filter "FullyQualifiedName~GpuMeasurementBootstrapContractTests"
```

- [ ] **Step 3: Add the v2 evidence records.**

Use this shape unless compilation reveals an existing naming conflict:

```csharp
public sealed record GpuAutoAffinityPairReport(
    int PairNumber,
    LogicalProcessorId Processor,
    int PhysicalCoreIndex,
    string Stage,
    int Attempt,
    Guid OriginalBeforeCaptureId,
    Guid CandidateCaptureId,
    Guid OriginalAfterCaptureId,
    double OriginalBeforeOnePercentLowFps,
    double CandidateOnePercentLowFps,
    double OriginalAfterOnePercentLowFps,
    double OnePercentLowEffect,
    double AvgEffect,
    double FrameP99Effect,
    double? Low01PctEffect,
    double ControlMovement,
    double DriftBudget,
    GpuAutoAffinityPairVerdict Verdict,
    string Reason);

public sealed record GpuAutoAffinityFinalistReport(
    LogicalProcessorId Processor,
    int PhysicalCoreIndex,
    IReadOnlyList<int> PairNumbers,
    double? MedianOnePercentLowEffect,
    double? MedianAvgEffect,
    double? MedianFrameP99Effect,
    double? MedianLow01PctEffect,
    string Verdict,
    string Reason);
```

Add init properties to `GpuAutoAffinityReport`:

```csharp
public GpuAutoAffinitySearchScope SearchScope { get; init; } = GpuAutoAffinitySearchScope.Full;
public IReadOnlyList<LogicalProcessorId> RequestedProcessors { get; init; } = [];
public IReadOnlyList<LogicalProcessorId> ValidatedProcessors { get; init; } = [];
public bool FullTopologyCoverage { get; init; }
public IReadOnlyList<GpuAutoAffinityPairReport> Pairs { get; init; } = [];
public IReadOnlyList<GpuAutoAffinityFinalistReport> Finalists { get; init; } = [];
public bool PracticalTie { get; init; }
```

Set the active schema to `latencypilot-gpu-auto-affinity-report-v2`; preserve old raw trial/audit fields while migrating readers.

- [ ] **Step 4: Run focused test + Benchmarking build.**

```powershell
dotnet test tests/LatencyPilot.CriticalTests/LatencyPilot.CriticalTests.csproj --configuration Release --filter "FullyQualifiedName~GpuMeasurementBootstrapContractTests"
dotnet build src/LatencyPilot.Benchmarking/LatencyPilot.Benchmarking.csproj --configuration Release --nologo
```

- [ ] **Step 5: Commit.**

```bash
git add src/LatencyPilot.Core/Benchmarking/GpuAutoAffinityReport.cs tests/LatencyPilot.CriticalTests/GpuMeasurementBootstrapContractTests.cs
git commit -m "feat(gpu): add paired affinity v2 evidence contract"
```

---

### Task 2: Replace v1 block normalization with chained local pairs

**Files:**
- Modify: `src/LatencyPilot.Benchmarking/Optimization/GpuAutoAffinitySession.cs`
- Modify existing tests: `tests/LatencyPilot.CriticalTests/GpuTemporalStabilityContractTests.cs`, `GpuAutoAffinitySessionTests.cs`

**Interfaces:**
- Produces: local pair evaluator, bounded one-retry rule, early-abort rule.
- Consumes: v2 pair records from Task 1 and existing backend apply/capture/rollback APIs.

- [ ] **Step 1: Change the existing temporal/session contracts to require `O0 -> C1 -> O1 -> C2 -> O2`.**

Pin these invariants:

```text
no ScreeningCandidatesPerControlBlock authority
no NormalizeScreeningEvaluations authority
one unstable-pair retry maximum
retry-exhausted candidate is Inconclusive
2 consecutive retry-exhausted candidates -> safe early RestoreOriginal
invalid OriginalAfter never becomes next OriginalBefore
```

- [ ] **Step 2: Run focused tests and verify red.**

```powershell
dotnet test tests/LatencyPilot.CriticalTests/LatencyPilot.CriticalTests.csproj --configuration Release --filter "FullyQualifiedName~GpuTemporalStabilityContractTests|FullyQualifiedName~GpuAutoAffinitySessionTests"
```

- [ ] **Step 3: Add the exact pair math from the approved spec.**

```csharp
private static double GeometricMean(double left, double right) => Math.Sqrt(left * right);
private static double HigherIsBetterEffect(double candidate, double before, double after) =>
    candidate / GeometricMean(before, after) - 1d;
private static double LowerIsBetterEffect(double candidate, double before, double after) =>
    GeometricMean(before, after) / candidate - 1d;
private static double RelativeMovement(double before, double after) =>
    Math.Abs(after - before) / Math.Max(before, after);
private static double ComputePairDriftBudget(double initialNoise) =>
    Math.Clamp(Math.Max(0.06d, 2d * initialNoise), 0.06d, 0.10d);
```

Reject non-finite/non-positive values instead of allowing NaN/Infinity into ranking.

- [ ] **Step 4: Keep initial Original qualification, then acquire a fresh 10-second `O0`.**

Qualification still uses the existing three-of-up-to-five bounded cluster. The accepted cluster supplies `InitialOriginal1PercentLowNoise`; it is not reused as a local control.

- [ ] **Step 5: Implement a single pair attempt.**

Each attempt must execute:

```text
verified OriginalBefore
-> ApplyCandidateAsync
-> device restart / candidate capture
-> RollbackAsync
-> VerifyOriginalStateAsync
-> fresh OriginalAfter capture
-> pair evaluation
```

Reuse the existing transition warm-up and backend/journal ownership. On any exception with an active experiment, existing rollback/recovery remains authoritative.

- [ ] **Step 6: Implement one retry and early abort.**

If `ControlMovement > DriftBudget`, retry that CPU once from a freshly reacquired valid Original. Second failure makes that CPU `Inconclusive`. Two consecutive retry-exhausted CPUs terminate screening safely with Original verified.

- [ ] **Step 7: Remove active v1 block/interpolation code.**

Stop calling and then delete dead active-method code for block controls, interpolation and session-wide normalized FPS ranking. Keep historical report fields only if another reader still needs them.

- [ ] **Step 8: Run focused tests and commit.**

```powershell
dotnet test tests/LatencyPilot.CriticalTests/LatencyPilot.CriticalTests.csproj --configuration Release --filter "FullyQualifiedName~GpuTemporalStabilityContractTests|FullyQualifiedName~GpuAutoAffinitySessionTests"
```

```bash
git add src/LatencyPilot.Benchmarking/Optimization/GpuAutoAffinitySession.cs tests/LatencyPilot.CriticalTests/GpuTemporalStabilityContractTests.cs tests/LatencyPilot.CriticalTests/GpuAutoAffinitySessionTests.cs
git commit -m "refactor(gpu): use paired local controls for screening"
```

---

### Task 3: Make full search topology-aware and shorter

**Files:**
- Modify if needed: `src/LatencyPilot.Benchmarking/Candidates/GpuAffinityCandidatePlanner.cs`
- Modify: `src/LatencyPilot.Benchmarking/Optimization/GpuAutoAffinitySession.cs`
- Modify existing tests: `GpuAffinityCandidatePlannerTests.cs`, `GpuAutoAffinitySessionTests.cs`

**Interfaces:**
- Produces: Stage-A representative list, Stage-B sibling refinement list, at-most-three finalist shortlist.

- [ ] **Step 1: Make existing planner/session tests fail for one-representative-per-physical-core behavior.**

Synthetic topology must prove that sibling mapping is derived from `PhysicalCoreIndex`, not CPU parity. Representative preference order:

```text
eligible/unallocated active CPU-set state
-> lower observed pressure
-> processor group/number deterministic fallback
```

CPU0 remains eligible when evidence permits.

- [ ] **Step 2: Run focused tests and verify red.**

```powershell
dotnet test tests/LatencyPilot.CriticalTests/LatencyPilot.CriticalTests.csproj --configuration Release --filter "FullyQualifiedName~GpuAffinityCandidatePlannerTests|FullyQualifiedName~GpuAutoAffinitySessionTests"
```

- [ ] **Step 3: Add the smallest representative helper only if it prevents duplicated eligibility logic.**

Preferred API:

```csharp
public static IReadOnlyList<GpuAffinityCandidate> SelectPhysicalCoreRepresentatives(
    IReadOnlyList<GpuAffinityCandidate> eligibleCandidates,
    ProcessorCpuSetSnapshot? cpuSets)
```

- [ ] **Step 4: Stage A deterministic-shuffles and screens one representative per physical core.**

Persist the realized order through pair order plus the existing shuffle seed.

- [ ] **Step 5: Stage B screens untested siblings only for the best two physical cores plus one third core within 1% of second place.**

Hard cap: three physical cores.

- [ ] **Step 6: Stage C advances the best two valid logical CPUs plus one third CPU within 1% of second place.**

Hard cap: three finalists. Short-screen 0.1% low is diagnostic only.

- [ ] **Step 7: Run focused tests and commit.**

```powershell
dotnet test tests/LatencyPilot.CriticalTests/LatencyPilot.CriticalTests.csproj --configuration Release --filter "FullyQualifiedName~GpuAffinityCandidatePlannerTests|FullyQualifiedName~GpuAutoAffinitySessionTests"
```

```bash
git add src/LatencyPilot.Benchmarking/Candidates/GpuAffinityCandidatePlanner.cs src/LatencyPilot.Benchmarking/Optimization/GpuAutoAffinitySession.cs tests/LatencyPilot.CriticalTests/GpuAffinityCandidatePlannerTests.cs tests/LatencyPilot.CriticalTests/GpuAutoAffinitySessionTests.cs
git commit -m "feat(gpu): screen physical cores before SMT refinement"
```

---

### Task 4: Confirm finalists with three independent 30-second pairs

**Files:**
- Modify: `src/LatencyPilot.Benchmarking/Optimization/GpuAutoAffinitySession.cs`
- Modify existing test: `GpuAutoAffinitySessionTests.cs`

**Interfaces:**
- Produces: `GpuAutoAffinityFinalistReport[]`, winner/tie/no-winner authority.

- [ ] **Step 1: Update the existing session test for three finalist pairs and practical ties.**

Required policy from the approved spec:

```text
3 valid 30 s pairs per finalist
>=2 of 3 primary paired effects positive
median 1%-low effect > max(1%, median finalist pair-control movement)
no valid pair has a material 1%-low regression beyond the same decision floor
AVG, frame-p99 and interrupt-tail guardrails remain noise-aware
<=1% difference between improvement-capable finalists => Practical tie
```

Do **not** add initial qualification noise to the finalist decision floor; the approved spec intentionally uses local finalist pair movement for that phase.

- [ ] **Step 2: Run focused test and verify red.**

```powershell
dotnet test tests/LatencyPilot.CriticalTests/LatencyPilot.CriticalTests.csproj --configuration Release --filter "FullyQualifiedName~GpuAutoAffinitySessionTests"
```

- [ ] **Step 3: Split screening and finalist durations in the request.**

```csharp
public sealed record GpuAutoAffinitySessionRequest(
    Guid SessionId,
    ProcessorTopologySnapshot Topology,
    IEnumerable<ProcessorPressureEvidence> PressureEvidence,
    ProcessorCpuSetSnapshot? CpuSets,
    int ShuffleSeed,
    TimeSpan ScreeningDuration,
    TimeSpan FinalistDuration,
    GpuAutoAffinitySearchScope SearchScope = GpuAutoAffinitySearchScope.Full,
    IReadOnlyList<LogicalProcessorId>? RequestedProcessors = null);
```

- [ ] **Step 4: Replace v1 finalist cluster re-screening with three paired finalist rounds.**

Capture a fresh 30-second Original before finalist round 1. Shuffle finalist order deterministically each round. Each locally unstable pair receives the same single retry; a finalist without three valid pairs is `Inconclusive`.

- [ ] **Step 5: Order improvement-capable finalists by median paired 1%-low effect.**

Mark `PracticalTie=true` for <=1% differences. Use the existing passive topology/pressure ordering only to choose an operational target inside a practical tie; do not claim that target proved faster.

- [ ] **Step 6: Preserve full-scope final apply + kernel ETW target-only proof exactly.**

No target-only runtime ISR proof => RestoreOriginal.

- [ ] **Step 7: Run session + runtime placement tests and commit.**

```powershell
dotnet test tests/LatencyPilot.CriticalTests/LatencyPilot.CriticalTests.csproj --configuration Release --filter "FullyQualifiedName~GpuAutoAffinitySessionTests|FullyQualifiedName~GpuRuntimePlacementContractTests"
```

```bash
git add src/LatencyPilot.Benchmarking/Optimization/GpuAutoAffinitySession.cs tests/LatencyPilot.CriticalTests/GpuAutoAffinitySessionTests.cs
git commit -m "feat(gpu): confirm finalists with repeated local pairs"
```

---

### Task 5: Add privileged custom-CPU diagnostic scope

**Files:**
- Modify: `tools/LatencyPilot.GateAValidation/GpuAutoAffinityGateARunner.cs`
- Modify: `tools/LatencyPilot.GateAValidation/GpuGateAProgressFile.cs`
- Modify an existing Gate A/measurement contract test.

**Interfaces:**
- Consumes CLI: `--candidate-cpus 4,8,12,6`
- Produces explicit `SearchScope=Custom`, requested/validated processor evidence and closure-ineligible terminal report.

- [ ] **Step 1: Make the existing Gate A contract fail for custom safety semantics.**

Pin:

```text
empty/malformed/duplicate list -> reject before mutation
out-of-range/ineligible CPU -> reject entire run before mutation
fresh helper topology is authority
custom scope -> GateAClosureEligible false
custom scope -> no KeepAsync/finalist confirmation
custom scope -> exact Original restored
```

- [ ] **Step 2: Extend `AutoOptions` with optional candidate processors.**

Accept `--candidate-cpus` only once. Parse invariant comma-separated group-0 logical processor numbers; reject duplicates and values outside 0..63 during argument parsing.

- [ ] **Step 3: Revalidate the whole subset after fresh topology + CPU-set capture and before backend/mutation creation.**

Build the normal eligible candidate list with `GpuAffinityCandidatePlanner.Create(...)`; every requested `LogicalProcessorId(0, number)` must exist in that fresh list or the run stops before the first mutation.

- [ ] **Step 4: Build the explicit session request.**

Full scope:

```csharp
ScreeningDuration = TimeSpan.FromSeconds(10)
FinalistDuration = TimeSpan.FromSeconds(30)
SearchScope = Full
```

Custom scope uses the same 10-second pair formula but only the validated subset and skips Stage-B expansion/finalists/Keep.

- [ ] **Step 5: Enforce custom safety in both session and runner.**

Even if a later bug returns `KeepCandidate`, runner must reject that impossible custom result before writing evidence. Stamp `GateAClosureEligible=false` and `FullTopologyCoverage=false` for custom scope.

- [ ] **Step 6: Progress reports `Full search` or `Custom: N CPUs` and uses the actual scoped candidate total.**

- [ ] **Step 7: Run focused tests and commit.**

```powershell
dotnet test tests/LatencyPilot.CriticalTests/LatencyPilot.CriticalTests.csproj --configuration Release --filter "FullyQualifiedName~GpuMeasurementBootstrapContractTests|FullyQualifiedName~GateAResultCompletionContractTests|FullyQualifiedName~GpuAutoAffinitySessionTests"
```

```bash
git add tools/LatencyPilot.GateAValidation/GpuAutoAffinityGateARunner.cs tools/LatencyPilot.GateAValidation/GpuGateAProgressFile.cs tests/LatencyPilot.CriticalTests
git commit -m "feat(gatea): add safe custom CPU diagnostic scope"
```

---

### Task 6: Add the CPU selector beside Gate A

**Files:**
- Modify: `src/LatencyPilot.App/GateAValidationExperience.cs`
- Avoid `MainWindow.xaml` unless dynamic insertion into existing `DeveloperValidationHost` proves insufficient.

**Interfaces:**
- Produces ephemeral `_gateACustomScope`, `_gateASelectedProcessors`, `_gateACpuScopeButton`.
- Consumes fresh `ProcessorTopologyReader` and best-effort `ProcessorCpuSetReader` for display only; elevated helper still revalidates authority.

- [ ] **Step 1: Add a compact scope button immediately beside the existing Gate A button.**

Default:

```text
[ Evidence-ready ] [ Run Gate A ] [ All CPUs ▾ ]
```

Custom:

```text
[ Evidence-ready ] [ Run selected CPUs ] [ 4 CPUs ▾ ]
```

- [ ] **Step 2: Open a WinUI `ContentDialog` from the scope button.**

Dialog contents:

```text
CPU scope
(o) All eligible CPUs
( ) Selected CPUs

Core 0   [ ] CPU 0   [ ] CPU 1
Core 1   [ ] CPU 2   [ ] CPU 3
...

Select all eligible   Clear
4 selected
```

Group rows by actual physical core topology. Disable ineligible entries and expose a concise non-color reason/tooltip. Do not infer SMT from numbering.

- [ ] **Step 3: Keep selection ephemeral.**

`Selected CPUs` requires at least one eligible checked CPU. `Use selection` updates in-memory state only. App restart resets to All eligible. Cancel preserves prior state.

- [ ] **Step 4: Update card copy/accessibility.**

Custom copy:

```text
Custom diagnostic scope: CPU 4, CPU 6, CPU 8, CPU 12. Screening only; Original will be restored and this run cannot close Gate A.
```

- [ ] **Step 5: Pass the exact custom list to the helper.**

```csharp
helperArguments.Add("--candidate-cpus");
helperArguments.Add(string.Join(',', _gateASelectedProcessors.Order()));
```

- [ ] **Step 6: Disable the scope button while source assessment/measurement/Gate A is busy so the selection cannot change mid-session.**

- [ ] **Step 7: Local Windows build/render check.**

```powershell
dotnet build src/LatencyPilot.App/LatencyPilot.App.csproj --configuration Debug --nologo
```

Inspect normal scaling + keyboard + High Contrast. Dialog must fit the machine's 16 logical CPUs without horizontal clipping and keep physical-core grouping understandable.

- [ ] **Step 8: Commit.**

```bash
git add src/LatencyPilot.App/GateAValidationExperience.cs
git commit -m "feat(gatea): add custom CPU selection dialog"
```

---

### Task 7: Present v2 paired evidence without re-ranking in UI

**Files:**
- Modify: `src/LatencyPilot.Benchmarking/Optimization/GateAResultPresentation.cs`
- Modify: `src/LatencyPilot.App/GpuOptimizationProgressWindow.xaml`
- Modify: `src/LatencyPilot.App/GpuOptimizationProgressWindow.xaml.cs`
- Modify existing test: `tests/LatencyPilot.CriticalTests/GateAResultCompletionContractTests.cs`

**Interfaces:**
- Consumes persisted `Pairs`, `Finalists`, `SearchScope`, `PracticalTie`, final recommendation/state.
- Produces presentation only; no winner inference from raw FPS.

- [ ] **Step 1: Make the existing result contract fail unless presentation consumes persisted v2 authority.**

Required vocabulary:

```text
Winner
Practical tie
No measured winner
Inconclusive
Custom diagnostic result
Best within selected CPUs
Original restored
```

- [ ] **Step 2: Render each screening pair directly.**

Example:

```text
CPU 8 · Core 4
Original before     198.2 FPS
Candidate           214.7 FPS
Original after      201.0 FPS
Local effect        +7.6%
Control movement    1.4% / 6.0% budget
Status              Valid
```

Do not show normalized pseudo-FPS.

- [ ] **Step 3: Render finalist evidence as three independent paired effects plus median.**

Custom mode never says machine-wide Winner; it says `Best within selected CPUs` and terminal `Original restored`.

- [ ] **Step 4: Keep semantic colors honest.**

Green only for verified Keep or verified restored-safe terminal state; diagnostic leader accent/blue; tie/uncertainty attention; failure only for integrity/recovery failure.

- [ ] **Step 5: Run focused test + local App build and commit.**

```powershell
dotnet test tests/LatencyPilot.CriticalTests/LatencyPilot.CriticalTests.csproj --configuration Release --filter "FullyQualifiedName~GateAResultCompletionContractTests"
dotnet build src/LatencyPilot.App/LatencyPilot.App.csproj --configuration Debug --nologo
```

```bash
git add src/LatencyPilot.Benchmarking/Optimization/GateAResultPresentation.cs src/LatencyPilot.App/GpuOptimizationProgressWindow.xaml src/LatencyPilot.App/GpuOptimizationProgressWindow.xaml.cs tests/LatencyPilot.CriticalTests/GateAResultCompletionContractTests.cs
git commit -m "feat(gatea): present paired GPU affinity evidence"
```

---

### Task 8: Reconcile docs, run full verification, and deliver exact HEAD

**Files:**
- Create: `docs/adr/0007-paired-local-control-gpu-affinity-v2.md`
- Modify: `docs/BENCHMARK_METHODOLOGY.md`
- Modify: `docs/superpowers/plans/2026-09-19-gpu-measurement-stability.md`
- Modify: `PROJECT_STATUS.md`
- Modify: `ROADMAP.md`
- Review all source/test files changed above.

**Interfaces:** no new runtime interface; this task closes software evidence only.

- [ ] **Step 1: Write ADR 0007.**

Record the accepted changes exactly:

```text
paired O-C-O controls
no block interpolation / pseudo-normalized FPS
10 s screen, 30 s finalist
representative-per-physical-core then sibling refinement
max three finalists
single retry for local pair drift
safe early abort
custom subset diagnostic-only
no CPU shielding/global reservation in initial v2
```

Document that Windows interrupt affinity remains `IrqPolicySpecifiedProcessors` + `AssignmentSetOverride` KAFFINITY within the current single processor group; do not expand device mutation semantics.

- [ ] **Step 2: Update benchmark methodology, old measurement plan, PROJECT_STATUS and ROADMAP without rewriting historical v1 hardware evidence.**

Execution ladder after software delivery:

```text
local build/render inspection
custom v2 run on exact selected CPUs
full v2 search
repeat full v2 search
Stop safely + one supported failure/recovery exercise
```

Physical Gate A remains open until those evidence requirements are satisfied.

- [ ] **Step 3: Run the complete permanent test suite.**

```powershell
dotnet test tests/LatencyPilot.CriticalTests/LatencyPilot.CriticalTests.csproj --configuration Release
```

Confirm permanent test count remains within the owner cap and every test file remains <=1200 lines.

- [ ] **Step 4: Build relevant projects locally on Windows.**

```powershell
dotnet build src/LatencyPilot.Benchmarking/LatencyPilot.Benchmarking.csproj --configuration Release --nologo
dotnet build tools/LatencyPilot.GateAValidation/LatencyPilot.GateAValidation.csproj --configuration Release --nologo
dotnet build src/LatencyPilot.App/LatencyPilot.App.csproj --configuration Debug --nologo
```

- [ ] **Step 5: Perform the mandatory step-back review.**

Explicitly answer:

```text
Does every new path preserve journal ownership and exact rollback?
Can any invalid OriginalAfter become the next chain anchor?
Can custom scope ever Keep or closure-qualify?
Can UI selection bypass elevated helper revalidation?
Can full search miss a physical core due to SMT-number assumptions?
Can a v1 normalized field still influence v2 ranking/presentation?
Does Stop safely restore Original from every v2 phase?
```

Fix any contradiction before final delivery.

- [ ] **Step 6: Review final diff and remove only dead active-v1 measurement code.**

Do not delete historical report evidence merely for cleanup.

- [ ] **Step 7: Commit docs/final reconciliation, push/confirm `main`, and require hosted Tests on the exact final HEAD.**

```bash
git add docs/adr/0007-paired-local-control-gpu-affinity-v2.md docs/BENCHMARK_METHODOLOGY.md docs/superpowers/plans/2026-09-19-gpu-measurement-stability.md PROJECT_STATUS.md ROADMAP.md
git commit -m "docs: adopt paired local-control GPU affinity v2"
```

Hosted CI is software-contract evidence only; record exact HEAD and Tests run after success.

---

## Physical evidence sequence after software delivery

1. Run the app on the exact clean green revision.
2. Open CPU scope beside Gate A and select a small diagnostic set, initially `4, 6, 8, 12` unless newer evidence suggests a different subset.
3. Run selected CPUs. Required terminal truth: custom diagnostic result + exact Original restored.
4. Inspect the v2 report for raw `O-C-O` values, pair movement, retry behavior and absence of pseudo-normalized FPS.
5. If coherent, return scope to All eligible CPUs and run the full v2 search.
6. Repeat the full search once with a new recorded shuffle seed.
7. Only reproducible winner/practical-tie evidence plus final ETW placement proof can justify a full-mode Keep claim.
8. Exercise Stop safely and one supported failure/recovery path; require exact terminal state and unresolved journal count `0`.
