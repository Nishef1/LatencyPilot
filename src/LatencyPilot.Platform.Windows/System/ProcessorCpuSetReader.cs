using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using LatencyPilot.Core.System;
using LatencyPilot.Platform.Windows.Interop;

namespace LatencyPilot.Platform.Windows.System;

public static class ProcessorCpuSetReader
{
    private const int ErrorInsufficientBuffer = 122;
    private const int EntryHeaderSize = 8;
    private const int MinimumCpuSetEntrySize = 32;
    private const uint CpuSetInformationType = 0;
    private const uint MaximumBufferSize = 16 * 1024 * 1024;

    public static ProcessorCpuSetSnapshot Capture()
    {
        var buffer = QueryCpuSets();
        var cpuSets = new List<ProcessorCpuSetEntry>();
        var seenProcessors = new HashSet<LogicalProcessorId>();

        var offset = 0;
        while (offset < buffer.Length)
        {
            var remaining = buffer.AsSpan(offset);
            if (remaining.Length < EntryHeaderSize)
            {
                throw new InvalidDataException("CPU-set information ended with a truncated entry header.");
            }

            var size = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(remaining));
            var type = BinaryPrimitives.ReadUInt32LittleEndian(remaining[4..]);
            if (size < EntryHeaderSize || size > remaining.Length)
            {
                throw new InvalidDataException($"CPU-set entry reported invalid size {size}.");
            }

            var entry = remaining[..size];
            if (type == CpuSetInformationType)
            {
                if (size < MinimumCpuSetEntrySize)
                {
                    throw new InvalidDataException("CPU-set entry is smaller than the Windows CPU-set v1 layout.");
                }

                var processor = new LogicalProcessorId(
                    BinaryPrimitives.ReadUInt16LittleEndian(entry[12..]),
                    entry[14]);
                if (!seenProcessors.Add(processor))
                {
                    throw new InvalidDataException($"Windows returned more than one CPU-set entry for logical processor {processor}.");
                }

                var flags = entry[19];
                cpuSets.Add(new ProcessorCpuSetEntry(
                    BinaryPrimitives.ReadUInt32LittleEndian(entry[8..]),
                    processor,
                    entry[15],
                    entry[16],
                    entry[17],
                    entry[18],
                    entry[20],
                    (flags & 0x01) != 0,
                    (flags & 0x02) != 0,
                    (flags & 0x04) != 0,
                    (flags & 0x08) != 0,
                    BinaryPrimitives.ReadUInt64LittleEndian(entry[24..])));
            }

            offset += size;
        }

        if (cpuSets.Count == 0)
        {
            throw new InvalidDataException("Windows returned no CPU-set entries on a supported Windows 11 system.");
        }

        cpuSets.Sort(static (left, right) =>
        {
            var groupComparison = left.Processor.Group.CompareTo(right.Processor.Group);
            return groupComparison != 0
                ? groupComparison
                : left.Processor.Number.CompareTo(right.Processor.Number);
        });

        return new ProcessorCpuSetSnapshot(cpuSets.ToArray(), DateTimeOffset.UtcNow);
    }

    private static unsafe byte[] QueryCpuSets()
    {
        var process = Kernel32.GetCurrentProcess();
        if (Kernel32.GetSystemCpuSetInformation(null, 0, out var requiredLength, process, 0))
        {
            if (requiredLength == 0)
            {
                return [];
            }

            throw new InvalidDataException("Windows unexpectedly returned CPU-set data without a destination buffer.");
        }

        var error = Marshal.GetLastPInvokeError();
        if (error != ErrorInsufficientBuffer)
        {
            throw new Win32Exception(error, "Unable to determine the CPU-set information buffer size.");
        }

        if (requiredLength < MinimumCpuSetEntrySize || requiredLength > MaximumBufferSize)
        {
            throw new InvalidDataException($"Windows reported an invalid CPU-set buffer size of {requiredLength} bytes.");
        }

        while (true)
        {
            var buffer = new byte[checked((int)requiredLength)];
            fixed (byte* pointer = buffer)
            {
                if (Kernel32.GetSystemCpuSetInformation(
                    pointer,
                    checked((uint)buffer.Length),
                    out var actualLength,
                    process,
                    0))
                {
                    if (actualLength > buffer.Length)
                    {
                        throw new InvalidDataException("Windows returned more CPU-set data than the allocated buffer can hold.");
                    }

                    return actualLength == buffer.Length
                        ? buffer
                        : buffer[..checked((int)actualLength)];
                }

                error = Marshal.GetLastPInvokeError();
                if (error == ErrorInsufficientBuffer &&
                    actualLength > buffer.Length &&
                    actualLength <= MaximumBufferSize)
                {
                    requiredLength = actualLength;
                    continue;
                }

                throw new Win32Exception(error, "Unable to capture CPU-set information.");
            }
        }
    }
}
