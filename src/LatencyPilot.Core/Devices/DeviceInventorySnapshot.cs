namespace LatencyPilot.Core.Devices;

public sealed record DriverMetadataSnapshot(
    string? Version,
    string? Provider,
    string? InfPath)
{
    public bool IsAvailable => Version is not null || Provider is not null || InfPath is not null;
}

public enum InterruptConfigurationReadStatus
{
    Available,
    HardwareKeyUnavailable,
}

public sealed record InterruptConfigurationSnapshot(
    InterruptConfigurationReadStatus ReadStatus,
    uint? NativeErrorCode,
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

    public static InterruptConfigurationSnapshot Available(
        uint? msiSupported,
        uint? messageNumberLimit,
        uint? devicePolicy,
        ulong? assignmentSetOverrideMask) =>
        new(
            InterruptConfigurationReadStatus.Available,
            null,
            msiSupported,
            messageNumberLimit,
            devicePolicy,
            assignmentSetOverrideMask);

    public static InterruptConfigurationSnapshot HardwareKeyUnavailable(uint nativeErrorCode) =>
        new(
            InterruptConfigurationReadStatus.HardwareKeyUnavailable,
            nativeErrorCode,
            null,
            null,
            null,
            null);
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

    public int DevicesWithReadableInterruptConfigurationCount => Devices.Count(
        static device => device.InterruptConfiguration.ReadStatus == InterruptConfigurationReadStatus.Available);
}
