using System.Buffers.Binary;
using LatencyPilot.Core.Devices;
using LatencyPilot.Core.System;
using LatencyPilot.Platform.Windows.Interop;
using Microsoft.Win32;

namespace LatencyPilot.Platform.Windows.Devices;

public sealed record RegistryValueSnapshot(
    bool Exists,
    RegistryValueKind? Kind,
    byte[] Data)
{
    public static RegistryValueSnapshot Missing { get; } = new(false, null, []);
}

public sealed record GpuInterruptAffinitySnapshot(
    string DeviceInstanceId,
    string DisplayName,
    string? DriverVersion,
    bool AffinityPolicyKeyExisted,
    RegistryValueSnapshot DevicePolicy,
    RegistryValueSnapshot AssignmentSetOverride);

public sealed record GpuInterruptAffinityCandidate(
    ushort ProcessorGroup,
    byte ProcessorNumber,
    ulong AffinityMask)
{
    public static GpuInterruptAffinityCandidate Create(
        ProcessorTopologySnapshot topology,
        LogicalProcessorId processor)
    {
        ArgumentNullException.ThrowIfNull(topology);

        if (topology.ProcessorGroupCount != 1)
        {
            throw new NotSupportedException(
                "GPU interrupt-affinity mutation v1 supports exactly one processor group.");
        }

        if (!topology.Cores.SelectMany(static core => core.LogicalProcessors).Contains(processor))
        {
            throw new ArgumentOutOfRangeException(nameof(processor), "Target processor does not exist in the captured topology.");
        }

        if (processor.Number >= 64)
        {
            throw new NotSupportedException("Target processor cannot be represented by an x64 KAFFINITY mask.");
        }

        return new GpuInterruptAffinityCandidate(
            processor.Group,
            processor.Number,
            1UL << processor.Number);
    }
}

public static class GpuInterruptAffinityPolicyStore
{
    public const uint IrqPolicySpecifiedProcessors = 4;

    private static readonly Guid DisplayDeviceClass = new("4D36E968-E325-11CE-BFC1-08002BE10318");
    private const string InterruptManagementSubKey = "Device Parameters\\Interrupt Management";
    private const string AffinityPolicySubKey = "Affinity Policy";
    private const string DevicePolicyValue = "DevicePolicy";
    private const string AssignmentSetOverrideValue = "AssignmentSetOverride";

    public static GpuInterruptAffinitySnapshot Capture(string deviceInstanceId)
    {
        var device = GetPresentDisplayAdapter(deviceInstanceId);
        using var hardwareKey = OpenHardwareKey(device.InstanceId, writable: false);
        using var interruptManagement = hardwareKey.OpenSubKey(InterruptManagementSubKey, writable: false);
        using var affinityPolicy = interruptManagement?.OpenSubKey(AffinityPolicySubKey, writable: false);

        return new GpuInterruptAffinitySnapshot(
            device.InstanceId,
            device.DisplayName,
            device.Driver.Version,
            affinityPolicy is not null,
            ReadSupportedValue(affinityPolicy, DevicePolicyValue),
            ReadSupportedValue(affinityPolicy, AssignmentSetOverrideValue));
    }

    public static void Apply(
        GpuInterruptAffinitySnapshot original,
        GpuInterruptAffinityCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(candidate);

        if (candidate.ProcessorGroup != 0)
        {
            throw new NotSupportedException(
                "GPU interrupt-affinity mutation v1 only applies a group-0 KAFFINITY mask.");
        }

        if (candidate.ProcessorNumber >= 64 ||
            candidate.AffinityMask != (1UL << candidate.ProcessorNumber))
        {
            throw new ArgumentException("Candidate affinity mask does not match its target logical processor.", nameof(candidate));
        }

        _ = GetPresentDisplayAdapter(original.DeviceInstanceId);

        using (var transaction = TransactionalRegistry.Begin("LatencyPilot GPU interrupt-affinity apply"))
        {
            using (var affinityPolicy = transaction.CreateOrOpenKey(
                       RegistryHive.LocalMachine,
                       GetAffinityPolicyPath(original.DeviceInstanceId)))
            {
                affinityPolicy.SetValue(
                    DevicePolicyValue,
                    unchecked((int)IrqPolicySpecifiedProcessors),
                    RegistryValueKind.DWord);

                var mask = new byte[sizeof(ulong)];
                BinaryPrimitives.WriteUInt64LittleEndian(mask, candidate.AffinityMask);
                affinityPolicy.SetValue(
                    AssignmentSetOverrideValue,
                    mask,
                    RegistryValueKind.Binary);
            }

            transaction.Commit();
        }

        VerifyCandidateStored(original.DeviceInstanceId, candidate);
    }

    public static void Restore(GpuInterruptAffinitySnapshot original)
    {
        ArgumentNullException.ThrowIfNull(original);
        _ = GetPresentDisplayAdapter(original.DeviceInstanceId);
        var affinityPolicyPath = GetAffinityPolicyPath(original.DeviceInstanceId);

        using (var transaction = TransactionalRegistry.Begin("LatencyPilot GPU interrupt-affinity restore"))
        {
            if (original.AffinityPolicyKeyExisted)
            {
                using var affinityPolicy = transaction.CreateOrOpenKey(RegistryHive.LocalMachine, affinityPolicyPath);
                RestoreValue(affinityPolicy, DevicePolicyValue, original.DevicePolicy);
                RestoreValue(affinityPolicy, AssignmentSetOverrideValue, original.AssignmentSetOverride);
            }
            else
            {
                using (var affinityPolicy = transaction.CreateOrOpenKey(RegistryHive.LocalMachine, affinityPolicyPath))
                {
                    EnsureOnlyMutationValuesPresent(affinityPolicy);
                }

                transaction.DeleteKey(RegistryHive.LocalMachine, affinityPolicyPath);
            }

            transaction.Commit();
        }

        VerifyRestored(original);
    }

    public static bool IsCandidateStored(
        string deviceInstanceId,
        GpuInterruptAffinityCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        var snapshot = Capture(deviceInstanceId);
        if (!TryDecodeDword(snapshot.DevicePolicy, out var policy) ||
            policy != IrqPolicySpecifiedProcessors ||
            !TryDecodeMask(snapshot.AssignmentSetOverride, out var mask))
        {
            return false;
        }

        return mask == candidate.AffinityMask;
    }

    public static bool IsRestored(GpuInterruptAffinitySnapshot original)
    {
        ArgumentNullException.ThrowIfNull(original);
        var current = Capture(original.DeviceInstanceId);
        return current.AffinityPolicyKeyExisted == original.AffinityPolicyKeyExisted &&
               ValuesEqual(current.DevicePolicy, original.DevicePolicy) &&
               ValuesEqual(current.AssignmentSetOverride, original.AssignmentSetOverride);
    }

    private static void VerifyCandidateStored(
        string deviceInstanceId,
        GpuInterruptAffinityCandidate candidate)
    {
        if (!IsCandidateStored(deviceInstanceId, candidate))
        {
            throw new InvalidOperationException(
                "GPU interrupt-affinity policy transaction committed but the candidate could not be verified from the target device hardware key.");
        }
    }

    private static void VerifyRestored(GpuInterruptAffinitySnapshot original)
    {
        if (!IsRestored(original))
        {
            throw new InvalidOperationException(
                "GPU interrupt-affinity restore transaction committed but the exact original state could not be verified.");
        }
    }

    private static void EnsureOnlyMutationValuesPresent(TransactionalRegistryKey affinityPolicy)
    {
        var counts = affinityPolicy.GetCounts();
        if (counts.SubKeyCount != 0)
        {
            throw new InvalidOperationException(
                "Refusing to remove the Affinity Policy key because it contains subkeys that were not created by the bounded mutation.");
        }

        var knownValueCount = 0u;
        if (affinityPolicy.ValueExists(DevicePolicyValue))
        {
            knownValueCount++;
        }

        if (affinityPolicy.ValueExists(AssignmentSetOverrideValue))
        {
            knownValueCount++;
        }

        if (counts.ValueCount != knownValueCount)
        {
            throw new InvalidOperationException(
                "Refusing to remove the Affinity Policy key because it contains registry values outside the bounded mutation contract.");
        }
    }

    private static PnPDeviceSnapshot GetPresentDisplayAdapter(string deviceInstanceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceInstanceId);
        if (deviceInstanceId.Contains('\0'))
        {
            throw new ArgumentException("Device instance ID contains an invalid NUL character.", nameof(deviceInstanceId));
        }

        var device = DeviceInventoryReader.CapturePresentDevices().Devices.FirstOrDefault(candidate =>
            candidate.ClassGuid == DisplayDeviceClass &&
            string.Equals(candidate.InstanceId, deviceInstanceId, StringComparison.OrdinalIgnoreCase));

        return device ?? throw new InvalidOperationException(
            "The requested GPU affinity target is not a present display adapter discovered by SetupAPI.");
    }

    private static RegistryKey OpenHardwareKey(string deviceInstanceId, bool writable)
    {
        var path = $"SYSTEM\\CurrentControlSet\\Enum\\{deviceInstanceId}";
        return Registry.LocalMachine.OpenSubKey(path, writable)
            ?? throw new InvalidOperationException(
                "The target display adapter hardware registry key is unavailable.");
    }

    private static string GetAffinityPolicyPath(string deviceInstanceId) =>
        $"SYSTEM\\CurrentControlSet\\Enum\\{deviceInstanceId}\\{InterruptManagementSubKey}\\{AffinityPolicySubKey}";

    private static RegistryValueSnapshot ReadSupportedValue(RegistryKey? key, string valueName)
    {
        if (key is null || !key.GetValueNames().Any(name =>
                string.Equals(name, valueName, StringComparison.OrdinalIgnoreCase)))
        {
            return RegistryValueSnapshot.Missing;
        }

        var kind = key.GetValueKind(valueName);
        var value = key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames)
            ?? throw new InvalidDataException($"Registry value '{valueName}' exists but returned no data.");

        var bytes = kind switch
        {
            RegistryValueKind.DWord when value is int dword => BitConverter.GetBytes(dword),
            RegistryValueKind.QWord when value is long qword => BitConverter.GetBytes(qword),
            RegistryValueKind.Binary when value is byte[] binary => binary.ToArray(),
            _ => throw new NotSupportedException(
                $"Registry value '{valueName}' uses unsupported kind {kind}; mutation is not applicable."),
        };

        return new RegistryValueSnapshot(true, kind, bytes);
    }

    private static void RestoreValue(TransactionalRegistryKey key, string valueName, RegistryValueSnapshot original)
    {
        if (!original.Exists)
        {
            key.DeleteValue(valueName);
            return;
        }

        if (original.Kind is null)
        {
            throw new InvalidDataException($"Original registry value '{valueName}' has no value kind.");
        }

        switch (original.Kind.Value)
        {
            case RegistryValueKind.DWord when original.Data.Length == sizeof(int):
                key.SetValue(valueName, BitConverter.ToInt32(original.Data, 0), RegistryValueKind.DWord);
                return;
            case RegistryValueKind.QWord when original.Data.Length == sizeof(long):
                key.SetValue(valueName, BitConverter.ToInt64(original.Data, 0), RegistryValueKind.QWord);
                return;
            case RegistryValueKind.Binary:
                key.SetValue(valueName, original.Data.ToArray(), RegistryValueKind.Binary);
                return;
            default:
                throw new InvalidDataException(
                    $"Original registry value '{valueName}' cannot be restored from its captured representation.");
        }
    }

    private static bool ValuesEqual(RegistryValueSnapshot left, RegistryValueSnapshot right) =>
        left.Exists == right.Exists &&
        left.Kind == right.Kind &&
        left.Data.AsSpan().SequenceEqual(right.Data);

    private static bool TryDecodeDword(RegistryValueSnapshot value, out uint result)
    {
        if (value.Exists && value.Kind == RegistryValueKind.DWord && value.Data.Length == sizeof(uint))
        {
            result = BinaryPrimitives.ReadUInt32LittleEndian(value.Data);
            return true;
        }

        result = 0;
        return false;
    }

    private static bool TryDecodeMask(RegistryValueSnapshot value, out ulong result)
    {
        if (!value.Exists || value.Kind is null)
        {
            result = 0;
            return false;
        }

        switch (value.Kind.Value)
        {
            case RegistryValueKind.DWord when value.Data.Length == sizeof(uint):
                result = BinaryPrimitives.ReadUInt32LittleEndian(value.Data);
                return true;
            case RegistryValueKind.QWord when value.Data.Length == sizeof(ulong):
                result = BinaryPrimitives.ReadUInt64LittleEndian(value.Data);
                return true;
            case RegistryValueKind.Binary when value.Data.Length is > 0 and <= sizeof(ulong):
                Span<byte> padded = stackalloc byte[sizeof(ulong)];
                value.Data.AsSpan().CopyTo(padded);
                result = BinaryPrimitives.ReadUInt64LittleEndian(padded);
                return true;
            default:
                result = 0;
                return false;
        }
    }
}
