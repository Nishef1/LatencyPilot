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
    public void RendererDoesNotRequireTimestampFrequencyToStayStaticAcrossFrameContexts()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "src",
            "LatencyPilot.GpuBenchmark",
            "D3D12BenchmarkRenderer.cs"));

        Assert.IsFalse(
            source.Contains("timestamps.Any(item => item.Frequency != TimestampFrequency)", StringComparison.Ordinal),
            "Different fresh frequencies from adjacent frame contexts are not a device error under dynamic clock scaling and must not abort the benchmark.");
        Assert.IsFalse(
            source.Contains("internal ulong TimestampFrequency { get; }", StringComparison.Ordinal),
            "The renderer must not expose a construction-time timestamp frequency as though it were stable session state; persisted frames carry the exact resolve-time value instead.");
    }

    [AuditCase]
    public void D3D12TimestampFrequencyChangesAcrossTrialsDoNotInvalidateConvertedTimingEvidence()
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
            "A fresh per-window timestamp frequency is part of the conversion provenance, not a session identity invariant. Converted millisecond evidence remains comparable when the queue frequency legitimately changes.");
        Assert.IsFalse(
            readiness.Reasons.Any(static reason =>
                reason.Contains("timestamp frequency", StringComparison.OrdinalIgnoreCase)),
            "Dynamic GPU clock scaling must not be mislabeled as a broken benchmark identity after each window is converted with its own fresh frequency.");
    }

    [AuditCase]
    public void BenchmarkArtifactPreservesResolvedTimestampFrequencyPerFrame()
    {
        var frameType = typeof(GpuBenchmarkTrialArtifact).Assembly.GetType(
            "LatencyPilot.Core.Benchmarking.GpuBenchmarkArtifactFrame",
            throwOnError: true)!;
        var frequencyProperty = frameType.GetProperty("GpuTimestampFrequency");
        Assert.IsNotNull(
            frequencyProperty,
            "Each serialized frame must preserve the queue frequency used to convert that frame's D3D12 timestamp ticks; one trial-level scalar is insufficient when dynamic clock scaling changes the frequency.");
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
            "The completed frame must snapshot the frequency from the exact frame context before that context is reused.");
        StringAssert.Contains(
            workloadSource,
            "frame.GpuTimestampFrequency",
            "The frame-context frequency must survive into the persisted benchmark artifact.");
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
