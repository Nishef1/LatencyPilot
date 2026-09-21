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
        Assert.IsGreaterThan(
            validationMarker,
            packagingMarker,
            "Gate A packaging must run only after the report schema/session identity has been validated.");
        Assert.IsGreaterThan(
            packagingMarker,
            presentationMarker,
            "The Overview model must consume the validated report plus the completed evidence-bundle result.");
        Assert.IsGreaterThan(
            presentationMarker,
            renderMarker,
            "Validated Gate A completion must hand the authoritative presentation model to Overview.");
        Assert.IsFalse(
            source.Contains("TryRevealReport(reportPath)", StringComparison.Ordinal),
            "Successful Gate A completion must not automatically shell-open the raw JSON report.");

        var resultExperience = File.ReadAllText(FindRepositoryFile(
            "src",
            "LatencyPilot.App",
            "GateAResultExperience.cs"));
        StringAssert.Contains(resultExperience, "Open ZIP");
        StringAssert.Contains(resultExperience, "Copy ZIP path");
        StringAssert.Contains(resultExperience, "Open session folder");
        StringAssert.Contains(resultExperience, "Open raw report");
        StringAssert.Contains(
            resultExperience,
            "paired effect",
            "The result metric card must render the persisted paired effect instead of an empty absolute Original-to-candidate value pair.");
        StringAssert.Contains(
            resultExperience,
            "BuildGateAPairEvidence",
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
        StringAssert.Contains(
            progressExperience,
            "DecisionRank",
            "The development Gate A completion view must consume the optimizer-persisted decision rank.");
        Assert.IsFalse(
            progressExperience.Contains(
                ".OrderByDescending(static row => row.DecisionOnePercentLowFps)",
                StringComparison.Ordinal) ||
            progressExperience.Contains(
                ".ThenByDescending(static row => row.DecisionAvgFps)",
                StringComparison.Ordinal),
            "The development Gate A completion view must not create a second ranking from metric decimals.");

        var presentation = File.ReadAllText(FindRepositoryFile(
            "src",
            "LatencyPilot.Benchmarking",
            "Optimization",
            "GateAResultPresentation.cs"));
        StringAssert.Contains(
            presentation,
            "int? DecisionRank",
            "The result display model must carry the optimizer-persisted decision rank to visualizations.");
        StringAssert.Contains(
            presentation,
            "candidate.DecisionRank",
            "Candidate presentation rows must preserve the optimizer-persisted decision rank.");
        StringAssert.Contains(
            presentation,
            "screening-finalists",
            "When finalist authority exists, the result presentation must prefer the three-pair finalist aggregate over a short-screen row with the same persisted rank.");
        StringAssert.Contains(
            presentation,
            "Best within selected CPUs",
            "Custom diagnostic results must describe their restricted authority rather than implying a machine-wide winner.");
        StringAssert.Contains(
            presentation,
            "Custom diagnostic result",
            "Custom scope needs an explicit primary result status.");
        Assert.IsFalse(
            presentation.Contains("candidate.Phase, \"finalists\"", StringComparison.Ordinal),
            "The result presentation must use the actual persisted finalist phase name.");

        var candidateChart = File.ReadAllText(FindRepositoryFile(
            "src",
            "LatencyPilot.App",
            "Controls",
            "GpuCandidateComparisonChart.cs"));
        StringAssert.Contains(
            candidateChart,
            ".OrderBy(static candidate => candidate.DecisionRank ?? int.MaxValue)",
            "The candidate chart must display authority-ranked candidates in persisted rank order.");
        StringAssert.Contains(
            candidateChart,
            "OnePercentLowEffect",
            "The candidate chart must visualize the persisted local paired effect rather than treating the last raw candidate FPS as the decision aggregate.");
        Assert.IsFalse(
            candidateChart.Contains("candidate.OnePercentLowFps / maximum", StringComparison.Ordinal),
            "The paired-v2 chart must not size decision bars from raw candidate FPS.");
        Assert.IsFalse(
            candidateChart.Contains("OrderByDescending(static candidate => candidate.OnePercentLowFps)", StringComparison.Ordinal),
            "The candidate chart must never infer rank from 1% low decimals.");

        var sessionSource = File.ReadAllText(FindRepositoryFile(
            "src",
            "LatencyPilot.Benchmarking",
            "Optimization",
            "GpuAutoAffinitySession.cs"));
        StringAssert.Contains(
            sessionSource,
            "DecisionFloor = decisionFloor",
            "The finalist decision floor used to accept a winner must be persisted for the evidence UI instead of silently becoming zero.");
        StringAssert.Contains(sessionSource, "RealizedCandidateOrder = pairReports");
        StringAssert.Contains(sessionSource, "RealizedFinalistPairOrder = pairReports");
        StringAssert.Contains(sessionSource, "InitialScreeningOriginalCaptureId = pairReports");
        StringAssert.Contains(sessionSource, "ScreeningDurationMilliseconds = request.ScreeningDuration.TotalMilliseconds");

        var scopeExperience = File.ReadAllText(FindRepositoryFile(
            "src",
            "LatencyPilot.App",
            "GateACpuScopeExperience.cs"));
        StringAssert.Contains(scopeExperience, "ContentDialog");
        StringAssert.Contains(scopeExperience, "XamlRoot");
        StringAssert.Contains(scopeExperience, "NotSupportedException");
        StringAssert.Contains(scopeExperience, "Original only · no system changes");
        StringAssert.Contains(scopeExperience, "Selected CPUs · restore Original");

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
