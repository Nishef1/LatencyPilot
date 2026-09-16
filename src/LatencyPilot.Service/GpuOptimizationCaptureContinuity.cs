using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using LatencyPilot.Platform.Windows.Devices;
using LatencyPilot.Platform.Windows.System;

namespace LatencyPilot.Service;

internal sealed record GpuOptimizationCaptureContinuitySnapshot(
    uint ProcessId,
    DateTimeOffset ProcessStartedAtUtc,
    string ProcessName,
    int ProcessSessionId,
    uint ActiveConsoleSessionId,
    SystemPowerSnapshot Power,
    SystemAwakeTimeSnapshot AwakeTime,
    GpuGraphicsTargetIdentitySnapshot GraphicsTarget);

internal sealed record GpuOptimizationCaptureContinuityResult(
    bool IsStable,
    IReadOnlyList<string> Reasons);

internal static class GpuOptimizationCaptureContinuity
{
    private const uint NoActiveConsoleSession = uint.MaxValue;

    internal static bool TryCapture(
        uint processId,
        string targetDeviceInstanceId,
        string? presentMonApiPath,
        string? presentMonControlPipeName,
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

        ArgumentException.ThrowIfNullOrWhiteSpace(targetDeviceInstanceId);

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

            var graphicsTarget = GpuGraphicsTargetIdentityResolver.Capture(
                targetDeviceInstanceId,
                presentMonApiPath,
                presentMonControlPipeName);
            if (!graphicsTarget.IsUsable || graphicsTarget.Identity is null)
            {
                reason = graphicsTarget.Reason ?? "The GPU target identity could not be established.";
                return false;
            }

            snapshot = new GpuOptimizationCaptureContinuitySnapshot(
                processId,
                new DateTimeOffset(process.StartTime.ToUniversalTime()),
                process.ProcessName,
                processSessionId,
                activeConsoleSessionId,
                RuntimeMeasurementContextReader.Capture().Power,
                SystemAwakeTimeReader.Capture(),
                graphicsTarget.Identity);
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

        var awakeInterval = SystemAwakeTimeReader.Evaluate(before.AwakeTime, after.AwakeTime);
        if (!awakeInterval.IsValid || awakeInterval.SleepOrSuspendDetected)
        {
            reasons.Add(awakeInterval.Reason ?? "The system sleep/awake interval is not usable for comparison.");
        }

        if (before.GraphicsTarget.HardwareAdapterCount != 1 ||
            after.GraphicsTarget.HardwareAdapterCount != 1)
        {
            reasons.Add("Hybrid or multi-GPU workload routing cannot be proven directly, so GPU optimization is fail-closed.");
        }

        if (!string.Equals(
                before.GraphicsTarget.DeviceInstanceId,
                after.GraphicsTarget.DeviceInstanceId,
                StringComparison.OrdinalIgnoreCase) ||
            before.GraphicsTarget.Luid != after.GraphicsTarget.Luid)
        {
            reasons.Add("The target GPU PnP or DXGI identity changed during capture.");
        }

        return new GpuOptimizationCaptureContinuityResult(
            reasons.Count == 0,
            reasons.AsReadOnly());
    }

    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();
}
