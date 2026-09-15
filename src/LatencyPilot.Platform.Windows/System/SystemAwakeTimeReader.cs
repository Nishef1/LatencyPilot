using System.ComponentModel;
using System.Runtime.InteropServices;

namespace LatencyPilot.Platform.Windows.System;

public sealed record SystemAwakeTimeSnapshot(
    ulong UptimeMilliseconds,
    ulong AwakeTime100Nanoseconds);

public sealed record SystemAwakeTimeInterval(
    bool IsValid,
    bool SleepOrSuspendDetected,
    double UptimeDeltaMilliseconds,
    double AwakeDeltaMilliseconds,
    double NonAwakeDeltaMilliseconds,
    string? Reason);

public static class SystemAwakeTimeReader
{
    public const double DefaultNonAwakeToleranceMilliseconds = 250d;
    private const double HundredNanosecondsPerMillisecond = 10_000d;

    public static SystemAwakeTimeSnapshot Capture()
    {
        if (!QueryUnbiasedInterruptTime(out var awakeTime100Nanoseconds))
        {
            throw new Win32Exception(
                Marshal.GetLastPInvokeError(),
                "Windows could not read unbiased awake time.");
        }

        return new SystemAwakeTimeSnapshot(
            GetTickCount64(),
            awakeTime100Nanoseconds);
    }

    public static SystemAwakeTimeInterval Evaluate(
        SystemAwakeTimeSnapshot before,
        SystemAwakeTimeSnapshot after,
        double nonAwakeToleranceMilliseconds = DefaultNonAwakeToleranceMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        if (!double.IsFinite(nonAwakeToleranceMilliseconds) || nonAwakeToleranceMilliseconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(nonAwakeToleranceMilliseconds));
        }

        if (after.UptimeMilliseconds < before.UptimeMilliseconds ||
            after.AwakeTime100Nanoseconds < before.AwakeTime100Nanoseconds)
        {
            return new SystemAwakeTimeInterval(
                false,
                true,
                0,
                0,
                0,
                "A monotonic Windows measurement clock moved backwards.");
        }

        var uptimeDeltaMilliseconds = (double)(after.UptimeMilliseconds - before.UptimeMilliseconds);
        var awakeDeltaMilliseconds =
            (after.AwakeTime100Nanoseconds - before.AwakeTime100Nanoseconds) /
            HundredNanosecondsPerMillisecond;
        var nonAwakeDeltaMilliseconds = Math.Max(0d, uptimeDeltaMilliseconds - awakeDeltaMilliseconds);
        var sleepDetected = nonAwakeDeltaMilliseconds > nonAwakeToleranceMilliseconds;

        return new SystemAwakeTimeInterval(
            true,
            sleepDetected,
            uptimeDeltaMilliseconds,
            awakeDeltaMilliseconds,
            nonAwakeDeltaMilliseconds,
            sleepDetected
                ? $"Windows spent about {nonAwakeDeltaMilliseconds:F0} ms asleep or suspended during measurement."
                : null);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryUnbiasedInterruptTime(out ulong unbiasedTime);

    [DllImport("kernel32.dll")]
    private static extern ulong GetTickCount64();
}
