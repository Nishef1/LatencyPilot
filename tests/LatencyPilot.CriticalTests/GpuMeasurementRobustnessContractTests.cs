#pragma warning disable CA1822 // AuditCase methods are reflection-invoked by ConsolidatedCriticalTests.
using System.Reflection;
using LatencyPilot.Benchmarking.Optimization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LatencyPilot.CriticalTests;

[TestClass]
public sealed class GpuMeasurementRobustnessContractTests
{
    [AuditCase]
    public void RepeatabilitySelectorRecoversOneModeratelySpreadThreeRunClusterButRejectsBroadInstability()
    {
        var selectorType = typeof(GpuAutoAffinitySession).Assembly.GetType(
            "LatencyPilot.Benchmarking.Optimization.GpuRepeatabilityClusterSelector",
            throwOnError: true)!;
        var select = selectorType.GetMethod(
            "Select",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("GPU repeatability selector entry point was not found.");
        var recoveryToleranceField = selectorType.GetField(
            "RecoveryRelativeTolerance",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.IsNotNull(recoveryToleranceField,
            "The four-attempt recovery path needs an explicit bounded tolerance rather than a hidden magic number.");
        var recoveryTolerance = (double)recoveryToleranceField.GetRawConstantValue()!;

        // Mirrors the shape seen on physical Gate A: one very fast Original sample
        // followed by three ordinary samples that are not inside the preferred ±3%
        // band, but are still a coherent local regime. The observed ~5.6% spread is
        // retained as decision uncertainty; the gross sample must not force a ~25%
        // whole-session noise floor.
        var physicalRunShape = new[] { 258.9d, 191.7d, 203.1d, 211.6d };
        var recovered = select.Invoke(null, [physicalRunShape]);

        Assert.IsNotNull(recovered,
            "After the fourth scored attempt, one gross sample must not mask a coherent three-run recovery cluster.");
        var indexes = (int[])recovered.GetType().GetProperty("Indexes")!.GetValue(recovered)!;
        var maximumRelativeDeviation = (double)recovered.GetType()
            .GetProperty("MaximumRelativeDeviation")!
            .GetValue(recovered)!;
        Assert.AreEqual(3, indexes.Length);
        Assert.IsFalse(indexes.Contains(0),
            "The gross high sample must stay in the audit trail but not define the decision cluster.");
        Assert.IsLessThanOrEqualTo(
            recoveryTolerance,
            maximumRelativeDeviation,
            "Recovery must stay bounded; it is not permission to absorb arbitrary Windows variance.");

        var broadlyUnstable = new[] { 76.9d, 62.5d, 52.6d, 45.5d };
        Assert.IsNull(
            select.Invoke(null, [broadlyUnstable]),
            "Broad multi-run instability must still fall through to the explicit all-run noise-aware path rather than deleting whichever point is inconvenient.");
    }

    [AuditCase]
    public void D3D12TimestampFrequencyIsRefreshedAtTheResolveBoundary()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "src",
            "LatencyPilot.GpuBenchmark",
            "GpuTimestampCollector.cs"));

        var resolveStart = source.IndexOf(
            "internal void RecordEndAndResolve",
            StringComparison.Ordinal);
        var readStart = source.IndexOf(
            "internal double ReadElapsedMilliseconds",
            StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, resolveStart);
        Assert.IsGreaterThan(resolveStart, readStart);

        var resolveBlock = source[resolveStart..readStart];
        var frequencyRead = resolveBlock.IndexOf("GetTimestampFrequency", StringComparison.Ordinal);
        var resolveCall = resolveBlock.IndexOf("ResolveQueryData", StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, frequencyRead,
            "Microsoft's current D3D12 timing guidance requires the queue timestamp frequency to be re-queried close to timestamp resolve rather than cached for a long renderer lifetime.");
        Assert.IsGreaterThan(frequencyRead, resolveCall,
            "The fresh queue frequency must be captured before the timestamp resolve is recorded.");
        Assert.IsFalse(
            source.Contains("private readonly ulong frequency", StringComparison.Ordinal),
            "A renderer-lifetime readonly timestamp frequency would reintroduce the stale-frequency defect.");
    }

    [AuditCase]
    public void PresentMonDynamicQueryRemainsBoundToTheBenchmarkProcessAndKeepsNoSwapChainsExplicit()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "src",
            "LatencyPilot.Platform.Windows",
            "Devices",
            "PresentMonWorkloadMetricsReader.cs"));

        StringAssert.Contains(source, "startTracking(session, processId)");
        StringAssert.Contains(source, "flushFrames(session, processId)");
        StringAssert.Contains(source, "Poll(query, processId");
        StringAssert.Contains(source, "PresentMonWorkloadCaptureStatus.NoSwapChains");
        StringAssert.Contains(source, "tracked process produced no PresentMon swap-chain rows");
    }

    private static string FindRepositoryFile(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. relativeParts]);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            "Unable to locate repository file: " + Path.Combine(relativeParts));
    }
}
