# GPU Paired Local-Control Benchmark v2 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the v1 block-normalized GPU-affinity search with an auditable paired `Original -> Candidate -> Original` method, add bounded custom-CPU diagnostic scope beside Gate A, and keep the existing mutation/recovery/ETW safety substrate intact.

**Architecture:** `GpuAutoAffinitySession` remains the orchestration owner, but it consumes an explicit search scope and produces versioned paired evidence instead of normalized pseudo-FPS. Full search screens one representative per physical core, refines selected SMT siblings, then confirms at most three finalists with repeated 30-second pairs; custom scope runs only the selected logical CPUs through the same paired screening path and always restores Original. `GpuAutoAffinityGateARunner` owns privileged fresh revalidation of the custom subset, while the non-elevated WinUI app owns only ephemeral selection UX.

**Tech Stack:** C# 14, .NET 10, WinUI 3 / Windows App SDK 2.4, existing `LatencyPilot.Benchmarking`, `LatencyPilot.Platform.Windows`, ETW/TraceEvent, PresentMon cross-check, SQLite mutation journal, MSTest/Microsoft.Testing.Platform. No new package or native dependency.

**Spec:** `docs/superpowers/specs/2026-09-21-gpu-paired-local-control-v2-design.md`

## Global Constraints

- Work on `main`; preserve unrelated work.
- Keep `ServiceBoundary.MutationAvailable = false` until physical arming criteria are satisfied.
- Preserve exact journal-owned snapshot/apply/rollback/recovery semantics and final-state verification.
- Keep the current single-Windows-processor-group limitation for the KAFFINITY mutation path.
- Do not add global `ReservedCpuSets`, `SetRTCores`, undocumented `NtSetSystemInformation`, MSI-mode, HAGS, power, NIC/RSS, audio or BIOS mutation.
- Do not add a new dependency for CPU selection or statistics.
- Custom CPU scope is development-only, ephemeral, always restores Original, never auto-Keeps, never qualifies for Gate A closure, and never arms product mutation.
- Full search and custom search both use raw paired evidence; do not emit normalized pseudo-FPS.
- Preserve CPU0 as an ordinary eligible processor; no hard-coded even/odd SMT assumptions.
- Keep the permanent-test suite within the owner-approved cap; modify existing durable audit cases rather than creating new permanent test methods unless no existing owner fits.
- Hosted CI proves software contracts only; local Windows render/build and physical hardware runs remain separate evidence.

## Review Focus

1. **Custom subset goes stale between dialog and elevation:** helper must rebuild topology/candidate eligibility and reject the whole subset before the first mutation if any requested CPU is missing/ineligible.
2. **Pair retry accidentally leaves a candidate applied:** every unstable/failed pair must pass through existing rollback and exact Original verification before retry or continuation.
3. **Chained control is reused after a failed/invalid control:** only a valid `OriginalAfter` may become the next pair's `OriginalBefore`; otherwise reacquire a fresh control or abort.
4. **Full-search representative selection misses topology semantics:** choose one representative per physical core from existing eligibility/pressure evidence, then explicitly test siblings only for selected top cores; never derive siblings from CPU-number parity.
5. **Custom result is mistaken for machine-wide authority:** persisted scope and coverage must force `GateAClosureEligible=false`, restore Original, suppress Keep, and render `Best within selected CPUs`.

---

## File Map

### Core/report contract
- Modify `src/LatencyPilot.Core/Benchmarking/GpuAutoAffinityReport.cs` — add v2 scope, paired evidence, finalist summary and coverage fields; retain v1-compatible raw trial/audit fields where useful.

### Benchmarking/session
- Modify `src/LatencyPilot.Benchmarking/Optimization/GpuAutoAffinitySession.cs` — remove block interpolation authority, add paired screening/retry/early-abort, representative/refinement/full finalist flow, and custom diagnostic semantics.
- Modify `src/LatencyPilot.Benchmarking/Candidates/GpuAffinityCandidatePlanner.cs` only if a small helper is required to expose per-physical-core representative selection without duplicating existing eligibility logic.
- Modify `src/LatencyPilot.Benchmarking/Optimization/GateAResultPresentation.cs` — consume persisted v2 pair/finalist authority; never reconstruct ranking from raw FPS.

### Elevated helper/progress
- Modify `tools/LatencyPilot.GateAValidation/GpuAutoAffinityGateARunner.cs` — parse/revalidate `--candidate-cpus`, create full/custom request, stamp scope/closure semantics, keep custom runs RestoreOriginal-only.
- Modify `tools/LatencyPilot.GateAValidation/GpuGateAProgressFile.cs` and `ProgressReportingGpuAutoAffinityBackend.cs` — show v2 phases/scope/pair status without implying a raw screen leader is the winner.

### WinUI development UX
- Modify `src/LatencyPilot.App/GateAValidationExperience.cs` — add ephemeral selected-CPU state, scope button beside Gate A, `ContentDialog` selection UI, and helper argument wiring.
- Modify `src/LatencyPilot.App/GpuOptimizationProgressWindow.xaml` / `.xaml.cs` only for v2 scope/pair evidence presentation.
- Avoid changing `MainWindow.xaml` unless a static host/property is genuinely required; `DeveloperValidationHost` already exists.

### Tests/docs
- Modify existing `tests/LatencyPilot.CriticalTests/GpuAutoAffinitySessionTests.cs`.
- Modify existing `tests/LatencyPilot.CriticalTests/GpuTemporalStabilityContractTests.cs`.
- Modify existing `tests/LatencyPilot.CriticalTests/GpuMeasurementBootstrapContractTests.cs` and/or `GateAResultCompletionContractTests.cs` only for durable cross-boundary contracts.
- Create `docs/adr/0007-paired-local-control-gpu-affinity-v2.md`.
- Modify `docs/BENCHMARK_METHODOLOGY.md`, `docs/superpowers/plans/2026-09-19-gpu-measurement-stability.md`, `PROJECT_STATUS.md`, and `ROADMAP.md` to reflect the active v2 method and physical evidence still open.

---

### Task 1: Version the v2 report and search-scope contract

**Files:**
- Modify: `src/LatencyPilot.Core/Benchmarking/GpuAutoAffinityReport.cs`
- Test: `tests/LatencyPilot.CriticalTests/GpuMeasurementBootstrapContractTests.cs`

**Interfaces:**
- Produces: `GpuAutoAffinitySearchScope`, `GpuAutoAffinityPairVerdict`, `GpuAutoAffinityPairReport`, `GpuAutoAffinityFinalistReport`, explicit v2 scope/coverage fields on `GpuAutoAffinityReport`.
- Consumes: existing `GpuAutoAffinityTrialReport`, `LogicalProcessorId`, `GpuAutoAffinityPlacementProof`.

- [ ] **Step 1: Extend the existing report-contract test so v2 evidence cannot regress to normalized pseudo-FPS authority.**

Add assertions in the existing GPU measurement contract that require a v2 schema/method surface and explicit paired fields, while rejecting source references to `UsesTimeLocalNormalization` as decision authority in the v2 session path. Keep this inside an existing `[TestMethod]`/audit case instead of creating a new permanent test method.

Expected core shape:

```csharp
public enum GpuAutoAffinitySearchScope
{
    Full,
    Custom,
}

public enum GpuAutoAffinityPairVerdict
{
    Valid,
    Unstable,
    Inconclusive,
}

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

Extend `GpuAutoAffinityReport` with init-only properties so old constructor call sites can be migrated incrementally:

```csharp
public GpuAutoAffinitySearchScope SearchScope { get; init; } = GpuAutoAffinitySearchScope.Full;
public IReadOnlyList<LogicalProcessorId> RequestedProcessors { get; init; } = [];
public IReadOnlyList<LogicalProcessorId> ValidatedProcessors { get; init; } = [];
public bool FullTopologyCoverage { get; init; }
public IReadOnlyList<GpuAutoAffinityPairReport> Pairs { get; init; } = [];
public IReadOnlyList<GpuAutoAffinityFinalistReport> Finalists { get; init; } = [];
public bool PracticalTie { get; init; }
public const string SchemaId = "latencypilot-gpu-auto-affinity-report-v2";
```

- [ ] **Step 2: Run the focused critical test and confirm it fails before the report model changes.**

Run:

```powershell
dotnet test tests/LatencyPilot.CriticalTests/LatencyPilot.CriticalTests.csproj --configuration Release --filter "GpuMeasurementBootstrapContractTests"
```

Expected: FAIL because v2 pair/scope fields do not exist yet.

- [ ] **Step 3: Implement the v2 report types with no normalization-derived FPS fields added.**

Keep legacy candidate fields only where they are still needed for historical/read compatibility during the migration; v2 presentation and decision code must consume `Pairs`/`Finalists`.

- [ ] **Step 4: Run the focused test and compile the Core/Benchmarking projects.**

```powershell
dotnet test tests/LatencyPilot.CriticalTests/LatencyPilot.CriticalTests.csproj --configuration Release --filter "GpuMeasurementBootstrapContractTests"
dotnet build src/LatencyPilot.Benchmarking/LatencyPilot.Benchmarking.csproj --configuration Release --nologo
```

Expected: PASS/build success after call-site adjustments required only by the schema constant.

- [ ] **Step 5: Commit.**

```bash
git add src/LatencyPilot.Core/Benchmarking/GpuAutoAffinityReport.cs tests/LatencyPilot.CriticalTests/GpuMeasurementBootstrapContractTests.cs
git commit -m "feat(gpu): add paired affinity v2 evidence contract"
```

---

### Task 2: Replace block normalization with a pure paired-evidence evaluator

**Files:**
- Modify: `src/LatencyPilot.Benchmarking/Optimization/GpuAutoAffinitySession.cs`
- Test: `tests/LatencyPilot.CriticalTests/GpuTemporalStabilityContractTests.cs`

**Interfaces:**
- Produces internal helpers: `CreatePairReport(...)`, `ComputePairDriftBudget(...)`, `ComputeHigherIsBetterEffect(...)`, `ComputeLowerIsBetterEffect(...)`.
- Consumes v2 report types from Task 1.

- [ ] **Step 1: Rewrite the existing temporal-stability audit expectations to require local pairs and forbid v1 block interpolation.**

The durable contract should assert these semantics in the existing test surface:

```text
O0 -> C1 -> O1 -> C2 -> O2
one retry maximum for an unstable pair
two consecutive retry-exhausted candidates -> early RestoreOriginal
no NormalizeScreeningEvaluations authority
no ScreeningCandidatesPerControlBlock authority
```

Also pin Review Focus item #3: a failed/unstable `OriginalAfter` cannot silently become the next `OriginalBefore`.

- [ ] **Step 2: Run the focused test and verify red.**

```powershell
dotnet test tests/LatencyPilot.CriticalTests/LatencyPilot.CriticalTests.csproj --configuration Release --filter "GpuTemporalStabilityContractTests"
```

Expected: FAIL against current block-control implementation.

- [ ] **Step 3: Add local-effect helpers.**

Use the spec equations exactly:

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

Reject non-finite/non-positive metric inputs as structural/inconclusive evidence rather than allowing NaN/Infinity into ranking.

- [ ] **Step 4: Replace the screening loop with chained `OriginalBefore -> Candidate -> OriginalAfter`.**

After initial Original qualification, acquire a fresh scored `O0` using `request.ScreeningDuration`. Then for each candidate:

```csharp
var pair = await MeasureScreeningPairAsync(
    candidate,
    originalBefore,
    driftBudget,
    request.ScreeningDuration,
    ...);

pairs.Add(pair.Report);
if (pair.Report.Verdict == GpuAutoAffinityPairVerdict.Valid)
{
    originalBefore = pair.OriginalAfter;
    consecutiveUnstableCandidates = 0;
}
else
{
    // retry once from a freshly verified/reacquired Original control;
    // never reuse an invalid control as the next chain anchor.
}
```

Every candidate attempt must use existing `ApplyCandidateAsync` / `CaptureCandidateAsync` / `RollbackAsync` and verify exact Original before continuation.

- [ ] **Step 5: Remove v1 decision authority.**

Delete or stop calling the v1 screening block machinery:

```text
ScreeningCandidatesPerControlBlock
NormalizeScreeningEvaluations
ControlDriftEnvelope as session-wide ranking correction
MaximumNoiseForFullFinalistConfirmation as a post-sweep gate
normalized Decision* FPS as v2 ranking authority
```

Keep any helper only if another non-v2 code path still legitimately consumes it; otherwise remove dead code.

- [ ] **Step 6: Implement bounded retry and early abort.**

Rules:

```text
pair drift <= budget -> Valid
pair drift > budget -> retry once
retry drift > budget -> candidate Inconclusive
2 consecutive retry-exhausted candidates -> safe early RestoreOriginal
valid candidate resets consecutive counter
structural evidence failure -> fail closed immediately
```

- [ ] **Step 7: Run the temporal/session focused tests.**

```powershell
dotnet test tests/LatencyPilot.CriticalTests/LatencyPilot.CriticalTests.csproj --configuration Release --filter "GpuTemporalStabilityContractTests|GpuAutoAffinitySessionTests"
```

Expected: PASS.

- [ ] **Step 8: Commit.**

```bash
git add src/LatencyPilot.Benchmarking/Optimization/GpuAutoAffinitySession.cs tests/LatencyPilot.CriticalTests/GpuTemporalStabilityContractTests.cs tests/LatencyPilot.CriticalTests/GpuAutoAffinitySessionTests.cs
git commit -m "refactor(gpu): use paired local controls for screening"
```

---

### Task 3: Implement full-search physical-core representatives and sibling refinement

**Files:**
- Modify: `src/LatencyPilot.Benchmarking/Candidates/GpuAffinityCandidatePlanner.cs` if needed
- Modify: `src/LatencyPilot.Benchmarking/Optimization/GpuAutoAffinitySession.cs`
- Test: `tests/LatencyPilot.CriticalTests/GpuAffinityCandidatePlannerTests.cs`
- Test: `tests/LatencyPilot.CriticalTests/GpuAutoAffinitySessionTests.cs`

**Interfaces:**
- Produces: one representative per eligible physical core for Stage A; explicit Stage B sibling list for at most three top cores.
- Consumes: existing `GpuAffinityCandidate`, `ProcessorTopologySnapshot`, CPU-set availability and pressure evidence.

- [ ] **Step 1: Extend existing candidate/session tests for topology-aware representative behavior.**

Cover Review Focus item #4 with a synthetic SMT topology whose sibling processor numbers are not assumed by parity. Expected behavior:

```text
one Stage-A representative per physical core
prefer eligible/unallocated/active CPU-set state
then lower pressure
then deterministic processor number
never hard-ban CPU0
```

- [ ] **Step 2: Run focused tests and confirm red.**

```powershell
dotnet test tests/LatencyPilot.CriticalTests/LatencyPilot.CriticalTests.csproj --configuration Release --filter "GpuAffinityCandidatePlannerTests|GpuAutoAffinitySessionTests"
```

- [ ] **Step 3: Add the smallest reusable representative-selection helper.**

Prefer an API on `GpuAffinityCandidatePlanner` only if it prevents session duplication, for example:

```csharp
public static IReadOnlyList<GpuAffinityCandidate> SelectPhysicalCoreRepresentatives(
    IReadOnlyList<GpuAffinityCandidate> eligibleCandidates,
    ProcessorCpuSetSnapshot? cpuSets)
```

Group by `PhysicalCoreIndex`; rank within a core by existing availability semantics, then `ObservedPressureScore`, then processor group/number.

- [ ] **Step 4: Stage A screens deterministic-shuffled representatives only.**

Persist realized order in the report through existing `ShuffleSeed` plus `ValidatedProcessors`/pair order.

- [ ] **Step 5: Stage B refines only siblings of the best two cores plus a third within 1% of second place.**

A sibling already screened as representative is not duplicated. Cap selected physical cores at three.

- [ ] **Step 6: From all valid Stage A/B logical CPUs, select top two plus one within 1% of second place, cap three finalists.**

Do not use 0.1% Low to rank the short screen.

- [ ] **Step 7: Run the focused tests.**

```powershell
dotnet test tests/LatencyPilot.CriticalTests/LatencyPilot.CriticalTests.csproj --configuration Release --filter "GpuAffinityCandidatePlannerTests|GpuAutoAffinitySessionTests"
```

- [ ] **Step 8: Commit.**

```bash
git add src/LatencyPilot.Benchmarking/Candidates/GpuAffinityCandidatePlanner.cs src/LatencyPilot.Benchmarking/Optimization/GpuAutoAffinitySession.cs tests/LatencyPilot.CriticalTests/GpuAffinityCandidatePlannerTests.cs tests/LatencyPilot.CriticalTests/GpuAutoAffinitySessionTests.cs
git commit -m "feat(gpu): screen physical cores before SMT refinement"
```

---

### Task 4: Add three-pair finalist confirmation and practical-tie decision

**Files:**
- Modify: `src/LatencyPilot.Benchmarking/Optimization/GpuAutoAffinitySession.cs`
- Test: `tests/LatencyPilot.CriticalTests/GpuAutoAffinitySessionTests.cs`
- Modify: `src/LatencyPilot.Core/Benchmarking/GpuAutoAffinityReport.cs` only if Task 1's finalist record needs a final field adjustment.

**Interfaces:**
- Produces: `GpuAutoAffinityFinalistReport[]`, winner/tie/no-winner authority.
- Consumes: valid short-screen candidate shortlist from Task 3.

- [ ] **Step 1: Modify the existing session test to model three independent finalist pairs.**

Cover:

```text
3 valid 30 s pairs per finalist
>=2/3 primary effects positive
median 1%-low effect > max(1%, initial Original noise, median pair movement)
material negative primary pair beyond floor -> reject
AVG/p99 existing noise-aware guardrails remain active
<=1% finalist difference -> practical tie
```

- [ ] **Step 2: Run focused session test and confirm red.**

```powershell
dotnet test tests/LatencyPilot.CriticalTests/LatencyPilot.CriticalTests.csproj --configuration Release --filter "GpuAutoAffinitySessionTests"
```

- [ ] **Step 3: Separate screening and finalist durations in the request.**

Change request shape to carry both durations explicitly:

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

Gate A runner will pass 10 seconds and 30 seconds respectively.

- [ ] **Step 4: Replace v1 `RescreenTopCandidatesAsync` cluster logic with three paired finalist rounds.**

Each finalist gets one pair per round; shuffle finalist order deterministically for every round. A locally unstable pair gets the same one-retry policy as screening. A finalist without three valid pairs after bounded retry becomes Inconclusive.

- [ ] **Step 5: Rank by median paired 1%-Low effect and apply practical-tie semantics.**

If multiple improvement-capable finalists are within 1%, mark `PracticalTie=true`; choose operational target using existing passive topology/pressure ordering, but preserve tie wording in report.

- [ ] **Step 6: Keep existing final apply + kernel-ETW target-only placement proof unchanged for full scope.**

No ETW proof means RestoreOriginal, regardless of benchmark winner.

- [ ] **Step 7: Run focused tests.**

```powershell
dotnet test tests/LatencyPilot.CriticalTests/LatencyPilot.CriticalTests.csproj --configuration Release --filter "GpuAutoAffinitySessionTests|GpuRuntimePlacementContractTests"
```

- [ ] **Step 8: Commit.**

```bash
git add src/LatencyPilot.Benchmarking/Optimization/GpuAutoAffinitySession.cs src/LatencyPilot.Core/Benchmarking/GpuAutoAffinityReport.cs tests/LatencyPilot.CriticalTests/GpuAutoAffinitySessionTests.cs
git commit -m "feat(gpu): confirm finalists with repeated local pairs"
```

---

### Task 5: Add privileged custom-CPU scope and hard safety semantics

**Files:**
- Modify: `tools/LatencyPilot.GateAValidation/GpuAutoAffinityGateARunner.cs`
- Modify: `tools/LatencyPilot.GateAValidation/GpuGateAProgressFile.cs`
- Test: `tests/LatencyPilot.CriticalTests/GpuMeasurementBootstrapContractTests.cs` or existing Gate A audit owner

**Interfaces:**
- Consumes CLI: `--candidate-cpus 4,8,12,6`.
- Produces `GpuAutoAffinitySessionRequest.SearchScope=Custom`, validated processor list, `GateAClosureEligible=false` for custom runs.

- [ ] **Step 1: Extend an existing Gate A/measurement audit case for custom-scope safety.**

Pin Review Focus #1 and #5:

```text
empty/malformed/duplicate CPU list -> reject before mutation
out-of-range/ineligible CPU -> reject entire custom run before mutation
fresh helper topology is authority, not UI
custom scope -> GateAClosureEligible false
custom scope -> no Keep path
custom scope -> exact Original restored
```

- [ ] **Step 2: Run the focused test and confirm red.**

```powershell
dotnet test tests/LatencyPilot.CriticalTests/LatencyPilot.CriticalTests.csproj --configuration Release --filter "GpuMeasurementBootstrapContractTests|GateAResultCompletionContractTests"
```

- [ ] **Step 3: Extend `AutoOptions` with the optional candidate list.**

Add `--candidate-cpus` to known options. Parse comma-separated group-0 processor numbers with invariant integer parsing. Reject whitespace-only tokens, duplicates and values outside `0..63` at argument parsing; fresh topology/eligibility is still checked later.

```csharp
private sealed record AutoOptions(
    ...,
    bool AllowDirtyDevelopmentSource,
    IReadOnlyList<byte>? CandidateProcessors);
```

- [ ] **Step 4: Revalidate requested CPUs after fresh topology and CPU-set capture.**

Build the normal eligible list with `GpuAffinityCandidatePlanner.Create(...)`; map requested processor numbers to exact `LogicalProcessorId(0, number)`. If any requested processor is not in the fresh eligible set, throw before progress initialization and before creating the mutation backend.

- [ ] **Step 5: Create the session request with explicit scope and durations.**

```csharp
var scope = options.CandidateProcessors is null
    ? GpuAutoAffinitySearchScope.Full
    : GpuAutoAffinitySearchScope.Custom;

var request = new GpuAutoAffinitySessionRequest(
    options.SessionId,
    topology,
    pressure,
    cpuSets,
    shuffleSeed,
    TimeSpan.FromSeconds(10),
    TimeSpan.FromSeconds(30),
    scope,
    validatedProcessors);
```

For custom scope, session candidate source is exactly the validated requested processors; do not run physical-core representative/refinement expansion outside that subset.

- [ ] **Step 6: Enforce custom RestoreOriginal semantics twice.**

Session layer must not call `KeepAsync` in custom scope. Runner/report layer must additionally force:

```csharp
GateAClosureEligible = false;
FullTopologyCoverage = false;
```

and reject any impossible custom `KeepCandidate` result as a structural contract failure before writing the report.

- [ ] **Step 7: Make custom progress explicit.**

Progress initialization includes `Full search` or `Custom: N CPUs`; candidate total reflects the actual validated scope.

- [ ] **Step 8: Run focused tests.**

```powershell
dotnet test tests/LatencyPilot.CriticalTests/LatencyPilot.CriticalTests.csproj --configuration Release --filter "GpuMeasurementBootstrapContractTests|GateAResultCompletionContractTests|GpuAutoAffinitySessionTests"
```

- [ ] **Step 9: Commit.**

```bash
git add tools/LatencyPilot.GateAValidation/GpuAutoAffinityGateARunner.cs tools/LatencyPilot.GateAValidation/GpuGateAProgressFile.cs tests/LatencyPilot.CriticalTests/GpuMeasurementBootstrapContractTests.cs tests/LatencyPilot.CriticalTests/GateAResultCompletionContractTests.cs
git commit -m "feat(gatea): add safe custom CPU diagnostic scope"
```

---

### Task 6: Add the CPU selection GUI beside Gate A

**Files:**
- Modify: `src/LatencyPilot.App/GateAValidationExperience.cs`
- Avoid `MainWindow.xaml` unless compilation proves dynamic host insertion insufficient.
- Test: existing source-contract/audit test only if a durable GUI safety string/argument contract already has an owner.

**Interfaces:**
- Produces ephemeral `HashSet<byte> _gateASelectedProcessors` plus `bool _gateACustomScope`.
- Consumes fresh `ProcessorTopologyReader.Capture()`, best-effort `ProcessorCpuSetReader.Capture()`, and existing `DeveloperValidationHost`.

- [ ] **Step 1: Add ephemeral selection state and a compact scope button beside `Run Gate A`.**

```csharp
private Button? _gateACpuScopeButton;
private bool _gateACustomScope;
private readonly HashSet<byte> _gateASelectedProcessors = [];
```

Default label: `All CPUs ▾`. Custom label: `4 CPUs ▾`. It resets on app restart by design; no settings/SQLite persistence.

- [ ] **Step 2: Build the `ContentDialog` from fresh topology at open time.**

Use WinUI controls already in the app. Dialog content should contain:

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

Rows are grouped by `PhysicalCoreIndex`; each checkbox/tag stores the exact logical processor number. Ineligible CPU-set entries are disabled and show a short tooltip/reason. Do not infer SMT by parity.

- [ ] **Step 3: Validate dialog acceptance.**

`Selected CPUs` mode requires at least one eligible checked CPU. `Use selection` updates only in-memory state. Cancel changes nothing.

- [ ] **Step 4: Update Gate A card copy and accessible names.**

Custom state:

```text
Custom diagnostic scope: CPU 4, CPU 6, CPU 8, CPU 12. Original will be restored; this run cannot close Gate A.
```

Run button becomes `Run selected CPUs`; AutomationProperties help text must include diagnostic-only semantics.

- [ ] **Step 5: Pass the exact subset to the elevated helper.**

Only in custom mode:

```csharp
helperArguments.Add("--candidate-cpus");
helperArguments.Add(string.Join(',', _gateASelectedProcessors.Order()));
```

The helper remains the authority and revalidates everything again.

- [ ] **Step 6: Make busy/source-state transitions disable both buttons consistently.**

During measurement, source refresh or Gate A execution, scope selection cannot change underneath a running session.

- [ ] **Step 7: Build App locally and inspect render.**

```powershell
dotnet build src/LatencyPilot.App/LatencyPilot.App.csproj --configuration Debug --nologo
```

Then inspect on the owner Windows machine at normal scaling and keyboard navigation. Required visual/accessibility checks:

```text
scope button remains beside Gate A without stretching the card
ContentDialog fits 16 logical CPUs without horizontal clipping
physical-core grouping is visually clear
selected/ineligible states have non-color cues
tab/space/enter navigation works
High Contrast does not hide selection state
```

Hosted CI cannot close this visual check.

- [ ] **Step 8: Commit.**

```bash
git add src/LatencyPilot.App/GateAValidationExperience.cs
git commit -m "feat(gatea): add custom CPU selection dialog"
```

---

### Task 7: Render v2 paired evidence without inventing a second ranking

**Files:**
- Modify: `src/LatencyPilot.Benchmarking/Optimization/GateAResultPresentation.cs`
- Modify: `src/LatencyPilot.App/GpuOptimizationProgressWindow.xaml`
- Modify: `src/LatencyPilot.App/GpuOptimizationProgressWindow.xaml.cs`
- Test: `tests/LatencyPilot.CriticalTests/GateAResultCompletionContractTests.cs`

**Interfaces:**
- Consumes `GpuAutoAffinityReport.Pairs`, `.Finalists`, `.SearchScope`, `.PracticalTie`, final recommendation/state.
- Produces result rows/cards only; never decision logic.

- [ ] **Step 1: Change the existing completion/result contract test first.**

Require presentation to use persisted pair/finalist authority and custom wording. Forbid independent ordering by raw/normalized FPS as a winner source.

Expected status vocabulary:

```text
Winner
Practical tie
No measured winner
Inconclusive
Custom diagnostic result
Best within selected CPUs
Original restored
```

- [ ] **Step 2: Run the focused test and verify red.**

```powershell
dotnet test tests/LatencyPilot.CriticalTests/LatencyPilot.CriticalTests.csproj --configuration Release --filter "GateAResultCompletionContractTests"
```

- [ ] **Step 3: Update presentation model.**

Each screening result row/card must show persisted values equivalent to:

```text
CPU 8 · Core 4
Original before     198.2 FPS
Candidate           214.7 FPS
Original after      201.0 FPS
Local effect        +7.6%
Control movement    1.4% / 6.0% budget
Status              Valid / Unstable / Inconclusive
```

No pseudo-normalized FPS.

- [ ] **Step 4: Update finalist presentation.**

Show three paired primary effects and median. For custom scope, show only `Best within selected CPUs` and terminal `Original restored`; never `Winner` machine-wide.

- [ ] **Step 5: Apply color semantics from the spec.**

Green is reserved for verified Keep or verified restored-safe terminal state. Diagnostic leaders use accent/blue; uncertainty/tie uses attention; failure styling means actual integrity/recovery failure.

- [ ] **Step 6: Run focused tests and local App build.**

```powershell
dotnet test tests/LatencyPilot.CriticalTests/LatencyPilot.CriticalTests.csproj --configuration Release --filter "GateAResultCompletionContractTests"
dotnet build src/LatencyPilot.App/LatencyPilot.App.csproj --configuration Debug --nologo
```

- [ ] **Step 7: Commit.**

```bash
git add src/LatencyPilot.Benchmarking/Optimization/GateAResultPresentation.cs src/LatencyPilot.App/GpuOptimizationProgressWindow.xaml src/LatencyPilot.App/GpuOptimizationProgressWindow.xaml.cs tests/LatencyPilot.CriticalTests/GateAResultCompletionContractTests.cs
git commit -m "feat(gatea): present paired GPU affinity evidence"
```

---

### Task 8: Reconcile canonical documentation and status

**Files:**
- Create: `docs/adr/0007-paired-local-control-gpu-affinity-v2.md`
- Modify: `docs/BENCHMARK_METHODOLOGY.md`
- Modify: `docs/superpowers/plans/2026-09-19-gpu-measurement-stability.md`
- Modify: `PROJECT_STATUS.md`
- Modify: `ROADMAP.md`

**Interfaces:**
- Documents actual source contract from Tasks 1–7; does not claim physical validation that has not happened.

- [ ] **Step 1: Write ADR 0007 as the accepted owner-directed replacement for ADR 0006 measurement/ranking sections only.**

Record:

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

Also cite Microsoft interrupt-affinity semantics: `IrqPolicySpecifiedProcessors` + `AssignmentSetOverride` is a KAFFINITY set within a processor group; do not broaden the current mutation contract beyond what Windows documents.

- [ ] **Step 2: Update benchmark methodology and mark the 2026-09-19 plan superseded by this v2 implementation plan for active measurement logic.**

Preserve old hardware runs as historical v1 evidence; do not rewrite them under v2.

- [ ] **Step 3: Update PROJECT_STATUS execution ladder.**

After software implementation, the next physical sequence is:

```text
local build/render inspection
custom v2 run on owner-selected CPUs (initially 4,6,8,12 is a useful probe, not a winner claim)
full v2 search
repeat full v2 search
Stop safely + one supported failure/recovery exercise
```

- [ ] **Step 4: Update ROADMAP only where Gate A evidence wording still assumes v1 block normalization.**

Do not close physical Gate A in docs.

- [ ] **Step 5: Commit.**

```bash
git add docs/adr/0007-paired-local-control-gpu-affinity-v2.md docs/BENCHMARK_METHODOLOGY.md docs/superpowers/plans/2026-09-19-gpu-measurement-stability.md PROJECT_STATUS.md ROADMAP.md
git commit -m "docs: adopt paired local-control GPU affinity v2"
```

---

### Task 9: Full software verification, step-back review, and exact-head delivery

**Files:**
- Review all files changed by Tasks 1–8.
- No new source file unless verification exposes a real missing boundary.

**Interfaces:** none; this task verifies the integrated contract.

- [ ] **Step 1: Run the complete permanent test suite.**

```powershell
dotnet test tests/LatencyPilot.CriticalTests/LatencyPilot.CriticalTests.csproj --configuration Release
```

Expected: all permanent tests pass; permanent test count remains within owner cap and every test file remains <=1200 lines.

- [ ] **Step 2: Build the relevant non-packaged projects locally on Windows.**

```powershell
dotnet build src/LatencyPilot.Benchmarking/LatencyPilot.Benchmarking.csproj --configuration Release --nologo
dotnet build tools/LatencyPilot.GateAValidation/LatencyPilot.GateAValidation.csproj --configuration Release --nologo
dotnet build src/LatencyPilot.App/LatencyPilot.App.csproj --configuration Debug --nologo
```

- [ ] **Step 3: Run the mandatory step-back review.**

Check the exact hidden assumptions:

```text
Does every mutation still have journal ownership and exact rollback?
Can an invalid OriginalAfter become the next pair anchor?
Can custom scope ever call KeepAsync or set closure eligible?
Can UI selection bypass helper revalidation?
Can full search accidentally skip an entire physical core because of SMT numbering assumptions?
Can a v1 normalized field still influence v2 ranking/presentation?
Does Stop safely restore Original from every new phase?
```

Fix any discovered contradiction before proceeding.

- [ ] **Step 4: Review the diff against the approved spec and remove dead v1 measurement code that no longer has a consumer.**

Do not delete historical report fields solely to make the code prettier if old report reading still needs them; remove only dead active-method code.

- [ ] **Step 5: Push/confirm `main` and require hosted Tests on the exact final HEAD.**

Hosted CI is software-contract evidence only. Record exact HEAD + Tests run in `PROJECT_STATUS.md` if status is updated after CI.

- [ ] **Step 6: Do not claim v2 measurement closed yet.**

Required owner-hardware evidence remains:

```text
quick custom run using exact selected CPUs
full v2 search
repeat full v2 search
Stop safely
one supported failure/recovery path
render/accessibility inspection
```

---

## Physical execution after software delivery

This section is not a coding task; it is the evidence sequence once Task 9 is green.

1. Launch the app on the exact clean green revision.
2. In Measure -> Developer validation, open the CPU-scope button beside Gate A.
3. Select a small diagnostic set such as CPUs `4, 6, 8, 12` (or another exact set chosen from prior evidence).
4. Run selected CPUs. Expected terminal truth: custom diagnostic result + exact Original restored, regardless of which selected CPU leads.
5. Inspect the uploaded v2 report for raw O-C-O values, pair movement, retry behavior and absence of pseudo-normalized FPS.
6. If the method behaves coherently, switch scope back to All eligible CPUs and run the full v2 search.
7. Repeat the full search once with a new recorded shuffle seed.
8. Only if winner/practical-tie evidence reproduces and final ETW placement is proven may full-mode Keep be considered physical decision evidence.
9. Then exercise Stop safely and one supported failure/recovery path; require exact terminal state and unresolved journal count 0.
