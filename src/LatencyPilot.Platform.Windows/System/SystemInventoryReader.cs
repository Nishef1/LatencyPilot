using System.Runtime.InteropServices;
using LatencyPilot.Core.System;

namespace LatencyPilot.Platform.Windows.System;

public static class SystemInventoryReader
{
    public static SystemInventorySnapshot Capture() => new(
        GetOperatingSystemDisplayName(),
        RuntimeInformation.OSArchitecture.ToString(),
        RuntimeInformation.ProcessArchitecture.ToString(),
        Environment.ProcessorCount,
        DateTimeOffset.UtcNow);

    private static string GetOperatingSystemDisplayName()
    {
        if (!OperatingSystem.IsWindows())
        {
            return RuntimeInformation.OSDescription;
        }

        var version = Environment.OSVersion.Version;
        var productName = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)
            ? "Microsoft Windows 11"
            : "Microsoft Windows";

        return $"{productName} · build {version.Build} · NT {version.Major}.{version.Minor}";
    }
}
