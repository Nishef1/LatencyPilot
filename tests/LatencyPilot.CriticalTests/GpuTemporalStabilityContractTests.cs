#pragma warning disable CA1822 // AuditCase methods are reflection-invoked by ConsolidatedCriticalTests.
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LatencyPilot.CriticalTests;

[TestClass]
public sealed class GpuTemporalStabilityContractTests
{
    [AuditCase]
    public void PairedLocalControlsReplaceBlockInterpolationAndBoundInstability()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "src",
            "LatencyPilot.Benchmarking",
            "Optimization",
            "GpuAutoAffinitySession.cs"));

        StringAssert.Contains(source, "MeasureScreeningPairAsync");
        StringAssert.Contains(source, "ComputePairDriftBudget");
        StringAssert.Contains(source, "HigherIsBetterEffect");
        StringAssert.Contains(source, "LowerIsBetterEffect");
        StringAssert.Contains(source, "screening-original-control");
        StringAssert.Contains(source, "GpuAutoAffinityPairVerdict.Unstable");
        StringAssert.Contains(source, "MaximumPairAttempts = 2");
        StringAssert.Contains(source, "MaximumConsecutiveUnstableCandidates = 2");
        StringAssert.Contains(source, "originalBefore = pair.OriginalAfter");
        Assert.IsFalse(
            source.Contains("NormalizeScreeningEvaluations", StringComparison.Ordinal),
            "v2 must not normalize a candidate back to a distant session baseline.");
        Assert.IsFalse(
            source.Contains("ScreeningCandidatesPerControlBlock", StringComparison.Ordinal),
            "v2 must not group candidates into temporal interpolation blocks.");
        Assert.IsFalse(
            source.Contains("ControlPoint.Interpolate", StringComparison.Ordinal),
            "v2 local control must come from measured adjacent Originals, not interpolation.");
    }

    [AuditCase]
    public void PairedDecisionEvidenceMustStayRawAndAuditableInPresentationContracts()
    {
        var report = File.ReadAllText(FindRepositoryFile(
            "src",
            "LatencyPilot.Core",
            "Benchmarking",
            "GpuAutoAffinityReport.cs"));
        StringAssert.Contains(report, "OriginalBeforeOnePercentLowFps");
        StringAssert.Contains(report, "CandidateOnePercentLowFps");
        StringAssert.Contains(report, "OriginalAfterOnePercentLowFps");
        StringAssert.Contains(report, "OnePercentLowEffect");
        StringAssert.Contains(report, "ControlMovement");
        StringAssert.Contains(report, "DriftBudget");

        var source = File.ReadAllText(FindRepositoryFile(
            "src",
            "LatencyPilot.Benchmarking",
            "Optimization",
            "GpuAutoAffinitySession.cs"));
        Assert.IsFalse(
            source.Contains("NormalizeMetric(", StringComparison.Ordinal),
            "v2 must not manufacture normalized FPS values for decision-making.");
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
