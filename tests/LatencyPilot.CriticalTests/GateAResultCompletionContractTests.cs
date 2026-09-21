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
