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
    public void RankingUsesMedianAndMadWithoutTurningNoiseIntoANoWinnerGate()
    {
        var sessionSource = File.ReadAllText(FindRepositoryFile(
            "src",
            "LatencyPilot.Benchmarking",
            "Optimization",
            "GpuAutoAffinitySession.cs"));
        var reportSource = File.ReadAllText(FindRepositoryFile(
            "src",
            "LatencyPilot.Core",
            "Benchmarking",
            "GpuAutoAffinityReport.cs"));

        StringAssert.Contains(sessionSource, "MedianAbsoluteDeviation");
        StringAssert.Contains(sessionSource, "RelativeMedianAbsoluteDeviation");
        StringAssert.Contains(sessionSource, "DetermineSelectionConfidence");
        StringAssert.Contains(sessionSource, "RecommendedForKeep");
        StringAssert.Contains(sessionSource, "SelectAdaptiveShortlist");
        StringAssert.Contains(sessionSource, "MaximumAdaptiveShortlistCandidates");
        StringAssert.Contains(sessionSource, "Math.Min(pair.Report.ControlMovement, pair.Report.DriftBudget)",
            "Shortlist uncertainty must be bounded so extreme drift keeps near-leaders alive without resurrecting clear losers.");
        StringAssert.Contains(sessionSource, "MaximumFinalists = 2");
        StringAssert.Contains(sessionSource, "MinimumFinalistPairs = 2");
        StringAssert.Contains(sessionSource, "MaximumFinalistPairs = 3");
        Assert.IsFalse(
            sessionSource.Contains("GpuRepeatabilityClusterSelector.Select(", StringComparison.Ordinal),
            "The historical cluster selector may remain as a utility, but it must not own v4 ranking authority.");
        StringAssert.Contains(reportSource, "BestObservedProcessor");
        StringAssert.Contains(reportSource, "SelectionConfidence");
        StringAssert.Contains(reportSource, "OnePercentLowEffectMedianAbsoluteDeviation");
        StringAssert.Contains(reportSource, "RecommendedForKeep");

        var rendererSource = File.ReadAllText(FindRepositoryFile(
            "src", "LatencyPilot.GpuBenchmark", "D3D12BenchmarkRenderer.cs"));
        var workerSource = File.ReadAllText(FindRepositoryFile(
            "src", "LatencyPilot.GpuBenchmark", "CpuRenderWorker.cs"));
        var evidenceSource = File.ReadAllText(FindRepositoryFile(
            "src", "LatencyPilot.Core", "Benchmarking", "GpuBenchmarkEvidence.cs"));
        var backendSource = File.ReadAllText(FindRepositoryFile(
            "tools", "LatencyPilot.GateAValidation", "GpuAutoAffinityGateABackend.cs"));
        StringAssert.Contains(rendererSource, "FrameCompletionTimeout");
        StringAssert.Contains(rendererSource, "fenceEvent.WaitOne(FrameCompletionTimeout)");
        StringAssert.Contains(workerSource, "completed.Wait(WorkerCompletionTimeout)");
        StringAssert.Contains(workerSource, "thread.Join(WorkerCompletionTimeout)");
        StringAssert.Contains(evidenceSource, "MinimumSampleCount");
        StringAssert.Contains(backendSource, "MinimumControlledFramesPerSecond");
        StringAssert.Contains(backendSource, "MaximumScoredWindowOverrunRatio");
        StringAssert.Contains(backendSource, "TrialDeadlineSlack");
        StringAssert.Contains(backendSource, "RendererRecreateDeadline");
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
