# GPU Go Mode Source Completion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Complete the repository-verifiable GPU optimization decision/orchestration layer from authoritative baseline evidence through bounded screening, balanced finalist confirmation and a safe Keep/Restore recommendation while public mutation remains unarmed.

**Architecture:** `LatencyPilot.Benchmarking` owns ranking and evidence interpretation. Existing candidate planning, `BenchmarkComparer`, ETW evidence and PresentMon capture are reused. Service mutation code remains a narrow executor and protocol v6 remains read-only until physical Gate A permits a later protocol-v7 tranche.

**Tech Stack:** C# 14, .NET 10 LTS, LatencyPilot.Core metric/device models, LatencyPilot.Benchmarking, MSTest/Microsoft.Testing.Platform.

**Spec:** `docs/superpowers/specs/2026-09-14-product-1.0-source-completion-design.md`

## Global Constraints

- Work directly on `main`.
- Public protocol stays v6/read-only in this plan.
- `ServiceBoundary.MutationAvailable` stays `false`.
- Permanent tests stay at 9/10; extend an existing high-blast-radius test instead of adding another test method.
- Hosted CI stays test-only.
- No hardware improvement, restart, runtime placement or PresentMon availability claim is inferred from hosted CI.
- Missing target or guardrail evidence fails closed; no weighted composite score.

---

### Task 1: Define the optimizer decision contract with a failing critical test

**Files:**
- Modify: `tests/LatencyPilot.CriticalTests/CriticalPathTests.cs`

**Interfaces:**
- Consumes existing `GpuAffinityCandidate`, `MetricSeries`, `ComparisonPolicy`, `ExperimentVerdict`.
- Specifies `GpuOptimizationMeasurementSet`, `GpuOptimizationCandidateMeasurement`, `GpuOptimizationDecisionEngine`, `GpuOptimizationRecommendation`, `GpuConfirmationOrder`.

- [ ] **Step 1: Extend `BenchmarkVerdictMatrixPreservesPrimaryAndGuardrailSemantics` with optimizer assertions**

Add `using LatencyPilot.Benchmarking.Optimization;` and assertions that define these behaviors:

```csharp
var original = new GpuOptimizationMeasurementSet(
    Series("DPC p99", 100),
    new Dictionary<string, MetricSeries>(StringComparer.Ordinal)
    {
        ["Frame time"] = Series("Frame time", 10),
    });

var clean = new GpuOptimizationCandidateMeasurement(
    new GpuAffinityCandidate(1, new LogicalProcessorId(0, 2), 0, true, 0.05),
    new GpuOptimizationMeasurementSet(
        Series("DPC p99", 85),
        new Dictionary<string, MetricSeries>(StringComparer.Ordinal)
        {
            ["Frame time"] = Series("Frame time", 10.1),
        }));

var tradeoff = new GpuOptimizationCandidateMeasurement(
    new GpuAffinityCandidate(2, new LogicalProcessorId(0, 4), 0, true, 0.02),
    new GpuOptimizationMeasurementSet(
        Series("DPC p99", 75),
        new Dictionary<string, MetricSeries>(StringComparer.Ordinal)
        {
            ["Frame time"] = Series("Frame time", 12),
        }));

var screening = GpuOptimizationDecisionEngine.Screen(original, [tradeoff, clean], Policy);
Assert.AreEqual(clean.Candidate, screening.Finalist?.Candidate);
Assert.AreEqual(GpuOptimizationRecommendation.KeepCandidate, screening.Recommendation);
Assert.AreEqual(ExperimentVerdict.Tradeoff, screening.Evaluations[0].Comparison.Verdict);
Assert.AreEqual(ExperimentVerdict.Improved, screening.Evaluations[1].Comparison.Verdict);

CollectionAssert.AreEqual(
    new[]
    {
        GpuConfirmationOrder.Original,
        GpuConfirmationOrder.Candidate,
        GpuConfirmationOrder.Candidate,
        GpuConfirmationOrder.Original,
        GpuConfirmationOrder.Candidate,
        GpuConfirmationOrder.Original,
        GpuConfirmationOrder.Original,
        GpuConfirmationOrder.Candidate,
    },
    GpuOptimizationDecisionEngine.CreateBalancedConfirmationSchedule().ToArray());
```

Also assert that a missing baseline guardrail in a candidate produces an Inconclusive evaluation and cannot become the finalist.

- [ ] **Step 2: Run hosted Tests and verify RED**

Expected: compile failure because the `LatencyPilot.Benchmarking.Optimization` contract does not exist.

- [ ] **Step 3: Record the RED run in the plan/status evidence notes**

Do not change the permanent test-method count.

---

### Task 2: Implement fail-closed GPU candidate screening

**Files:**
- Create: `src/LatencyPilot.Benchmarking/Optimization/GpuOptimizationModels.cs`
- Create: `src/LatencyPilot.Benchmarking/Optimization/GpuOptimizationDecisionEngine.cs`

**Interfaces:**
- Produces:

```csharp
public sealed record GpuOptimizationMeasurementSet(
    MetricSeries Primary,
    IReadOnlyDictionary<string, MetricSeries> Guardrails);

public sealed record GpuOptimizationCandidateMeasurement(
    GpuAffinityCandidate Candidate,
    GpuOptimizationMeasurementSet Measurement);

public sealed record GpuOptimizationCandidateEvaluation(
    GpuAffinityCandidate Candidate,
    ComparisonResult Comparison);

public enum GpuOptimizationRecommendation
{
    RestoreOriginal,
    KeepCandidate,
}

public enum GpuConfirmationOrder
{
    Original,
    Candidate,
}

public sealed record GpuOptimizationScreeningResult(
    IReadOnlyList<GpuOptimizationCandidateEvaluation> Evaluations,
    GpuOptimizationCandidateEvaluation? Finalist,
    GpuOptimizationRecommendation Recommendation,
    string Reason);
```

- [ ] **Step 1: Implement measurement validation**

Require non-null primary/guardrails, exact guardrail-key parity between original and every candidate, and matching metric name/direction for same-named guardrails. If evidence shape is incomplete, produce an Inconclusive `ComparisonResult`; do not throw for an ordinary missing candidate guardrail.

- [ ] **Step 2: Implement per-candidate comparison**

Use `BenchmarkComparer.Compare` with original primary vs candidate primary and the exact named guardrail pairs. Do not duplicate threshold/verdict math.

- [ ] **Step 3: Implement finalist eligibility and ranking**

Only `ExperimentVerdict.Improved` with no regressed guardrails is eligible. Rank by greatest finite `RelativeImprovement`, then `PhysicalCoreIndex`, processor group and processor number for deterministic ties.

- [ ] **Step 4: Implement recommendation**

A clean finalist yields `KeepCandidate`. No eligible finalist yields `RestoreOriginal`, preserving all candidate evaluations and an explanatory reason.

- [ ] **Step 5: Implement balanced confirmation schedule**

Return fixed `ABBA + BAAB` order:

```text
Original, Candidate, Candidate, Original,
Candidate, Original, Original, Candidate
```

The method schedules evidence collection only; it does not claim confirmation without actual measurements.

- [ ] **Step 6: Run hosted Tests and verify GREEN**

Expected: existing permanent total remains 9 and all pass.

- [ ] **Step 7: Commit**

Commit message: `feat(optimizer): add GPU candidate decision engine`

---

### Task 3: Convert PresentMon snapshots into explicit guardrail metrics

**Files:**
- Create: `src/LatencyPilot.Benchmarking/Optimization/PresentMonGuardrailSeriesBuilder.cs`
- Modify: `tests/LatencyPilot.CriticalTests/CriticalPathTests.cs` within the existing verdict-matrix method only.

**Interfaces:**
- Consumes: `PresentMonWorkloadMetricsSnapshot` from Core.
- Produces:

```csharp
public static IReadOnlyDictionary<string, MetricSeries> Create(
    IEnumerable<PresentMonWorkloadMetricsSnapshot> windows);
```

- [ ] **Step 1: Add RED assertions inside the existing test method**

Use two or more synthetic available PresentMon windows and require named LowerIsBetter series for available metrics such as CPU frame time, GPU latency, display latency and dropped-frame ratio. Require absent optional metrics to stay absent. Require unavailable/invalid snapshots to contribute no fabricated sample.

- [ ] **Step 2: Verify RED in hosted Tests**

Expected: missing builder compile failure.

- [ ] **Step 3: Implement minimal builder**

For each available snapshot, derive one representative value per metric from its swapchains using the worst finite observed value for latency/drop guardrails and do not synthesize zero for absent data. Only return a metric series when at least one finite sample exists. Keep displayed/presented FPS out of automatic LowerIsBetter guardrails unless represented separately with `HigherIsBetter`.

- [ ] **Step 4: Verify GREEN**

All 9 permanent tests pass.

- [ ] **Step 5: Commit**

Commit message: `feat(optimizer): add PresentMon guardrail series`

---

### Task 4: Add a source-level Go plan to the App without arming mutation

**Files:**
- Create: `src/LatencyPilot.App/GpuOptimizationPlanExperience.cs`
- Modify: `src/LatencyPilot.App/GpuAffinityCandidatePreparation.cs`
- Modify only the minimum required XAML/text in `src/LatencyPilot.App/MainWindow.xaml` if the plan can be exposed without implying execution.

**Interfaces:**
- Consumes the authoritative Real-world baseline and `_latestGpuAffinityCandidates`.
- Produces an in-memory/read-only `GpuOptimizationPlanPreview` that describes candidate count, exact candidate processors, required ABBA/BAAB confirmation and why execution is locked while mutation is unavailable.

- [ ] **Step 1: Keep the UI state explicitly locked**

Any Go/Optimize surface must state that optimization execution is unavailable until physical safety validation/arming. Do not wire a mutation click path.

- [ ] **Step 2: Compose existing candidate data into a deterministic preview**

Do not duplicate candidate generation. The preview is invalidated when baseline/scenario/source topology changes.

- [ ] **Step 3: Perform source review for WinUI compile risks**

Because hosted CI is test-only and cannot prove App build, keep XAML/code-behind edits minimal and mark owner-local App build as required evidence.

- [ ] **Step 4: Commit**

Commit message: `feat(app): surface locked GPU optimization plan`

---

### Task 5: Reconcile live status after Go source completion

**Files:**
- Modify: `PROJECT_STATUS.md`
- Modify: `ROADMAP.md`
- Modify: `docs/OPTIMIZER_TARGET_GRAPH.md`
- Modify: this plan

**Interfaces:** live execution ladder.

- [ ] **Step 1: Mark only source-proven items complete**

Candidate screening/decision scheduling/PresentMon guardrail conversion may be source-complete after CI evidence. Do not check physical runtime placement, mutation IPC, restart/rollback, or user-facing arming.

- [ ] **Step 2: Record exact commits and CI runs**

Include RED/GREEN evidence and final 9-test count.

- [ ] **Step 3: Set the next source slice to USB/xHCI/input analysis**

Keep Gate A as the next owner-local mutation-safety action in parallel.

- [ ] **Step 4: Final test-only CI on exact HEAD**

Require success before calling this source slice complete.
