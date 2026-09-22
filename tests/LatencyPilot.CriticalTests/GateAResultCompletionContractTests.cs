#pragma warning disable CA1822 // AuditCase methods are reflection-invoked by ConsolidatedCriticalTests.
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LatencyPilot.CriticalTests;

[TestClass]
public sealed class GateAResultCompletionContractTests
{
    [AuditCase]
    public void ValidatedGateACompletionPackagesAndHandsOffAuthoritativeResult()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "src",
            "LatencyPilot.App",
            "GateAValidationExperience.cs"));

        var validationMarker = source.IndexOf(
            "report.SessionId != sessionId",
            StringComparison.Ordinal);
        var packagingMarker = source.IndexOf(
            "GateAEvidenceBundleExporter.TryCreateAsync(",
            StringComparison.Ordinal);
        var presentationMarker = source.IndexOf(
            "GateAResultPresentation.Create(",
            StringComparison.Ordinal);
        var renderMarker = source.IndexOf(
            "RenderGateAResult(",
            StringComparison.Ordinal);

        Assert.IsGreaterThanOrEqualTo(0, validationMarker);
        Assert.IsGreaterThan(validationMarker, packagingMarker,
            "Gate A packaging must run only after the report schema/session identity has been validated.");
        Assert.IsGreaterThan(packagingMarker, presentationMarker,
            "The Overview model must consume the validated report plus the completed evidence-bundle result.");
        Assert.IsGreaterThan(presentationMarker, renderMarker,
            "Validated Gate A completion must hand the authoritative presentation model to Overview.");
        Assert.IsFalse(source.Contains("TryRevealReport(reportPath)", StringComparison.Ordinal),
            "Successful Gate A completion must not automatically shell-open the raw JSON report.");

        var resultExperience = File.ReadAllText(FindRepositoryFile(
            "src",
            "LatencyPilot.App",
            "GateAResultExperience.cs"));
        StringAssert.Contains(resultExperience, "Open ZIP");
        StringAssert.Contains(resultExperience, "Copy ZIP path");
        StringAssert.Contains(resultExperience, "Open session folder");
        StringAssert.Contains(resultExperience, "Open raw report");
        StringAssert.Contains(resultExperience, "paired effect",
            "The result metric card must render the persisted paired effect instead of an empty absolute Original-to-candidate value pair.");
        StringAssert.Contains(resultExperience, "BuildGateAPairEvidence",
            "The v2 result must expose the direct Original -> Candidate -> Original evidence instead of hiding it only in raw JSON.");
        StringAssert.Contains(resultExperience, "Original before");
        StringAssert.Contains(resultExperience, "Original after");
        StringAssert.Contains(resultExperience, "Control movement");
        Assert.IsFalse(
            resultExperience.Contains("DecisionRank", StringComparison.Ordinal) ||
            resultExperience.Contains("OrderByDescending", StringComparison.Ordinal),
            "The WinUI result renderer must consume the presentation model and must not re-rank GPU candidates.");

        var progressExperience = File.ReadAllText(FindRepositoryFile(
            "src",
            "LatencyPilot.App",
            "GpuOptimizationProgressWindow.xaml.cs"));
        StringAssert.Contains(progressExperience, "DecisionRank",
            "The development Gate A completion view must consume the optimizer-persisted decision rank.");
        StringAssert.Contains(progressExperience, "DecisionOnePercentLowEffect",
            "Rankable progress rows must identify the persisted paired effect instead of presenting raw candidate FPS as the decision metric.");
        StringAssert.Contains(progressExperience, "No decision aggregate",
            "Unrankable measured candidates must be labeled explicitly rather than receiving a synthetic decision value.");
        StringAssert.Contains(progressExperience, "Raw attempts:",
            "Unrankable measured candidates must retain their raw attempts as diagnostic context.");
        StringAssert.Contains(progressExperience, "Open report",
            "Terminal progress must expose the raw report as an action instead of printing a filesystem path into the status paragraph.");
        StringAssert.Contains(progressExperience, "Copy report path",
            "Terminal progress must let the owner copy the report path without displaying the full path inline.");
        StringAssert.Contains(progressExperience, "not reached",
            "Restricted runs must disclose selected/tested/not-reached coverage when early instability stops the search.");
        Assert.IsFalse(progressExperience.Contains("Report: {reportPath}", StringComparison.Ordinal),
            "The terminal status paragraph must not append the raw report path.");
        Assert.IsFalse(
            progressExperience.Contains("candidate!.DecisionOnePercentLowFps : rawLow1", StringComparison.Ordinal) ||
            progressExperience.Contains("candidate!.DecisionAvgFps : rawAvg", StringComparison.Ordinal) ||
            progressExperience.Contains("candidate!.DecisionFrameP99Milliseconds : rawP99", StringComparison.Ordinal),
            "Raw attempt medians must never be substituted into persisted decision fields when a pair was unrankable.");
        Assert.IsFalse(
            progressExperience.Contains(
                ".OrderByDescending(static row => row.DecisionOnePercentLowFps)",
                StringComparison.Ordinal) ||
            progressExperience.Contains(
                ".ThenByDescending(static row => row.DecisionAvgFps)",
                StringComparison.Ordinal),
            "The development Gate A completion view must not create a second ranking from metric decimals.");

        var progressXaml = File.ReadAllText(FindRepositoryFile(
            "src",
            "LatencyPilot.App",
            "GpuOptimizationProgressWindow.xaml"));
        StringAssert.Contains(progressXaml, "Affinity / ISR state",
            "The progress card must use a state-neutral label because RestoreOriginal verifies terminal affinity state without claiming final winner ISR placement.");
        Assert.IsFalse(progressXaml.Contains("Text=\"GPU ISR placement\"", StringComparison.Ordinal),
            "A terminal Original restore must not be shown under a label that implies winner ISR placement was verified.");
        StringAssert.Contains(progressXaml, "Candidate evidence",
            "The completion card can contain raw unrankable attempts, so its heading must not imply every row is decision-grade.");

        var progressFile = File.ReadAllText(FindRepositoryFile(
            "tools",
            "LatencyPilot.GateAValidation",
            "GpuGateAProgressFile.cs"));
        StringAssert.Contains(progressFile, "PairRetryAdditionalUnits = 6",
            "A paired retry adds a fresh Original-before warm-up/score plus candidate and Original-after warm-up/score: six progress units.");
        StringAssert.Contains(progressFile, "-retry-original-before-warmup",
            "Progress accounting must recognize the bounded pair retry at its fresh Original-before boundary.");
        StringAssert.Contains(progressFile, "RecoveryOriginalAdditionalUnits = 2",
            "A recovery Original-control chain reacquires warm-up plus scored control: two progress units.");
        StringAssert.Contains(progressFile, "-recovery-original-control-warmup",
            "Progress accounting must budget the recovery Original-control at its warm-up boundary so ETA never claims completion mid-recovery.");
        StringAssert.Contains(progressFile, "request.RetryAttempt > 0",
            "A transient benchmark/collector retry adds exactly one extra progress unit without inventing a new pair budget.");
        Assert.IsFalse(progressFile.Contains("finalistWarmupsStarted", StringComparison.Ordinal),
            "Finalist warm-up counting must not double-count paired retries after the retry boundary owns the six-unit budget.");

        var presentation = File.ReadAllText(FindRepositoryFile(
            "src",
            "LatencyPilot.Benchmarking",
            "Optimization",
            "GateAResultPresentation.cs"));
        StringAssert.Contains(presentation, "int? DecisionRank",
            "The result display model must carry the optimizer-persisted decision rank to visualizations.");
        StringAssert.Contains(presentation, "candidate.DecisionRank",
            "Candidate presentation rows must preserve the optimizer-persisted decision rank.");
        StringAssert.Contains(presentation, "screening-finalists",
            "When finalist authority exists, the result presentation must prefer the three-pair finalist aggregate over a short-screen row with the same persisted rank.");
        StringAssert.Contains(presentation, "Best within selected CPUs",
            "Custom diagnostic results must describe their restricted authority rather than implying a machine-wide winner.");
        StringAssert.Contains(presentation, "Custom diagnostic result",
            "Custom scope needs an explicit primary result status.");
        StringAssert.Contains(presentation, "selected ·",
            "Custom diagnostic summary must state how many CPUs were selected.");
        StringAssert.Contains(presentation, "tested ·",
            "Custom diagnostic summary must state how many selected CPUs were actually tested.");
        StringAssert.Contains(presentation, "not reached",
            "Custom diagnostic summary must disclose CPUs not reached after an early instability stop.");
        StringAssert.Contains(presentation, "trial.Role, \"Candidate\"",
            "Trial history must retain scored candidate observations even when no candidate is authority-ranked.");
        Assert.IsFalse(
            presentation.Contains("candidate.Phase, \"finalists\"", StringComparison.Ordinal),
            "The result presentation must use the actual persisted finalist phase name.");

        var candidateChart = File.ReadAllText(FindRepositoryFile(
            "src",
            "LatencyPilot.App",
            "Controls",
            "GpuCandidateComparisonChart.cs"));
        StringAssert.Contains(candidateChart,
            ".OrderBy(static candidate => candidate.DecisionRank ?? int.MaxValue)",
            "The candidate chart must display authority-ranked candidates in persisted rank order.");
        StringAssert.Contains(candidateChart, "OnePercentLowEffect",
            "The candidate chart must visualize the persisted local paired effect rather than treating the last raw candidate FPS as the decision aggregate.");
        StringAssert.Contains(candidateChart, "No decision-grade candidate could be charted",
            "A completed run with unrankable pairs must not claim that no candidate evidence exists.");
        StringAssert.Contains(candidateChart, "OnCreateAutomationPeer",
            "The custom candidate chart must be represented explicitly in the UI Automation tree.");
        StringAssert.Contains(candidateChart, "FrameworkElementAutomationPeer",
            "The candidate chart must reuse WinUI framework automation support instead of relying on Canvas primitives.");
        StringAssert.Contains(candidateChart, "AutomationProperties.SetItemStatus",
            "The candidate chart must expose authority-ranked data to assistive technology instead of making visual bars/tooltips the only detail.");
        Assert.IsFalse(candidateChart.Contains("candidate.OnePercentLowFps / maximum", StringComparison.Ordinal),
            "The paired-v2 chart must not size decision bars from raw candidate FPS.");
        Assert.IsFalse(candidateChart.Contains("OrderByDescending(static candidate => candidate.OnePercentLowFps)", StringComparison.Ordinal),
            "The candidate chart must never infer rank from 1% low decimals.");

        var trialHistory = File.ReadAllText(FindRepositoryFile(
            "src",
            "LatencyPilot.App",
            "Controls",
            "GateATrialHistoryChart.cs"));
        StringAssert.Contains(trialHistory, "RenderCandidateMarkers",
            "Candidate measurements from different CPUs must be rendered as discrete observations, not connected into a synthetic time series.");
        StringAssert.Contains(trialHistory, "SemanticAttentionBrush",
            "Unstable/inconclusive paired attempts need an explicit visual state in the diagnostic history.");
        StringAssert.Contains(trialHistory, "OnCreateAutomationPeer",
            "The custom stability chart must be represented explicitly in the UI Automation tree.");
        StringAssert.Contains(trialHistory, "FrameworkElementAutomationPeer",
            "The stability chart must reuse WinUI framework automation support.");
        StringAssert.Contains(trialHistory, "AutomationProperties.SetItemStatus",
            "The stability chart must expose a concise accessible series summary in addition to visual marks.");

        var sessionSource = File.ReadAllText(FindRepositoryFile(
            "src",
            "LatencyPilot.Benchmarking",
            "Optimization",
            "GpuAutoAffinitySession.cs"));
        StringAssert.Contains(sessionSource, "DecisionFloor = decisionFloor",
            "The finalist decision floor used to accept a winner must be persisted for the evidence UI instead of silently becoming zero.");

        var scopeExperience = File.ReadAllText(FindRepositoryFile(
            "src",
            "LatencyPilot.App",
            "GateACpuScopeExperience.cs"));
        StringAssert.Contains(scopeExperience, "ContentDialog");
        StringAssert.Contains(scopeExperience, "XamlRoot");
        StringAssert.Contains(scopeExperience, "NotSupportedException");
        StringAssert.Contains(scopeExperience, "Original only · no system changes");
        StringAssert.Contains(scopeExperience, "Selected CPUs · restore Original");
        StringAssert.Contains(scopeExperience, "GpuAutoAffinitySearchScope.Full",
            "Scope UI must distinguish Full from diagnostic scopes.");
        StringAssert.Contains(scopeExperience, "ApplyGateAButtonFromSourceState(assessment)",
            "Returning to Full scope must restore the source-state Run Gate A button, not leave a diagnostic label stuck.");
        StringAssert.Contains(scopeExperience, "Run selected CPUs",
            "Custom diagnostic scope must label the run as selected-CPU only.");
        StringAssert.Contains(scopeExperience, "Check Original",
            "Original-only diagnostic scope must label its restricted action.");

        var finalSummarySource = File.ReadAllText(FindRepositoryFile(
            "src",
            "LatencyPilot.App",
            "GateAValidationExperience.cs"));
        StringAssert.Contains(finalSummarySource, "MedianTrialMetric",
            "Final-summary ranked medians must go through one filtered ranking helper.");
        StringAssert.Contains(finalSummarySource, "GpuAutoAffinityPairVerdict.Valid",
            "Ranked medians must only consume Valid local pairs, never Unstable/Inconclusive attempts.");
        StringAssert.Contains(finalSummarySource, "pair.CandidateCaptureId",
            "Pair-to-trial join must use the persisted candidate capture id.");
        StringAssert.Contains(finalSummarySource, "rankedCaptures.Contains(trial.CaptureId)",
            "Ranked medians must restrict trials to captures that belong to a Valid pair for the compared CPU.");
        StringAssert.Contains(finalSummarySource, "string.Equals(trial.ReadinessState, \"Ready\", StringComparison.Ordinal)",
            "Ranked medians must exclude non-Ready (failed/retry) observations.");
        StringAssert.Contains(finalSummarySource, "trial.Phase.StartsWith(\"screening-\"",
            "Ranked medians must only include real screening trial phases.");
        StringAssert.Contains(finalSummarySource, "!trial.Phase.EndsWith(\"-warmup\"",
            "Warm-up observations must never enter ranked medians.");
        Assert.IsFalse(
            finalSummarySource.Contains("report.Trials.Where(static trial => trial.Phase == \"screening\")", StringComparison.Ordinal),
            "Candidate-report Phase \"screening\" is not a trial phase; medians must use trial phases.");

        var progressExperienceSource = File.ReadAllText(FindRepositoryFile(
            "src",
            "LatencyPilot.App",
            "GpuOptimizationProgressWindow.xaml.cs"));
        StringAssert.Contains(progressExperienceSource,
            "string.Equals(trial.ReadinessState, \"Ready\", StringComparison.Ordinal)",
            "Ranked progress raw-attempt rows must only summarize Ready candidate observations.");
        StringAssert.Contains(progressExperienceSource,
            ".OrderBy(static row => row.DecisionRank ?? int.MaxValue)",
            "Ranked progress rows must order by persisted decision rank, never re-rank from decimals.");

        var reportContract = File.ReadAllText(FindRepositoryFile(
            "src",
            "LatencyPilot.Core",
            "Benchmarking",
            "GpuAutoAffinityReport.cs"));
        StringAssert.Contains(reportContract, "latencypilot-gpu-auto-affinity-report-v2");
        StringAssert.Contains(reportContract, "GpuAutoAffinitySearchScope");
        StringAssert.Contains(reportContract, "GpuAutoAffinityPairVerdict");
        StringAssert.Contains(reportContract, "GpuAutoAffinityPairReport");
        StringAssert.Contains(reportContract, "GpuAutoAffinityFinalistReport");
        StringAssert.Contains(reportContract, "IReadOnlyList<GpuAutoAffinityPairReport> Pairs");
        StringAssert.Contains(reportContract, "IReadOnlyList<GpuAutoAffinityFinalistReport> Finalists");
        StringAssert.Contains(reportContract, "IReadOnlyList<LogicalProcessorId> RequestedProcessors");
        StringAssert.Contains(reportContract, "IReadOnlyList<LogicalProcessorId> ValidatedProcessors");
        StringAssert.Contains(reportContract, "IReadOnlyList<LogicalProcessorId> RealizedCandidateOrder");
        StringAssert.Contains(reportContract, "IReadOnlyList<LogicalProcessorId> RealizedFinalistPairOrder");
        StringAssert.Contains(reportContract, "Guid? InitialScreeningOriginalCaptureId");
        StringAssert.Contains(reportContract, "double ScreeningDurationMilliseconds");
        StringAssert.Contains(reportContract, "double FinalistDurationMilliseconds");
        StringAssert.Contains(reportContract, "RequestedDurationMilliseconds");
        StringAssert.Contains(reportContract, "PairNumber");
        StringAssert.Contains(reportContract, "bool FullTopologyCoverage");
    }

    private static string FindRepositoryFile(params string[] relativeParts)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine([directory.FullName, .. relativeParts]);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException(
            "Unable to locate repository file: " + Path.Combine(relativeParts));
    }
}