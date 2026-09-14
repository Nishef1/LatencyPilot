using Microsoft.Win32;

namespace LatencyPilot.Platform.Windows.Devices;

public static class GpuInterruptAffinityApplicability
{
    public static void EnsureSupportedOriginalState(GpuInterruptAffinitySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        ValidateDevicePolicy(snapshot.DevicePolicy);
        ValidateAssignmentSetOverride(snapshot.AssignmentSetOverride);
    }

    private static void ValidateDevicePolicy(RegistryValueSnapshot value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!value.Exists)
        {
            return;
        }

        if (value.Kind != RegistryValueKind.DWord || value.Data.Length != sizeof(uint))
        {
            throw new NotSupportedException(
                "GPU interrupt-affinity mutation requires an existing DevicePolicy value to use the documented REG_DWORD representation.");
        }
    }

    private static void ValidateAssignmentSetOverride(RegistryValueSnapshot value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!value.Exists)
        {
            return;
        }

        var supported = value.Kind switch
        {
            RegistryValueKind.DWord => value.Data.Length == sizeof(uint),
            RegistryValueKind.QWord => value.Data.Length == sizeof(ulong),
            RegistryValueKind.Binary => value.Data.Length is > 0 and <= sizeof(ulong),
            _ => false,
        };

        if (!supported)
        {
            throw new NotSupportedException(
                "GPU interrupt-affinity mutation requires AssignmentSetOverride to use the documented REG_DWORD, REG_QWORD, or <=64-bit REG_BINARY representation.");
        }
    }
}
