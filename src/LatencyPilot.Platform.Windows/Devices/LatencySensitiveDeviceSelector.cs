using LatencyPilot.Core.Devices;

namespace LatencyPilot.Platform.Windows.Devices;

public static class LatencySensitiveDeviceSelector
{
    private static readonly Guid DisplayClass = new("4D36E968-E325-11CE-BFC1-08002BE10318");
    private static readonly Guid MediaClass = new("4D36E96C-E325-11CE-BFC1-08002BE10318");
    private static readonly Guid AudioEndpointClass = new("C166523C-FE0C-4A94-A586-F1A80CFBBF3E");
    private static readonly Guid NetworkClass = new("4D36E972-E325-11CE-BFC1-08002BE10318");
    private static readonly Guid UsbClass = new("36FC9E60-C465-11CF-8056-444553540000");
    private static readonly Guid HidClass = new("745A17A0-74D3-11D0-B6FE-00A0C90F57DA");
    private static readonly Guid KeyboardClass = new("4D36E96B-E325-11CE-BFC1-08002BE10318");
    private static readonly Guid MouseClass = new("4D36E96F-E325-11CE-BFC1-08002BE10318");
    private static readonly Guid BluetoothClass = new("E0CBF06C-CD8B-4647-BB8A-263B43F0F974");
    private static readonly Guid HdcClass = new("4D36E96A-E325-11CE-BFC1-08002BE10318");
    private static readonly Guid ScsiAdapterClass = new("4D36E97B-E325-11CE-BFC1-08002BE10318");
    private static readonly Guid DiskDriveClass = new("4D36E967-E325-11CE-BFC1-08002BE10318");
    private static readonly Guid NvmeDiskClass = new("75416E63-5912-4DFA-AE8F-3EFACCAFFB14");
    private static readonly Guid SystemClass = new("4D36E97D-E325-11CE-BFC1-08002BE10318");

    public static LatencySensitiveDeviceInventory Select(DeviceInventorySnapshot inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);

        var selected = new List<LatencySensitiveDeviceEvidence>();
        foreach (var device in inventory.Devices)
        {
            if (TryClassify(device, out var kind))
            {
                selected.Add(new LatencySensitiveDeviceEvidence(kind, device));
            }
        }

        selected.Sort(static (left, right) =>
        {
            var kindComparison = left.Kind.CompareTo(right.Kind);
            return kindComparison != 0
                ? kindComparison
                : StringComparer.OrdinalIgnoreCase.Compare(left.Device.InstanceId, right.Device.InstanceId);
        });

        return new LatencySensitiveDeviceInventory(selected.ToArray());
    }

    private static bool TryClassify(PnPDeviceSnapshot device, out LatencySensitiveDeviceKind kind)
    {
        if (device.ClassGuid == DisplayClass)
        {
            kind = LatencySensitiveDeviceKind.DisplayAdapter;
            return true;
        }

        if (device.ClassGuid == MediaClass)
        {
            kind = LatencySensitiveDeviceKind.AudioAdapter;
            return true;
        }

        if (device.ClassGuid == AudioEndpointClass)
        {
            kind = LatencySensitiveDeviceKind.AudioEndpoint;
            return true;
        }

        if (device.ClassGuid == NetworkClass)
        {
            kind = LatencySensitiveDeviceKind.NetworkAdapter;
            return true;
        }

        if (device.ClassGuid == UsbClass ||
            string.Equals(device.ServiceName, "USBXHCI", StringComparison.OrdinalIgnoreCase))
        {
            kind = LatencySensitiveDeviceKind.UsbHostController;
            return true;
        }

        if (device.ClassGuid == KeyboardClass)
        {
            kind = LatencySensitiveDeviceKind.Keyboard;
            return true;
        }

        if (device.ClassGuid == MouseClass)
        {
            kind = LatencySensitiveDeviceKind.Mouse;
            return true;
        }

        if (device.ClassGuid == HidClass)
        {
            kind = LatencySensitiveDeviceKind.HumanInterface;
            return true;
        }

        if (device.ClassGuid == BluetoothClass)
        {
            kind = LatencySensitiveDeviceKind.Bluetooth;
            return true;
        }

        if (device.ClassGuid == HdcClass || device.ClassGuid == ScsiAdapterClass)
        {
            kind = LatencySensitiveDeviceKind.StorageController;
            return true;
        }

        if (device.ClassGuid == DiskDriveClass || device.ClassGuid == NvmeDiskClass)
        {
            kind = LatencySensitiveDeviceKind.StorageDevice;
            return true;
        }

        if (device.ClassGuid == SystemClass)
        {
            kind = LatencySensitiveDeviceKind.SystemDevice;
            return true;
        }

        kind = default;
        return false;
    }
}
