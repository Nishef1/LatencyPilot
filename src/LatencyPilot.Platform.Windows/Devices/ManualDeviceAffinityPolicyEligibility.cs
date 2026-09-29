using LatencyPilot.Core.Devices;

namespace LatencyPilot.Platform.Windows.Devices;

public enum ManualDeviceAffinityPolicyTargetKind
{
    Device = 0,
    Gpu = 1,
    Xhci = 2,
}

public static class ManualDeviceAffinityPolicyEligibility
{
    private static readonly Guid DisplayClass =
        new("4D36E968-E325-11CE-BFC1-08002BE10318");
    private static readonly Guid SystemClass =
        new("4D36E97D-E325-11CE-BFC1-08002BE10318");

    private static readonly HashSet<Guid> StorageClasses =
    [
        new("4D36E965-E325-11CE-BFC1-08002BE10318"), // CDROM
        new("4D36E967-E325-11CE-BFC1-08002BE10318"), // DiskDrive
        new("4D36E969-E325-11CE-BFC1-08002BE10318"), // FDC
        new("4D36E96A-E325-11CE-BFC1-08002BE10318"), // HDC
        new("4D36E970-E325-11CE-BFC1-08002BE10318"), // MTD
        new("4D36E97B-E325-11CE-BFC1-08002BE10318"), // SCSIAdapter / RAID / NVMe controllers
        new("4D36E980-E325-11CE-BFC1-08002BE10318"), // FloppyDisk
        new("75416E63-5912-4DFA-AE8F-3EFACCAFFB14"), // NvmeDisk
        new("71A27CDD-812A-11D0-BEC7-08002BE2092F"), // Volume
        new("6D807884-7D21-11CF-801C-08002BE10318"), // TapeDrive
        new("CE5939AE-EBDE-11D0-B181-0000F8753EC4"), // MediumChanger
        new("533C5B84-EC70-11D2-9505-00C04F79DEAF"), // VolumeSnapshot
    ];

    public static ManualDeviceAffinityPolicyTargetKind ClassifyTarget(PnPDeviceSnapshot device)
    {
        ArgumentNullException.ThrowIfNull(device);

        if (device.ClassGuid == DisplayClass)
        {
            return ManualDeviceAffinityPolicyTargetKind.Gpu;
        }

        if (string.Equals(device.ServiceName, "USBXHCI", StringComparison.OrdinalIgnoreCase))
        {
            return ManualDeviceAffinityPolicyTargetKind.Xhci;
        }

        return ManualDeviceAffinityPolicyTargetKind.Device;
    }

    public static bool CanStartNewPolicyMutation(
        PnPDeviceSnapshot device,
        out string? inspectionOnlyReason)
    {
        ArgumentNullException.ThrowIfNull(device);

        if (StorageClasses.Contains(device.ClassGuid))
        {
            inspectionOnlyReason =
                "Storage-class devices are diagnostics-only in LatencyPilot. Their interrupt affinity, MSI/MSI-X layout and queue topology are intentionally left to Windows and the storage driver.";
            return false;
        }

        if (device.ClassGuid == SystemClass)
        {
            inspectionOnlyReason =
                "Windows System-class infrastructure is inspection-only in LatencyPilot. This class includes system buses, bridges, ACPI and other platform devices that are outside the supported mutation scope.";
            return false;
        }

        var targetKind = ClassifyTarget(device);
        if (targetKind == ManualDeviceAffinityPolicyTargetKind.Device &&
            !device.InterruptConfiguration.InterruptManagementKeyExists &&
            !device.InterruptResources.HasAssignedInterrupts)
        {
            inspectionOnlyReason =
                "This device exposes neither an Interrupt Management registry surface nor allocated interrupt resources. LatencyPilot will not invent an affinity policy for a PnP node that has no interrupt evidence.";
            return false;
        }

        inspectionOnlyReason = null;
        return true;
    }
}
