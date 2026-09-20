using LatencyPilot.Core.Benchmarking;
using LatencyPilot.Core.System;

namespace LatencyPilot.Benchmarking.Optimization;

public enum GateAMetricState
{
    Unavailable,
    Improved,
    WithinMeasuredUncertainty,
    Regressed,
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
    private const double MinimumDisplayUncertainty = 0.01d;

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

        var kept = string.Equals(
            report.FinalRecommendation,
            GpuOptimizationRecommendation.KeepCandidate.ToString(),
            StringComparison.Ordinal) && report.FinalProcessor is not null;
        var compared = SelectComparedCandidate(report, kept);
        var comparedProcessor = compared?.Processor;
        var diagnosticOnly = !kept && compared is not null;
        var original = BuildOriginalMetrics(report.Trials);
        var uncertainty = Math.Max(
            MinimumDisplayUncertainty,
            SanitizeUncertainty(compared?.LocalControlUncertainty));
        var metrics = BuildMetricComparisons(original, compared, uncertainty);
        var candidateBars = BuildCandidateBars(report, comparedProcessor, kept);
        var trialPoints = BuildTrialPoints(report.Trials, comparedProcessor);
        var decisionRows = BuildDecisionRows(report, compared, metrics);

        var title = kept
            ? $"CPU {report.FinalProcessor!.Value.Number} kept"
            : report.FinalStateVerified && report.OriginalStateRestored
                ? "Original kept"
                : "Result needs attention";
        var summary = BuildSummary(report, compared, kept);
        var eligibilityLabel = report.GateAClosureEligible
            ? "Closure eligible"
            : "Development evidence";
        var comparedLabel = compared is null
            ? "No comparable candidate"
            : kept
                ? $"CPU {compared.Processor.Number} · kept"
                : $"CPU {compared.Processor.Number} · best tested · not kept";
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
        bool kept)
    {
        if (kept && report.FinalProcessor is { } finalProcessor)
        {
            return report.Candidates
                .Where(candidate => candidate.Processor == finalProcessor && HasDecisionMetrics(candidate))
                .LastOrDefault();
        }

        var finalist = report.Candidates.FirstOrDefault(candidate =>
            string.Equals(candidate.Phase, "screening-finalists", StringComparison.Ordinal) &&
            string.Equals(candidate.Verdict, "Ranked", StringComparison.OrdinalIgnoreCase) &&
            HasDecisionMetrics(candidate));
        return finalist ?? report.Candidates.FirstOrDefault(candidate =>
            string.Equals(candidate.Verdict, "Ranked", StringComparison.OrdinalIgnoreCase) &&
            HasDecisionMetrics(candidate));
    }

    private static bool HasDecisionMetrics(GpuAutoAffinityCandidateReport candidate) =>
        IsFinitePositive(candidate.DecisionOnePercentLowFps) ||
        IsFinitePositive(candidate.DecisionAvgFps) ||
        IsFinitePositive(candidate.DecisionFrameP99Milliseconds) ||
        IsFinitePositive(candidate.DecisionLow01PctFps);

    private static OriginalMetrics BuildOriginalMetrics(IReadOnlyList<GpuAutoAffinityTrialReport> trials)
    {
        var original = trials
            .Where(static trial =>
                string.Equals(trial.Role, "Original", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(trial.Phase, "screening-original", StringComparison.Ordinal) &&
                string.Equals(trial.ReadinessState, "Ready", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return new OriginalMetrics(
            Median(original.Select(static trial => trial.OnePercentLowFps)),
            Median(original.Select(static trial => trial.AvgFps)),
            Median(original.Select(static trial => trial.FrameP99Milliseconds)),
            Median(original.Select(static trial => trial.Low01PctFps)));
    }

    private static IReadOnlyList<GateAMetricComparison> BuildMetricComparisons(
        OriginalMetrics original,
        GpuAutoAffinityCandidateReport? candidate,
        double uncertainty) =>
        new[]
        {
            CreateMetric("low1", "1% low", "FPS", original.OnePercentLowFps,
                candidate?.DecisionOnePercentLowFps, lowerIsBetter: false, uncertainty),
            CreateMetric("avg", "Average", "FPS", original.AvgFps,
                candidate?.DecisionAvgFps, lowerIsBetter: false, uncertainty),
            CreateMetric("p99", "Frame p99", "ms", original.FrameP99Milliseconds,
                candidate?.DecisionFrameP99Milliseconds, lowerIsBetter: true, uncertainty),
            CreateMetric("low01", "0.1% low", "FPS", original.Low01PctFps,
                candidate?.DecisionLow01PctFps, lowerIsBetter: false, uncertainty),
        };

    private static GateAMetricComparison CreateMetric(
        string key,
        string label,
        string unit,
        double? original,
        double? candidate,
        bool lowerIsBetter,
        double uncertainty)
    {
        if (!IsFinitePositive(original) || !IsFinitePositive(candidate))
        {
            return new GateAMetricComparison(
                key, label, unit, original, candidate, null, uncertainty,
                GateAMetricState.Unavailable, lowerIsBetter);
        }

        var delta = lowerIsBetter
            ? (original!.Value - candidate!.Value) / original.Value
            : (candidate!.Value - original!.Value) / original.Value;
        var state = delta > uncertainty
            ? GateAMetricState.Improved
            : delta < -uncertainty
                ? GateAMetricState.Regressed
                : GateAMetricState.WithinMeasuredUncertainty;
        return new GateAMetricComparison(
            key, label, unit, original, candidate, delta, uncertainty, state, lowerIsBetter);
    }

    private static IReadOnlyList<GateACandidateBar> BuildCandidateBars(
        GpuAutoAffinityReport report,
        LogicalProcessorId? comparedProcessor,
        bool kept)
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
            var isKept = kept && report.FinalProcessor == processor;
            var isCompared = comparedProcessor == processor;
            var state = isKept
                ? "Kept"
                : isCompared && !kept
                    ? "Not kept"
                    : string.Equals(candidate.Verdict, "Inconclusive", StringComparison.OrdinalIgnoreCase)
                        ? "Inconclusive"
                        : "Tested";
            return new GateACandidateBar(
                processor,
                candidate.PhysicalCoreIndex,
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

    private static IReadOnlyList<GateATrialPoint> BuildTrialPoints(
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

    private static IReadOnlyList<GateADecisionEvidenceRow> BuildDecisionRows(
        GpuAutoAffinityReport report,
        GpuAutoAffinityCandidateReport? compared,
        IReadOnlyList<GateAMetricComparison> metrics)
    {
        var primary = metrics.First(static metric => metric.Key == "low1");
        var primaryState = primary.State switch
        {
            GateAMetricState.Improved => "Passed",
            GateAMetricState.Regressed => "Blocked",
            GateAMetricState.WithinMeasuredUncertainty => "Within uncertainty",
            _ => "Unavailable",
        };
        var repeatabilityState = compared is null
            ? "Unavailable"
            : compared.TrialCount >= GpuOriginalBaselinePolicy.PreferredRunCount
                ? "Measured"
                : "Diagnostic only";
        var guardrailState = compared is null
            ? "Unavailable"
            : compared.RegressedGuardrails.Count == 0
                ? "Passed"
                : "Blocked";
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

        return new[]
        {
            new GateADecisionEvidenceRow(
                "Primary improvement",
                primaryState,
                DescribeMetric(primary)),
            new GateADecisionEvidenceRow(
                "Repeatability / uncertainty",
                repeatabilityState,
                compared is null
                    ? "No comparable ranked candidate was recorded."
                    : $"{compared.TrialCount} scored candidate observation(s); local-control uncertainty {SanitizeUncertainty(compared.LocalControlUncertainty):P1}."),
            new GateADecisionEvidenceRow(
                "Performance guardrails",
                guardrailState,
                compared is null
                    ? "Guardrail evidence is unavailable."
                    : compared.RegressedGuardrails.Count == 0
                        ? "No candidate guardrail regression is recorded in the final report."
                        : string.Join("; ", compared.RegressedGuardrails)),
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
        };
    }

    private static string BuildSummary(
        GpuAutoAffinityReport report,
        GpuAutoAffinityCandidateReport? compared,
        bool kept)
    {
        if (kept && report.FinalProcessor is { } processor)
        {
            return report.FinalStateVerified
                ? $"CPU {processor.Number} survived the benchmark decision gates and is the verified terminal GPU interrupt-affinity state. The evidence below shows the measured deltas, uncertainty and runtime placement proof."
                : $"CPU {processor.Number} was selected, but the final machine state is not fully verified. Review the evidence before treating this run as complete.";
        }

        if (report.OriginalStateRestored && report.FinalStateVerified)
        {
            return compared is null
                ? "LatencyPilot retained and verified the exact original GPU affinity policy because no candidate produced decision-grade evidence worth keeping."
                : $"CPU {compared.Processor.Number} was the strongest comparable tested candidate shown here, but it was not kept. LatencyPilot retained and verified the exact original policy instead of turning uncertain evidence into a system change.";
        }

        return "Gate A produced a report, but the terminal machine state is not fully verified. Use the evidence and recovery status below before continuing.";
    }

    private static string DescribeMetric(GateAMetricComparison metric)
    {
        if (!IsFinitePositive(metric.OriginalValue) || !IsFinitePositive(metric.CandidateValue))
        {
            return $"{metric.Label} does not have enough comparable scored evidence.";
        }
        var delta = metric.ImprovementFraction is null
            ? string.Empty
            : $" ({metric.ImprovementFraction.Value:+0.0%;-0.0%;0.0%} improvement-direction delta)";
        return $"Original {metric.OriginalValue:0.##} {metric.Unit} → candidate {metric.CandidateValue:0.##} {metric.Unit}{delta}; measured comparison uncertainty {metric.UncertaintyFraction:P1}.";
    }

    private static double? Median(IEnumerable<double?> values)
    {
        var ordered = values
            .Where(IsFinitePositive)
            .Select(static value => value!.Value)
            .Order()
            .ToArray();
        if (ordered.Length == 0)
        {
            return null;
        }
        return ordered.Length % 2 == 0
            ? (ordered[(ordered.Length / 2) - 1] + ordered[ordered.Length / 2]) / 2d
            : ordered[ordered.Length / 2];
    }

    private static bool IsFinitePositive(double? value) =>
        value is > 0d && double.IsFinite(value.Value);

    private static double SanitizeUncertainty(double? value) =>
        value is >= 0d && double.IsFinite(value.Value) ? value.Value : 0d;

    private sealed record OriginalMetrics(
        double? OnePercentLowFps,
        double? AvgFps,
        double? FrameP99Milliseconds,
        double? Low01PctFps);
}
