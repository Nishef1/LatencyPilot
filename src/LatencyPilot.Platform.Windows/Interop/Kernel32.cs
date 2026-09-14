using System.Runtime.InteropServices;

namespace LatencyPilot.Platform.Windows.Interop;

internal enum LogicalProcessorRelationship : int
{
    ProcessorCore = 0,
    ProcessorPackage = 3,
    All = 0xFFFF,
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeFileTime
{
    internal uint LowDateTime;
    internal uint HighDateTime;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeSystemPowerStatus
{
    internal byte AcLineStatus;
    internal byte BatteryFlag;
    internal byte BatteryLifePercent;
    internal byte SystemStatusFlag;
    internal uint BatteryLifeTime;
    internal uint BatteryFullLifeTime;
}

internal static partial class Kernel32
{
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static unsafe partial bool GetLogicalProcessorInformationEx(
        LogicalProcessorRelationship relationshipType,
        byte* buffer,
        ref uint returnedLength);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static unsafe partial bool GetSystemCpuSetInformation(
        byte* information,
        uint bufferLength,
        out uint returnedLength,
        nint process,
        uint flags);

    [LibraryImport("kernel32.dll")]
    internal static partial nint GetCurrentProcess();

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetSystemTimes(
        out NativeFileTime idleTime,
        out NativeFileTime kernelTime,
        out NativeFileTime userTime);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetSystemPowerStatus(out NativeSystemPowerStatus systemPowerStatus);

    [LibraryImport("kernel32.dll")]
    internal static partial nint LocalFree(nint memory);
}
