using System.Management;
using System.Runtime.InteropServices;

namespace LatencyPilot.Platform.Windows.Devices;

public enum DeviceInterruptVectorReadStatus
{
    Available = 0,
    NoInterruptResources = 1,
    ProviderUnavailable = 2,
    ReadFailed = 3,
}

public sealed record DeviceInterruptVectorSnapshot(
    string DeviceInstanceId,
    DeviceInterruptVectorReadStatus Status,
    IReadOnlyList<int> Vectors,
    string? Error)
{
    public bool HasVectors =>
        Status == DeviceInterruptVectorReadStatus.Available &&
        Vectors.Count > 0;
}

/// <summary>
/// Read-only fallback that maps a concrete PnP device to its Windows IRQ
/// resources through Win32_PnPAllocatedResource -> Win32_IRQResource.
/// This is intentionally independent from ConfigMgr's allocated-log-config
/// reader because some modern PCI devices expose one surface while the other
/// returns no readable translated allocation.
/// </summary>
public static class PnpInterruptVectorReader
{
    public static IReadOnlyDictionary<string, DeviceInterruptVectorSnapshot> CaptureMany(
        IEnumerable<string> deviceInstanceIds)
    {
        ArgumentNullException.ThrowIfNull(deviceInstanceIds);

        var ids = deviceInstanceIds
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var result = new Dictionary<string, DeviceInterruptVectorSnapshot>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var id in ids)
        {
            result[id] = Capture(id);
        }

        return result;
    }

    public static DeviceInterruptVectorSnapshot Capture(string deviceInstanceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceInstanceId);

        if (!OperatingSystem.IsWindows())
        {
            return new(
                deviceInstanceId,
                DeviceInterruptVectorReadStatus.ProviderUnavailable,
                [],
                "Win32_PnPAllocatedResource is available only on Windows.");
        }

        try
        {
            var escapedId = EscapeObjectPathKey(deviceInstanceId);
            var query =
                $"ASSOCIATORS OF {{Win32_PnPEntity.DeviceID=\"{escapedId}\"}} " +
                "WHERE AssocClass=Win32_PnPAllocatedResource ResultClass=Win32_IRQResource";

            using var searcher = new ManagementObjectSearcher(
                new ManagementScope(@"\\.\root\cimv2"),
                new ObjectQuery(query));
            using var objects = searcher.Get();

            var vectors = new HashSet<int>();
            foreach (ManagementObject resource in objects)
            {
                using (resource)
                {
                    if (resource["Vector"] is null)
                    {
                        continue;
                    }

                    var vector = Convert.ToUInt32(
                        resource["Vector"],
                        global::System.Globalization.CultureInfo.InvariantCulture);
                    if (vector is > 0 and <= byte.MaxValue)
                    {
                        vectors.Add(checked((int)vector));
                    }
                }
            }

            var ordered = vectors.OrderBy(static vector => vector).ToArray();
            return ordered.Length == 0
                ? new(
                    deviceInstanceId,
                    DeviceInterruptVectorReadStatus.NoInterruptResources,
                    [],
                    null)
                : new(
                    deviceInstanceId,
                    DeviceInterruptVectorReadStatus.Available,
                    ordered,
                    null);
        }
        catch (Exception exception) when (exception is
            ManagementException or
            COMException or
            UnauthorizedAccessException or
            global::System.Security.SecurityException)
        {
            return new(
                deviceInstanceId,
                DeviceInterruptVectorReadStatus.ReadFailed,
                [],
                $"{exception.GetType().Name}: {exception.Message}");
        }
    }

    private static string EscapeObjectPathKey(string value) =>
        value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
}
