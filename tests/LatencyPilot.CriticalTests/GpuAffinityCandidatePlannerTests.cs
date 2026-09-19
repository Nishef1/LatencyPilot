#pragma warning disable CA1822 // AuditCase methods are reflection-invoked by ConsolidatedCriticalTests.
using System.Buffers.Binary;
using LatencyPilot.Benchmarking.Candidates;
using LatencyPilot.Core.System;
using LatencyPilot.Platform.Windows.Devices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LatencyPilot.CriticalTests;

[TestClass]
public sealed class GpuAffinityCandidatePlannerTests
{
    [AuditCase]
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

        var allCandidates = GpuAffinityCandidatePlanner.Create(topology, pressure);
        CollectionAssert.AreEqual(
            new[] { cpu3, cpu1, cpu2, cpu0 },
            allCandidates.Select(static candidate => candidate.Processor).ToArray(),
            "The automatic search must cover every eligible logical processor and rank using logical-CPU pressure.");

        var cappedCandidates = GpuAffinityCandidatePlanner.Create(topology, pressure, maximumCandidates: 2);
        Assert.AreEqual(2, cappedCandidates.Count);
        Assert.AreEqual(cpu3, cappedCandidates[0].Processor);
        Assert.AreEqual(cpu1, cappedCandidates[1].Processor,
            "An explicit cap must preserve physical-core diversity before spending budget on SMT siblings.");
        Assert.AreNotEqual(
            cappedCandidates[0].PhysicalCoreIndex,
            cappedCandidates[1].PhysicalCoreIndex);

        var cpuSets = new ProcessorCpuSetSnapshot(
            [
                new ProcessorCpuSetEntry(0, cpu0, 0, 0, 0, 0, 0, false, false, false, true, 0),
                new ProcessorCpuSetEntry(1, cpu1, 0, 0, 0, 0, 0, false, false, false, false, 0),
                new ProcessorCpuSetEntry(2, cpu2, 1, 0, 0, 0, 0, false, false, false, false, 0),
                new ProcessorCpuSetEntry(3, cpu3, 1, 0, 0, 0, 0, false, false, false, false, 0),
            ],
            DateTimeOffset.UnixEpoch);
        var eligibleCandidates = GpuAffinityCandidatePlanner.Create(topology, pressure, cpuSets);
        Assert.IsFalse(eligibleCandidates.Any(candidate => candidate.Processor == cpu0),
            "A realtime logical CPU must remain excluded.");
        Assert.IsTrue(eligibleCandidates.Any(candidate => candidate.Processor == cpu1),
            "An eligible SMT sibling must remain searchable even when the lower-numbered sibling is ineligible.");

        var manyProcessors = Enumerable.Range(0, 24)
            .Select(static index => new LogicalProcessorId(0, checked((byte)index)))
            .ToArray();
        var manyTopology = new ProcessorTopologySnapshot(
            [new ProcessorPackageSnapshot(0, manyProcessors)],
            manyProcessors.Select((processor, index) =>
                new ProcessorCoreSnapshot(index, 0, [processor])).ToArray(),
            DateTimeOffset.UnixEpoch);
        var manyPressure = manyProcessors
            .Select((processor, index) => new ProcessorPressureEvidence(processor, index / 100d))
            .ToArray();
        Assert.AreEqual(24, GpuAffinityCandidatePlanner.Create(manyTopology, manyPressure).Count,
            "The default automatic search must cover every eligible logical CPU in the supported single processor group, not silently stop at 16.");

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
    }
}
