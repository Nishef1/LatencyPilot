namespace LatencyPilot.Core.Devices;

public enum UsbPortConnectionStatus
{
    Unknown = 0,
    NotConnected = 1,
    Connected = 2,
    Failed = 3,
}

public enum UsbDeviceSpeed
{
    Unknown = 0,
    Low = 1,
    Full = 2,
    High = 3,
    Super = 4,
    SuperPlus = 5,
}

public enum UsbPortRouteResolutionStatus
{
    Available = 0,
    DriverKeyUnavailable = 1,
    NotFound = 2,
    Ambiguous = 3,
    HostControllerUnavailable = 4,
    HostControllerMismatch = 5,
}

public sealed record UsbHubPortSnapshot(
    string HubDevicePath,
    string? HubInstanceId,
    string? HostControllerInstanceId,
    uint ConnectionIndex,
    string? DriverKeyName,
    UsbPortConnectionStatus ConnectionStatus,
    UsbDeviceSpeed Speed,
    bool DeviceIsHub)
{
    public bool SupportsUsb1 { get; init; }

    public bool SupportsUsb2 { get; init; }

    public bool SupportsUsb3 { get; init; }

    public bool OperatingAtSuperSpeedOrHigher { get; init; }
}

public sealed record UsbPortRouteEvidence(
    UsbPortRouteResolutionStatus Status,
    UsbHubPortSnapshot? Port,
    string? Reason)
{
    public bool IsAvailable => Status == UsbPortRouteResolutionStatus.Available && Port is not null;
}

public sealed record UsbTopologySnapshot(
    IReadOnlyList<UsbHubPortSnapshot> Ports,
    DateTimeOffset CapturedAtUtc,
    IReadOnlyList<string> Errors);
