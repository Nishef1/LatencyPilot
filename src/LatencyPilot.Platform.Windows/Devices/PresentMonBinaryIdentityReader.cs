using System.Diagnostics;

namespace LatencyPilot.Platform.Windows.Devices;

public sealed record PresentMonBinaryIdentity(
    string Path,
    string? ProductVersion,
    bool MeetsGpuBenchmarkMinimum);

public static class PresentMonBinaryIdentityReader
{
    private static readonly Version MinimumGpuBenchmarkVersion = new(2, 5, 1);

    public static PresentMonBinaryIdentity? Capture(string? apiPath = null)
    {
        var resolvedPath = PresentMonApiLocator.Resolve(apiPath);
        if (resolvedPath is null)
        {
            return null;
        }

        var versionInfo = FileVersionInfo.GetVersionInfo(resolvedPath);
        var productVersion = string.IsNullOrWhiteSpace(versionInfo.ProductVersion)
            ? versionInfo.FileVersion
            : versionInfo.ProductVersion;
        return new PresentMonBinaryIdentity(
            resolvedPath,
            productVersion,
            IsAtLeastMinimum(productVersion));
    }

    private static bool IsAtLeastMinimum(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim().Split(['-', '+'], 2)[0];
        return Version.TryParse(normalized, out var version) && version >= MinimumGpuBenchmarkVersion;
    }
}
