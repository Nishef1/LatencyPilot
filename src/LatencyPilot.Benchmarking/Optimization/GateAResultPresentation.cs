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

public sealed record GateAOriginalMetricSummary(
    string Key,
    string Label,
    string Unit,
    double Median,
    double Minimum,
    double Maximum);

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
    bool IsCompared)
{
    public double? OnePercentLowEffect { get; init; }
}

public sealed record GateATrialPoint(
    int RunNumber,
    string Series,
    LogicalProcessorId? Processor,
    double OnePercentLowFps,
    string Phase,
    string ReadinessState)
{
    public GpuAutoAffinityPairVerdict? PairVerdict { get; init; }
    public int? PairAttempt { get; init; }
}

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
    string SelectionConfidence,
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
    public IReadOnlyList<GpuAutoAffinityPairReport> Pairs { get; init; } = [];
    public IReadOnlyList<GpuAutoAffinityFinalistReport> Finalists { get; init; } = [];
    public bool BaselineQualificationFailed { get; init; }
    public bool OriginalOnlyResult { get; init; }
    public bool OriginalEvidenceRepeatable { get; init; }
    public IReadOnlyList<GateAOriginalMetricSummary> OriginalMetricSummaries { get; init; } = [];
    public string OriginalEvidenceDetail { get; init; } = string.Empty;
    public string TerminalStateLabel { get; init; } = string.Empty;
    public bool TerminalStateVerified { get; init; }
}

public static class GateAResultPresentation
{
    private const string FinalistPhaseName = "screening-finalists";
    private const string ScreeningOriginalPhaseName = "screening-original";
    private const string DiagnosticOriginalPhaseName = "diagnostic-original";

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
        var localControlUncertainty = SanitizeUncertainty(
            report.Finalists.LastOrDefault(finalist => finalist.Processor == comparedProcessor)?.NoiseFraction
            ?? report.Finalists.LastOrDefault(finalist => finalist.Processor == comparedProcessor)?.DecisionFloor
            ?? compared?.LocalControlUncertainty);
        var metrics = BuildMetricComparisons(report, compared, localControlUncertainty, verifiedKeep);
        var candidateBars = BuildCandidateBars(report, comparedProcessor, verifiedKeep);
        var trialPoints = BuildTrialPoints(report);
        var terminalOriginalVerified = report.FinalStateVerified && report.OriginalStateRestored;
        var baselineQualificationFailed = IsBaselineQualificationFailure(report);
        var originalOnlyResult = baselineQualificationFailed || report.OriginalDiagnostic is not null;
        GateAOriginalMetricSummary[] originalMetricSummaries = originalOnlyResult
            ? BuildOriginalMetricSummaries(report)
            : [];
        var originalEvidenceRepeatable = report.OriginalDiagnostic?.Repeatable == true &&
                                         !baselineQualificationFailed;
        var originalEvidenceDetail = originalOnlyResult
            ? BuildOriginalEvidenceDetail(report, baselineQualificationFailed)
            : string.Empty;
        var decisionRows = BuildDecisionRows(
            report,
            compared,
            metrics,
            verifiedKeep,
            baselineQualificationFailed);

        var title = baselineQualificationFailed
            ? terminalOriginalVerified
                ? "Baseline evidence unavailable"
                : "Baseline evidence unavailable · recovery needs attention"
            : report.OriginalDiagnostic is { } diagnostic
                ? diagnostic.Repeatable ? "Original repeatable in this sample" : "Original variability is high"
                : report.SearchScope == GpuAutoAffinitySearchScope.Custom && terminalOriginalVerified
                    ? compared is null
                        ? "Custom diagnostic result"
                        : $"Best observed · CPU {compared.Processor.Number} · diagnostic only"
                    : verifiedKeep && report.FinalProcessor is { } selectedProcessor
                        ? report.PracticalTie
                            ? $"Best observed in practical tie · CPU {selectedProcessor.Number} kept"
                            : $"Selected · CPU {selectedProcessor.Number}"
                        : terminalOriginalVerified
                            ? compared is null
                                ? "No valid candidate evidence · Original restored"
                                : $"Best observed · CPU {compared.Processor.Number} · Original restored"
                            : "Result needs attention";
        var summary = BuildSummary(report, compared, metrics, verifiedKeep, baselineQualificationFailed);
        var gateAResultEligible = report.SearchScope == GpuAutoAffinitySearchScope.Full && report.GateAClosureEligible;
        var eligibilityLabel = report.SearchScope == GpuAutoAffinitySearchScope.Full
            ? gateAResultEligible ? "Evidence eligible" : "Development evidence"
            : "Diagnostic only";
        var comparedLabel = baselineQualificationFailed
            ? "Candidate testing not started · Original restored"
            : compared is null
                ? report.OriginalDiagnostic is not null
                    ? "Original-only diagnostic · no candidate tested"
                    : "No authoritative comparison candidate"
                : report.SearchScope == GpuAutoAffinitySearchScope.Custom
                    ? $"CPU {compared.Processor.Number} · Best within selected CPUs · diagnostic only · Original restored"
                    : verifiedKeep
                        ? $"CPU {compared.Processor.Number} · kept"
                        : $"CPU {compared.Processor.Number} · best measured · comparison only · not kept";
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

        var terminalStateVerified = verifiedKeep || terminalOriginalVerified;
        var terminalStateLabel = verifiedKeep && report.FinalProcessor is { } keptProcessor
            ? $"CPU {keptProcessor.Number} kept"
            : report.OriginalDiagnostic is not null && terminalOriginalVerified
                ? "Original verified"
                : terminalOriginalVerified
                    ? "Original restored"
                    : "State not fully verified";

        return new GateAResultViewModel(
            title,
            summary,
            eligibilityLabel,
            gateAResultEligible,
            comparedProcessor,
            diagnosticOnly,
            comparedLabel,
            report.SelectionConfidence,
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
                : TimeSpan.Zero)
        {
            Pairs = report.Pairs,
            Finalists = report.Finalists,
            BaselineQualificationFailed = baselineQualificationFailed,
            OriginalOnlyResult = originalOnlyResult,
            OriginalEvidenceRepeatable = originalEvidenceRepeatable,
            OriginalMetricSummaries = originalMetricSummaries,
            OriginalEvidenceDetail = originalEvidenceDetail,
            TerminalStateLabel = terminalStateLabel,
            TerminalStateVerified = terminalStateVerified,
        };
    }

    private static GpuAutoAffinityCandidateReport? SelectComparedCandidate(
        GpuAutoAffinityReport report,
        bool recommendationKeepsCandidate)
    {
        if (recommendationKeepsCandidate && report.FinalProcessor is { } finalProcessor)
        {
            return report.Candidates
                .Where(candidate => candidate.Processor == finalProcessor && HasDecisionMetrics(candidate))
                .OrderByDescending(static candidate =>
                    string.Equals(candidate.Phase, FinalistPhaseName, StringComparison.Ordinal))
                .FirstOrDefault();
        }

        // Collection order is execution order and may be shuffled. The report's
        // persisted DecisionRank is the only presentation authority. If both a
        // short screen and finalist aggregate carry rank #1, prefer the finalist.
        return report.Candidates
            .Where(static candidate => candidate.DecisionRank == 1)
            .Where(HasDecisionMetrics)
            .OrderByDescending(static candidate =>
                string.Equals(candidate.Phase, FinalistPhaseName, StringComparison.Ordinal))
            .FirstOrDefault();
    }

    private static bool HasDecisionMetrics(GpuAutoAffinityCandidateReport candidate) =>
        IsFinite(candidate.DecisionOnePercentLowEffect);

    private static bool PreferCandidateBarEntry(GpuAutoAffinityCandidateReport candidate, GpuAutoAffinityCandidateReport existing)
    {
        var candidateFinalist = string.Equals(candidate.Phase, FinalistPhaseName, StringComparison.Ordinal);
        var existingFinalist = string.Equals(existing.Phase, FinalistPhaseName, StringComparison.Ordinal);
        if (candidateFinalist != existingFinalist)
        {
            return candidateFinalist;
        }

        return HasDecisionMetrics(candidate) && !HasDecisionMetrics(existing);
    }

    private static GateAMetricComparison[] BuildMetricComparisons(
        GpuAutoAffinityReport report,
        GpuAutoAffinityCandidateReport? candidate,
        double localControlUncertainty,
        bool verifiedKeep)
    {
        var finalist = report.Finalists.LastOrDefault(item => item.Processor == candidate?.Processor);
        return
        [
            CreateMetric(
                "low1",
                "1% low",
                "FPS",
                finalist?.MedianOriginalOnePercentLowFps ?? report.DecisionBaseline?.OnePercentLowFps,
                finalist?.MedianCandidateOnePercentLowFps ?? candidate?.DecisionOnePercentLowFps,
                candidate?.DecisionOnePercentLowEffect,
                lowerIsBetter: false,
                localControlUncertainty,
                verifiedKeep,
                primaryMetric: true),
            CreateMetric(
                "avg",
                "Average",
                "FPS",
                finalist?.MedianOriginalAvgFps ?? report.DecisionBaseline?.AvgFps,
                finalist?.MedianCandidateAvgFps ?? candidate?.DecisionAvgFps,
                candidate?.DecisionAvgEffect,
                lowerIsBetter: false,
                localControlUncertainty,
                verifiedKeep,
                primaryMetric: false),
            CreateMetric(
                "p99",
                "Frame p99",
                "ms",
                finalist?.MedianOriginalFrameP99Milliseconds ?? report.DecisionBaseline?.FrameP99Milliseconds,
                finalist?.MedianCandidateFrameP99Milliseconds ?? candidate?.DecisionFrameP99Milliseconds,
                candidate?.DecisionFrameP99Effect,
                lowerIsBetter: true,
                localControlUncertainty,
                verifiedKeep,
                primaryMetric: false),
            CreateMetric(
                "low01",
                "0.1% low",
                "FPS",
                finalist?.MedianOriginalLow01PctFps ?? report.DecisionBaseline?.Low01PctFps,
                finalist?.MedianCandidateLow01PctFps ?? candidate?.DecisionLow01PctFps,
                candidate?.DecisionLow01PctEffect,
                lowerIsBetter: false,
                localControlUncertainty,
                verifiedKeep: false,
                primaryMetric: false),
        ];
    }

    private static GateAMetricComparison CreateMetric(
        string key,
        string label,
        string unit,
        double? original,
        double? candidate,
        double? effect,
        bool lowerIsBetter,
        double localControlUncertainty,
        bool verifiedKeep,
        bool primaryMetric)
    {
        if (!IsFinite(effect))
        {
            return new GateAMetricComparison(
                key, label, unit, original, candidate, null, localControlUncertainty,
                GateAMetricState.Unavailable, lowerIsBetter);
        }

        var state = !verifiedKeep
            ? GateAMetricState.DiagnosticOnly
            : primaryMetric
                ? GateAMetricState.Improved
                : GateAMetricState.DecisionGuardrailSatisfied;
        return new GateAMetricComparison(
            key, label, unit, original, candidate, effect, localControlUncertainty, state, lowerIsBetter);
    }

    private static GateAOriginalMetricSummary[] BuildOriginalMetricSummaries(GpuAutoAffinityReport report)
    {
        var phase = report.OriginalDiagnostic is null
            ? ScreeningOriginalPhaseName
            : DiagnosticOriginalPhaseName;
        var trials = report.Trials
            .Where(trial =>
                string.Equals(trial.Role, "Original", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(trial.Phase, phase, StringComparison.Ordinal))
            .ToArray();

        return new GateAOriginalMetricSummary?[]
        {
            CreateOriginalMetricSummary("low1", "1% low", "FPS", trials, static trial => trial.OnePercentLowFps),
            CreateOriginalMetricSummary("avg", "Average", "FPS", trials, static trial => trial.AvgFps),
            CreateOriginalMetricSummary("p99", "Frame p99", "ms", trials, static trial => trial.FrameP99Milliseconds),
            CreateOriginalMetricSummary("low01", "0.1% low", "FPS", trials, static trial => trial.Low01PctFps),
        }
        .Where(static summary => summary is not null)
        .Select(static summary => summary!)
        .ToArray();
    }

    private static GateAOriginalMetricSummary? CreateOriginalMetricSummary(
        string key,
        string label,
        string unit,
        IReadOnlyList<GpuAutoAffinityTrialReport> trials,
        Func<GpuAutoAffinityTrialReport, double?> selector)
    {
        var values = trials
            .Select(selector)
            .Where(IsFinitePositive)
            .Select(static value => value!.Value)
            .OrderBy(static value => value)
            .ToArray();
        if (values.Length == 0)
        {
            return null;
        }

        return new GateAOriginalMetricSummary(
            key,
            label,
            unit,
            Median(values),
            values[0],
            values[^1]);
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
            if (!latestByProcessor.TryGetValue(candidate.Processor, out var existing) ||
                PreferCandidateBarEntry(candidate, existing))
            {
                if (!latestByProcessor.ContainsKey(candidate.Processor))
                {
                    order.Add(candidate.Processor);
                }
                latestByProcessor[candidate.Processor] = candidate;
            }
        }

        return order.Where(processor => HasDecisionMetrics(latestByProcessor[processor])).Select(processor =>
        {
            var candidate = latestByProcessor[processor];
            var isKept = verifiedKeep && report.FinalProcessor == processor;
            var isCompared = comparedProcessor == processor;
            var state = isKept
                ? "Kept"
                : isCompared
                    ? "Not kept"
                    : candidate.Verdict.Equals("Rejected", StringComparison.OrdinalIgnoreCase)
                        ? "Rejected"
                        : string.Equals(candidate.Verdict, "Inconclusive", StringComparison.OrdinalIgnoreCase)
                            ? "Inconclusive"
                            : "Tested";
            return new GateACandidateBar(
                processor,
                candidate.PhysicalCoreIndex,
                candidate.DecisionRank,
                candidate.DecisionOnePercentLowFps ?? 0d,
                candidate.DecisionAvgFps,
                candidate.DecisionFrameP99Milliseconds,
                candidate.DecisionLow01PctFps,
                candidate.TrialCount,
                candidate.Verdict,
                state,
                candidate.LocalControlUncertainty,
                isKept,
                isCompared)
            {
                OnePercentLowEffect = candidate.DecisionOnePercentLowEffect,
            };
        }).ToArray();
    }

    private static GateATrialPoint[] BuildTrialPoints(GpuAutoAffinityReport report)
    {
        var pairByCandidateCapture = report.Pairs
            .GroupBy(static pair => pair.CandidateCaptureId)
            .ToDictionary(static group => group.Key, static group => group.Last());

        return report.Trials
            .Where(trial =>
                IsFinitePositive(trial.OnePercentLowFps) &&
                !trial.Phase.EndsWith("-warmup", StringComparison.Ordinal) &&
                (string.Equals(trial.Role, "Original", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(trial.Role, "Candidate", StringComparison.OrdinalIgnoreCase)))
            .OrderBy(static trial => trial.RunNumber)
            .Select(trial =>
            {
                var isOriginal = string.Equals(trial.Role, "Original", StringComparison.OrdinalIgnoreCase);
                GpuAutoAffinityPairReport? pair = null;
                if (!isOriginal)
                {
                    pairByCandidateCapture.TryGetValue(trial.CaptureId, out pair);
                }

                return new GateATrialPoint(
                    trial.RunNumber,
                    isOriginal ? "Original" : $"CPU {trial.Processor?.Number}",
                    trial.Processor,
                    trial.OnePercentLowFps!.Value,
                    trial.Phase,
                    trial.ReadinessState)
                {
                    PairVerdict = pair?.Verdict,
                    PairAttempt = pair?.Attempt,
                };
            })
            .ToArray();
    }

    private static GateADecisionEvidenceRow[] BuildDecisionRows(
        GpuAutoAffinityReport report,
        GpuAutoAffinityCandidateReport? compared,
        IReadOnlyList<GateAMetricComparison> metrics,
        bool verifiedKeep,
        bool baselineQualificationFailed)
    {
        if (baselineQualificationFailed)
        {
            var scoredOriginals = CountScoredOriginalTrials(report, ScreeningOriginalPhaseName);
            var terminalOriginalVerified = report.OriginalStateRestored && report.FinalStateVerified;
            return
            [
                new GateADecisionEvidenceRow(
                    "Baseline evidence",
                    "Unavailable",
                    $"{scoredOriginals} scored Original measurement(s) were captured, but no structurally valid ranking baseline was persisted."),
                new GateADecisionEvidenceRow(
                    "Candidate testing",
                    "Not started",
                    "Candidate ranking did not start because the baseline evidence itself was unusable, not merely because it was noisy."),
                new GateADecisionEvidenceRow(
                    "Final machine state",
                    terminalOriginalVerified ? "Restored" : "Needs attention",
                    terminalOriginalVerified
                        ? "The exact original GPU affinity state was restored and verified."
                        : "The report does not prove a verified restored Original state."),
            ];
        }

        if (report.OriginalDiagnostic is { } diagnostic)
        {
            var terminalOriginalVerified = report.OriginalStateRestored && report.FinalStateVerified;
            return
            [
                new GateADecisionEvidenceRow(
                    "Original diagnostic",
                    diagnostic.Repeatable ? "Repeatable" : "Unstable",
                    diagnostic.Reason),
                new GateADecisionEvidenceRow(
                    "Candidate testing",
                    "Not run",
                    "Original-only diagnostic scope performs no affinity mutation or device restart and does not measure candidate benefit."),
                new GateADecisionEvidenceRow(
                    "Final machine state",
                    terminalOriginalVerified ? "Verified" : "Needs attention",
                    terminalOriginalVerified
                        ? "The exact original GPU affinity state remained active and was verified."
                        : "The report does not prove the terminal Original state."),
            ];
        }

        var primary = metrics.First(static metric => metric.Key == "low1");
        var primaryState = compared is null
            ? "Unavailable"
            : primary.ImprovementFraction is > 0d
                ? "Best observed"
                : "Ranked";
        var confidenceState = compared is null ? "Unavailable" : report.SelectionConfidence;
        var guardrailState = compared is null
            ? "Unavailable"
            : verifiedKeep ? "Passed" : "Not kept";
        var finalPlacement = report.Trials
            .Where(trial =>
                string.Equals(trial.Phase, "final-verification", StringComparison.Ordinal) &&
                (report.FinalProcessor is null || trial.Processor == report.FinalProcessor))
            .LastOrDefault();
        var placementState =
            report.FinalProcessor is null
                ? "Not required"
                : finalPlacement?.Placement is { ConfirmsRequestedPlacement: true }
                    ? "Passed"
                    : finalPlacement?.Placement is not null
                        ? "Failed"
                        : "Unavailable";
        var finalState = report.FinalStateVerified &&
                         (report.FinalProcessor is not null || report.OriginalStateRestored)
            ? "Passed"
            : "Needs attention";

        return
        [
            new GateADecisionEvidenceRow(
                "Best-observed ranking",
                primaryState,
                compared is null
                    ? "No structurally valid candidate could be ranked."
                    : DescribeMetric(
                        primary,
                        "Valid candidates are always ranked. Noise changes confidence; it does not erase the best-observed CPU.")),
            new GateADecisionEvidenceRow(
                "Selection confidence",
                confidenceState,
                compared is null
                    ? "No ranked candidate exists."
                    : $"Confidence is {report.SelectionConfidence}. The displayed uncertainty/noise guide is {primary.UncertaintyFraction:P1}; it is descriptive and does not act as a winner threshold."),
            new GateADecisionEvidenceRow(
                "Keep guardrails",
                guardrailState,
                compared is null
                    ? "Keep guardrails are unavailable because no candidate was ranked."
                    : verifiedKeep
                        ? "Positive median benefit, performance guardrails and final runtime placement all allowed the selected CPU to be retained."
                        : "A best-observed CPU can still be reported when Keep is not recommended or final runtime placement cannot be proved; Original remains active in that case."),
            new GateADecisionEvidenceRow(
                "Runtime ISR placement",
                placementState,
                finalPlacement?.Placement is { } placement
                    ? $"Target ISR {placement.TargetIsrEventCount}; off-target ISR {placement.OffTargetIsrEventCount}."
                    : report.FinalProcessor is null
                        ? "No candidate was retained, so final candidate ISR-placement proof was not required."
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
        IReadOnlyList<GateAMetricComparison> metrics,
        bool verifiedKeep,
        bool baselineQualificationFailed)
    {
        if (baselineQualificationFailed)
        {
            var scoredOriginals = CountScoredOriginalTrials(report, ScreeningOriginalPhaseName);
            var terminalState = report.OriginalStateRestored && report.FinalStateVerified
                ? "The exact Original GPU affinity policy was restored and verified."
                : "The terminal Original state is not fully verified.";
            return $"{scoredOriginals} scored Original measurements completed, but the evidence was structurally unusable for ranking. Ordinary run-to-run noise alone does not trigger this state. {terminalState}";
        }

        if (report.OriginalDiagnostic is { } diagnostic)
        {
            return $"{diagnostic.ObservationCount} Original observations; 1%-low noise {diagnostic.OnePercentLowRelativeNoise:P1}, AVG noise {diagnostic.AvgRelativeNoise:P1}, frame-p99 noise {diagnostic.FrameP99RelativeNoise:P1}. {diagnostic.Reason} Final state verified: {report.FinalStateVerified}.";
        }

        var primary = metrics.FirstOrDefault(static metric => metric.Key == "low1");
        var gain = primary is null ? string.Empty : DescribeUserGain(primary);

        if (report.SearchScope == GpuAutoAffinitySearchScope.Custom)
        {
            var coverage = BuildCustomCoverageSummary(report);
            return compared is null
                ? $"{coverage}. No selected CPU produced structurally valid ranking evidence. The exact Original policy was restored and verified."
                : $"{coverage}. Best observed within the selected CPUs is CPU {compared.Processor.Number}. {gain} Confidence: {report.SelectionConfidence}. This diagnostic always restores Original.";
        }

        if (verifiedKeep && report.FinalProcessor is { } processor)
        {
            var tie = report.PracticalTie
                ? " The top finalists are practically tied, so confidence is intentionally reduced."
                : string.Empty;
            return $"CPU {processor.Number} is the best observed CPU and passed the separate Keep safety checks. {gain} Confidence: {report.SelectionConfidence}.{tie}";
        }

        if (report.OriginalStateRestored && report.FinalStateVerified)
        {
            return compared is null
                ? "LatencyPilot retained and verified the exact original GPU affinity policy because no structurally valid candidate could be ranked."
                : $"CPU {compared.Processor.Number} remains the best observed CPU even though it was not kept. {gain} Confidence: {report.SelectionConfidence}. The exact Original policy is restored and verified.";
        }

        return "Gate A produced a report, but the terminal machine state is not fully verified. Use the evidence and recovery status below before continuing.";
    }

    private static bool IsBaselineQualificationFailure(GpuAutoAffinityReport report) =>
        report.SearchScope != GpuAutoAffinitySearchScope.OriginalDiagnostics &&
        report.DecisionBaseline is null &&
        report.Candidates.Count == 0 &&
        report.Pairs.Count == 0 &&
        CountScoredOriginalTrials(report, ScreeningOriginalPhaseName) > 0;

    private static string BuildOriginalEvidenceDetail(
        GpuAutoAffinityReport report,
        bool baselineQualificationFailed)
    {
        if (baselineQualificationFailed)
        {
            var scored = CountScoredOriginalTrials(report, ScreeningOriginalPhaseName);
            var selected = report.RequestedProcessors.Count;
            var tested = report.ValidatedProcessors.Count;
            var notReached = Math.Max(0, selected - tested);
            return $"{scored} scored Original measurement(s). Qualification requires a stable three-run 1% low cluster: ±3% preferred, with bounded recovery up to ±6%. No valid cluster was found. {selected} candidate CPU(s) selected · {tested} candidate CPU(s) tested · {notReached} not reached.";
        }

        if (report.OriginalDiagnostic is { } diagnostic)
        {
            var state = diagnostic.Repeatable
                ? "Repeatable within the diagnostic budget."
                : "Measurement is unstable before any affinity change.";
            return $"{diagnostic.ObservationCount} scored Original measurement(s). {state} No candidate, restart, or affinity-change evidence is implied by this diagnostic.";
        }

        return string.Empty;
    }

    private static string BuildCustomCoverageSummary(GpuAutoAffinityReport report)
    {
        var selected = report.RequestedProcessors.Count;
        var tested = report.ValidatedProcessors.Count;
        var notReached = Math.Max(0, selected - tested);
        return $"{selected} candidate CPUs selected · {tested} candidate CPUs tested · {notReached} not reached";
    }

    private static int CountScoredOriginalTrials(GpuAutoAffinityReport report, string phase) =>
        report.Trials.Count(trial =>
            string.Equals(trial.Role, "Original", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(trial.Phase, phase, StringComparison.Ordinal) &&
            IsFinitePositive(trial.OnePercentLowFps));

    private static double Median(double[] orderedValues)
    {
        if (orderedValues.Length == 0)
        {
            throw new ArgumentException("Median requires at least one value.", nameof(orderedValues));
        }

        var middle = orderedValues.Length / 2;
        return orderedValues.Length % 2 == 0
            ? (orderedValues[middle - 1] + orderedValues[middle]) / 2d
            : orderedValues[middle];
    }

    private static string DescribeMetric(GateAMetricComparison metric, string interpretation)
    {
        if (!IsFinite(metric.ImprovementFraction))
        {
            return $"{metric.Label} does not have enough authority-selected comparable evidence. {interpretation}";
        }

        return $"{DescribeUserGain(metric)} Noise/uncertainty guide {metric.UncertaintyFraction:P1}. {interpretation}";
    }

    private static string DescribeUserGain(GateAMetricComparison metric)
    {
        if (metric.OriginalValue is { } original &&
            metric.CandidateValue is { } candidate &&
            double.IsFinite(original) &&
            double.IsFinite(candidate))
        {
            var absoluteImprovement = metric.LowerIsBetter
                ? original - candidate
                : candidate - original;
            var pairedEffect = metric.ImprovementFraction is { } effect && double.IsFinite(effect)
                ? $" paired effect {effect:+0.0%;-0.0%;0.0%}"
                : string.Empty;
            var direction = absoluteImprovement >= 0d ? "improvement" : "regression";
            return $"{metric.Label}: {original:0.##} → {candidate:0.##} {metric.Unit}; {direction} {Math.Abs(absoluteImprovement):0.##} {metric.Unit};{pairedEffect}.";
        }

        return metric.ImprovementFraction is { } fallback && double.IsFinite(fallback)
            ? $"{metric.Label}: {fallback:+0.0%;-0.0%;0.0%} paired effect."
            : $"{metric.Label}: unavailable.";
    }

    private static bool IsFinitePositive(double? value) =>
        value is > 0d && double.IsFinite(value.Value);

    private static double SanitizeUncertainty(double? value) =>
        value is >= 0d && double.IsFinite(value.Value) ? value.Value : 0d;

    private static bool IsFinite(double? value) => value is { } number && double.IsFinite(number);
}
