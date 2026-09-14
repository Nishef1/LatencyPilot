namespace LatencyPilot.Core.Devices;

public enum RawInputDeviceKind
{
    Mouse = 0,
    Keyboard = 1,
    HumanInterface = 2,
}

public enum RawInputRouteResolutionStatus
{
    Available,
    DeviceInterfaceUnavailable,
    PnPInstanceUnavailable,
    PnPDeviceUnavailable,
    ReadFailed,
}

public sealed record RawInputDeviceSnapshot(
    RawInputDeviceKind Kind,
    string? DeviceInterfacePath,
    string? PnPInstanceId,
    uint? VendorId,
    uint? ProductId,
    uint? VersionNumber,
    ushort? UsagePage,
    ushort? Usage,
    RawInputRouteResolutionStatus ResolutionStatus,
    uint? NativeStatusCode,
    string? Error);

public sealed record InputDeviceRouteSnapshot(
    RawInputDeviceSnapshot RawInputDevice,
    string? PnPDisplayName,
    string? UsbHostControllerInstanceId,
    string? BluetoothAncestorInstanceId,
    IReadOnlyList<string> KnownAncestorInstanceIds)
{
    public bool IsUsbBacked => UsbHostControllerInstanceId is not null;

    public bool IsBluetoothBacked => BluetoothAncestorInstanceId is not null;
}

public sealed record UserInputRouteInventory(
    IReadOnlyList<InputDeviceRouteSnapshot> Routes,
    DateTimeOffset CapturedAtUtc)
{
    public int MouseCount => Routes.Count(static route =>
        route.RawInputDevice.Kind == RawInputDeviceKind.Mouse);

    public int KeyboardCount => Routes.Count(static route =>
        route.RawInputDevice.Kind == RawInputDeviceKind.Keyboard);

    public int HidCount => Routes.Count(static route =>
        route.RawInputDevice.Kind == RawInputDeviceKind.HumanInterface);

    public int ResolvedPnPRouteCount => Routes.Count(static route =>
        route.RawInputDevice.ResolutionStatus == RawInputRouteResolutionStatus.Available);

    public int UsbBackedRouteCount => Routes.Count(static route => route.IsUsbBacked);

    public int BluetoothBackedRouteCount => Routes.Count(static route => route.IsBluetoothBacked);
}
