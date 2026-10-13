using System.ComponentModel;
using System.Runtime.InteropServices;

namespace LatencyPilot.Platform.Windows.System;

public sealed record ProcessorTimes(long Idle, long Kernel, long User);

/// <summary>
/// Per-logical-processor busy time from NtQuerySystemInformation
/// (SystemProcessorPerformanceInformation). It is the load a CPU spends running
/// threads, which an interrupt placed there steals from; ETW DPC/ISR totals alone
/// cannot see it.
/// </summary>
public static partial class ProcessorUtilizationSampler
{
    private const int SystemProcessorPerformanceInformation = 8;
    private const int EntrySize = 48; // 5 x LARGE_INTEGER + ULONG InterruptCount + padding

    public static IReadOnlyList<ProcessorTimes> Capture()
    {
        unsafe
        {
            var buffer = new byte[EntrySize * 256];
            fixed (byte* pointer = buffer)
            {
                var status = NtQuerySystemInformation(
                    SystemProcessorPerformanceInformation,
                    pointer,
                    buffer.Length,
                    out var returned);
                if (status != 0)
                {
                    throw new Win32Exception(
                        status,
                        "NtQuerySystemInformation(SystemProcessorPerformanceInformation) failed.");
                }

                var count = returned / EntrySize;
                var result = new ProcessorTimes[count];
                for (var index = 0; index < count; index++)
                {
                    var offset = index * EntrySize;
                    result[index] = new ProcessorTimes(
                        BitConverter.ToInt64(buffer, offset),
                        BitConverter.ToInt64(buffer, offset + 8),
                        BitConverter.ToInt64(buffer, offset + 16));
                }

                return result;
            }
        }
    }

    /// <summary>Busy fraction (0..1) per logical processor number between two samples.</summary>
    public static IReadOnlyDictionary<int, double> BusyFractions(
        IReadOnlyList<ProcessorTimes> before,
        IReadOnlyList<ProcessorTimes> after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        var result = new Dictionary<int, double>();
        var count = Math.Min(before.Count, after.Count);
        for (var index = 0; index < count; index++)
        {
            var idle = after[index].Idle - before[index].Idle;
            // KernelTime already includes idle time.
            var total = (after[index].Kernel - before[index].Kernel) + (after[index].User - before[index].User);
            if (total <= 0)
            {
                continue;
            }

            result[index] = Math.Clamp(1d - ((double)idle / total), 0d, 1d);
        }

        return result;
    }

    [LibraryImport("ntdll.dll")]
    private static unsafe partial int NtQuerySystemInformation(
        int informationClass,
        byte* buffer,
        int length,
        out int returnLength);
}
