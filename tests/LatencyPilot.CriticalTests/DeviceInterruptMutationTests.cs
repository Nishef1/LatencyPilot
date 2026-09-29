#pragma warning disable CA1822 // AuditCase methods are reflection-invoked by ConsolidatedCriticalTests.
using LatencyPilot.Core.Devices;
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
        Assert.IsTrue(MutationJournalStateMachine.CanTransition(MutationJournalState.Applied, MutationJournalState.ApplyRebootPending),
            "An in-place device restart that leaves translated allocation unavailable must be able to defer verification to one full reboot without discarding the journaled candidate.");
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

        var storageDevice = CreatePresentDevice(
            new Guid("4D36E967-E325-11CE-BFC1-08002BE10318"),
            "Test disk");
        Assert.IsFalse(
            ManualDeviceAffinityPolicyEligibility.CanStartNewPolicyMutation(
                storageDevice,
                out var storageReason));
        StringAssert.Contains(storageReason!, "diagnostics-only");

        var systemDevice = CreatePresentDevice(
            new Guid("4D36E97D-E325-11CE-BFC1-08002BE10318"),
            "Test PCI bridge");
        Assert.IsFalse(
            ManualDeviceAffinityPolicyEligibility.CanStartNewPolicyMutation(
                systemDevice,
                out var systemReason));
        StringAssert.Contains(systemReason!, "inspection-only");

        var networkDevice = CreatePresentDevice(
            new Guid("4D36E972-E325-11CE-BFC1-08002BE10318"),
            "Test NIC") with
        {
            InterruptConfiguration = InterruptConfigurationSnapshot
                .Available(null, null, null, null) with
            {
                InterruptManagementKeyExists = true,
            },
        };
        Assert.IsTrue(
            ManualDeviceAffinityPolicyEligibility.CanStartNewPolicyMutation(
                networkDevice,
                out var networkReason));
        Assert.IsNull(networkReason);
        Assert.AreEqual(
            ManualDeviceAffinityPolicyTargetKind.Device,
            ManualDeviceAffinityPolicyEligibility.ClassifyTarget(networkDevice));

        var gpuDevice = CreatePresentDevice(
            new Guid("4D36E968-E325-11CE-BFC1-08002BE10318"),
            "Test GPU");
        Assert.AreEqual(
            ManualDeviceAffinityPolicyTargetKind.Gpu,
            ManualDeviceAffinityPolicyEligibility.ClassifyTarget(gpuDevice));

        var xhciDevice = CreatePresentDevice(
            Guid.NewGuid(),
            "Test xHCI") with { ServiceName = "USBXHCI" };
        Assert.AreEqual(
            ManualDeviceAffinityPolicyTargetKind.Xhci,
            ManualDeviceAffinityPolicyEligibility.ClassifyTarget(xhciDevice));

        var nonInterruptDevice = CreatePresentDevice(
            new Guid("50127DC3-0F36-415E-A6CC-4CB3BE910B65"),
            "Test processor node");
        Assert.IsFalse(
            ManualDeviceAffinityPolicyEligibility.CanStartNewPolicyMutation(
                nonInterruptDevice,
                out var nonInterruptReason));
        StringAssert.Contains(
            nonInterruptReason!,
            "does not expose the documented Interrupt Management registry surface");


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
        var inventoryReaderSource = File.ReadAllText(Path.Combine(root, "src", "LatencyPilot.Platform.Windows", "Devices", "DeviceInventoryReader.cs"));
        StringAssert.Contains(inventoryReaderSource, "InterruptManagementKeyExists = true",
            "Device inventory must preserve whether the documented Interrupt Management surface actually exists.");

        var manualUiSourceFilter = File.ReadAllText(Path.Combine(root, "src", "LatencyPilot.App", "ManualDeviceAffinityExperience.cs"));
        Assert.IsFalse(
            manualUiSourceFilter.Contains("HasAssignedInterrupts", StringComparison.Ordinal),
            "Policy Lab editability/filtering must not depend on current ConfigMgr allocated-resource visibility.");
        StringAssert.Contains(manualUiSourceFilter, "interrupt-capable devices",
            "Policy Lab should not present every present PnP node as an editable interrupt target.");

        var storeSource = File.ReadAllText(Path.Combine(root, "src", "LatencyPilot.Platform.Windows", "Devices", "DeviceInterruptConfigurationStore.cs"));
        StringAssert.Contains(storeSource, "MSISupported");
        StringAssert.Contains(storeSource, "MessageNumberLimit");
        StringAssert.Contains(storeSource, "HDAudBus");
        StringAssert.Contains(storeSource, "HighDefinitionAudioController");
        Assert.IsFalse(storeSource.Contains("CmResourceInterruptMessage", StringComparison.Ordinal),
            "IRQ_DES_64 flags must never be reinterpreted as CM_PARTIAL_RESOURCE_DESCRIPTOR message-signaled flags.");
        Assert.IsFalse(storeSource.Contains("SetValue(MessageNumberLimitValue", StringComparison.Ordinal),
            "LatencyPilot must never tune MessageNumberLimit as part of bounded MSI enablement.");
        StringAssert.Contains(storeSource, "ManualDeviceAffinityPolicyEligibility.CanStartNewPolicyMutation",
            "The lowest public generic affinity write boundary must enforce inspection-only storage/System classes; exact Restore remains separate.");

        StringAssert.Contains(storeSource, "RegistryValueKind.DWord when value.Data.Length == sizeof(uint)",
            "AssignmentSetOverride must accept the documented REG_DWORD representation.");
        StringAssert.Contains(storeSource, "RegistryValueKind.QWord when value.Data.Length == sizeof(ulong)",
            "AssignmentSetOverride must accept the documented REG_QWORD representation.");
        StringAssert.Contains(storeSource, "SetAffinityMaskValue",
            "Affinity writes should preserve an existing supported mask representation when possible instead of forcing every foreign policy to REG_BINARY.");
        var txSource = File.ReadAllText(Path.Combine(root, "src", "LatencyPilot.Service", "DeviceInterruptMutationTransaction.cs"));
        StringAssert.Contains(txSource, "MutationOperationLock.Acquire()");
        StringAssert.Contains(txSource, "ApplyRebootPending");
        StringAssert.Contains(txSource, "RollbackRebootPending");
        StringAssert.Contains(txSource, "ResumeAfterReboot");
        StringAssert.Contains(txSource, "DeferXhciApplyVerificationToReboot",
            "Applied xHCI candidates need a bounded, xHCI-only reboot-verification deferral instead of treating unavailable translated allocation as contradictory.");
        StringAssert.Contains(txSource, "original.TargetKind != DeviceInterruptTargetKind.XhciController",
            "The Applied -> ApplyRebootPending deferral must not become a generic escape hatch for arbitrary device mutations.");
        StringAssert.Contains(txSource, "original.TargetKind == DeviceInterruptTargetKind.DisplayAdapter",
            "Manual display-adapter affinity must use reboot-pending activation instead of live-restarting the GPU that may be rendering the WinUI app.");
        StringAssert.Contains(txSource, "MutationJournalState.ApplyRebootPending",
            "Display-adapter Apply must remain resumable after reboot.");
        StringAssert.Contains(txSource, "MutationJournalState.RollbackRebootPending",
            "Display-adapter Restore must avoid a live GPU restart and remain resumable after reboot.");
        StringAssert.Contains(txSource, "restartDisplayAdapter",
            "The bounded device transaction must make display-adapter restart an explicit opt-in rather than an implicit side effect.");
        StringAssert.Contains(txSource, "DeviceConfigurationRestartCoordinator.RestartAfterConfigurationChange",
            "The optional GPU driver restart must use the documented SetupAPI device-property-change path.");
        StringAssert.Contains(txSource, "RestartedInPlace",
            "An optional GPU driver restart must be verified as an in-place healthy device restart before continuing.");
        StringAssert.Contains(txSource, "measurementVerified");
        StringAssert.Contains(txSource, "DeviceAffinityKind");
        StringAssert.Contains(txSource, "PrepareDeviceAffinity",
            "Generic manual affinity must use the same durable mutation transaction rather than bypassing journal/recovery.");
        StringAssert.Contains(txSource, "ManualDeviceAffinityPolicyEligibility.CanStartNewPolicyMutation",
            "The transaction itself must reject storage/System-class generic mutation even if a future caller bypasses the current UI/helper.");
        StringAssert.Contains(txSource, "KeepStoredPolicyVerified",
            "Policy-only manual retention must remain semantically distinct from GPU/xHCI runtime measurement verification.");
        Assert.IsFalse(txSource.Contains("Manual CPU affinity requires a present device node with allocated interrupt resources.", StringComparison.Ordinal),
            "Current ConfigMgr allocation visibility must not gate IntPolicy-style manual policy editing.");

        var launcherSource = File.ReadAllText(Path.Combine(root, "run.ps1"));
        StringAssert.Contains(launcherSource, "$installStatus = [string]$installResult.status",
            "The non-elevated launcher must distinguish a deferred recovery-host start from a completed protected-service replacement.");
        StringAssert.Contains(launcherSource, "'Deferred'",
            "A deferred recovery-host start must remain visible to the non-elevated launcher.");
        var installSource = File.ReadAllText(Path.Combine(root, "scripts", "Install-Service.ps1"));
        StringAssert.Contains(installSource, "Start-RecoveryHostForDeferredReplacement",
            "The elevated installer must start the existing protected recovery host when replacement is blocked by an unresolved experiment.");
        StringAssert.Contains(installSource, ".IndexOf('Replacement blocked: Experiment ', [System.StringComparison]::Ordinal) -ge 0",
            "The elevated installer must classify unresolved replacement blockers separately from uninstall blockers.");
        StringAssert.Contains(installSource, "--check-replacement",
            "Ordinary protected-Service replacement must use the replacement-safety contract so terminal Kept policies survive updates.");
        Assert.IsFalse(installSource.Contains("--check-uninstall", StringComparison.Ordinal),
            "Service replacement must not reuse uninstall safety, because uninstall intentionally rejects terminal Kept policies.");
        StringAssert.Contains(installSource, "Write-InstallResult -Status 'Deferred'",
            "The elevated installer must report a deferred recovery-host start without claiming that replacement succeeded.");

        var serviceProgramSource = File.ReadAllText(Path.Combine(root, "src", "LatencyPilot.Service", "Program.cs"));
        StringAssert.Contains(serviceProgramSource, "--check-replacement",
            "Protected-Service upgrades need a replacement safety contract distinct from uninstall.");
        StringAssert.Contains(serviceProgramSource, "LATENCYPILOT_REPLACEMENT_SAFE_V1");

        var journalInspectorSource = File.ReadAllText(Path.Combine(
            root, "src", "LatencyPilot.Persistence", "MutationJournalReadOnlyInspector.cs"));
        StringAssert.Contains(journalInspectorSource, "EnsureSafeForReplacement");
        StringAssert.Contains(
            journalInspectorSource,
            "MutationJournalState.Kept",
            "A terminal Kept policy must remain legal during ordinary Service replacement while uninstall stays stricter.");

        var manualUiSource = File.ReadAllText(Path.Combine(
            root, "src", "LatencyPilot.App", "ManualDeviceAffinityExperience.cs"));
        StringAssert.Contains(manualUiSource, "Steered by Windows (system)",
            "IRQ policy value 6 must be presented as the system-reserved steering policy rather than an unknown number.");

        StringAssert.Contains(manualUiSource, "AppliedAllocationVerified",
            "Policy Lab must present allocation-authoritative xHCI completion as a successful state rather than a neutral/unknown helper result.");
        StringAssert.Contains(manualUiSource, "AlreadyConfiguredAllocationVerified");
        StringAssert.Contains(manualUiSource, "P-core");
        StringAssert.Contains(manualUiSource, "E-core");
        StringAssert.Contains(manualUiSource, "processor groups",
            "The manual editor must disclose its group-0-only KAFFINITY boundary instead of silently hiding additional processor groups.");

        var manualRunnerSource = File.ReadAllText(Path.Combine(
            root, "tools", "LatencyPilot.GateAValidation", "ManualDeviceAffinityRunner.cs"));
        StringAssert.Contains(manualRunnerSource, "ManualAffinityTargetKind.Gpu");
        StringAssert.Contains(manualRunnerSource, "ManualAffinityTargetKind.Xhci");
        StringAssert.Contains(manualRunnerSource, "ManualAffinityTargetKind.Device");
        StringAssert.Contains(manualRunnerSource, "ManualAffinityTargetKind.AudioMsi");
        StringAssert.Contains(manualRunnerSource, "ManualDeviceAffinityPolicyEligibility.CanStartNewPolicyMutation",
            "The elevated helper must enforce inspection-only device classes even if its CLI is invoked directly.");
        StringAssert.Contains(manualRunnerSource, "ManualDeviceAffinityPolicyEligibility.ClassifyTarget",
            "The elevated helper must derive the required verification path from the actual PnP device instead of trusting --target-kind.");
        StringAssert.Contains(manualRunnerSource, "stronger device-specific verification path cannot be bypassed",
            "GPU/xHCI targets must not be downgraded to the generic policy-only path by a forged helper argument.");
        Assert.IsFalse(manualRunnerSource.Contains("VerifyAndKeepAudioMsi", StringComparison.Ordinal),
            "The superseded HDAudio MSI Apply/Keep path must not return.");
        StringAssert.Contains(manualRunnerSource, "latencypilot-manual-device-affinity-v2");
        StringAssert.Contains(manualRunnerSource, "VerifyAllocatedAffinity");
        StringAssert.Contains(manualRunnerSource, "GpuInterruptRuntimePlacementVerifier",
            "A fully runtime-verified manual GPU result must still require ETW placement evidence.");
        Assert.IsFalse(
            manualRunnerSource.Contains("CanObserveAllocatedAffinity", StringComparison.Ordinal),
            "Allocation observability and mask verification must come from one coherent device snapshot so a restart-time re-enumeration cannot turn missing evidence into a false contradiction.");
        StringAssert.Contains(manualRunnerSource, "out var allocationObservable",
            "The single allocation verifier must explicitly return whether translated allocation is usable.");
        StringAssert.Contains(manualRunnerSource, "activeMasksByGroup",
            "Translated allocation must be checked against the processors that are actually active in each Windows processor group.");
        StringAssert.Contains(manualRunnerSource, "resource.AffinityMask == ulong.MaxValue",
            "The documented all-processors affinity sentinel must be normalized against the active processor mask before comparison.");
        StringAssert.Contains(manualRunnerSource, "KeepStoredPolicyVerified",
            "When GPU allocation is unavailable, the explicit manual policy may be retained only through the policy-only journal keep path.");
        Assert.IsFalse(manualRunnerSource.Contains("GpuInterruptAffinityMutationTransaction(journal)", StringComparison.Ordinal),
            "The WinUI-launched manual GPU path must not use the live-restart GPU benchmark transaction.");
        StringAssert.Contains(manualRunnerSource, "select that same mask after reboot to resume it",
            "Manual GPU affinity must expose a journal-owned reboot/resume flow.");
        StringAssert.Contains(manualRunnerSource, "XhciInterruptRuntimePlacementVerifier",
            "Manual xHCI verification must still attempt the stronger controller-attributed ETW path when ownership can be proven.");
        StringAssert.Contains(manualRunnerSource, "AppliedAllocationVerified",
            "Usable target translated allocation is the minimum xHCI Keep authority when controller-specific ETW attribution is unavailable.");
        StringAssert.Contains(manualRunnerSource, "RawInputTimingCapture.CaptureAsync",
            "Manual xHCI verification must collect a bounded host-observable Raw Input timing sanity sample when one exact mouse route owns the controller.");
        StringAssert.Contains(manualRunnerSource, "InputTimingAnalyzer.Analyze",
            "Raw Input timing sanity must use the shared timing analyzer instead of inventing a second interval interpretation.");
        StringAssert.Contains(manualRunnerSource, "not click-to-photon latency",
            "The xHCI timing sanity result must never be mislabeled as physical click-to-photon latency.");
        StringAssert.Contains(manualRunnerSource, "ApplyRebootPending");
        StringAssert.Contains(manualRunnerSource, "pendingCandidate.ProcessorNumber != candidate.ProcessorNumber");
        StringAssert.Contains(manualRunnerSource, "pendingCandidate.AffinityMask != candidate.AffinityMask",
            "A reboot-pending xHCI experiment must not be resumed under a newly selected processor mask.");
        StringAssert.Contains(manualRunnerSource, "--mask",
            "Manual affinity must accept an explicit KAFFINITY mask for multi-select.");
        StringAssert.Contains(manualRunnerSource, "--restart-device-only",
            "Manual GPU affinity must expose an explicit device-only restart choice separate from a full system reboot.");
        StringAssert.Contains(manualRunnerSource, "RestartedInPlace",
            "Manual GPU affinity must verify the device-only restart result before claiming the candidate is active.");
        StringAssert.Contains(manualRunnerSource, "(item.EffectiveMask & ~targetMask) == 0",
            "Normalized allocated interrupt resources must remain inside the requested processor mask.");
        StringAssert.Contains(manualRunnerSource, "VerificationFailedRolledBack",
            "Readable contradictory allocation must fail closed and restore exact original state.");
        StringAssert.Contains(manualRunnerSource, "active placement is unavailable rather than contradictory",
            "Unusable translated descriptors such as an impossible processor group must not be mislabeled as evidence that escaped the requested mask.");
        StringAssert.Contains(manualRunnerSource, "allowRebootDeferral: true",
            "A fresh xHCI Apply should get one reboot-verification deferral when the in-place restart cannot expose usable translated allocation.");
        StringAssert.Contains(manualRunnerSource, "allowRebootDeferral: false",
            "A resumed post-reboot xHCI verification must not loop forever by requesting another reboot.");
        StringAssert.Contains(manualRunnerSource, "runtimeWithoutAllocation",
            "Manual GPU verification must still attempt authoritative direct-driver ETW when ConfigMgr translated allocation is unavailable.");
        StringAssert.Contains(manualRunnerSource, "AppliedPolicyKept",
            "When both allocation and authoritative ETW proof are unavailable, manual GPU policy must remain distinguishable as policy-only.");
        StringAssert.Contains(manualRunnerSource, "AppliedAndKept",
            "Direct-driver ETW may close manual GPU runtime verification when translated allocation is unavailable.");
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
        var appProjectSource = File.ReadAllText(Path.Combine(
            root, "src", "LatencyPilot.App", "LatencyPilot.App.csproj"));
        StringAssert.Contains(appProjectSource, "Microsoft.Data.Sqlite",
            "The self-contained App must carry the SQLite runtime used by its project-referenced mutation journal inspector.");
        StringAssert.Contains(appSource, "if (row.CanStartNewPolicyMutation)",
            "The processor-mask controls must be gated by the shared device-class mutation policy, not by UI classification alone.");
        StringAssert.Contains(appSource, "ManualDeviceAffinityPolicyEligibility.CanStartNewPolicyMutation",
            "The App and elevated helper must share one device-class mutation boundary.");
        StringAssert.Contains(appSource, "ManualDeviceAffinityPolicyEligibility.ClassifyTarget",
            "The App must use the same actual-device target classification as the elevated helper.");
        StringAssert.Contains(appSource, "Restore journal-owned original",
            "Inspection-only rows must preserve recovery for previously journaled LatencyPilot state.");
        StringAssert.Contains(appSource, "_gateAValidationRunning",
            "Manual mutation must not run concurrently with GPU Gate A.");
        StringAssert.Contains(appSource, "IsDevelopmentInterruptPolicyLabAvailable",
            "The manual interrupt-policy lab must have its own development availability boundary instead of reusing the GPU Gate A predicate.");
        Assert.IsFalse(appSource.Contains("IsDevelopmentGateAAvailable(_gateARepositoryRoot)", StringComparison.Ordinal),
            "The manual interrupt-policy lab must not be visibility-coupled to the GPU Gate A availability predicate.");
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
        StringAssert.Contains(appSource, "MutationJournalReadOnlyInspector.GetUnresolved",
            "Manual affinity UI must inspect the durable journal so pending recovery cannot be hidden by a default registry policy.");
        StringAssert.Contains(appSource, "HasPendingRecovery",
            "A device with a journal-owned pending recovery must expose an explicit recovery affordance.");
        StringAssert.Contains(appSource, "TryGetPendingAffinityMask",
            "A reboot-pending manual candidate must reconstruct its processor mask from the durable journal.");
        StringAssert.Contains(appSource, "row.PendingMask ??",
            "The journal-owned pending mask must win over missing current-allocation visibility when rebuilding CPU selection.");
        StringAssert.Contains(appSource, "Resume & verify",
            "ApplyRebootPending must resume verification instead of silently converting Apply into Restore.");
        StringAssert.Contains(appSource, "isOtherPendingRecovery",
            "Only non-Apply pending recovery should force the recovery-only confirmation path.");
        StringAssert.Contains(appSource, "ContentDialog",
            "Manual affinity changes must require an explicit Windows-style confirmation before saving or restoring policy.");
        StringAssert.Contains(appSource, "Restore only this device",
            "Recovery confirmation must make the target-scoped restore choice explicit.");
        StringAssert.Contains(appSource, "Save policy; I will reboot manually",
            "GPU policy confirmation must state that LatencyPilot will not reboot Windows automatically.");
        StringAssert.Contains(appSource, "Restart only this GPU driver/device",
            "GPU confirmation must expose the device-only restart choice separately from a full system reboot.");
        StringAssert.Contains(appSource, "restart-device-only",
            "The UI must pass the explicit device-only restart choice to the elevated helper.");
        var recoveryAssessment = File.ReadAllText(Path.Combine(
            root, "src", "LatencyPilot.Service", "MutationRecoveryAssessment.cs"));
        StringAssert.Contains(recoveryAssessment, "DeviceInterruptRecoveryInspector.Inspect",
            "Startup recovery assessment must classify manual device-interrupt journal entries with their bounded inspector.");
        StringAssert.Contains(recoveryAssessment, "Manual device-interrupt recovery is owned by the elevated manual-affinity helper",
            "Startup recovery must preserve the manual helper boundary instead of reporting the journal kind as unsupported.");
        StringAssert.Contains(appSource, "--mask");
        Assert.IsFalse(appSource.Contains("AudioMsi", StringComparison.Ordinal),
            "HDAudio MSI must not be exposed as an editable development UI target.");
    }

    private static PnPDeviceSnapshot CreatePresentDevice(Guid classGuid, string displayName) =>
        new(
            $"ROOT\\LATENCYPILOT_TEST\\{classGuid:N}",
            classGuid,
            displayName,
            "LatencyPilot",
            "ROOT",
            "test",
            new DriverMetadataSnapshot(null, null, null),
            InterruptConfigurationSnapshot.Available(null, null, null, null),
            InterruptResourceSnapshot.Available([]));

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "LatencyPilot.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException();
    }
}
