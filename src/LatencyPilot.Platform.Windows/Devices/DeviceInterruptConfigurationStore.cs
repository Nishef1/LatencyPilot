using System.Buffers.Binary;
using LatencyPilot.Core.Devices;
using LatencyPilot.Platform.Windows.Interop;
using Microsoft.Win32;

namespace LatencyPilot.Platform.Windows.Devices;

public enum DeviceInterruptTargetKind { DisplayAdapter = 0, XhciController = 1 }

public sealed record DeviceInterruptConfigurationSnapshot(
    string DeviceInstanceId, string DisplayName, string? DriverVersion, DeviceInterruptTargetKind TargetKind,
    bool MsiPropertiesKeyExisted, RegistryValueSnapshot MsiSupported, RegistryValueSnapshot MessageNumberLimit,
    bool AffinityPolicyKeyExisted, RegistryValueSnapshot DevicePolicy, RegistryValueSnapshot AssignmentSetOverride);

public sealed record DeviceInterruptAffinityCandidate(ushort ProcessorGroup, byte ProcessorNumber, ulong AffinityMask);

public static class DeviceInterruptConfigurationStore
{
    public const uint IrqPolicySpecifiedProcessors = 4;
    private static readonly Guid DisplayClass = new("4D36E968-E325-11CE-BFC1-08002BE10318");
    private const string Interrupt = "Device Parameters\\Interrupt Management";
    private const string MsiSubKey = Interrupt + "\\MessageSignaledInterruptProperties";
    private const string AffinitySubKey = Interrupt + "\\Affinity Policy";
    private const string MsiSupportedValue = "MSISupported";
    private const string MessageNumberLimitValue = "MessageNumberLimit";
    private const string DevicePolicyValue = "DevicePolicy";
    private const string AssignmentSetOverrideValue = "AssignmentSetOverride";

    public static DeviceInterruptConfigurationSnapshot Capture(string deviceInstanceId)
    {
        var device = GetSupportedPresentTarget(deviceInstanceId);
        using var hardware = Registry.LocalMachine.OpenSubKey($"SYSTEM\\CurrentControlSet\\Enum\\{device.InstanceId}", false)
            ?? throw new InvalidOperationException("Target hardware registry key is unavailable.");
        using var msi = hardware.OpenSubKey(MsiSubKey, false);
        using var affinity = hardware.OpenSubKey(AffinitySubKey, false);
        var kind = device.ClassGuid == DisplayClass ? DeviceInterruptTargetKind.DisplayAdapter : DeviceInterruptTargetKind.XhciController;
        return new DeviceInterruptConfigurationSnapshot(device.InstanceId, device.DisplayName, device.Driver.Version, kind,
            msi is not null, ReadValue(msi, MsiSupportedValue), ReadValue(msi, MessageNumberLimitValue),
            affinity is not null, ReadValue(affinity, DevicePolicyValue), ReadValue(affinity, AssignmentSetOverrideValue));
    }

    public static bool IsMsiEnabled(DeviceInterruptConfigurationSnapshot snapshot) =>
        TryDword(snapshot.MsiSupported, out var value) && value == 1;

    public static void EnsureMsiApplicable(DeviceInterruptConfigurationSnapshot snapshot)
    {
        if (snapshot.TargetKind != DeviceInterruptTargetKind.DisplayAdapter)
            throw new NotSupportedException("MSI enablement in v1 is limited to the present display adapter target.");
        if (!TryDword(snapshot.MsiSupported, out var value) || value > 1)
            throw new NotSupportedException("MSISupported must already exist as DWORD 0 or 1; LatencyPilot will not invent unsupported MSI policy.");
    }

    public static void ApplyMsi(DeviceInterruptConfigurationSnapshot original)
    {
        EnsureMsiApplicable(original);
        if (IsMsiEnabled(original)) return;
        var current = Capture(original.DeviceInstanceId);
        EnsureSameDriverAndCollateral(current, original);
        using var tx = TransactionalRegistry.Begin("LatencyPilot bounded MSI enablement");
        using (var key = tx.CreateOrOpenKey(RegistryHive.LocalMachine, HardwareSubPath(original.DeviceInstanceId, MsiSubKey)))
            key.SetValue(MsiSupportedValue, 1, RegistryValueKind.DWord);
        tx.Commit();
        var stored = Capture(original.DeviceInstanceId);
        if (!stored.MsiPropertiesKeyExisted || !IsMsiEnabled(stored) || !ValuesEqual(stored.MessageNumberLimit, original.MessageNumberLimit))
            throw new InvalidOperationException("MSI candidate write could not be verified without collateral MessageNumberLimit change.");
    }

    public static void ApplyXhciAffinity(DeviceInterruptConfigurationSnapshot original, DeviceInterruptAffinityCandidate candidate)
    {
        if (original.TargetKind != DeviceInterruptTargetKind.XhciController)
            throw new NotSupportedException("xHCI affinity mutation requires a USBXHCI controller target.");
        ValidateCandidate(candidate);
        var current = Capture(original.DeviceInstanceId); EnsureSameDriverAndCollateral(current, original);
        using var tx = TransactionalRegistry.Begin("LatencyPilot xHCI interrupt affinity");
        using (var key = tx.CreateOrOpenKey(RegistryHive.LocalMachine, HardwareSubPath(original.DeviceInstanceId, AffinitySubKey)))
        {
            key.SetValue(DevicePolicyValue, unchecked((int)IrqPolicySpecifiedProcessors), RegistryValueKind.DWord);
            var mask = new byte[8]; BinaryPrimitives.WriteUInt64LittleEndian(mask, candidate.AffinityMask);
            key.SetValue(AssignmentSetOverrideValue, mask, RegistryValueKind.Binary);
        }
        tx.Commit();
        if (!IsXhciAffinityStored(Capture(original.DeviceInstanceId), candidate))
            throw new InvalidOperationException("xHCI affinity candidate could not be verified from stored state.");
    }

    public static void RestoreMsi(DeviceInterruptConfigurationSnapshot original)
    {
        var before = Capture(original.DeviceInstanceId);
        if (!string.Equals(before.DriverVersion, original.DriverVersion, StringComparison.OrdinalIgnoreCase) ||
            !ValuesEqual(before.MessageNumberLimit, original.MessageNumberLimit))
        {
            throw new InvalidOperationException("MSI restore refused because driver identity or MessageNumberLimit changed after the captured original state.");
        }
        using var tx = TransactionalRegistry.Begin("LatencyPilot MSI exact restore");
        if (original.MsiPropertiesKeyExisted)
        {
            using (var key = tx.CreateOrOpenKey(RegistryHive.LocalMachine, HardwareSubPath(original.DeviceInstanceId, MsiSubKey)))
            {
                RestoreValue(key, MsiSupportedValue, original.MsiSupported);
                RestoreValue(key, MessageNumberLimitValue, original.MessageNumberLimit);
            }
        }
        else
        {
            using (var key = tx.CreateOrOpenKey(RegistryHive.LocalMachine, HardwareSubPath(original.DeviceInstanceId, MsiSubKey)))
            {
                var counts = key.GetCounts();
                var known = (key.ValueExists(MsiSupportedValue) ? 1u : 0u) + (key.ValueExists(MessageNumberLimitValue) ? 1u : 0u);
                if (counts.SubKeyCount != 0 || counts.ValueCount != known)
                    throw new InvalidOperationException("Refusing to remove MSI properties key containing state outside the bounded mutation contract.");
            }
            tx.DeleteKey(RegistryHive.LocalMachine, HardwareSubPath(original.DeviceInstanceId, MsiSubKey));
            TryDeleteEmptyInterruptManagementParent(tx, original.DeviceInstanceId);
        }
        tx.Commit();
        var current = Capture(original.DeviceInstanceId);
        if (!ValuesEqual(current.MsiSupported, original.MsiSupported) || !ValuesEqual(current.MessageNumberLimit, original.MessageNumberLimit) ||
            current.MsiPropertiesKeyExisted != original.MsiPropertiesKeyExisted)
            throw new InvalidOperationException("Exact MSI original state was not restored.");
    }

    public static void RestoreXhciAffinity(DeviceInterruptConfigurationSnapshot original)
    {
        var before = Capture(original.DeviceInstanceId);
        if (!string.Equals(before.DriverVersion, original.DriverVersion, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("xHCI affinity restore refused because driver identity changed after the captured original state.");
        }
        using var tx = TransactionalRegistry.Begin("LatencyPilot xHCI affinity exact restore");
        if (original.AffinityPolicyKeyExisted)
        {
            using var key = tx.CreateOrOpenKey(RegistryHive.LocalMachine, HardwareSubPath(original.DeviceInstanceId, AffinitySubKey));
            RestoreValue(key, DevicePolicyValue, original.DevicePolicy);
            RestoreValue(key, AssignmentSetOverrideValue, original.AssignmentSetOverride);
        }
        else
        {
            using (var key = tx.CreateOrOpenKey(RegistryHive.LocalMachine, HardwareSubPath(original.DeviceInstanceId, AffinitySubKey)))
            {
                var counts = key.GetCounts();
                var known = (key.ValueExists(DevicePolicyValue) ? 1u : 0u) + (key.ValueExists(AssignmentSetOverrideValue) ? 1u : 0u);
                if (counts.SubKeyCount != 0 || counts.ValueCount != known)
                    throw new InvalidOperationException("Refusing to remove affinity key containing state outside the bounded mutation contract.");
            }
            tx.DeleteKey(RegistryHive.LocalMachine, HardwareSubPath(original.DeviceInstanceId, AffinitySubKey));
            TryDeleteEmptyInterruptManagementParent(tx, original.DeviceInstanceId);
        }
        tx.Commit();
        var current = Capture(original.DeviceInstanceId);
        if (!ValuesEqual(current.DevicePolicy, original.DevicePolicy) || !ValuesEqual(current.AssignmentSetOverride, original.AssignmentSetOverride) ||
            current.AffinityPolicyKeyExisted != original.AffinityPolicyKeyExisted)
            throw new InvalidOperationException("Exact xHCI affinity original state was not restored.");
    }

    private static void TryDeleteEmptyInterruptManagementParent(TransactionalRegistry tx, string deviceInstanceId)
    {
        try
        {
            var parentPath = HardwareSubPath(deviceInstanceId, Interrupt);
            using var parent = tx.CreateOrOpenKey(RegistryHive.LocalMachine, parentPath);
            var counts = parent.GetCounts();
            if (counts.SubKeyCount == 0 && counts.ValueCount == 0)
            {
                tx.DeleteKey(RegistryHive.LocalMachine, parentPath);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public static bool IsXhciAffinityStored(DeviceInterruptConfigurationSnapshot snapshot, DeviceInterruptAffinityCandidate candidate)
    {
        ValidateCandidate(candidate);
        return TryDword(snapshot.DevicePolicy, out var policy) && policy == IrqPolicySpecifiedProcessors &&
            TryMask(snapshot.AssignmentSetOverride, out var mask) && mask == candidate.AffinityMask;
    }

    public static bool MatchesCandidate(
        DeviceInterruptConfigurationSnapshot current,
        DeviceInterruptConfigurationSnapshot original,
        DeviceInterruptMutationCandidate candidate)
    {
        if (!string.Equals(current.DriverVersion, original.DriverVersion, StringComparison.OrdinalIgnoreCase) ||
            !ValuesEqual(current.MessageNumberLimit, original.MessageNumberLimit))
        {
            return false;
        }

        return candidate.Operation == DeviceInterruptMutationOperation.EnableMsi
            ? IsMsiEnabled(current)
            : IsXhciAffinityStored(current, candidate.ToAffinityCandidate());
    }

    public static bool MatchesOriginal(DeviceInterruptConfigurationSnapshot current, DeviceInterruptConfigurationSnapshot original, DeviceInterruptMutationOperation operation)
    {
        if (!string.Equals(current.DriverVersion, original.DriverVersion, StringComparison.OrdinalIgnoreCase)) return false;
        return operation == DeviceInterruptMutationOperation.EnableMsi
            ? current.MsiPropertiesKeyExisted == original.MsiPropertiesKeyExisted &&
              ValuesEqual(current.MsiSupported, original.MsiSupported) &&
              ValuesEqual(current.MessageNumberLimit, original.MessageNumberLimit)
            : current.AffinityPolicyKeyExisted == original.AffinityPolicyKeyExisted && ValuesEqual(current.DevicePolicy, original.DevicePolicy) && ValuesEqual(current.AssignmentSetOverride, original.AssignmentSetOverride);
    }

    private static void EnsureSameDriverAndCollateral(DeviceInterruptConfigurationSnapshot current, DeviceInterruptConfigurationSnapshot original)
    {
        if (!string.Equals(current.DriverVersion, original.DriverVersion, StringComparison.OrdinalIgnoreCase) ||
            !ValuesEqual(current.MessageNumberLimit, original.MessageNumberLimit))
            throw new InvalidOperationException("Driver version or non-owned interrupt configuration changed since the journaled snapshot.");
    }

    private static PnPDeviceSnapshot GetSupportedPresentTarget(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var device = DeviceInventoryReader.CapturePresentDevices().Devices.FirstOrDefault(d => string.Equals(d.InstanceId, id, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Interrupt mutation target is not present.");
        if (device.ClassGuid != DisplayClass && !string.Equals(device.ServiceName, "USBXHCI", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("Only the present display adapter or a USBXHCI controller is supported by this mutation store.");
        return device;
    }

    private static string HardwareSubPath(string id, string child) => $"SYSTEM\\CurrentControlSet\\Enum\\{id}\\{child}";
    private static RegistryValueSnapshot ReadValue(RegistryKey? key, string name)
    {
        if (key is null || !key.GetValueNames().Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase))) return RegistryValueSnapshot.Missing;
        var kind = key.GetValueKind(name); var value = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames)
            ?? throw new InvalidDataException($"Registry value {name} returned no data.");
        var data = kind switch { RegistryValueKind.DWord when value is int v => BitConverter.GetBytes(v), RegistryValueKind.QWord when value is long v => BitConverter.GetBytes(v), RegistryValueKind.Binary when value is byte[] v => v.ToArray(), _ => throw new NotSupportedException($"Unsupported registry kind {kind} for {name}.") };
        return new RegistryValueSnapshot(true, kind, data);
    }
    private static void RestoreValue(TransactionalRegistryKey key, string name, RegistryValueSnapshot value)
    {
        if (!value.Exists) { key.DeleteValue(name); return; }
        switch (value.Kind)
        {
            case RegistryValueKind.DWord when value.Data.Length == 4: key.SetValue(name, BitConverter.ToInt32(value.Data), RegistryValueKind.DWord); return;
            case RegistryValueKind.QWord when value.Data.Length == 8: key.SetValue(name, BitConverter.ToInt64(value.Data), RegistryValueKind.QWord); return;
            case RegistryValueKind.Binary: key.SetValue(name, value.Data.ToArray(), RegistryValueKind.Binary); return;
            default: throw new InvalidDataException($"Cannot restore {name} from captured representation.");
        }
    }
    private static bool ValuesEqual(RegistryValueSnapshot a, RegistryValueSnapshot b) => a.Exists == b.Exists && a.Kind == b.Kind && a.Data.AsSpan().SequenceEqual(b.Data);
    private static bool TryDword(RegistryValueSnapshot value, out uint result) { if (value.Exists && value.Kind == RegistryValueKind.DWord && value.Data.Length == 4) { result = BinaryPrimitives.ReadUInt32LittleEndian(value.Data); return true; } result = 0; return false; }
    private static bool TryMask(RegistryValueSnapshot value, out ulong result) { if (value.Exists && value.Kind == RegistryValueKind.Binary && value.Data.Length is > 0 and <= 8) { Span<byte> p = stackalloc byte[8]; p.Clear(); value.Data.CopyTo(p); result = BinaryPrimitives.ReadUInt64LittleEndian(p); return true; } result = 0; return false; }
    private static void ValidateCandidate(DeviceInterruptAffinityCandidate c) { if (c.ProcessorGroup != 0 || c.ProcessorNumber >= 64 || c.AffinityMask != (1UL << c.ProcessorNumber)) throw new ArgumentException("xHCI affinity candidate must be one valid group-0 KAFFINITY bit.", nameof(c)); }
}
