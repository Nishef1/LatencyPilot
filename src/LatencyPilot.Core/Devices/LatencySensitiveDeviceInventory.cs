namespace LatencyPilot.Core.Devices;

public enum LatencySensitiveDeviceKind
{
    DisplayAdapter = 1,
    AudioAdapter = 2,
    AudioEndpoint = 3,
    NetworkAdapter = 4,
    UsbHostController = 5,
    HumanInterface = 6,
    Keyboard = 7,
    Mouse = 8,
    Bluetooth = 9,
    StorageController = 10,
    StorageDevice = 11,
    SystemDevice = 12,
}

public sealed record LatencySensitiveDeviceEvidence(
    LatencySensitiveDeviceKind Kind,
    PnPDeviceSnapshot Device);

public sealed record LatencySensitiveDeviceInventory(
    IReadOnlyList<LatencySensitiveDeviceEvidence> Devices)
{
    public IReadOnlyList<LatencySensitiveDeviceEvidence> OfKind(LatencySensitiveDeviceKind kind) =>
        Devices.Where(device => device.Kind == kind).ToArray();

    public int DisplayAdapterCount => Devices.Count(static device =>
        device.Kind == LatencySensitiveDeviceKind.DisplayAdapter);

    public bool HasMultipleDisplayAdapters => DisplayAdapterCount > 1;

    public bool HasBluetooth => Devices.Any(static device =>
        device.Kind == LatencySensitiveDeviceKind.Bluetooth);

    public bool HasUsbInput => Devices.Any(static device =>
        device.Kind is LatencySensitiveDeviceKind.HumanInterface or
            LatencySensitiveDeviceKind.Keyboard or
            LatencySensitiveDeviceKind.Mouse);
}
