using LatencyPilot.Core.Benchmarking;
using LatencyPilot.Core.System;

namespace LatencyPilot.Benchmarking.Optimization;

public enum GateAMetricState
{
    Unavailable,
    DiagnosticOnly,
    Improved,
    DecisionGuardrailSatisfied,
}

public sealed record GateAMetricComparison(
    string Key,
    string Label,
    string Unit,
    double? OriginalValue,
    double? CandidateValue,
    double? ImprovementFraction,
    double UncertaintyFraction,
    GateAMetricState State,
    bool LowerIsBetter);

public sealed record GateACandidateBar(
    LogicalProcessorId Processor,
    int PhysicalCoreIndex,
    int? DecisionRank,
    double OnePercentLowFps,
    double? AvgFps,
    double? FrameP99Milliseconds,
    double? Low01PctFps,
    int TrialCount,
    string Verdict,
    string StateLabel,
    double? LocalControlUncertainty,
    bool IsKept,
    bool IsCompared);

public sealed record GateATrialPoint(
    int RunNumber,
    string Series,
    LogicalProcessorId? Processor,
    double OnePercentLowFps,
    string Phase,
    string ReadinessState);

public sealed record GateADecisionEvidenceRow(
    string Label,
    string State,
    string Detail);

public sealed record GateAResultViewModel(
    string Title,
    string Summary,
    string EligibilityLabel,
    bool GateAClosureEligible,
    LogicalProcessorId? ComparedProcessor,
    bool ComparedCandidateIsDiagnosticOnly,
    string ComparedCandidateLabel,
    IReadOnlyList<GateAMetricComparison> Metrics,
    IReadOnlyList<GateACandidateBar> Candidates,
    IReadOnlyList<GateATrialPoint> Trials,
    IReadOnlyList<GateADecisionEvidenceRow> DecisionEvidence,
    string SessionDirectory,
    string ReportPath,
    string? ZipPath,
    string BundleStatus,
    string SourceRevision,
    TimeSpan Duration)
{
    public bool BundleAvailable => !string.IsNullOrWhiteSpace(ZipPath);
}

public static class GateAResultPresentation
{
    public static GateAResultViewModel Create(
        GpuAutoAffinityReport report,
        string sessionDirectory,
        string reportPath,
        GateAEvidenceBundleExportResult bundle)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(reportPath);
        ArgumentNullException.ThrowIfNull(bundle);

        var recommendationKeepsCandidate = string.Equals(
            report.FinalRecommendation,
            GpuOptimizationRecommendation.KeepCandidate.ToString(),
            StringComparison.Ordinal) && report.FinalProcessor is not null;
        var verifiedKeep = recommendationKeepsCandidate && report.FinalStateVerified;
        var compared = SelectComparedCandidate(report, recommendationKeepsCandidate);
        var comparedProcessor = compared?.Processor;
        var diagnosticOnly = compared is not null && !verifiedKeep;
        var original = OriginalMetrics.From(report.DecisionBaseline);
        var localControlUncertainty = SanitizeUncertainty(compared?.LocalControlUncertainty);
        var metrics = BuildMetricComparisons(
            original,
            compared,
            localControlUncertainty,
            verifiedKeep);
        var candidateBars = BuildCandidateBars(report, comparedProcessor, verifiedKeep);
        var trialPoints = BuildTrialPoints(report.Trials, comparedProcessor);
        var decisionRows = BuildDecisionRows(report, compared, metrics, verifiedKeep);

        var title = verifiedKeep
            ? $"CPU {report.FinalProcessor!.Value.Number} kept"
            : report.FinalStateVerified && report.OriginalStateRestored
                ? "Original kept"
                : "Result needs attention";
        var summary = BuildSummary(report, compared, verifiedKeep);
        var eligibilityLabel = report.GateAClosureEligible
            ? "Closure eligible"
            : "Development evidence";
        var comparedLabel = compared is null
            ? "No authoritative comparison candidate"
            : verifiedKeep
                ? $"CPU {compared.Processor.Number} · kept"
                : $"CPU {compared.Processor.Number} · best measured · not kept";
        var bundleStatus = bundle.Succeeded
            ? "Shareable evidence ZIP is ready."
            : string.IsNullOrWhiteSpace(bundle.Error)
                ? "Evidence bundle could not be packaged."
                : $"Evidence bundle could not be packaged: {bundle.Error}";
        var sourceRevision = report.Provenance?.SourceRevisionId;
        if (string.IsNullOrWhiteSpace(sourceRevision))
        {
            sourceRevision = "Source revision unavailable";
        }

        return new GateAResultViewModel(
            title,
            summary,
            eligibilityLabel,
            report.GateAClosureEligible,
            comparedProcessor,
            diagnosticOnly,
            comparedLabel,
            metrics,
            candidateBars,
            trialPoints,
            decisionRows,
            Path.GetFullPath(sessionDirectory),
            Path.GetFullPath(reportPath),
            bundle.Succeeded ? Path.GetFullPath(bundle.ZipPath!) : null,
            bundleStatus,
            sourceRevision,
            report.EndedAtUtc >= report.StartedAtUtc
                ? report.EndedAtUtc - report.StartedAtUtc
                : TimeSpan.Zero);
    }

    private static GpuAutoAffinityCandidateReport? SelectComparedCandidate(
        GpuAutoAffinityReport report,
        bool recommendationKeepsCandidate)
    {
        if (recommendationKeepsCandidate && report.FinalProcessor is { } finalProcessor)
        {
            return report.Candidates
                .Where(candidate => candidate.Processor == finalProcessor && HasDecisionMetrics(candidate))
                .LastOrDefault();
        }

        // Candidate collection order is measurement order and is deliberately
        // shuffled. A diagnostic comparison is only safe when the optimizer has
        // persisted an explicit decision rank; never infer a winner from list order.
        return report.Candidates
            .Where(static candidate =>
                candidate.DecisionRank == 1 &&
                string.Equals(candidate.Verdict, "Ranked", StringComparison.OrdinalIgnoreCase))
            .Where(HasDecisionMetrics)
            .OrderByDescending(static candidate =>
                string.Equals(candidate.Phase, "screening-finalists", StringComparison.Ordinal))
            .FirstOrDefault();
    }

    private static bool HasDecisionMetrics(GpuAutoAffinityCandidateReport candidate) =>
        IsFinitePositive(candidate.DecisionOnePercentLowFps) ||
        IsFinitePositive(candidate.DecisionAvgFps) ||
        IsFinitePositive(candidate.DecisionFrameP99Milliseconds) ||
        IsFinitePositive(candidate.DecisionLow01PctFps);

    private static GateAMetricComparison[] BuildMetricComparisons(
        OriginalMetrics original,
        GpuAutoAffinityCandidateReport? candidate,
        double localControlUncertainty,
        bool verifiedKeep) =>
        [
            CreateMetric(
                "low1",
                "1% low",
                "FPS",
                original.OnePercentLowFps,
                candidate?.DecisionOnePercentLowFps,
                lowerIsBetter: false,
                localControlUncertainty,
                verifiedKeep,
                primaryMetric: true),
            CreateMetric(
                "avg",
                "Average",
                "FPS",
                original.AvgFps,
                candidate?.DecisionAvgFps,
                lowerIsBetter: false,
                localControlUncertainty,
                verifiedKeep,
                primaryMetric: false),
            CreateMetric(
                "p99",
                "Frame p99",
                "ms",
                original.FrameP99Milliseconds,
                candidate?.DecisionFrameP99Milliseconds,
                lowerIsBetter: true,
                localControlUncertainty,
                verifiedKeep,
                primaryMetric: false),
            CreateMetric(
                "low01",
                "0.1% low",
                "FPS",
                original.Low01PctFps,
                candidate?.DecisionLow01PctFps,
                lowerIsBetter: false,
                localControlUncertainty,
                verifiedKeep,
                primaryMetric: false),
        ];

    private static GateAMetricComparison CreateMetric(
        string key,
        string label,
        string unit,
        double? original,
        double? candidate,
        bool lowerIsBetter,
        double localControlUncertainty,
        bool verifiedKeep,
        bool primaryMetric)
    {
        if (!IsFinitePositive(original) || !IsFinitePositive(candidate))
        {
            return new GateAMetricComparison(
                key,
                label,
                unit,
                original,
                candidate,
                null,
                localControlUncertainty,
                GateAMetricState.Unavailable,
                lowerIsBetter);
        }

        var delta = lowerIsBetter
            ? (original!.Value - candidate!.Value) / original.Value
            : (candidate!.Value - original!.Value) / original.Value;
        var state = !verifiedKeep
            ? GateAMetricState.DiagnosticOnly
            : primaryMetric
                ? GateAMetricState.Improved
                : GateAMetricState.DecisionGuardrailSatisfied;
        return new GateAMetricComparison(
            key,
            label,
            unit,
            original,
            candidate,
            delta,
            localControlUncertainty,
            state,
            lowerIsBetter);
    }

    private static GateACandidateBar[] BuildCandidateBars(
        GpuAutoAffinityReport report,
        LogicalProcessorId? comparedProcessor,
        bool verifiedKeep)
    {
        var latestByProcessor = new Dictionary<LogicalProcessorId, GpuAutoAffinityCandidateReport>();
        var order = new List<LogicalProcessorId>();
        foreach (var candidate in report.Candidates)
        {
            if (!IsFinitePositive(candidate.DecisionOnePercentLowFps))
            {
                continue;
            }
            if (!latestByProcessor.ContainsKey(candidate.Processor))
            {
                order.Add(candidate.Processor);
            }
            latestByProcessor[candidate.Processor] = candidate;
        }

        return order.Select(processor =>
        {
            var candidate = latestByProcessor[processor];
            var isKept = verifiedKeep && report.FinalProcessor == processor;
            var isCompared = comparedProcessor == processor;
            var state = isKept
                ? "Kept"
                : isCompared
                    ? "Not kept"
                    : string.Equals(candidate.Verdict, "Inconclusive", StringComparison.OrdinalIgnoreCase)
                        ? "Inconclusive"
                        : "Tested";
            return new GateACandidateBar(
                processor,
                candidate.PhysicalCoreIndex,
                candidate.DecisionRank,
                candidate.DecisionOnePercentLowFps!.Value,
                candidate.DecisionAvgFps,
                candidate.DecisionFrameP99Milliseconds,
                candidate.DecisionLow01PctFps,
                candidate.TrialCount,
                candidate.Verdict,
                state,
                candidate.LocalControlUncertainty,
                isKept,
                isCompared);
        }).ToArray();
    }

    private static GateATrialPoint[] BuildTrialPoints(
        IReadOnlyList<GpuAutoAffinityTrialReport> trials,
        LogicalProcessorId? comparedProcessor) =>
        trials
            .Where(trial =>
                IsFinitePositive(trial.OnePercentLowFps) &&
                !trial.Phase.EndsWith("-warmup", StringComparison.Ordinal) &&
                (string.Equals(trial.Role, "Original", StringComparison.OrdinalIgnoreCase) ||
                 (comparedProcessor is not null && trial.Processor == comparedProcessor)))
            .OrderBy(static trial => trial.RunNumber)
            .Select(trial => new GateATrialPoint(
                trial.RunNumber,
                string.Equals(trial.Role, "Original", StringComparison.OrdinalIgnoreCase)
                    ? "Original"
                    : $"CPU {trial.Processor?.Number}",
                trial.Processor,
                trial.OnePercentLowFps!.Value,
                trial.Phase,
                trial.ReadinessState))
            .ToArray();

    private static GateADecisionEvidenceRow[] BuildDecisionRows(
        GpuAutoAffinityReport report,
        GpuAutoAffinityCandidateReport? compared,
        IReadOnlyList<GateAMetricComparison> metrics,
        bool verifiedKeep)
    {
        var primary = metrics.First(static metric => metric.Key == "low1");
        var primaryState = primary.State switch
        {
            GateAMetricState.Improved => "Passed",
            GateAMetricState.DiagnosticOnly => "Diagnostic only",
            _ => "Unavailable",
        };
        var repeatabilityState = compared is null
            ? "Unavailable"
            : verifiedKeep
                ? "Decision-grade"
                : "Diagnostic only";
        var guardrailState = compared is null
            ? "Unavailable"
            : verifiedKeep
                ? "Passed"
                : "Diagnostic only";
        var finalPlacement = report.Trials
            .Where(trial =>
                string.Equals(trial.Phase, "final-verification", StringComparison.Ordinal) &&
                (report.FinalProcessor is null || trial.Processor == report.FinalProcessor))
            .LastOrDefault();
        var placementState = finalPlacement?.Placement is { ConfirmsRequestedPlacement: true }
            ? "Passed"
            : report.FinalProcessor is null
                ? "Not required"
                : "Unavailable";
        var finalState = report.FinalStateVerified &&
                         (report.FinalProcessor is not null || report.OriginalStateRestored)
            ? "Passed"
            : "Needs attention";

        return
        [
            new GateADecisionEvidenceRow(
                "Primary improvement",
                primaryState,
                verifiedKeep
                    ? DescribeMetric(primary, "The optimizer's verified Keep means this primary metric cleared its full noise-aware decision threshold.")
                    : DescribeMetric(primary, "Measured delta only; this run did not establish a Keep decision for the comparison candidate.")),
            new GateADecisionEvidenceRow(
                "Repeatability / uncertainty",
                repeatabilityState,
                compared is null
                    ? "No authority-ranked comparison candidate is persisted in this report."
                    : verifiedKeep
                        ? $"The candidate survived the optimizer's repeatability and uncertainty gates. Local-control uncertainty recorded for presentation is {SanitizeUncertainty(compared.LocalControlUncertainty):P1}."
                        : $"The best measured candidate is diagnostic only. Its local-control uncertainty is {SanitizeUncertainty(compared.LocalControlUncertainty):P1}; no independent pass is inferred here."),
            new GateADecisionEvidenceRow(
                "Performance guardrails",
                guardrailState,
                compared is null
                    ? "Guardrail decision evidence is unavailable because no authority-ranked comparison candidate is persisted."
                    : verifiedKeep
                        ? "The verified Keep was emitted only after the optimizer accepted its AVG, frame-p99, 0.1%-low and interrupt-tail guardrails."
                        : "Measured guardrail values remain diagnostic because the run did not reach a verified Keep decision."),
            new GateADecisionEvidenceRow(
                "Runtime ISR placement",
                placementState,
                finalPlacement?.Placement is { } placement
                    ? $"Target ISR {placement.TargetIsrEventCount}; off-target ISR {placement.OffTargetIsrEventCount}."
                    : report.FinalProcessor is null
                        ? "Original policy was retained; no forced final processor needs Keep placement proof."
                        : "Final runtime placement proof is not available in the report."),
            new GateADecisionEvidenceRow(
                "Final machine state",
                finalState,
                report.FinalProcessor is not null
                    ? report.FinalStateVerified
                        ? $"CPU {report.FinalProcessor.Value.Number} is recorded as the verified terminal state."
                        : "The kept processor is not recorded as a verified terminal state."
                    : report.OriginalStateRestored && report.FinalStateVerified
                        ? "The exact original GPU affinity state was restored and verified."
                        : "The report does not prove a verified restored Original state."),
        ];
    }

    private static string BuildSummary(
        GpuAutoAffinityReport report,
        GpuAutoAffinityCandidateReport? compared,
        bool verifiedKeep)
    {
        if (verifiedKeep && report.FinalProcessor is { } processor)
        {
            return $"CPU {processor.Number} survived the benchmark decision gates and is the verified terminal GPU interrupt-affinity state. The evidence below shows the authority-selected baseline, measured deltas and runtime placement proof.";
        }

        if (report.OriginalStateRestored && report.FinalStateVerified)
        {
            return compared is null
                ? "LatencyPilot retained and verified the exact original GPU affinity policy. No authority-ranked comparison candidate is available for a trustworthy before/after claim in this report."
                : $"CPU {compared.Processor.Number} was the optimizer's best measured candidate, but it did not clear the full Keep gates. LatencyPilot retained and verified the exact original policy instead of turning uncertain evidence into a system change.";
        }

        return "Gate A produced a report, but the terminal machine state is not fully verified. Use the evidence and recovery status below before continuing.";
    }

    private static string DescribeMetric(GateAMetricComparison metric, string interpretation)
    {
        if (!IsFinitePositive(metric.OriginalValue) || !IsFinitePositive(metric.CandidateValue))
        {
            return $"{metric.Label} does not have enough authority-selected comparable evidence. {interpretation}";
        }
        var delta = metric.ImprovementFraction is null
            ? string.Empty
            : $" ({metric.ImprovementFraction.Value:+0.0%;-0.0%;0.0%} improvement-direction delta)";
        return $"Original {metric.OriginalValue:0.##} {metric.Unit} → candidate {metric.CandidateValue:0.##} {metric.Unit}{delta}; local-control uncertainty {metric.UncertaintyFraction:P1}. {interpretation}";
    }

    private static bool IsFinitePositive(double? value) =>
        value is > 0d && double.IsFinite(value.Value);

    private static double SanitizeUncertainty(double? value) =>
        value is >= 0d && double.IsFinite(value.Value) ? value.Value : 0d;

    private sealed record OriginalMetrics(
        double? OnePercentLowFps,
        double? AvgFps,
        double? FrameP99Milliseconds,
        double? Low01PctFps)
    {
        internal static OriginalMetrics From(GpuAutoAffinityDecisionBaselineReport? baseline) =>
            baseline is null
                ? new OriginalMetrics(null, null, null, null)
                : new OriginalMetrics(
                    baseline.OnePercentLowFps,
                    baseline.AvgFps,
                    baseline.FrameP99Milliseconds,
                    baseline.Low01PctFps);
    }
}
