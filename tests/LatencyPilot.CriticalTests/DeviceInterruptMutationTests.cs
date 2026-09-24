#pragma warning disable CA1822 // AuditCase methods are reflection-invoked by ConsolidatedCriticalTests.
using LatencyPilot.Persistence;
using LatencyPilot.Platform.Windows.Devices;
using LatencyPilot.Service;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Win32;

namespace LatencyPilot.CriticalTests;

[TestClass]
public sealed class DeviceInterruptMutationTests
{
    [AuditCase]
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

        var manualRunnerSource = File.ReadAllText(Path.Combine(
            root, "tools", "LatencyPilot.GateAValidation", "ManualDeviceAffinityRunner.cs"));
        StringAssert.Contains(manualRunnerSource, "ManualAffinityTargetKind.Gpu");
        StringAssert.Contains(manualRunnerSource, "ManualAffinityTargetKind.Xhci");
        StringAssert.Contains(manualRunnerSource, "VerifyAllocatedAffinity");
        StringAssert.Contains(manualRunnerSource, "GpuInterruptRuntimePlacementVerifier",
            "Manual GPU Keep must require runtime ETW placement evidence, not only stored or allocated state.");
        StringAssert.Contains(manualRunnerSource, "XhciInterruptRuntimePlacementVerifier",
            "Manual xHCI Keep must require controller-attributed runtime ETW placement evidence.");
        StringAssert.Contains(manualRunnerSource, "ApplyRebootPending");
        StringAssert.Contains(manualRunnerSource, "pendingCandidate.ProcessorNumber != candidate.ProcessorNumber",
            "A reboot-pending xHCI experiment must not be resumed under a newly selected CPU.");
        StringAssert.Contains(manualRunnerSource, "VerificationFailedRolledBack",
            "Manual affinity must fail closed and restore exact original state when active allocation is not verified.");
        StringAssert.Contains(manualRunnerSource, "does not claim ownership",
            "A pre-existing matching policy must not be claimed as LatencyPilot-owned.");
        Assert.IsFalse(
            manualRunnerSource.Contains("Registry.LocalMachine", StringComparison.Ordinal),
            "Manual affinity UI/helper must reuse bounded mutation stores instead of becoming an arbitrary registry writer.");

        var appSource = File.ReadAllText(Path.Combine(
            root, "src", "LatencyPilot.App", "ManualDeviceAffinityExperience.cs"));
        StringAssert.Contains(appSource, "Read only",
            "Unsupported latency-sensitive devices must remain inspectable without exposing mutation.");
        StringAssert.Contains(appSource, "_gateAValidationRunning",
            "Manual mutation must not run concurrently with GPU Gate A.");
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "LatencyPilot.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException();
    }
}
