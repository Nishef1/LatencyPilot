using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace LatencyPilot.Platform.Windows.System;

public enum SystemPowerLineState
{
    Offline = 0,
    Online = 1,
    Unknown = 255,
}

public enum UserConfiguredPowerMode
{
    BestPowerEfficiency = 1,
    Balanced = 2,
    BestPerformance = 3,
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
    bool? BatterySaverEnabled,
    Guid? ActiveSchemeId,
    string? ActiveSchemeName,
    Guid? UserConfiguredPowerModeId,
    UserConfiguredPowerMode? UserConfiguredPowerMode);

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
        StartPower.BatterySaverEnabled != EndPower.BatterySaverEnabled ||
        StartPower.ActiveSchemeId != EndPower.ActiveSchemeId ||
        StartPower.UserConfiguredPowerModeId != EndPower.UserConfiguredPowerModeId;
}

public static class RuntimeMeasurementContextReader
{
    private static readonly Guid BestEfficiencyPowerModeId = new("961cc777-2547-4f9d-8174-7d86181b8a7a");
    private static readonly Guid BalancedPowerModeId = Guid.Empty;
    private static readonly Guid BestPerformancePowerModeId = new("ded574b5-45a0-4f42-8737-46345c09c238");

    private const byte UnknownByte = byte.MaxValue;
    private const byte BatteryChargingFlag = 0x08;
    private const byte NoSystemBatteryFlag = 0x80;
    private const uint ErrorSuccess = 0;

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
        var (activeSchemeId, activeSchemeName) = TryCaptureActivePowerScheme();
        var (configuredModeId, configuredMode) = TryCaptureUserConfiguredPowerMode(lineState);

        return new SystemPowerSnapshot(
            lineState,
            batteryPresent,
            charging,
            batteryPercent,
            batterySaverEnabled,
            activeSchemeId,
            activeSchemeName,
            configuredModeId,
            configuredMode);
    }

    private static (Guid? SchemeId, string? SchemeName) TryCaptureActivePowerScheme()
    {
        var result = PowerGetActiveScheme(IntPtr.Zero, out var activePolicyGuid);
        if (result != ErrorSuccess || activePolicyGuid == IntPtr.Zero)
        {
            return (null, null);
        }

        try
        {
            var schemeId = Marshal.PtrToStructure<Guid>(activePolicyGuid);
            return (schemeId, TryReadPowerSchemeFriendlyName(schemeId));
        }
        finally
        {
            _ = LocalFree(activePolicyGuid);
        }
    }

    private static (Guid? ModeId, UserConfiguredPowerMode? Mode) TryCaptureUserConfiguredPowerMode(
        SystemPowerLineState lineState)
    {
        Guid modeId;
        var result = lineState switch
        {
            SystemPowerLineState.Online => PowerGetUserConfiguredACPowerMode(out modeId),
            SystemPowerLineState.Offline => PowerGetUserConfiguredDCPowerMode(out modeId),
            _ => uint.MaxValue,
        };

        if (result != ErrorSuccess)
        {
            return (null, null);
        }

        return (modeId, MapUserConfiguredPowerMode(modeId));
    }

    private static UserConfiguredPowerMode MapUserConfiguredPowerMode(Guid modeId)
    {
        if (modeId == BestEfficiencyPowerModeId)
        {
            return UserConfiguredPowerMode.BestPowerEfficiency;
        }

        if (modeId == BalancedPowerModeId)
        {
            return UserConfiguredPowerMode.Balanced;
        }

        return modeId == BestPerformancePowerModeId
            ? UserConfiguredPowerMode.BestPerformance
            : UserConfiguredPowerMode.Unknown;
    }

    private static string? TryReadPowerSchemeFriendlyName(Guid schemeId)
    {
        uint bufferSize = 0;
        var result = PowerReadFriendlyName(
            IntPtr.Zero,
            ref schemeId,
            IntPtr.Zero,
            IntPtr.Zero,
            null,
            ref bufferSize);
        if (result != ErrorSuccess || bufferSize == 0)
        {
            return null;
        }

        var buffer = new byte[bufferSize];
        result = PowerReadFriendlyName(
            IntPtr.Zero,
            ref schemeId,
            IntPtr.Zero,
            IntPtr.Zero,
            buffer,
            ref bufferSize);
        if (result != ErrorSuccess)
        {
            return null;
        }

        var name = Encoding.Unicode.GetString(buffer).TrimEnd('\0');
        return string.IsNullOrWhiteSpace(name) ? null : name;
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

    [DllImport("powrprof.dll")]
    private static extern uint PowerGetActiveScheme(
        IntPtr userRootPowerKey,
        out IntPtr activePolicyGuid);

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadFriendlyName(
        IntPtr rootPowerKey,
        ref Guid schemeGuid,
        IntPtr subgroupOfPowerSettingsGuid,
        IntPtr powerSettingGuid,
        [Out] byte[]? buffer,
        ref uint bufferSize);

    [DllImport("powrprof.dll")]
    private static extern uint PowerGetUserConfiguredACPowerMode(out Guid powerModeGuid);

    [DllImport("powrprof.dll")]
    private static extern uint PowerGetUserConfiguredDCPowerMode(out Guid powerModeGuid);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);

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
