using System.Buffers.Binary;
using System.ComponentModel;
using System.Numerics;
using System.Runtime.InteropServices;
using LatencyPilot.Core.System;
using LatencyPilot.Platform.Windows.Interop;

namespace LatencyPilot.Platform.Windows.System;

public static class ProcessorTopologyReader
{
    private const int ErrorInsufficientBuffer = 122;
    private const int EntryHeaderSize = 8;
    private const int ProcessorRelationshipHeaderSize = 24;
    private const int GroupAffinityReservedSize = 6;

    public static ProcessorTopologySnapshot Capture()
    {
        var buffer = QueryAllRelationships();
        var cores = new List<ProcessorCoreSnapshot>();
        var packages = new List<ProcessorPackageSnapshot>();

        var offset = 0;
        while (offset < buffer.Length)
        {
            var remaining = buffer.AsSpan(offset);
            if (remaining.Length < EntryHeaderSize)
            {
                throw new InvalidDataException("Processor topology ended with a truncated entry header.");
            }

            var relationship = (LogicalProcessorRelationship)BinaryPrimitives.ReadInt32LittleEndian(remaining);
            var entrySize = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(remaining[4..]));
            if (entrySize < EntryHeaderSize || entrySize > remaining.Length)
            {
                throw new InvalidDataException($"Processor topology entry reported invalid size {entrySize}.");
            }

            var entry = remaining[..entrySize];
            if (relationship is LogicalProcessorRelationship.ProcessorCore or LogicalProcessorRelationship.ProcessorPackage)
            {
                var parsed = ParseProcessorRelationship(entry);
                if (relationship == LogicalProcessorRelationship.ProcessorCore)
                {
                    cores.Add(new ProcessorCoreSnapshot(cores.Count, parsed.EfficiencyClass, parsed.LogicalProcessors));
                }
                else
                {
                    packages.Add(new ProcessorPackageSnapshot(packages.Count, parsed.LogicalProcessors));
                }
            }

            offset += entrySize;
        }

        if (cores.Count == 0 || packages.Count == 0)
        {
            throw new InvalidDataException("Windows returned an incomplete processor topology without cores or packages.");
        }

        var coreProcessors = cores
            .SelectMany(static core => core.LogicalProcessors)
            .ToHashSet();
        var packageProcessors = packages
            .SelectMany(static package => package.LogicalProcessors)
            .ToHashSet();

        if (!coreProcessors.SetEquals(packageProcessors))
        {
            throw new InvalidDataException("Core and package processor masks disagree in the same topology snapshot.");
        }

        return new ProcessorTopologySnapshot(packages.ToArray(), cores.ToArray(), DateTimeOffset.UtcNow);
    }

    private static unsafe byte[] QueryAllRelationships()
    {
        uint requiredLength = 0;
        if (Kernel32.GetLogicalProcessorInformationEx(LogicalProcessorRelationship.All, null, ref requiredLength))
        {
            throw new InvalidDataException("Windows unexpectedly returned processor topology without a destination buffer.");
        }

        var error = Marshal.GetLastPInvokeError();
        if (error != ErrorInsufficientBuffer)
        {
            throw new Win32Exception(error, "Unable to determine the processor topology buffer size.");
        }

        if (requiredLength < EntryHeaderSize)
        {
            throw new InvalidDataException("Windows reported an invalid processor topology buffer size.");
        }

        while (true)
        {
            var buffer = new byte[checked((int)requiredLength)];
            fixed (byte* pointer = buffer)
            {
                var actualLength = checked((uint)buffer.Length);
                if (Kernel32.GetLogicalProcessorInformationEx(LogicalProcessorRelationship.All, pointer, ref actualLength))
                {
                    if (actualLength > buffer.Length)
                    {
                        throw new InvalidDataException("Windows returned more processor topology data than the allocated buffer can hold.");
                    }

                    return actualLength == buffer.Length
                        ? buffer
                        : buffer[..checked((int)actualLength)];
                }

                error = Marshal.GetLastPInvokeError();
                if (error == ErrorInsufficientBuffer && actualLength > buffer.Length)
                {
                    requiredLength = actualLength;
                    continue;
                }

                throw new Win32Exception(error, "Unable to capture processor topology.");
            }
        }
    }

    private static ParsedProcessorRelationship ParseProcessorRelationship(ReadOnlySpan<byte> entry)
    {
        var payload = entry[EntryHeaderSize..];
        if (payload.Length < ProcessorRelationshipHeaderSize)
        {
            throw new InvalidDataException("Processor relationship entry is truncated.");
        }

        var efficiencyClass = payload[1];
        var groupCount = BinaryPrimitives.ReadUInt16LittleEndian(payload[22..]);
        if (groupCount == 0)
        {
            throw new InvalidDataException("Processor relationship contains no affinity groups.");
        }

        var groupAffinitySize = IntPtr.Size + sizeof(ushort) + GroupAffinityReservedSize;
        var requiredPayloadSize = checked(ProcessorRelationshipHeaderSize + (groupCount * groupAffinitySize));
        if (payload.Length < requiredPayloadSize)
        {
            throw new InvalidDataException("Processor relationship affinity data is truncated.");
        }

        var logicalProcessors = new List<LogicalProcessorId>();
        for (var groupIndex = 0; groupIndex < groupCount; groupIndex++)
        {
            var affinityOffset = ProcessorRelationshipHeaderSize + (groupIndex * groupAffinitySize);
            var affinity = payload.Slice(affinityOffset, groupAffinitySize);
            var mask = IntPtr.Size == sizeof(ulong)
                ? BinaryPrimitives.ReadUInt64LittleEndian(affinity)
                : BinaryPrimitives.ReadUInt32LittleEndian(affinity);
            var group = BinaryPrimitives.ReadUInt16LittleEndian(affinity[IntPtr.Size..]);

            while (mask != 0)
            {
                var processorNumber = BitOperations.TrailingZeroCount(mask);
                logicalProcessors.Add(new LogicalProcessorId(group, checked((byte)processorNumber)));
                mask &= mask - 1;
            }
        }

        if (logicalProcessors.Count == 0)
        {
            throw new InvalidDataException("Processor relationship contains an empty affinity mask.");
        }

        var ordered = logicalProcessors
            .Distinct()
            .OrderBy(static processor => processor.Group)
            .ThenBy(static processor => processor.Number)
            .ToArray();

        return new ParsedProcessorRelationship(efficiencyClass, ordered);
    }

    private readonly record struct ParsedProcessorRelationship(
        byte EfficiencyClass,
        IReadOnlyList<LogicalProcessorId> LogicalProcessors);
}
