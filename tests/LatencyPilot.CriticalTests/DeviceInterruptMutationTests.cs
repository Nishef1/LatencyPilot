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
        Assert.ThrowsExactly<NotSupportedException>(() =>
            DeviceInterruptConfigurationStore.EnsureMsiApplicable(audioOriginal),
            "New HDAudio MSI mutation must stay disabled until active message-signaled delivery has an authoritative verifier.");

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

        var genericCandidate = DeviceInterruptMutationCandidate.DeviceAffinity(
            new DeviceInterruptAffinityCandidate(
                0,
                GpuInterruptAffinityCandidate.GetPrimaryProcessorNumber(multiMask),
                multiMask));
        var genericRoundTrip = DeviceInterruptMutationJournalCodec.DeserializeCandidate(
            DeviceInterruptMutationJournalCodec.SerializeCandidate(genericCandidate));
        Assert.AreEqual(DeviceInterruptMutationOperation.DeviceAffinity, genericRoundTrip.Operation);
        Assert.AreEqual(multiMask, genericRoundTrip.ToAffinityCandidate().AffinityMask);

        var root = FindRepositoryRoot();
        var storeSource = File.ReadAllText(Path.Combine(root, "src", "LatencyPilot.Platform.Windows", "Devices", "DeviceInterruptConfigurationStore.cs"));
        StringAssert.Contains(storeSource, "MSISupported");
        StringAssert.Contains(storeSource, "MessageNumberLimit");
        StringAssert.Contains(storeSource, "HDAudBus");
        StringAssert.Contains(storeSource, "HighDefinitionAudioController");
        Assert.IsFalse(storeSource.Contains("CmResourceInterruptMessage", StringComparison.Ordinal),
            "IRQ_DES_64 flags must never be reinterpreted as CM_PARTIAL_RESOURCE_DESCRIPTOR message-signaled flags.");
        Assert.IsFalse(storeSource.Contains("SetValue(MessageNumberLimitValue", StringComparison.Ordinal),
            "LatencyPilot must never tune MessageNumberLimit as part of bounded MSI enablement.");
        var txSource = File.ReadAllText(Path.Combine(root, "src", "LatencyPilot.Service", "DeviceInterruptMutationTransaction.cs"));
        StringAssert.Contains(txSource, "MutationOperationLock.Acquire()");
        StringAssert.Contains(txSource, "ApplyRebootPending");
        StringAssert.Contains(txSource, "RollbackRebootPending");
        StringAssert.Contains(txSource, "ResumeAfterReboot");
        StringAssert.Contains(txSource, "original.TargetKind == DeviceInterruptTargetKind.DisplayAdapter",
            "Manual display-adapter affinity must use reboot-pending activation instead of live-restarting the GPU that may be rendering the WinUI app.");
        StringAssert.Contains(txSource, "MutationJournalState.ApplyRebootPending",
            "Display-adapter Apply must remain resumable after reboot.");
        StringAssert.Contains(txSource, "MutationJournalState.RollbackRebootPending",
            "Display-adapter Restore must avoid a live GPU restart and remain resumable after reboot.");
        StringAssert.Contains(txSource, "measurementVerified");
        StringAssert.Contains(txSource, "DeviceAffinityKind");
        StringAssert.Contains(txSource, "PrepareDeviceAffinity",
            "Generic manual affinity must use the same durable mutation transaction rather than bypassing journal/recovery.");
        StringAssert.Contains(txSource, "KeepStoredPolicyVerified",
            "Policy-only manual retention must remain semantically distinct from GPU/xHCI runtime measurement verification.");
        Assert.IsFalse(txSource.Contains("Manual CPU affinity requires a present device node with allocated interrupt resources.", StringComparison.Ordinal),
            "Current ConfigMgr allocation visibility must not gate IntPolicy-style manual policy editing.");

        var manualRunnerSource = File.ReadAllText(Path.Combine(
            root, "tools", "LatencyPilot.GateAValidation", "ManualDeviceAffinityRunner.cs"));
        StringAssert.Contains(manualRunnerSource, "ManualAffinityTargetKind.Gpu");
        StringAssert.Contains(manualRunnerSource, "ManualAffinityTargetKind.Xhci");
        StringAssert.Contains(manualRunnerSource, "ManualAffinityTargetKind.Device");
        StringAssert.Contains(manualRunnerSource, "ManualAffinityTargetKind.AudioMsi");
        Assert.IsFalse(manualRunnerSource.Contains("VerifyAndKeepAudioMsi", StringComparison.Ordinal),
            "The superseded HDAudio MSI Apply/Keep path must not return.");
        StringAssert.Contains(manualRunnerSource, "latencypilot-manual-device-affinity-v2");
        StringAssert.Contains(manualRunnerSource, "VerifyAllocatedAffinity");
        StringAssert.Contains(manualRunnerSource, "GpuInterruptRuntimePlacementVerifier",
            "Manual GPU Keep must require runtime ETW placement evidence, not only stored or allocated state.");
        Assert.IsFalse(manualRunnerSource.Contains("GpuInterruptAffinityMutationTransaction(journal)", StringComparison.Ordinal),
            "The WinUI-launched manual GPU path must not use the live-restart GPU benchmark transaction.");
        StringAssert.Contains(manualRunnerSource, "select that same mask after reboot to resume it",
            "Manual GPU affinity must expose a journal-owned reboot/resume flow.");
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
            "Readable contradictory allocation must fail closed and restore exact original state.");
        StringAssert.Contains(manualRunnerSource, "AppliedPolicyKept",
            "When allocation is unavailable, generic manual policy must be retained only under an explicit policy-only status.");
        StringAssert.Contains(manualRunnerSource, "AlreadyStoredPolicy",
            "A pre-existing generic policy with unavailable allocation must remain distinguishable from fully active-verified placement.");
        StringAssert.Contains(manualRunnerSource, "No subsystem-specific ISR attribution claim is made",
            "Generic device affinity must not imply GPU/xHCI-style ISR attribution.");
        StringAssert.Contains(manualRunnerSource, "does not claim ownership",
            "A pre-existing matching policy must not be claimed as LatencyPilot-owned.");
        Assert.IsFalse(
            manualRunnerSource.Contains("Registry.LocalMachine", StringComparison.Ordinal),
            "Manual affinity UI/helper must reuse bounded mutation stores instead of becoming an arbitrary registry writer.");

        var appSource = File.ReadAllText(Path.Combine(
            root, "src", "LatencyPilot.App", "ManualDeviceAffinityExperience.cs"));
        StringAssert.Contains(appSource, "if (row.TargetKind is not null)",
            "The shared processor-mask control path must remain explicit in the affinity workspace.");
        StringAssert.Contains(appSource, "_gateAValidationRunning",
            "Manual mutation must not run concurrently with GPU Gate A.");
        StringAssert.Contains(appSource, "HashSet<byte>",
            "Manual affinity UI must support independent multi-selection of processor buttons.");
        StringAssert.Contains(appSource, "cpuButton.Checked +=",
            "CPU selection must follow the ToggleButton checked-state event rather than infer state from a generic click.");
        Assert.IsFalse(appSource.Contains("button.Background = selected", StringComparison.Ordinal),
            "CPU selection must not re-style every ToggleButton from inside a Checked/Unchecked event; let WinUI own the visual state.");
        StringAssert.Contains(appSource, "Reboot Windows after this step",
            "GPU Apply guidance must match the reboot-safe manual activation path.");
        StringAssert.Contains(appSource, "cpuButton.Unchecked +=",
            "CPU deselection must follow the ToggleButton unchecked-state event.");
        StringAssert.Contains(appSource, "_manualAffinityDraftMasks",
            "A selected processor mask must survive detail re-renders until an authoritative action result replaces it.");
        StringAssert.Contains(appSource, "Select all");
        StringAssert.Contains(appSource, ": \"Device\"",
            "Every non-GPU/non-xHCI device row must receive the generic manual Windows affinity-policy target.");
        Assert.IsFalse(appSource.Contains("HasAssignedInterrupts", StringComparison.Ordinal),
            "UI editability must not depend on current ConfigMgr allocated-resource visibility.");
        StringAssert.Contains(appSource, "ToolTipService.SetToolTip(item, row.Device.DisplayName)",
            "Truncated device rows must expose the complete device name on hover.");
        StringAssert.Contains(appSource, "new GridLength(400d)",
            "The desktop master column must remain wide enough for useful device identification.");
        StringAssert.Contains(appSource, "Receive Side Scaling (RSS) is a separate NIC/NDIS processor policy",
            "NIC users must be told that device interrupt affinity and RSS processor steering are separate mechanisms.");
        StringAssert.Contains(appSource, "GroupBy(static option => option.PhysicalCoreIndex)",
            "Logical CPU choices must preserve physical-core/SMT grouping in the manual selector.");
        StringAssert.Contains(appSource, "AppWindowTitleBar.IsCustomizationSupported()",
            "The independent tool window must keep its title chrome aligned with the application theme when Windows supports customization.");
        StringAssert.Contains(appSource, "restoreMayBeNeeded",
            "Restore must be hidden from the proven-default case without blocking recovery when current policy inspection is unavailable.");
        StringAssert.Contains(appSource, "--mask");
        Assert.IsFalse(appSource.Contains("AudioMsi", StringComparison.Ordinal),
            "HDAudio MSI must not be exposed as an editable development UI target.");
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "LatencyPilot.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException();
    }
}
