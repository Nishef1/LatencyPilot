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

        var audioOriginal = original with
        {
            TargetKind = DeviceInterruptTargetKind.HighDefinitionAudioController,
        };
        DeviceInterruptConfigurationStore.EnsureMsiApplicable(audioOriginal);
        Assert.AreEqual(
            true,
            DeviceInterruptConfigurationStore.IsMessageSignaledInterruptActive(
                InterruptResourceSnapshot.Available(
                    [new AllocatedInterruptResourceSnapshot(55, 0, 1UL << 10, 0x0002)])));
        Assert.AreEqual(
            false,
            DeviceInterruptConfigurationStore.IsMessageSignaledInterruptActive(
                InterruptResourceSnapshot.Available(
                    [new AllocatedInterruptResourceSnapshot(55, 0, 1UL << 10, 0x0000)])));
        Assert.IsNull(
            DeviceInterruptConfigurationStore.IsMessageSignaledInterruptActive(
                InterruptResourceSnapshot.NoAllocatedConfiguration(0)));

        var candidate = DeviceInterruptMutationCandidate.EnableMsi();
        var candidateRoundTrip = DeviceInterruptMutationJournalCodec.DeserializeCandidate(
            DeviceInterruptMutationJournalCodec.SerializeCandidate(candidate));
        Assert.AreEqual(DeviceInterruptMutationOperation.EnableMsi, candidateRoundTrip.Operation);

        var multiMask = (1UL << 2) | (1UL << 4) | (1UL << 6);
        var multiCandidate = DeviceInterruptMutationCandidate.XhciAffinity(
            new DeviceInterruptAffinityCandidate(
                0,
                GpuInterruptAffinityCandidate.GetPrimaryProcessorNumber(multiMask),
                multiMask));
        var multiRoundTrip = DeviceInterruptMutationJournalCodec.DeserializeCandidate(
            DeviceInterruptMutationJournalCodec.SerializeCandidate(multiCandidate));
        Assert.AreEqual(multiMask, multiRoundTrip.AffinityMask);
        Assert.AreEqual((byte)2, multiRoundTrip.ProcessorNumber);

        var root = FindRepositoryRoot();
        var storeSource = File.ReadAllText(Path.Combine(root, "src", "LatencyPilot.Platform.Windows", "Devices", "DeviceInterruptConfigurationStore.cs"));
        StringAssert.Contains(storeSource, "MSISupported");
        StringAssert.Contains(storeSource, "MessageNumberLimit");
        StringAssert.Contains(storeSource, "HDAudBus");
        StringAssert.Contains(storeSource, "HighDefinitionAudioController");
        StringAssert.Contains(storeSource, "CmResourceInterruptMessage");
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
        StringAssert.Contains(manualRunnerSource, "ManualAffinityTargetKind.AudioMsi");
        StringAssert.Contains(manualRunnerSource, "VerifyAndKeepAudioMsi");
        StringAssert.Contains(manualRunnerSource, "latencypilot-manual-device-affinity-v2");
        StringAssert.Contains(manualRunnerSource, "VerifyAllocatedAffinity");
        StringAssert.Contains(manualRunnerSource, "GpuInterruptRuntimePlacementVerifier",
            "Manual GPU Keep must require runtime ETW placement evidence, not only stored or allocated state.");
        StringAssert.Contains(manualRunnerSource, "XhciInterruptRuntimePlacementVerifier",
            "Manual xHCI Keep must require controller-attributed runtime ETW placement evidence.");
        StringAssert.Contains(manualRunnerSource, "ApplyRebootPending");
        StringAssert.Contains(manualRunnerSource, "pendingCandidate.ProcessorNumber != candidate.ProcessorNumber");
        StringAssert.Contains(manualRunnerSource, "pendingCandidate.AffinityMask != candidate.AffinityMask",
            "A reboot-pending xHCI experiment must not be resumed under a newly selected processor mask.");
        StringAssert.Contains(manualRunnerSource, "--mask",
            "Manual affinity must accept an explicit KAFFINITY mask for multi-select.");
        StringAssert.Contains(manualRunnerSource, "(resource.AffinityMask & ~targetMask) == 0",
            "Allocated interrupt resources must remain inside the requested processor mask.");
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
        StringAssert.Contains(appSource, "HashSet<byte>",
            "Manual affinity UI must support independent multi-selection of processor buttons.");
        StringAssert.Contains(appSource, "Select all");
        StringAssert.Contains(appSource, "--mask");
        StringAssert.Contains(appSource, "AudioMsi");
        StringAssert.Contains(appSource, "Enable MSI & verify");
        StringAssert.Contains(appSource, "MessageNumberLimit is never changed");
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "LatencyPilot.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException();
    }
}
