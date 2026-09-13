using System.ComponentModel;
using System.Runtime.InteropServices;

namespace LatencyPilot.Platform.Windows.System;

public enum SystemPowerLineState
{
    Offline = 0,
    Online = 1,
    Unknown = 255,
}

public sealed record SystemLoadSnapshot(
    ulong IdleTime100Nanoseconds,
    ulong KernelTime100Nanoseconds,
    ulong UserTime100Nanoseconds);

public sealed record SystemPowerSnapshot(
    SystemPowerLineState LineState,
    bool? BatteryPresent,
    bool? Charging,
    int? BatteryPercent,
    bool? BatterySaverEnabled);

public sealed record RuntimeMeasurementContextSnapshot(
    SystemLoadSnapshot SystemLoad,
    SystemPowerSnapshot Power);

public sealed record RuntimeMeasurementContextInterval(
    double? SystemCpuBusyPercent,
    SystemPowerSnapshot StartPower,
    SystemPowerSnapshot EndPower)
{
    public bool PowerContextChanged =>
        StartPower.LineState != EndPower.LineState ||
        StartPower.Charging != EndPower.Charging ||
        StartPower.BatterySaverEnabled != EndPower.BatterySaverEnabled;
}

public static class RuntimeMeasurementContextReader
{
    private const byte UnknownByte = byte.MaxValue;
    private const byte BatteryChargingFlag = 0x08;
    private const byte NoSystemBatteryFlag = 0x80;

    public static RuntimeMeasurementContextSnapshot Capture() =>
        new(CaptureSystemLoad(), CapturePower());

    public static RuntimeMeasurementContextInterval CreateInterval(
        RuntimeMeasurementContextSnapshot start,
        RuntimeMeasurementContextSnapshot end)
    {
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(end);

        return new RuntimeMeasurementContextInterval(
            CalculateSystemCpuBusyPercent(start.SystemLoad, end.SystemLoad),
            start.Power,
            end.Power);
    }

    public static double? CalculateSystemCpuBusyPercent(
        SystemLoadSnapshot start,
        SystemLoadSnapshot end)
    {
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(end);

        if (end.IdleTime100Nanoseconds < start.IdleTime100Nanoseconds ||
            end.KernelTime100Nanoseconds < start.KernelTime100Nanoseconds ||
            end.UserTime100Nanoseconds < start.UserTime100Nanoseconds)
        {
            return null;
        }

        var idleDelta = end.IdleTime100Nanoseconds - start.IdleTime100Nanoseconds;
        var kernelDelta = end.KernelTime100Nanoseconds - start.KernelTime100Nanoseconds;
        var userDelta = end.UserTime100Nanoseconds - start.UserTime100Nanoseconds;
        var totalDelta = kernelDelta + (double)userDelta;

        if (totalDelta <= 0d || idleDelta > totalDelta)
        {
            return null;
        }

        var busyPercent = (totalDelta - idleDelta) * 100d / totalDelta;
        return Math.Clamp(busyPercent, 0d, 100d);
    }

    private static SystemLoadSnapshot CaptureSystemLoad()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "GetSystemTimes failed.");
        }

        return new SystemLoadSnapshot(
            ToUInt64(idle),
            ToUInt64(kernel),
            ToUInt64(user));
    }

    private static SystemPowerSnapshot CapturePower()
    {
        if (!GetSystemPowerStatus(out var status))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "GetSystemPowerStatus failed.");
        }

        var lineState = status.AcLineStatus switch
        {
            0 => SystemPowerLineState.Offline,
            1 => SystemPowerLineState.Online,
            _ => SystemPowerLineState.Unknown,
        };

        bool? batteryPresent = status.BatteryFlag == UnknownByte
            ? null
            : (status.BatteryFlag & NoSystemBatteryFlag) == 0;
        bool? charging = status.BatteryFlag == UnknownByte || batteryPresent == false
            ? null
            : (status.BatteryFlag & BatteryChargingFlag) != 0;
        int? batteryPercent = status.BatteryLifePercent == UnknownByte
            ? null
            : status.BatteryLifePercent;
        bool? batterySaverEnabled = status.SystemStatusFlag switch
        {
            0 => false,
            1 => true,
            _ => null,
        };

        return new SystemPowerSnapshot(
            lineState,
            batteryPresent,
            charging,
            batteryPercent,
            batterySaverEnabled);
    }

    private static ulong ToUInt64(NativeFileTime value) =>
        ((ulong)value.HighDateTime << 32) | value.LowDateTime;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(
        out NativeFileTime idleTime,
        out NativeFileTime kernelTime,
        out NativeFileTime userTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out NativeSystemPowerStatus systemPowerStatus);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeFileTime
    {
        public uint LowDateTime;
        public uint HighDateTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSystemPowerStatus
    {
        public byte AcLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }
}
