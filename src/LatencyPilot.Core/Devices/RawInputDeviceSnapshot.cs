namespace LatencyPilot.Core.Devices;

public enum RawInputDeviceKind
{
    Mouse,
    Keyboard,
    HumanInterface,
    Unknown,
}

public sealed record RawInputDeviceSnapshot(
    RawInputDeviceKind Kind,
    string InterfaceName,
    string? DeviceInstanceId,
    uint? VendorId,
    uint? ProductId,
    uint? VersionNumber,
    ushort? UsagePage,
    ushort? Usage,
    string? MappingError)
{
    public bool HasResolvedDeviceInstance => !string.IsNullOrWhiteSpace(DeviceInstanceId);
}

public sealed record RawInputDeviceInventory(
    IReadOnlyList<RawInputDeviceSnapshot> Devices,
    DateTimeOffset CapturedAtUtc)
{
    public int MouseCount => Devices.Count(static device => device.Kind == RawInputDeviceKind.Mouse);

    public int KeyboardCount => Devices.Count(static device => device.Kind == RawInputDeviceKind.Keyboard);

    public int HidCount => Devices.Count(static device => device.Kind == RawInputDeviceKind.HumanInterface);

    public int ResolvedDeviceInstanceCount => Devices.Count(static device => device.HasResolvedDeviceInstance);
}
