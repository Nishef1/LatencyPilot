namespace LatencyPilot.Core.Devices;

public sealed record DriverMetadataSnapshot(
    string? Version,
    string? Provider,
    string? InfPath)
{
    public bool IsAvailable => Version is not null || Provider is not null || InfPath is not null;
}

public sealed record PnPDeviceSnapshot(
    string InstanceId,
    Guid ClassGuid,
    string DisplayName,
    string? Manufacturer,
    string? EnumeratorName,
    string? ServiceName,
    DriverMetadataSnapshot Driver);

public sealed record DeviceInventorySnapshot(
    IReadOnlyList<PnPDeviceSnapshot> Devices,
    DateTimeOffset CapturedAtUtc)
{
    public int PresentDeviceCount => Devices.Count;

    public int DevicesWithDriverMetadataCount => Devices.Count(static device => device.Driver.IsAvailable);
}
