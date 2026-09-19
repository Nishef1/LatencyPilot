from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]

def read(path): return (ROOT / path).read_text(encoding="utf-8")
def write(path, text):
    target = ROOT / path; target.parent.mkdir(parents=True, exist_ok=True); target.write_text(text, encoding="utf-8", newline="\n")
def replace_once(path, old, new):
    text = read(path)
    if text.count(old) != 1: raise RuntimeError(f"{path}: expected one anchor, found {text.count(old)}")
    write(path, text.replace(old, new, 1))

def apply_tests():
    write("tests/LatencyPilot.CriticalTests/DeviceInterruptMutationTests.cs", r'''using LatencyPilot.Persistence;
using LatencyPilot.Platform.Windows.Devices;
using LatencyPilot.Service;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Win32;

namespace LatencyPilot.CriticalTests;

[TestClass]
public sealed class DeviceInterruptMutationTests
{
    [TestMethod]
    public void DeviceMutationContractIsJournaledRebootResumableAndDoesNotTuneMessageCount()
    {
        Assert.IsTrue(MutationJournalStateMachine.CanTransition(MutationJournalState.Applying, MutationJournalState.ApplyRebootPending));
        Assert.IsTrue(MutationJournalStateMachine.CanTransition(MutationJournalState.ApplyRebootPending, MutationJournalState.Applied));
        Assert.IsTrue(MutationJournalStateMachine.CanTransition(MutationJournalState.ApplyRebootPending, MutationJournalState.Reverting));
        Assert.IsTrue(MutationJournalStateMachine.CanTransition(MutationJournalState.Reverting, MutationJournalState.RollbackRebootPending));
        Assert.IsTrue(MutationJournalStateMachine.CanTransition(MutationJournalState.RollbackRebootPending, MutationJournalState.Reverted));

        var original = new DeviceInterruptConfigurationSnapshot(
            "PCI\\VEN_TEST&DEV_TEST", "Test device", "1.0", DeviceInterruptTargetKind.DisplayAdapter,
            true, new RegistryValueSnapshot(true, RegistryValueKind.DWord, BitConverter.GetBytes(0)),
            new RegistryValueSnapshot(true, RegistryValueKind.DWord, BitConverter.GetBytes(8)),
            false, RegistryValueSnapshot.Missing, RegistryValueSnapshot.Missing);
        var originalRoundTrip = DeviceInterruptMutationJournalCodec.DeserializeOriginal(
            DeviceInterruptMutationJournalCodec.SerializeOriginal(original));
        Assert.AreEqual(original.DeviceInstanceId, originalRoundTrip.DeviceInstanceId);
        CollectionAssert.AreEqual(original.MessageNumberLimit.Data, originalRoundTrip.MessageNumberLimit.Data);

        var candidate = DeviceInterruptMutationCandidate.EnableMsi();
        var candidateRoundTrip = DeviceInterruptMutationJournalCodec.DeserializeCandidate(
            DeviceInterruptMutationJournalCodec.SerializeCandidate(candidate));
        Assert.AreEqual(DeviceInterruptMutationOperation.EnableMsi, candidateRoundTrip.Operation);

        var root = FindRepositoryRoot();
        var storeSource = File.ReadAllText(Path.Combine(root, "src", "LatencyPilot.Platform.Windows", "Devices", "DeviceInterruptConfigurationStore.cs"));
        StringAssert.Contains(storeSource, "MSISupported");
        StringAssert.Contains(storeSource, "MessageNumberLimit");
        Assert.IsFalse(storeSource.Contains("SetValue(MessageNumberLimitValue", StringComparison.Ordinal),
            "LatencyPilot must never tune MessageNumberLimit as part of bounded MSI enablement.");
        var txSource = File.ReadAllText(Path.Combine(root, "src", "LatencyPilot.Service", "DeviceInterruptMutationTransaction.cs"));
        StringAssert.Contains(txSource, "MutationOperationLock.Acquire()");
        StringAssert.Contains(txSource, "ApplyRebootPending");
        StringAssert.Contains(txSource, "RollbackRebootPending");
        StringAssert.Contains(txSource, "ResumeAfterReboot");
        StringAssert.Contains(txSource, "measurementVerified");
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "LatencyPilot.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException();
    }
}
''')

def apply_implementation():
    journal = "src/LatencyPilot.Persistence/MutationJournal.cs"
    replace_once(journal,
'''    AbortedBeforeApply = 10,\n''',
'''    AbortedBeforeApply = 10,\n    ApplyRebootPending = 11,\n    RollbackRebootPending = 12,\n''')
    replace_once(journal,
'''        (MutationJournalState.Applying, MutationJournalState.Applied) => true,\n        (MutationJournalState.Applying, MutationJournalState.RecoveryRequired) => true,\n''',
'''        (MutationJournalState.Applying, MutationJournalState.Applied) => true,\n        (MutationJournalState.Applying, MutationJournalState.ApplyRebootPending) => true,\n        (MutationJournalState.Applying, MutationJournalState.RecoveryRequired) => true,\n        (MutationJournalState.ApplyRebootPending, MutationJournalState.Applied) => true,\n        (MutationJournalState.ApplyRebootPending, MutationJournalState.Reverting) => true,\n        (MutationJournalState.ApplyRebootPending, MutationJournalState.RecoveryRequired) => true,\n''')
    replace_once(journal,
'''        (MutationJournalState.Reverting, MutationJournalState.Reverted) => true,\n        (MutationJournalState.Reverting, MutationJournalState.RecoveryRequired) => true,\n''',
'''        (MutationJournalState.Reverting, MutationJournalState.Reverted) => true,\n        (MutationJournalState.Reverting, MutationJournalState.RollbackRebootPending) => true,\n        (MutationJournalState.Reverting, MutationJournalState.RecoveryRequired) => true,\n        (MutationJournalState.RollbackRebootPending, MutationJournalState.Reverted) => true,\n        (MutationJournalState.RollbackRebootPending, MutationJournalState.RecoveryRequired) => true,\n''')

    write("src/LatencyPilot.Platform.Windows/Devices/DeviceConfigurationRestartCoordinator.cs", r'''using System.ComponentModel;
using System.Runtime.InteropServices;
using LatencyPilot.Platform.Windows.Interop;

namespace LatencyPilot.Platform.Windows.Devices;

public sealed record DeviceConfigurationRestartResult(
    string DeviceInstanceId, bool SystemRestartRequired, bool DeviceStarted, bool DeviceHasProblem,
    uint? ProblemCode, uint DeviceNodeStatusFlags, uint DeviceInstallFlags)
{
    public bool RestartedInPlace => !SystemRestartRequired && DeviceStarted && !DeviceHasProblem;
}

public static class DeviceConfigurationRestartCoordinator
{
    public static DeviceConfigurationRestartResult RestartAfterConfigurationChange(string deviceInstanceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceInstanceId);
        if (deviceInstanceId.Contains('\0')) throw new ArgumentException("Device instance ID contains NUL.", nameof(deviceInstanceId));
        if (!DeviceInventoryReader.CapturePresentDevices().Devices.Any(device =>
            string.Equals(device.InstanceId, deviceInstanceId, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("The requested restart target is not a present PnP device.");

        using var set = SetupApi.GetPresentDeviceInfoSet();
        var data = SpDevInfoData.Create();
        if (!SetupApi.SetupDiOpenDeviceInfo(set, deviceInstanceId, IntPtr.Zero, 0, ref data))
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Unable to open target device for configuration restart.");
        var change = SpPropChangeParams.CreatePropertyChange();
        if (!SetupApi.SetupDiSetClassInstallParams(set, ref data, ref change, checked((uint)Marshal.SizeOf<SpPropChangeParams>())))
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Unable to configure property-change restart.");
        if (!SetupApi.SetupDiCallClassInstaller(SetupApi.DifPropertyChange, set, ref data))
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Windows rejected device property-change restart.");
        var install = SpDevInstallParams.Create();
        if (!SetupApi.SetupDiGetDeviceInstallParams(set, ref data, ref install))
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Unable to read device install flags after restart.");
        var result = ConfigurationManager.CM_Get_DevNode_Status(out var status, out var problem, data.DevInst, 0);
        if (result != ConfigurationManager.Success)
            throw new InvalidOperationException($"Unable to verify devnode after restart (CONFIGRET 0x{result:X8}).");
        var hasProblem = (status & ConfigurationManager.DeviceNodeHasProblem) != 0;
        var started = (status & ConfigurationManager.DeviceNodeStarted) != 0;
        var reboot = (install.Flags & (SetupApi.DiNeedRestart | SetupApi.DiNeedReboot)) != 0 ||
            (hasProblem && problem == ConfigurationManager.ProblemNeedRestart);
        return new DeviceConfigurationRestartResult(deviceInstanceId, reboot, started, hasProblem,
            hasProblem ? problem : null, status, install.Flags);
    }
}
''')

    write("src/LatencyPilot.Platform.Windows/Devices/DeviceInterruptConfigurationStore.cs", r'''using System.Buffers.Binary;
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
        if (!IsMsiEnabled(stored) || !ValuesEqual(stored.MessageNumberLimit, original.MessageNumberLimit))
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
        using var tx = TransactionalRegistry.Begin("LatencyPilot MSI exact restore");
        using (var key = tx.CreateOrOpenKey(RegistryHive.LocalMachine, HardwareSubPath(original.DeviceInstanceId, MsiSubKey)))
            RestoreValue(key, MsiSupportedValue, original.MsiSupported);
        tx.Commit();
        var current = Capture(original.DeviceInstanceId);
        if (!ValuesEqual(current.MsiSupported, original.MsiSupported) || !ValuesEqual(current.MessageNumberLimit, original.MessageNumberLimit))
            throw new InvalidOperationException("Exact MSI original state was not restored.");
    }

    public static void RestoreXhciAffinity(DeviceInterruptConfigurationSnapshot original)
    {
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
        }
        tx.Commit();
        var current = Capture(original.DeviceInstanceId);
        if (!ValuesEqual(current.DevicePolicy, original.DevicePolicy) || !ValuesEqual(current.AssignmentSetOverride, original.AssignmentSetOverride) ||
            current.AffinityPolicyKeyExisted != original.AffinityPolicyKeyExisted)
            throw new InvalidOperationException("Exact xHCI affinity original state was not restored.");
    }

    public static bool IsXhciAffinityStored(DeviceInterruptConfigurationSnapshot snapshot, DeviceInterruptAffinityCandidate candidate)
    {
        ValidateCandidate(candidate);
        return TryDword(snapshot.DevicePolicy, out var policy) && policy == IrqPolicySpecifiedProcessors &&
            TryMask(snapshot.AssignmentSetOverride, out var mask) && mask == candidate.AffinityMask;
    }

    public static bool MatchesOriginal(DeviceInterruptConfigurationSnapshot current, DeviceInterruptConfigurationSnapshot original, DeviceInterruptMutationOperation operation)
    {
        if (!string.Equals(current.DriverVersion, original.DriverVersion, StringComparison.OrdinalIgnoreCase)) return false;
        return operation == DeviceInterruptMutationOperation.EnableMsi
            ? ValuesEqual(current.MsiSupported, original.MsiSupported) && ValuesEqual(current.MessageNumberLimit, original.MessageNumberLimit)
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
    private static bool TryMask(RegistryValueSnapshot value, out ulong result) { if (value.Exists && value.Kind == RegistryValueKind.Binary && value.Data.Length is > 0 and <= 8) { Span<byte> p = stackalloc byte[8]; value.Data.CopyTo(p); result = BinaryPrimitives.ReadUInt64LittleEndian(p); return true; } result = 0; return false; }
    private static void ValidateCandidate(DeviceInterruptAffinityCandidate c) { if (c.ProcessorGroup != 0 || c.ProcessorNumber >= 64 || c.AffinityMask != (1UL << c.ProcessorNumber)) throw new ArgumentException("xHCI affinity candidate must be one valid group-0 KAFFINITY bit.", nameof(c)); }
}
''')

    write("src/LatencyPilot.Platform.Windows/Devices/DeviceInterruptMutationJournalCodec.cs", r'''using System.Text.Json;

namespace LatencyPilot.Platform.Windows.Devices;

public enum DeviceInterruptMutationOperation { EnableMsi = 0, XhciAffinity = 1 }

public sealed record DeviceInterruptMutationCandidate(
    DeviceInterruptMutationOperation Operation, ushort? ProcessorGroup, byte? ProcessorNumber, ulong? AffinityMask)
{
    public static DeviceInterruptMutationCandidate EnableMsi() => new(DeviceInterruptMutationOperation.EnableMsi, null, null, null);
    public static DeviceInterruptMutationCandidate XhciAffinity(DeviceInterruptAffinityCandidate c) => new(DeviceInterruptMutationOperation.XhciAffinity, c.ProcessorGroup, c.ProcessorNumber, c.AffinityMask);
    public DeviceInterruptAffinityCandidate ToAffinityCandidate() => Operation == DeviceInterruptMutationOperation.XhciAffinity && ProcessorGroup is { } g && ProcessorNumber is { } n && AffinityMask is { } m ? new(g, n, m) : throw new InvalidDataException("Journal candidate does not contain xHCI affinity identity.");
}

public static class DeviceInterruptMutationJournalCodec
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    public static string SerializeOriginal(DeviceInterruptConfigurationSnapshot value) => JsonSerializer.Serialize(value, Options);
    public static DeviceInterruptConfigurationSnapshot DeserializeOriginal(string json) => JsonSerializer.Deserialize<DeviceInterruptConfigurationSnapshot>(json, Options) ?? throw new InvalidDataException("Device interrupt original snapshot is empty.");
    public static string SerializeCandidate(DeviceInterruptMutationCandidate value) => JsonSerializer.Serialize(value, Options);
    public static DeviceInterruptMutationCandidate DeserializeCandidate(string json) => JsonSerializer.Deserialize<DeviceInterruptMutationCandidate>(json, Options) ?? throw new InvalidDataException("Device interrupt candidate is empty.");
}
''')

    write("src/LatencyPilot.Service/DeviceInterruptMutationTransaction.cs", r'''using LatencyPilot.Persistence;
using LatencyPilot.Platform.Windows.Devices;

namespace LatencyPilot.Service;

internal static class DeviceInterruptMutationContract
{
    internal const string MsiKind = "device-msi-enable";
    internal const string XhciAffinityKind = "xhci-interrupt-affinity";
}

internal sealed record DeviceInterruptPrepareResult(bool NoWriteRequired, MutationJournalEntry? Entry, DeviceInterruptConfigurationSnapshot Original);
internal sealed record DeviceInterruptMutationStepResult(MutationJournalEntry Entry, DeviceConfigurationRestartResult? Restart, bool OriginalStateRestored);

internal sealed class DeviceInterruptMutationTransaction
{
    private readonly MutationJournal journal;
    internal DeviceInterruptMutationTransaction(MutationJournal journal) => this.journal = journal ?? throw new ArgumentNullException(nameof(journal));

    internal DeviceInterruptPrepareResult PrepareMsi(string deviceInstanceId)
    {
        using var guard = MutationOperationLock.Acquire();
        var original = DeviceInterruptConfigurationStore.Capture(deviceInstanceId);
        DeviceInterruptConfigurationStore.EnsureMsiApplicable(original);
        if (DeviceInterruptConfigurationStore.IsMsiEnabled(original)) return new(true, null, original);
        var entry = journal.CreatePrepared(Guid.NewGuid(), DeviceInterruptMutationContract.MsiKind, original.DeviceInstanceId,
            DeviceInterruptMutationJournalCodec.SerializeOriginal(original), DeviceInterruptMutationJournalCodec.SerializeCandidate(DeviceInterruptMutationCandidate.EnableMsi()));
        return new(false, entry, original);
    }

    internal DeviceInterruptPrepareResult PrepareXhciAffinity(string deviceInstanceId, DeviceInterruptAffinityCandidate candidate)
    {
        using var guard = MutationOperationLock.Acquire();
        var original = DeviceInterruptConfigurationStore.Capture(deviceInstanceId);
        if (original.TargetKind != DeviceInterruptTargetKind.XhciController) throw new NotSupportedException("xHCI affinity target is not a USBXHCI controller.");
        if (DeviceInterruptConfigurationStore.IsXhciAffinityStored(original, candidate)) return new(true, null, original);
        var c = DeviceInterruptMutationCandidate.XhciAffinity(candidate);
        var entry = journal.CreatePrepared(Guid.NewGuid(), DeviceInterruptMutationContract.XhciAffinityKind, original.DeviceInstanceId,
            DeviceInterruptMutationJournalCodec.SerializeOriginal(original), DeviceInterruptMutationJournalCodec.SerializeCandidate(c));
        return new(false, entry, original);
    }

    internal DeviceInterruptMutationStepResult Apply(Guid experimentId)
    {
        using var guard = MutationOperationLock.Acquire();
        var prepared = GetEntry(experimentId, MutationJournalState.Prepared);
        var original = DeviceInterruptMutationJournalCodec.DeserializeOriginal(prepared.OriginalStateJson);
        var candidate = DeviceInterruptMutationJournalCodec.DeserializeCandidate(prepared.CandidateStateJson);
        var current = DeviceInterruptConfigurationStore.Capture(original.DeviceInstanceId);
        if (!DeviceInterruptConfigurationStore.MatchesOriginal(current, original, candidate.Operation))
            return AbortPrepared(prepared, "Interrupt configuration or driver changed after the exact original snapshot; no write was attempted.");
        var applying = journal.Transition(prepared.ExperimentId, prepared.Revision, MutationJournalState.Prepared, MutationJournalState.Applying);
        try
        {
            ApplyCandidate(original, candidate);
            var restart = DeviceConfigurationRestartCoordinator.RestartAfterConfigurationChange(original.DeviceInstanceId);
            if (restart.SystemRestartRequired)
            {
                var pending = journal.Transition(applying.ExperimentId, applying.Revision, MutationJournalState.Applying,
                    MutationJournalState.ApplyRebootPending, "Candidate is stored; Windows requires a system reboot before activation can be verified.");
                return new(pending, restart, false);
            }
            if (!restart.RestartedInPlace || !CandidateStored(original.DeviceInstanceId, candidate))
            {
                var recovery = journal.Transition(applying.ExperimentId, applying.Revision, MutationJournalState.Applying,
                    MutationJournalState.RecoveryRequired, "Candidate could not be verified active after device restart.");
                return new(recovery, restart, false);
            }
            var applied = journal.Transition(applying.ExperimentId, applying.Revision, MutationJournalState.Applying, MutationJournalState.Applied);
            return new(applied, restart, false);
        }
        catch (Exception ex)
        {
            var recovery = journal.Transition(applying.ExperimentId, applying.Revision, MutationJournalState.Applying,
                MutationJournalState.RecoveryRequired, Bound(ex));
            return new(recovery, null, false);
        }
    }

    internal MutationJournalEntry KeepVerified(Guid experimentId, bool measurementVerified)
    {
        using var guard = MutationOperationLock.Acquire();
        if (!measurementVerified) throw new InvalidOperationException("Candidate cannot be kept without an explicit verified measurement stage.");
        var applied = GetEntry(experimentId, MutationJournalState.Applied);
        var measuring = journal.Transition(applied.ExperimentId, applied.Revision, MutationJournalState.Applied, MutationJournalState.Measuring);
        var decision = journal.Transition(measuring.ExperimentId, measuring.Revision, MutationJournalState.Measuring, MutationJournalState.AwaitingDecision);
        return journal.Transition(decision.ExperimentId, decision.Revision, MutationJournalState.AwaitingDecision, MutationJournalState.Kept);
    }

    internal DeviceInterruptMutationStepResult ResumeAfterReboot(Guid experimentId)
    {
        using var guard = MutationOperationLock.Acquire();
        var entry = GetEntry(experimentId);
        var original = DeviceInterruptMutationJournalCodec.DeserializeOriginal(entry.OriginalStateJson);
        var candidate = DeviceInterruptMutationJournalCodec.DeserializeCandidate(entry.CandidateStateJson);
        if (entry.State == MutationJournalState.ApplyRebootPending)
        {
            var next = CandidateStored(original.DeviceInstanceId, candidate)
                ? journal.Transition(entry.ExperimentId, entry.Revision, entry.State, MutationJournalState.Applied)
                : journal.Transition(entry.ExperimentId, entry.Revision, entry.State, MutationJournalState.RecoveryRequired, "Candidate is not stored after reboot.");
            return new(next, null, false);
        }
        if (entry.State == MutationJournalState.RollbackRebootPending)
        {
            var restored = DeviceInterruptConfigurationStore.MatchesOriginal(DeviceInterruptConfigurationStore.Capture(original.DeviceInstanceId), original, candidate.Operation);
            var next = restored
                ? journal.Transition(entry.ExperimentId, entry.Revision, entry.State, MutationJournalState.Reverted)
                : journal.Transition(entry.ExperimentId, entry.Revision, entry.State, MutationJournalState.RecoveryRequired, "Original state is not restored after reboot.");
            return new(next, null, restored);
        }
        throw new InvalidOperationException($"Experiment is not awaiting reboot verification: {entry.State}.");
    }

    internal DeviceInterruptMutationStepResult Rollback(Guid experimentId)
    {
        using var guard = MutationOperationLock.Acquire();
        var entry = GetEntry(experimentId);
        if (entry.State == MutationJournalState.Prepared)
        {
            var aborted = journal.Transition(entry.ExperimentId, entry.Revision, entry.State, MutationJournalState.AbortedBeforeApply, "Prepared experiment cancelled before any write.");
            return new(aborted, null, true);
        }
        var original = DeviceInterruptMutationJournalCodec.DeserializeOriginal(entry.OriginalStateJson);
        var candidate = DeviceInterruptMutationJournalCodec.DeserializeCandidate(entry.CandidateStateJson);
        if (entry.State is MutationJournalState.Reverted or MutationJournalState.AbortedBeforeApply) return new(entry, null, true);
        var reverting = entry.State == MutationJournalState.Reverting ? entry :
            journal.Transition(entry.ExperimentId, entry.Revision, entry.State, MutationJournalState.Reverting);
        try
        {
            Restore(original, candidate.Operation);
            var restart = DeviceConfigurationRestartCoordinator.RestartAfterConfigurationChange(original.DeviceInstanceId);
            if (restart.SystemRestartRequired)
            {
                var pending = journal.Transition(reverting.ExperimentId, reverting.Revision, MutationJournalState.Reverting,
                    MutationJournalState.RollbackRebootPending, "Original state is stored; Windows requires reboot before rollback activation can be verified.");
                return new(pending, restart, true);
            }
            var restored = DeviceInterruptConfigurationStore.MatchesOriginal(DeviceInterruptConfigurationStore.Capture(original.DeviceInstanceId), original, candidate.Operation);
            var next = restored && restart.RestartedInPlace
                ? journal.Transition(reverting.ExperimentId, reverting.Revision, MutationJournalState.Reverting, MutationJournalState.Reverted)
                : journal.Transition(reverting.ExperimentId, reverting.Revision, MutationJournalState.Reverting, MutationJournalState.RecoveryRequired, "Rollback could not verify active original state.");
            return new(next, restart, restored);
        }
        catch (Exception ex)
        {
            var recovery = journal.Transition(reverting.ExperimentId, reverting.Revision, MutationJournalState.Reverting, MutationJournalState.RecoveryRequired, Bound(ex));
            return new(recovery, null, false);
        }
    }

    internal DeviceInterruptMutationStepResult Recover(Guid experimentId)
    {
        var entry = GetEntry(experimentId);
        return entry.State switch
        {
            MutationJournalState.ApplyRebootPending or MutationJournalState.RollbackRebootPending => ResumeAfterReboot(experimentId),
            MutationJournalState.Prepared or MutationJournalState.Applied or MutationJournalState.Measuring or MutationJournalState.AwaitingDecision or MutationJournalState.Kept or MutationJournalState.RecoveryRequired or MutationJournalState.Reverting => Rollback(experimentId),
            MutationJournalState.Reverted or MutationJournalState.AbortedBeforeApply => new(entry, null, true),
            _ => throw new InvalidOperationException($"Unsupported recovery state {entry.State}."),
        };
    }

    private DeviceInterruptMutationStepResult AbortPrepared(MutationJournalEntry entry, string reason)
    {
        var aborted = journal.Transition(entry.ExperimentId, entry.Revision, MutationJournalState.Prepared, MutationJournalState.AbortedBeforeApply, reason);
        return new(aborted, null, true);
    }
    private MutationJournalEntry GetEntry(Guid id, params MutationJournalState[] allowed)
    {
        var entry = journal.GetRequired(id);
        if (entry.Kind is not (DeviceInterruptMutationContract.MsiKind or DeviceInterruptMutationContract.XhciAffinityKind)) throw new InvalidOperationException("Journal entry is not a bounded device interrupt mutation.");
        if (allowed.Length > 0 && !allowed.Contains(entry.State)) throw new InvalidOperationException($"Expected {string.Join('/', allowed)}, found {entry.State}.");
        return entry;
    }
    private static void ApplyCandidate(DeviceInterruptConfigurationSnapshot original, DeviceInterruptMutationCandidate c)
    { if (c.Operation == DeviceInterruptMutationOperation.EnableMsi) DeviceInterruptConfigurationStore.ApplyMsi(original); else DeviceInterruptConfigurationStore.ApplyXhciAffinity(original, c.ToAffinityCandidate()); }
    private static bool CandidateStored(string id, DeviceInterruptMutationCandidate c)
    { var current = DeviceInterruptConfigurationStore.Capture(id); return c.Operation == DeviceInterruptMutationOperation.EnableMsi ? DeviceInterruptConfigurationStore.IsMsiEnabled(current) : DeviceInterruptConfigurationStore.IsXhciAffinityStored(current, c.ToAffinityCandidate()); }
    private static void Restore(DeviceInterruptConfigurationSnapshot original, DeviceInterruptMutationOperation operation)
    { if (operation == DeviceInterruptMutationOperation.EnableMsi) DeviceInterruptConfigurationStore.RestoreMsi(original); else DeviceInterruptConfigurationStore.RestoreXhciAffinity(original); }
    private static string Bound(Exception ex) { var value = $"{ex.GetType().Name}: {ex.Message}"; return value.Length <= 1024 ? value : value[..1024]; }
}
''')

if __name__ == "__main__":
    if len(sys.argv) != 2 or sys.argv[1] not in {"tests", "implementation"}: raise SystemExit("usage: audit-closure-task5-device-mutations.py tests|implementation")
    (apply_tests if sys.argv[1] == "tests" else apply_implementation)()
