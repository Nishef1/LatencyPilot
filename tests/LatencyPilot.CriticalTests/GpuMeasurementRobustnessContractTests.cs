#pragma warning disable CA1822 // AuditCase methods are reflection-invoked by ConsolidatedCriticalTests.
using System.Reflection;
using LatencyPilot.Benchmarking.Optimization;
using LatencyPilot.Core.Benchmarking;
using LatencyPilot.Core.Devices;
using LatencyPilot.Core.System;
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
    public void D3D12TimestampFrequencyIsCapturedOncePerCommandQueue()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "src",
            "LatencyPilot.GpuBenchmark",
            "GpuTimestampCollector.cs"));

        var constructorStart = source.IndexOf(
            "internal GpuTimestampCollector",
            StringComparison.Ordinal);
        var recordBeginStart = source.IndexOf(
            "internal void RecordBegin",
            StringComparison.Ordinal);
        var resolveStart = source.IndexOf(
            "internal void RecordEndAndResolve",
            StringComparison.Ordinal);
        var readStart = source.IndexOf(
            "internal double ReadElapsedMilliseconds",
            StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, constructorStart);
        Assert.IsGreaterThan(constructorStart, recordBeginStart);
        Assert.IsGreaterThan(recordBeginStart, resolveStart);
        Assert.IsGreaterThan(resolveStart, readStart);

        var constructorBlock = source[constructorStart..recordBeginStart];
        var resolveBlock = source[resolveStart..readStart];
        StringAssert.Contains(
            constructorBlock,
            "GetTimestampFrequency",
            "The queue timestamp rate must be captured when the D3D12 command queue is established.");
        Assert.IsFalse(
            resolveBlock.Contains("GetTimestampFrequency", StringComparison.Ordinal),
            "Direct/compute queue timestamp frequency is constant; querying it in every scored frame adds unnecessary host work to the measured Present cadence.");
        StringAssert.Contains(
            source,
            "private readonly ulong frequency",
            "The timestamp conversion rate is immutable for the lifetime of this command queue.");
    }

    [AuditCase]
    public void RendererDoesNotInventTimestampRateChangesFromGpuClockScaling()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "src",
            "LatencyPilot.GpuBenchmark",
            "D3D12BenchmarkRenderer.cs"));

        Assert.IsFalse(
            source.Contains("timestamps.Any(item => item.Frequency != TimestampFrequency)", StringComparison.Ordinal),
            "The renderer does not need a cross-context clock-scaling check; each collector belongs to the same command queue and therefore uses the same stable queue timestamp rate.");
        Assert.IsFalse(
            source.Contains("internal ulong TimestampFrequency { get; }", StringComparison.Ordinal),
            "Timestamp conversion provenance should remain attached to captured frame/trial evidence rather than becoming a separate mutable renderer contract.");
    }

    [AuditCase]
    public void D3D12TimestampFrequencyDifferencesAcrossIndependentTrialsDoNotInvalidateConvertedTimingEvidence()
    {
        var reference = CreateVideoEvidence(timestampFrequency: 1_000_000, trialIndex: 1);
        var trial = CreateVideoEvidence(timestampFrequency: 975_000, trialIndex: 2);

        var readiness = GpuBenchmarkReadiness.Evaluate(
            reference,
            trial,
            GpuBenchmarkContaminationContext.Clean);

        Assert.AreEqual(
            GpuBenchmarkReadinessState.Ready,
            readiness.State,
            "Timestamp frequency is conversion provenance, not a cross-trial hardware identity invariant. Independently created queues may still provide comparable converted millisecond evidence.");
        Assert.IsFalse(
            readiness.Reasons.Any(static reason =>
                reason.Contains("timestamp frequency", StringComparison.OrdinalIgnoreCase)),
            "A different valid conversion rate from another trial must not by itself invalidate already-converted timing evidence.");
    }

    [AuditCase]
    public void BenchmarkArtifactPreservesQueueTimestampFrequencyPerFrame()
    {
        var frameType = typeof(GpuBenchmarkTrialArtifact).Assembly.GetType(
            "LatencyPilot.Core.Benchmarking.GpuBenchmarkArtifactFrame",
            throwOnError: true)!;
        var frequencyProperty = frameType.GetProperty("GpuTimestampFrequency");
        Assert.IsNotNull(
            frequencyProperty,
            "Serialized frame evidence should preserve the D3D12 queue frequency used for timestamp conversion provenance.");
        Assert.AreEqual(typeof(ulong), frequencyProperty.PropertyType);

        var rendererSource = File.ReadAllText(FindRepositoryFile(
            "src",
            "LatencyPilot.GpuBenchmark",
            "D3D12BenchmarkRenderer.cs"));
        var workloadSource = File.ReadAllText(FindRepositoryFile(
            "src",
            "LatencyPilot.GpuBenchmark",
            "BenchmarkWorkload.cs"));
        StringAssert.Contains(
            rendererSource,
            "GpuTimestampFrequency: timestamps[contextIndex].Frequency",
            "The completed frame must carry the stable frequency of its command queue as conversion provenance.");
        StringAssert.Contains(
            workloadSource,
            "frame.GpuTimestampFrequency",
            "Timestamp conversion provenance must survive into the persisted benchmark artifact.");
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

    private static GpuBenchmarkEvidence CreateVideoEvidence(ulong timestampFrequency, int trialIndex)
    {
        const uint processId = 77;
        var started = DateTimeOffset.UnixEpoch.AddSeconds(trialIndex * 40L);
        var periods = Enumerable.Repeat(8d, 120).ToArray();
        return new GpuBenchmarkEvidence(
            GpuBenchmarkEvidence.SchemaId,
            new string('a', 40),
            GpuBenchmarkEvidence.MethodIdValue,
            "windows-test",
            "gpu-test",
            "driver-test",
            "topology-test",
            processId,
            trialIndex == 1 ? "Original" : "Candidate",
            trialIndex,
            trialIndex == 1 ? null : new LogicalProcessorId(0, 2),
            "workload-test",
            [new LogicalProcessorId(0, 0), new LogicalProcessorId(0, 2)],
            0x51A7,
            timestampFrequency,
            Enumerable.Repeat(4d, periods.Length).ToArray(),
            new PresentMonFrameCaptureSnapshot(
                PresentMonWorkloadCaptureStatus.NoSwapChains,
                processId,
                30_000,
                0,
                new PresentMonApiVersionSnapshot(3, 4, 0),
                [],
                [],
                "PresentMonAPI2.dll",
                null,
                "Synthetic no-swap-chain guardrail capture.",
                started,
                started.AddSeconds(30)),
            null,
            Guid.Empty,
            false,
            0,
            [],
            FramePeriodMilliseconds: periods);
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
