using LatencyPilot.Core.Devices;

namespace LatencyPilot.Platform.Windows.Devices;

public enum RepresentativeDeviceKind
{
    DisplayAdapter = 1,
    NetworkAdapter = 2,
    XhciController = 3,
}

public sealed record RepresentativeDeviceEvidence(
    RepresentativeDeviceKind Kind,
    PnPDeviceSnapshot Device)
{
    public bool StoredInterruptConfigurationAvailable =>
        Device.InterruptConfiguration.ReadStatus == InterruptConfigurationReadStatus.Available;

    public bool AllocatedInterruptResourcesAvailable =>
        Device.InterruptResources.ReadStatus == InterruptResourceReadStatus.Available;
}

public static class RepresentativeDeviceEvidenceSelector
{
    private static readonly Guid DisplayDeviceClass = new("4D36E968-E325-11CE-BFC1-08002BE10318");
    private static readonly Guid NetworkDeviceClass = new("4D36E972-E325-11CE-BFC1-08002BE10318");

    public static IReadOnlyList<RepresentativeDeviceEvidence> Select(
        DeviceInventorySnapshot inventory,
        int maximumPerKind = 3)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        if (maximumPerKind < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumPerKind));
        }

        var selected = new List<RepresentativeDeviceEvidence>();
        AddKind(
            selected,
            inventory.Devices.Where(static device => device.ClassGuid == DisplayDeviceClass),
            RepresentativeDeviceKind.DisplayAdapter,
            maximumPerKind);
        AddKind(
            selected,
            inventory.Devices.Where(static device => device.ClassGuid == NetworkDeviceClass),
            RepresentativeDeviceKind.NetworkAdapter,
            maximumPerKind);
        AddKind(
            selected,
            inventory.Devices.Where(static device =>
                string.Equals(device.ServiceName, "USBXHCI", StringComparison.OrdinalIgnoreCase)),
            RepresentativeDeviceKind.XhciController,
            maximumPerKind);
        return selected;
    }

    private static void AddKind(
        List<RepresentativeDeviceEvidence> destination,
        IEnumerable<PnPDeviceSnapshot> source,
        RepresentativeDeviceKind kind,
        int maximum)
    {
        foreach (var device in source
                     .OrderByDescending(static device => device.InterruptResources.HasAssignedInterrupts)
                     .ThenByDescending(static device => device.InterruptConfiguration.HasAnyConfiguration)
                     .ThenByDescending(static device => device.Driver.IsAvailable)
                     .ThenBy(static device => device.DisplayName, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(static device => device.InstanceId, StringComparer.OrdinalIgnoreCase)
                     .Take(maximum))
        {
            destination.Add(new RepresentativeDeviceEvidence(kind, device));
        }
    }
}
