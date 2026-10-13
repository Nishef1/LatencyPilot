using Microsoft.Win32;

namespace LatencyPilot.Platform.Windows.Devices;

/// <summary>
/// Maps a PnP driver service name to the kernel module file stems it can load as.
/// A service name is not a module name: Realtek's <c>rt68cx21</c> service loads
/// <c>rt68cx21x64.sys</c> and Intel's <c>e1dexpress</c> loads <c>e1d68x64.sys</c>.
/// The documented <c>Services\&lt;name&gt;\ImagePath</c> value is the authoritative
/// service-to-image link; the service name stays as a fallback candidate.
/// </summary>
public static class DriverServiceImageResolver
{
    private const string ServicesKeyPath = @"SYSTEM\CurrentControlSet\Services\";

    public static IReadOnlyList<string> ResolveModuleStems(string? serviceName)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
        {
            return [];
        }

        var stems = new List<string>(2);
        var serviceStem = NormalizeStem(serviceName);
        if (serviceStem.Length > 0)
        {
            stems.Add(serviceStem);
        }

        var imageStem = TryReadImageStem(serviceName.Trim());
        if (imageStem is not null &&
            !stems.Contains(imageStem, StringComparer.OrdinalIgnoreCase))
        {
            stems.Add(imageStem);
        }

        return stems;
    }

    internal static string? ParseImageStem(string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            return null;
        }

        var value = imagePath.Trim().Trim('"');
        var sysIndex = value.IndexOf(".sys", StringComparison.OrdinalIgnoreCase);
        if (sysIndex < 0)
        {
            return null;
        }

        var stem = NormalizeStem(value[..(sysIndex + 4)]);
        return stem.Length == 0 ? null : stem;
    }

    private static string? TryReadImageStem(string serviceName)
    {
        // The service name becomes one registry key component; reject separators so
        // a hostile device property cannot redirect the read to another key.
        if (serviceName.IndexOfAny(['\\', '/', '\0']) >= 0)
        {
            return null;
        }

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(ServicesKeyPath + serviceName, writable: false);
            var imagePath = key?.GetValue(
                "ImagePath",
                null,
                RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
            return ParseImageStem(imagePath);
        }
        catch (Exception exception) when (
            exception is global::System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    private static string NormalizeStem(string value) =>
        Path.GetFileNameWithoutExtension(Path.GetFileName(value.Trim()));
}
