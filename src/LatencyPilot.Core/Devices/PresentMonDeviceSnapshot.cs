namespace LatencyPilot.Core.Devices;

public enum PresentMonDiscoveryStatus
{
    Available,
    ApiUnavailable,
    ServiceUnavailable,
    IntrospectionFailed,
    InvalidData,
}

public sealed record PresentMonGraphicsDeviceSnapshot(
    uint DeviceId,
    int NativeVendor,
    string? Name,
    GraphicsAdapterLuid? Luid);

public sealed record PresentMonDeviceInventory(
    PresentMonDiscoveryStatus Status,
    IReadOnlyList<PresentMonGraphicsDeviceSnapshot> GraphicsDevices,
    string? ApiPath,
    int? NativeStatusCode,
    string? Error,
    DateTimeOffset CapturedAtUtc)
{
    public bool IsAvailable => Status == PresentMonDiscoveryStatus.Available;

    public bool HasGraphicsLuidEvidence => GraphicsDevices.Any(static device => device.Luid is not null);

    public PresentMonGraphicsDeviceSnapshot? FindByLuid(GraphicsAdapterLuid luid) =>
        GraphicsDevices.FirstOrDefault(device => device.Luid == luid);
}
