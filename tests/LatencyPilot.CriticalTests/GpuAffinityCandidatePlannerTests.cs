using System.Buffers.Binary;
using LatencyPilot.Benchmarking.Candidates;
using LatencyPilot.Benchmarking.Optimization;
using LatencyPilot.Core.Devices;
using LatencyPilot.Core.System;
using LatencyPilot.Platform.Windows.Devices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LatencyPilot.CriticalTests;

[TestClass]
public sealed class GpuAffinityCandidatePlannerTests
{
    [TestMethod]
    public void BaselineInterruptSharesDrivePhysicalCoreCandidateRanking()
    {
        var cpu0 = new LogicalProcessorId(0, 0);
        var cpu1 = new LogicalProcessorId(0, 1);
        var cpu2 = new LogicalProcessorId(0, 2);
        var cpu3 = new LogicalProcessorId(0, 3);
        var topology = new ProcessorTopologySnapshot(
            [new ProcessorPackageSnapshot(0, [cpu0, cpu1, cpu2, cpu3])],
            [
                new ProcessorCoreSnapshot(0, 0, [cpu0, cpu1]),
                new ProcessorCoreSnapshot(1, 0, [cpu2, cpu3]),
            ],
            DateTimeOffset.UnixEpoch);

        IReadOnlyList<IReadOnlyList<ProcessorInterruptCountEvidence>> windows =
        [
            [
                new ProcessorInterruptCountEvidence(cpu0, 90, 10),
                new ProcessorInterruptCountEvidence(cpu1, 10, 0),
                new ProcessorInterruptCountEvidence(cpu2, 20, 0),
                new ProcessorInterruptCountEvidence(cpu3, 0, 0),
            ],
            [
                new ProcessorInterruptCountEvidence(cpu0, 80, 10),
                new ProcessorInterruptCountEvidence(cpu1, 10, 0),
                new ProcessorInterruptCountEvidence(cpu2, 30, 0),
                new ProcessorInterruptCountEvidence(cpu3, 0, 0),
            ],
        ];

        var pressure = ProcessorPressureEvidenceBuilder.Create(topology, windows);
        Assert.AreEqual(1d, pressure.Sum(static item => item.PressureScore), 0.000001d);

        var candidates = GpuAffinityCandidatePlanner.Create(topology, pressure, maximumCandidates: 2);
        Assert.AreEqual(2, candidates.Count);
        Assert.AreEqual(cpu3, candidates[0].Processor);
        Assert.AreEqual(cpu1, candidates[1].Processor);
        Assert.IsTrue(candidates[0].ObservedPressureScore < candidates[1].ObservedPressureScore);

        Span<byte> descriptor = stackalloc byte[AllocatedIrqDescriptorParser.Descriptor64Size];
        BinaryPrimitives.WriteUInt32LittleEndian(descriptor[0..4], 0);
        BinaryPrimitives.WriteUInt32LittleEndian(descriptor[4..8], AllocatedIrqDescriptorParser.IrqTypeRange);
        BinaryPrimitives.WriteUInt16LittleEndian(descriptor[8..10], 0x0002);
        BinaryPrimitives.WriteUInt16LittleEndian(descriptor[10..12], 0);
        BinaryPrimitives.WriteUInt32LittleEndian(descriptor[12..16], 42);
        BinaryPrimitives.WriteUInt64LittleEndian(descriptor[16..24], 1UL << 3);

        Assert.IsTrue(AllocatedIrqDescriptorParser.TryParseResourceList(descriptor, out var parsed));
        Assert.AreEqual(42u, parsed.Irq);
        Assert.AreEqual((ushort)0, parsed.ProcessorGroup);
        Assert.AreEqual(1UL << 3, parsed.AffinityMask);
        Assert.AreEqual((ushort)0x0002, parsed.RawFlags);

        Assert.IsFalse(AllocatedIrqDescriptorParser.TryParseResourceList(descriptor[..^1], out _));

        var requirementsList = descriptor.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(requirementsList.AsSpan(0, 4), 1);
        Assert.IsFalse(AllocatedIrqDescriptorParser.TryParseResourceList(requirementsList, out _));

        var wrongType = descriptor.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(wrongType.AsSpan(4, 4), 8);
        Assert.IsFalse(AllocatedIrqDescriptorParser.TryParseResourceList(wrongType, out _));

        var noProcessorTarget = descriptor.ToArray();
        BinaryPrimitives.WriteUInt64LittleEndian(noProcessorTarget.AsSpan(16, 8), 0);
        Assert.IsFalse(AllocatedIrqDescriptorParser.TryParseResourceList(noProcessorTarget, out _));

        var startedAt = DateTimeOffset.UnixEpoch;
        var rawFrames = new PresentMonFrameCaptureSnapshot(
            PresentMonWorkloadCaptureStatus.Available,
            77,
            30_000,
            30_000,
            new PresentMonApiVersionSnapshot(3, 4, 0),
            Enumerable.Range(0, 1_000)
                .Select(index => new PresentMonFrameMetricsSnapshot(
                    1,
                    8d + index / 10_000d,
                    5d,
                    3d,
                    6d,
                    5d,
                    1d,
                    index == 999,
                    4d,
                    7d))
                .ToArray(),
            [],
            "PresentMonAPI2.dll",
            null,
            null,
            startedAt,
            startedAt.AddSeconds(30));

        var rawSeries = PresentMonGuardrailSeriesBuilder.Create(rawFrames);
        Assert.AreEqual(1_000, rawSeries[PresentMonGuardrailSeriesBuilder.CpuFrameTimeMetric].Samples.Count);
        Assert.AreEqual(1_000, rawSeries[PresentMonGuardrailSeriesBuilder.DroppedFrameRatioMetric].Samples.Count);
        Assert.AreEqual(1d, rawSeries[PresentMonGuardrailSeriesBuilder.DroppedFrameRatioMetric].Samples[^1]);
        Assert.IsFalse(rawSeries.ContainsKey(PresentMonGuardrailSeriesBuilder.DisplayedFpsMetric));
        Assert.IsFalse(rawSeries.ContainsKey(PresentMonGuardrailSeriesBuilder.PresentedFpsMetric));

        var incompleteFrames = rawFrames with
        {
            Frames = rawFrames.Frames.Select((frame, index) =>
                index == 500 ? frame with { CpuFrameTimeMilliseconds = null } : frame).ToArray(),
        };
        Assert.IsFalse(PresentMonGuardrailSeriesBuilder.Create(incompleteFrames)
            .ContainsKey(PresentMonGuardrailSeriesBuilder.CpuFrameTimeMetric));
    }
}
