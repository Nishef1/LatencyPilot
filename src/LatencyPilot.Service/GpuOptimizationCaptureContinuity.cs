using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using LatencyPilot.Platform.Windows.System;

namespace LatencyPilot.Service;

internal sealed record GpuOptimizationCaptureContinuitySnapshot(
    uint ProcessId,
    DateTimeOffset ProcessStartedAtUtc,
    string ProcessName,
    int ProcessSessionId,
    uint ActiveConsoleSessionId,
    SystemPowerSnapshot Power);

internal sealed record GpuOptimizationCaptureContinuityResult(
    bool IsStable,
    IReadOnlyList<string> Reasons);

internal static partial class GpuOptimizationCaptureContinuity
{
    private const uint NoActiveConsoleSession = uint.MaxValue;

    internal static bool TryCapture(
        uint processId,
        out GpuOptimizationCaptureContinuitySnapshot? snapshot,
        out string? reason)
    {
        snapshot = null;
        reason = null;

        if (processId == 0 || processId > int.MaxValue)
        {
            reason = "The workload process ID is outside the supported local-process range.";
            return false;
        }

        try
        {
            using var process = Process.GetProcessById(checked((int)processId));
            process.Refresh();
            if (process.HasExited)
            {
                reason = "The workload process exited before continuity could be verified.";
                return false;
            }

            var processSessionId = process.SessionId;
            var activeConsoleSessionId = WTSGetActiveConsoleSessionId();
            if (activeConsoleSessionId == NoActiveConsoleSession)
            {
                reason = "Windows reports no active local console session for the workload.";
                return false;
            }

            if (processSessionId < 0 || (uint)processSessionId != activeConsoleSessionId)
            {
                reason = "The workload process is not running in the active local console session.";
                return false;
            }

            snapshot = new GpuOptimizationCaptureContinuitySnapshot(
                processId,
                new DateTimeOffset(process.StartTime.ToUniversalTime()),
                process.ProcessName,
                processSessionId,
                activeConsoleSessionId,
                RuntimeMeasurementContextReader.Capture().Power);
            return true;
        }
        catch (Exception exception) when (exception is
            ArgumentException or
            InvalidOperationException or
            Win32Exception or
            NotSupportedException)
        {
            reason = $"Workload process continuity could not be read: {exception.GetType().Name}: {exception.Message}";
            return false;
        }
    }

    internal static GpuOptimizationCaptureContinuityResult Evaluate(
        GpuOptimizationCaptureContinuitySnapshot before,
        GpuOptimizationCaptureContinuitySnapshot after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        var reasons = new List<string>();
        if (before.ProcessId != after.ProcessId ||
            before.ProcessStartedAtUtc != after.ProcessStartedAtUtc ||
            !string.Equals(before.ProcessName, after.ProcessName, StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add("The workload process instance changed or its PID was reused during capture.");
        }

        if (before.ActiveConsoleSessionId == NoActiveConsoleSession ||
            after.ActiveConsoleSessionId == NoActiveConsoleSession ||
            before.ActiveConsoleSessionId != after.ActiveConsoleSessionId ||
            before.ProcessSessionId != after.ProcessSessionId ||
            before.ProcessSessionId < 0 ||
            after.ProcessSessionId < 0 ||
            (uint)before.ProcessSessionId != before.ActiveConsoleSessionId ||
            (uint)after.ProcessSessionId != after.ActiveConsoleSessionId)
        {
            reasons.Add("The active local console or workload session changed during capture.");
        }

        var powerInterval = new RuntimeMeasurementContextInterval(
            SystemCpuBusyPercent: null,
            before.Power,
            after.Power);
        if (powerInterval.PowerContextChanged)
        {
            reasons.Add("The AC/DC, charging, Battery Saver, power scheme, or Windows power mode changed during capture.");
        }

        return new GpuOptimizationCaptureContinuityResult(
            reasons.Count == 0,
            reasons.AsReadOnly());
    }

    [LibraryImport("kernel32.dll")]
    private static partial uint WTSGetActiveConsoleSessionId();
}
