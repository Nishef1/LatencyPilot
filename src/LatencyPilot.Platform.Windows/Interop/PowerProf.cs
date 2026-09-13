using System.Runtime.InteropServices;

namespace LatencyPilot.Platform.Windows.Interop;

internal static partial class PowerProf
{
    [LibraryImport("powrprof.dll")]
    internal static partial uint PowerGetActiveScheme(
        nint userRootPowerKey,
        out nint activePolicyGuid);

    [LibraryImport("powrprof.dll")]
    internal static partial uint PowerReadFriendlyName(
        nint rootPowerKey,
        ref Guid schemeGuid,
        nint subgroupOfPowerSettingsGuid,
        nint powerSettingGuid,
        nint buffer,
        ref uint bufferSize);

    [LibraryImport("powrprof.dll")]
    internal static partial uint PowerGetUserConfiguredACPowerMode(out Guid powerModeGuid);

    [LibraryImport("powrprof.dll")]
    internal static partial uint PowerGetUserConfiguredDCPowerMode(out Guid powerModeGuid);
}
