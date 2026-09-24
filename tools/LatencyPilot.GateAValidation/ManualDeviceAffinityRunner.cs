using System.Globalization;
using System.Security.Principal;
using System.Text.Json;
using LatencyPilot.Core.Devices;
using LatencyPilot.Core.System;
using LatencyPilot.Persistence;
using LatencyPilot.Platform.Windows.Devices;
using LatencyPilot.Service;

namespace LatencyPilot.GateAValidation;

internal static class ManualDeviceAffinityRunner
{
    internal const string ModeFlag = "--manual-device-affinity";
    private const string ConfirmationFlag = "--confirm-physical-mutation";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    internal static async Task<int> RunAsync(string[] args)
    {
        ManualAffinityOptions? options = null;
        ManualDeviceAffinityReport? report = null;
        Guid? experimentId = null;

        try
        {
            options = ManualAffinityOptions.Parse(args);
            EnsureAdministrator();
            Directory.CreateDirectory(
                Path.GetDirectoryName(options.OutputPath)
                ?? throw new InvalidOperationException("Manual affinity report path has no parent directory."));

            var journal = new MutationJournal(MutationJournal.GetDefaultDatabasePath());
            journal.Initialize();

            report = options.Action switch
            {
                ManualAffinityAction.Restore => RestoreTarget(journal, options),
                ManualAffinityAction.Apply => ApplyTarget(journal, options, out experimentId),
                _ => throw new ArgumentOutOfRangeException(nameof(options.Action)),
            };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var device = options?.DeviceInstanceId ?? "unknown";
            report = new ManualDeviceAffinityReport(
                Schema: "latencypilot-manual-device-affinity-v1",
                Action: options?.Action.ToString() ?? "Unknown",
                Status: "Failed",
                Succeeded: false,
                DeviceInstanceId: device,
                DisplayName: null,
                TargetKind: options?.TargetKind.ToString(),
                ProcessorNumber: options?.ProcessorNumber,
                ExperimentId: experimentId,
                RestartRequired: false,
                StoredMask: TryReadStoredMask(device),
                AllocatedMasks: [],
                Verification: null,
                Message: $"{exception.GetType().Name}: {exception.Message}");
        }

        if (options is null)
        {
            Console.Error.WriteLine(report?.Message ?? "Manual affinity options could not be parsed.");
            return 2;
        }

        report ??= throw new InvalidOperationException("Manual affinity runner did not produce a report.");
        await File.WriteAllTextAsync(options.OutputPath, JsonSerializer.Serialize(report, JsonOptions));
        Console.WriteLine($"manual-affinity-status={report.Status}");
        Console.WriteLine($"manual-affinity-report={options.OutputPath}");
        if (!string.IsNullOrWhiteSpace(report.Message))
        {
            Console.WriteLine($"manual-affinity-message={report.Message}");
        }

        return report.Status switch
        {
            "AppliedAndKept" or "AlreadyConfigured" or "Restored" or "NoLatencyPilotChange"
                when report.Succeeded => 0,
            "RebootRequired" => 3,
            _ => 1,
        };
    }

    private static ManualDeviceAffinityReport RestoreTarget(
        MutationJournal journal,
        ManualAffinityOptions options)
    {
        var result = new GlobalRestoreBaselineExecutor(journal).RestoreTarget(options.DeviceInstanceId);
        var device = TryGetPresentDevice(options.DeviceInstanceId);
        var status = result.RestoredCount == 0 ? "NoLatencyPilotChange" : "Restored";
        var message = result.RestoredCount == 0
            ? "No retained LatencyPilot mutation owns this device, so no registry or device state was changed."
            : $"Restored {result.RestoredCount.ToString(CultureInfo.InvariantCulture)} retained LatencyPilot change(s) for this device to their exact captured original state.";

        var restoredExperiment = result.RestoredExperimentIds.FirstOrDefault();
        return CreateReport(
            options,
            status,
            succeeded: true,
            device,
            experimentId: restoredExperiment == Guid.Empty ? null : restoredExperiment,
            restartRequired: false,
            verification: result.RestoredCount == 0 ? "No owned retained change." : "Exact journal-owned original state restored.",
            message);
    }

    private static ManualDeviceAffinityReport ApplyTarget(
        MutationJournal journal,
        ManualAffinityOptions options,
        out Guid? experimentId)
    {
        experimentId = null;
        if (options.ProcessorNumber is not { } processorNumber)
        {
            throw new ArgumentException("--processor is required for manual affinity apply.");
        }

        var topology = ProcessorTopologyReader.Capture();
        var logicalProcessor = new LogicalProcessorId(0, processorNumber);
        var validatedGpuCandidate = GpuInterruptAffinityCandidate.Create(topology, logicalProcessor);

        return options.TargetKind switch
        {
            ManualAffinityTargetKind.Gpu => ApplyGpu(
                journal,
                options,
                validatedGpuCandidate,
                out experimentId),
            ManualAffinityTargetKind.Xhci => ApplyXhci(
                journal,
                options,
                new DeviceInterruptAffinityCandidate(
                    validatedGpuCandidate.ProcessorGroup,
                    validatedGpuCandidate.ProcessorNumber,
                    validatedGpuCandidate.AffinityMask),
                out experimentId),
            _ => throw new NotSupportedException(
                "Manual affinity mutation is supported only for the display adapter and USBXHCI controllers."),
        };
    }

    private static ManualDeviceAffinityReport ApplyGpu(
        MutationJournal journal,
        ManualAffinityOptions options,
        GpuInterruptAffinityCandidate candidate,
        out Guid? experimentId)
    {
        EnsureNoUnresolvedMutation(journal);
        var original = GpuInterruptAffinityPolicyStore.Capture(options.DeviceInstanceId);
        var device = TryGetPresentDevice(options.DeviceInstanceId);
        if (GpuInterruptAffinityStateComparer.MatchesCandidate(original, candidate))
        {
            var verified = VerifyAllocatedAffinity(options.DeviceInstanceId, candidate.AffinityMask, out var masks, out var reason);
            return CreateReport(
                options,
                verified ? "AlreadyConfigured" : "AlreadyStoredUnverified",
                succeeded: verified,
                device,
                experimentId: null,
                restartRequired: false,
                verification: reason,
                message: verified
                    ? "The requested GPU affinity was already stored and its allocated interrupt resources match the requested CPU. No write was attempted."
                    : "The requested GPU affinity is already stored, but active allocated interrupt resources do not prove it. No write was attempted and LatencyPilot does not claim ownership of this existing policy.",
                allocatedMasks: masks);
        }

        var transaction = new GpuInterruptAffinityMutationTransaction(journal);
        var prepared = transaction.Prepare(options.DeviceInstanceId, candidate, original);
        experimentId = prepared.ExperimentId;
        try
        {
            var applied = transaction.ApplyAndActivate(prepared.ExperimentId);
            if (applied.JournalEntry.State != MutationJournalState.Applied)
            {
                throw new InvalidOperationException(
                    applied.JournalEntry.FailureReason ??
                    $"GPU affinity apply stopped in {applied.JournalEntry.State}.");
            }

            var backend = new GpuAffinityMutationBackend(journal);
            backend.BeginMeasurement(prepared.ExperimentId);
            var verified = VerifyAllocatedAffinity(options.DeviceInstanceId, candidate.AffinityMask, out var masks, out var reason);
            if (!verified)
            {
                backend.Rollback(prepared.ExperimentId);
                return CreateReport(
                    options,
                    "VerificationFailedRolledBack",
                    succeeded: false,
                    device,
                    prepared.ExperimentId,
                    restartRequired: false,
                    verification: reason,
                    message: "GPU affinity was applied but allocated interrupt resources did not verify the requested CPU; exact original state was restored.",
                    allocatedMasks: masks);
            }

            backend.AwaitDecision(prepared.ExperimentId);
            backend.KeepCandidate(prepared.ExperimentId);
            return CreateReport(
                options,
                "AppliedAndKept",
                succeeded: true,
                device,
                prepared.ExperimentId,
                restartRequired: false,
                verification: reason,
                message: "GPU affinity was journaled, applied, restarted, verified from allocated interrupt resources, and retained as an explicit manual choice.",
                allocatedMasks: masks);
        }
        catch
        {
            TryRollbackGpu(transaction, journal, prepared.ExperimentId);
            throw;
        }
    }

    private static ManualDeviceAffinityReport ApplyXhci(
        MutationJournal journal,
        ManualAffinityOptions options,
        DeviceInterruptAffinityCandidate candidate,
        out Guid? experimentId)
    {
        var transaction = new DeviceInterruptMutationTransaction(journal);
        var pending = journal.GetUnresolved()
            .Where(entry =>
                string.Equals(entry.TargetId, options.DeviceInstanceId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(entry.Kind, DeviceInterruptMutationContract.XhciAffinityKind, StringComparison.Ordinal))
            .ToArray();

        if (pending.Length > 1)
        {
            throw new InvalidOperationException("More than one unresolved xHCI mutation owns this device; recover them before manual affinity changes.");
        }

        if (pending.Length == 1)
        {
            EnsureOnlyTargetPending(journal, pending[0].ExperimentId);
            experimentId = pending[0].ExperimentId;
            if (pending[0].State != MutationJournalState.ApplyRebootPending)
            {
                throw new InvalidOperationException(
                    $"The existing xHCI experiment is {pending[0].State}; recover or restore it before applying another manual affinity.");
            }

            var pendingCandidate = DeviceInterruptMutationJournalCodec
                .DeserializeCandidate(pending[0].CandidateStateJson)
                .ToAffinityCandidate();
            if (pendingCandidate.ProcessorGroup != candidate.ProcessorGroup ||
                pendingCandidate.ProcessorNumber != candidate.ProcessorNumber ||
                pendingCandidate.AffinityMask != candidate.AffinityMask)
            {
                throw new InvalidOperationException(
                    $"The pending xHCI experiment targets CPU {pendingCandidate.ProcessorNumber}; select that same CPU to resume it, or restore/recover the pending experiment first.");
            }

            var resumed = transaction.ResumeAfterReboot(pending[0].ExperimentId);
            if (resumed.Entry.State == MutationJournalState.Applied)
            {
                return VerifyAndKeepXhci(transaction, options, candidate, pending[0].ExperimentId);
            }

            if (resumed.Entry.State == MutationJournalState.ApplyRebootPending)
            {
                return CreateReport(
                    options,
                    "RebootRequired",
                    succeeded: false,
                    TryGetPresentDevice(options.DeviceInstanceId),
                    pending[0].ExperimentId,
                    restartRequired: true,
                    verification: resumed.Entry.FailureReason,
                    message: "The stored xHCI candidate is still awaiting reboot activation/verification.");
            }

            throw new InvalidOperationException(
                resumed.Entry.FailureReason ??
                $"xHCI reboot resume stopped in {resumed.Entry.State}; recover the journal before another manual change.");
        }

        EnsureNoUnresolvedMutation(journal);
        var prepared = transaction.PrepareXhciAffinity(options.DeviceInstanceId, candidate);
        if (prepared.NoWriteRequired)
        {
            var verified = VerifyAllocatedAffinity(options.DeviceInstanceId, candidate.AffinityMask, out var masks, out var reason);
            return CreateReport(
                options,
                verified ? "AlreadyConfigured" : "AlreadyStoredUnverified",
                succeeded: verified,
                TryGetPresentDevice(options.DeviceInstanceId),
                experimentId: null,
                restartRequired: false,
                verification: reason,
                message: verified
                    ? "The requested xHCI affinity was already stored and active; no LatencyPilot write was required."
                    : "The requested xHCI affinity is already stored, but active allocated interrupt resources do not prove it. No write was attempted and LatencyPilot does not claim ownership of this existing policy.",
                allocatedMasks: masks);
        }

        var entry = prepared.Entry
            ?? throw new InvalidOperationException("xHCI prepare returned neither a no-op nor a journal entry.");
        experimentId = entry.ExperimentId;
        try
        {
            var applied = transaction.Apply(entry.ExperimentId);
            if (applied.Entry.State == MutationJournalState.ApplyRebootPending)
            {
                return CreateReport(
                    options,
                    "RebootRequired",
                    succeeded: false,
                    TryGetPresentDevice(options.DeviceInstanceId),
                    entry.ExperimentId,
                    restartRequired: true,
                    verification: applied.Entry.FailureReason,
                    message: "Windows stored the xHCI affinity candidate but requires a reboot before active allocation can be verified. Reboot, reopen the manual affinity panel, and apply the same target again to resume this experiment.");
            }

            if (applied.Entry.State != MutationJournalState.Applied)
            {
                throw new InvalidOperationException(
                    applied.Entry.FailureReason ??
                    $"xHCI affinity apply stopped in {applied.Entry.State}.");
            }

            return VerifyAndKeepXhci(transaction, options, candidate, entry.ExperimentId);
        }
        catch
        {
            TryRollbackDevice(transaction, journal, entry.ExperimentId);
            throw;
        }
    }

    private static ManualDeviceAffinityReport VerifyAndKeepXhci(
        DeviceInterruptMutationTransaction transaction,
        ManualAffinityOptions options,
        DeviceInterruptAffinityCandidate candidate,
        Guid experimentId)
    {
        var verified = VerifyAllocatedAffinity(options.DeviceInstanceId, candidate.AffinityMask, out var masks, out var reason);
        if (!verified)
        {
            var rollback = transaction.Rollback(experimentId);
            var restored = rollback.Entry.State == MutationJournalState.Reverted && rollback.OriginalStateRestored;
            return CreateReport(
                options,
                "VerificationFailedRolledBack",
                succeeded: false,
                TryGetPresentDevice(options.DeviceInstanceId),
                experimentId,
                restartRequired: rollback.Entry.State == MutationJournalState.RollbackRebootPending,
                verification: reason,
                message: restored
                    ? "xHCI affinity did not verify from active allocated interrupt resources; exact original state was restored."
                    : "xHCI affinity did not verify and rollback still requires recovery/reboot attention.",
                allocatedMasks: masks);
        }

        var kept = transaction.KeepVerified(experimentId, measurementVerified: true);
        return CreateReport(
            options,
            "AppliedAndKept",
            succeeded: kept.State == MutationJournalState.Kept,
            TryGetPresentDevice(options.DeviceInstanceId),
            experimentId,
            restartRequired: false,
            verification: reason,
            message: "xHCI affinity was journaled, applied, restarted, verified from active allocated interrupt resources, and retained as an explicit manual choice.",
            allocatedMasks: masks);
    }

    private static bool VerifyAllocatedAffinity(
        string deviceInstanceId,
        ulong targetMask,
        out IReadOnlyList<string> masks,
        out string reason)
    {
        var device = TryGetPresentDevice(deviceInstanceId)
            ?? throw new InvalidOperationException("The manual affinity target is no longer a present PnP device.");
        var resources = device.InterruptResources;
        masks = resources.Resources
            .Select(resource => string.Create(
                CultureInfo.InvariantCulture,
                $"group {resource.ProcessorGroup}:0x{resource.AffinityMask:X}"))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (resources.ReadStatus != InterruptResourceReadStatus.Available || resources.Resources.Count == 0)
        {
            reason = $"Allocated interrupt resources are {resources.ReadStatus}; active affinity cannot be proven.";
            return false;
        }

        var verified = resources.Resources.All(resource =>
            resource.ProcessorGroup == 0 &&
            resource.AffinityMask == targetMask);
        reason = verified
            ? $"All {resources.Resources.Count.ToString(CultureInfo.InvariantCulture)} allocated interrupt resource(s) resolve to group 0 mask 0x{targetMask:X}."
            : $"Allocated interrupt resources do not all resolve to group 0 mask 0x{targetMask:X}.";
        return verified;
    }

    private static void EnsureNoUnresolvedMutation(MutationJournal journal)
    {
        var unresolved = journal.GetUnresolved();
        if (unresolved.Count != 0)
        {
            throw new InvalidOperationException(
                $"Manual affinity is blocked by {unresolved.Count.ToString(CultureInfo.InvariantCulture)} unresolved mutation experiment(s). Recover them first.");
        }
    }

    private static void EnsureOnlyTargetPending(MutationJournal journal, Guid experimentId)
    {
        var unresolved = journal.GetUnresolved();
        if (unresolved.Any(entry => entry.ExperimentId != experimentId))
        {
            throw new InvalidOperationException(
                "Another unresolved mutation experiment exists; manual xHCI resume is blocked until recovery is complete.");
        }
    }

    private static void TryRollbackGpu(
        GpuInterruptAffinityMutationTransaction transaction,
        MutationJournal journal,
        Guid experimentId)
    {
        try
        {
            var entry = journal.TryGet(experimentId);
            if (entry is not null && !entry.IsTerminal)
            {
                _ = transaction.RollbackAndActivate(experimentId);
            }
        }
        catch
        {
            // The durable unresolved journal remains the recovery authority.
        }
    }

    private static void TryRollbackDevice(
        DeviceInterruptMutationTransaction transaction,
        MutationJournal journal,
        Guid experimentId)
    {
        try
        {
            var entry = journal.TryGet(experimentId);
            if (entry is not null && !entry.IsTerminal)
            {
                _ = transaction.Rollback(experimentId);
            }
        }
        catch
        {
            // The durable unresolved journal remains the recovery authority.
        }
    }

    private static PnPDeviceSnapshot? TryGetPresentDevice(string deviceInstanceId) =>
        DeviceInventoryReader.CapturePresentDevices().Devices.FirstOrDefault(device =>
            string.Equals(device.InstanceId, deviceInstanceId, StringComparison.OrdinalIgnoreCase));

    private static ulong? TryReadStoredMask(string deviceInstanceId)
    {
        try
        {
            return TryGetPresentDevice(deviceInstanceId)?.InterruptConfiguration.AssignmentSetOverrideMask;
        }
        catch
        {
            return null;
        }
    }

    private static ManualDeviceAffinityReport CreateReport(
        ManualAffinityOptions options,
        string status,
        bool succeeded,
        PnPDeviceSnapshot? device,
        Guid? experimentId,
        bool restartRequired,
        string? verification,
        string message,
        IReadOnlyList<string>? allocatedMasks = null) =>
        new(
            Schema: "latencypilot-manual-device-affinity-v1",
            Action: options.Action.ToString(),
            Status: status,
            Succeeded: succeeded,
            DeviceInstanceId: options.DeviceInstanceId,
            DisplayName: device?.DisplayName,
            TargetKind: options.TargetKind.ToString(),
            ProcessorNumber: options.ProcessorNumber,
            ExperimentId: experimentId,
            RestartRequired: restartRequired,
            StoredMask: device?.InterruptConfiguration.AssignmentSetOverrideMask,
            AllocatedMasks: allocatedMasks ??
                device?.InterruptResources.Resources
                    .Select(resource => string.Create(
                        CultureInfo.InvariantCulture,
                        $"group {resource.ProcessorGroup}:0x{resource.AffinityMask:X}"))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray() ??
                [],
            Verification: verification,
            Message: message);

    private static void EnsureAdministrator()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Manual device affinity is supported only on Windows.");
        }

        using var identity = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
        {
            throw new UnauthorizedAccessException("Manual device affinity must run in the elevated helper.");
        }
    }

    private sealed record ManualAffinityOptions(
        ManualAffinityAction Action,
        ManualAffinityTargetKind TargetKind,
        string DeviceInstanceId,
        byte? ProcessorNumber,
        string OutputPath)
    {
        internal static ManualAffinityOptions Parse(string[] args)
        {
            ArgumentNullException.ThrowIfNull(args);
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            var confirmation = false;

            for (var index = 0; index < args.Length; index++)
            {
                var token = args[index];
                if (string.Equals(token, ModeFlag, StringComparison.Ordinal))
                {
                    continue;
                }
                if (string.Equals(token, ConfirmationFlag, StringComparison.Ordinal))
                {
                    confirmation = true;
                    continue;
                }
                if (token is not ("--action" or "--target-kind" or "--device" or "--processor" or "--output"))
                {
                    throw new ArgumentException($"Unknown manual affinity option '{token}'.");
                }
                if (index + 1 >= args.Length)
                {
                    throw new ArgumentException($"Manual affinity option '{token}' requires a value.");
                }
                values[token] = args[++index];
            }

            if (!confirmation)
            {
                throw new InvalidOperationException(
                    $"Manual affinity requires the explicit {ConfirmationFlag} acknowledgement.");
            }

            string Required(string key) =>
                values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
                    ? value
                    : throw new ArgumentException($"Required manual affinity option '{key}' is missing.");

            if (!Enum.TryParse<ManualAffinityAction>(Required("--action"), ignoreCase: true, out var action) ||
                !Enum.IsDefined(action))
            {
                throw new ArgumentException("--action must be Apply or Restore.");
            }
            if (!Enum.TryParse<ManualAffinityTargetKind>(Required("--target-kind"), ignoreCase: true, out var targetKind) ||
                !Enum.IsDefined(targetKind))
            {
                throw new ArgumentException("--target-kind must be Gpu or Xhci.");
            }

            byte? processor = null;
            if (values.TryGetValue("--processor", out var processorText))
            {
                if (!byte.TryParse(processorText, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ||
                    parsed >= 64)
                {
                    throw new ArgumentException("--processor must be a group-0 logical CPU from 0 through 63.");
                }
                processor = parsed;
            }
            if (action == ManualAffinityAction.Apply && processor is null)
            {
                throw new ArgumentException("--processor is required for manual affinity Apply.");
            }

            return new ManualAffinityOptions(
                action,
                targetKind,
                Required("--device"),
                processor,
                Path.GetFullPath(Required("--output")));
        }
    }
}

internal enum ManualAffinityAction
{
    Apply = 0,
    Restore = 1,
}

internal enum ManualAffinityTargetKind
{
    Gpu = 0,
    Xhci = 1,
}

internal sealed record ManualDeviceAffinityReport(
    string Schema,
    string Action,
    string Status,
    bool Succeeded,
    string DeviceInstanceId,
    string? DisplayName,
    string? TargetKind,
    byte? ProcessorNumber,
    Guid? ExperimentId,
    bool RestartRequired,
    ulong? StoredMask,
    IReadOnlyList<string> AllocatedMasks,
    string? Verification,
    string Message);
