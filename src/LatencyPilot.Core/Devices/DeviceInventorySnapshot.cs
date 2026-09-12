namespace LatencyPilot.Core.Devices;

public sealed record PnPDeviceSnapshot(
    string InstanceId,
    Guid ClassGuid,
    string DisplayName,
    string? Manufacturer,
    string? EnumeratorName,
    string? ServiceName);

public sealed record DeviceInventorySnapshot(
    IReadOnlyList<PnPDeviceSnapshot> Devices,
    DateTimeOffset CapturedAtUtc)
{
    public int PresentDeviceCount => Devices.Count;
}
