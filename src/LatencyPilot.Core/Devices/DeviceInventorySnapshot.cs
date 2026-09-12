namespace LatencyPilot.Core.Devices;

public sealed record DriverMetadataSnapshot(
    string? Version,
    string? Provider,
    string? InfPath)
{
    public bool IsAvailable => Version is not null || Provider is not null || InfPath is not null;
}

public sealed record InterruptConfigurationSnapshot(
    uint? MsiSupported,
    uint? MessageNumberLimit,
    uint? DevicePolicy,
    ulong? AssignmentSetOverrideMask)
{
    public bool HasAnyConfiguration =>
        MsiSupported is not null ||
        MessageNumberLimit is not null ||
        DevicePolicy is not null ||
        AssignmentSetOverrideMask is not null;

    public bool IsMsiConfiguredEnabled => MsiSupported == 1;
}

public sealed record PnPDeviceSnapshot(
    string InstanceId,
    Guid ClassGuid,
    string DisplayName,
    string? Manufacturer,
    string? EnumeratorName,
    string? ServiceName,
    DriverMetadataSnapshot Driver,
    InterruptConfigurationSnapshot InterruptConfiguration);

public sealed record DeviceInventorySnapshot(
    IReadOnlyList<PnPDeviceSnapshot> Devices,
    DateTimeOffset CapturedAtUtc)
{
    public int PresentDeviceCount => Devices.Count;

    public int DevicesWithDriverMetadataCount => Devices.Count(static device => device.Driver.IsAvailable);

    public int DevicesWithInterruptConfigurationCount => Devices.Count(static device => device.InterruptConfiguration.HasAnyConfiguration);
}
