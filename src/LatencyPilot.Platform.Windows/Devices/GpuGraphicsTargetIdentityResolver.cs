using System.Globalization;
using LatencyPilot.Core.Devices;

namespace LatencyPilot.Platform.Windows.Devices;

public sealed record GpuGraphicsTargetIdentitySnapshot(
    string DeviceInstanceId,
    GraphicsAdapterLuid Luid,
    uint PresentMonDeviceId,
    string AdapterName,
    int HardwareAdapterCount);

public sealed record GpuGraphicsTargetIdentityResolution(
    bool IsUsable,
    GpuGraphicsTargetIdentitySnapshot? Identity,
    string? Reason)
{
    internal static GpuGraphicsTargetIdentityResolution Unusable(string reason) =>
        new(false, null, reason);
}

public static class GpuGraphicsTargetIdentityResolver
{
    private static readonly Guid DisplayDeviceClass = new("4D36E968-E325-11CE-BFC1-08002BE10318");

    public static GpuGraphicsTargetIdentityResolution Capture(
        string deviceInstanceId,
        string? presentMonApiPath = null,
        string? presentMonControlPipeName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceInstanceId);

        try
        {
            var target = DeviceInventoryReader.CapturePresentDevices().Devices.FirstOrDefault(device =>
                device.ClassGuid == DisplayDeviceClass &&
                string.Equals(device.InstanceId, deviceInstanceId, StringComparison.OrdinalIgnoreCase));
            if (target is null)
            {
                return GpuGraphicsTargetIdentityResolution.Unusable(
                    "The GPU optimization target is not a present display adapter.");
            }

            return Resolve(
                target,
                GraphicsAdapterReader.Capture(),
                PresentMonDeviceReader.Capture(presentMonApiPath, presentMonControlPipeName));
        }
        catch (Exception exception) when (exception is
            InvalidDataException or
            InvalidOperationException or
            NotSupportedException or
            global::System.ComponentModel.Win32Exception or
            global::System.Runtime.InteropServices.COMException)
        {
            return GpuGraphicsTargetIdentityResolution.Unusable(
                $"GPU identity could not be resolved: {exception.GetType().Name}: {exception.Message}");
        }
    }

    public static GpuGraphicsTargetIdentityResolution Resolve(
        PnPDeviceSnapshot target,
        GraphicsAdapterInventory graphics,
        PresentMonDeviceInventory presentMon)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(graphics);
        ArgumentNullException.ThrowIfNull(presentMon);

        if (target.ClassGuid != DisplayDeviceClass)
        {
            return GpuGraphicsTargetIdentityResolution.Unusable(
                "The requested optimization target is not a display adapter.");
        }

        var hardwareAdapters = graphics.HardwareAdapters;
        if (hardwareAdapters.Count != 1)
        {
            return GpuGraphicsTargetIdentityResolution.Unusable(
                "GPU optimization requires one hardware graphics adapter until the workload-to-adapter route can be proven directly on hybrid or multi-GPU systems.");
        }

        if (!TryReadPciToken(target.InstanceId, "VEN_", 4, out var vendorId) ||
            !TryReadPciToken(target.InstanceId, "DEV_", 4, out var deviceId))
        {
            return GpuGraphicsTargetIdentityResolution.Unusable(
                "The target display adapter does not expose an authoritative PCI vendor/device identity.");
        }

        var hasSubsystem = TryReadPciToken(target.InstanceId, "SUBSYS_", 8, out var subsystemId);
        var hasRevision = TryReadPciToken(target.InstanceId, "REV_", 2, out var revision);
        var dxgiMatches = hardwareAdapters.Where(adapter =>
                adapter.VendorId == vendorId &&
                adapter.DeviceId == deviceId &&
                (!hasSubsystem || adapter.SubSystemId == subsystemId) &&
                (!hasRevision || adapter.Revision == revision))
            .ToArray();
        if (dxgiMatches.Length != 1)
        {
            return GpuGraphicsTargetIdentityResolution.Unusable(
                "The target PnP display adapter could not be mapped uniquely to one DXGI hardware adapter.");
        }

        if (!presentMon.IsAvailable)
        {
            return GpuGraphicsTargetIdentityResolution.Unusable(
                "PresentMon graphics-device evidence is unavailable.");
        }

        var dxgi = dxgiMatches[0];
        PresentMonGraphicsDeviceSnapshot presentMonDevice;
        if (presentMon.HasGraphicsLuidEvidence)
        {
            var presentMonMatches = presentMon.GraphicsDevices
                .Where(device => device.Luid == dxgi.Luid)
                .ToArray();
            if (presentMonMatches.Length != 1)
            {
                return GpuGraphicsTargetIdentityResolution.Unusable(
                    "The DXGI target could not be mapped uniquely to one PresentMon graphics device by LUID.");
            }

            presentMonDevice = presentMonMatches[0];
        }
        else
        {
            if (presentMon.GraphicsDevices.Count != 1)
            {
                return GpuGraphicsTargetIdentityResolution.Unusable(
                    "PresentMon did not expose a LUID and reported more than one graphics device, so the target cannot be correlated uniquely.");
            }

            presentMonDevice = presentMon.GraphicsDevices[0];
            if (!MatchesVendor(dxgi.VendorId, presentMonDevice.NativeVendor) ||
                !string.Equals(dxgi.Description, presentMonDevice.Name, StringComparison.OrdinalIgnoreCase))
            {
                return GpuGraphicsTargetIdentityResolution.Unusable(
                    "PresentMon did not expose a LUID and its unique graphics device did not match the DXGI vendor and adapter name.");
            }
        }

        return new GpuGraphicsTargetIdentityResolution(
            true,
            new GpuGraphicsTargetIdentitySnapshot(
                target.InstanceId,
                dxgi.Luid,
                presentMonDevice.DeviceId,
                dxgi.Description,
                hardwareAdapters.Count),
            null);
    }

    private static bool MatchesVendor(uint pciVendorId, int presentMonVendor) =>
        presentMonVendor switch
        {
            0 => pciVendorId == 0x8086,
            1 => pciVendorId == 0x10DE,
            2 => pciVendorId is 0x1002 or 0x1022,
            _ => false,
        };

    private static bool TryReadPciToken(
        string instanceId,
        string marker,
        int hexadecimalDigits,
        out uint value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            return false;
        }

        var index = instanceId.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return false;
        }

        var start = index + marker.Length;
        if (start + hexadecimalDigits > instanceId.Length)
        {
            return false;
        }

        var token = instanceId.AsSpan(start, hexadecimalDigits);
        return uint.TryParse(
            token,
            NumberStyles.AllowHexSpecifier,
            CultureInfo.InvariantCulture,
            out value);
    }
}
