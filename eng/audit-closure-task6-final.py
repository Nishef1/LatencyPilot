from pathlib import Path
import re
import sys

ROOT = Path(__file__).resolve().parents[1]


def read(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8")


def write(path: str, text: str) -> None:
    target = ROOT / path
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(text, encoding="utf-8", newline="\n")


def replace_once(path: str, old: str, new: str) -> None:
    text = read(path)
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{path}: expected one anchor, found {count}: {old[:120]!r}")
    write(path, text.replace(old, new, 1))


def append_section(path: str, marker: str, section: str) -> None:
    text = read(path)
    if marker in text:
        return
    suffix = "" if text.endswith("\n") else "\n"
    write(path, text + suffix + "\n" + section.strip() + "\n")


def apply_tests() -> None:
    write("tests/LatencyPilot.CriticalTests/AuditClosureIntegrationTests.cs", r'''using LatencyPilot.Service;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LatencyPilot.CriticalTests;

[TestClass]
public sealed class AuditClosureIntegrationTests
{
    [TestMethod]
    public async Task OptimizeWorkflowRecoveryRestoreAndPublicArmingStayFailClosed()
    {
        var expected = new[]
        {
            AutomaticOptimizationStage.OriginalMeasurement,
            AutomaticOptimizationStage.GpuAffinity,
            AutomaticOptimizationStage.Msi,
            AutomaticOptimizationStage.PrimaryInputUsbXhci,
            AutomaticOptimizationStage.FinalVerification,
            AutomaticOptimizationStage.Report,
        };
        CollectionAssert.AreEqual(expected, AutomaticOptimizationWorkflow.OrderedStages.ToArray());

        var successRunner = new RecordingStageRunner(static stage =>
            new AutomaticOptimizationStageResult(
                stage,
                AutomaticOptimizationStageDisposition.Completed,
                $"{stage} complete",
                $"before:{stage}",
                $"after:{stage}"));
        var success = await new AutomaticOptimizationWorkflow().RunAsync(successRunner);
        Assert.IsTrue(success.Completed);
        CollectionAssert.AreEqual(expected, successRunner.Calls.ToArray());
        Assert.AreEqual(AutomaticOptimizationStage.Report, success.TerminalStage);

        var rebootRunner = new RecordingStageRunner(static stage =>
            stage == AutomaticOptimizationStage.Msi
                ? new AutomaticOptimizationStageResult(
                    stage,
                    AutomaticOptimizationStageDisposition.RebootPending,
                    "Windows restart required",
                    "before:msi",
                    "stored:msi")
                : new AutomaticOptimizationStageResult(
                    stage,
                    AutomaticOptimizationStageDisposition.Completed,
                    $"{stage} complete",
                    $"before:{stage}",
                    $"after:{stage}"));
        var reboot = await new AutomaticOptimizationWorkflow().RunAsync(rebootRunner);
        Assert.IsFalse(reboot.Completed);
        Assert.AreEqual(AutomaticOptimizationStage.Msi, reboot.TerminalStage);
        Assert.AreEqual(3, rebootRunner.Calls.Count);
        Assert.IsFalse(rebootRunner.Calls.Contains(AutomaticOptimizationStage.PrimaryInputUsbXhci));

        var root = FindRepositoryRoot();
        var boundary = File.ReadAllText(Path.Combine(root, "src", "LatencyPilot.Service", "ServiceBoundary.cs"));
        StringAssert.Contains(boundary, "MutationAvailable = false");

        var restore = File.ReadAllText(Path.Combine(root, "src", "LatencyPilot.Service", "GlobalRestoreBaseline.cs"));
        StringAssert.Contains(restore, "MsiEnable");
        StringAssert.Contains(restore, "XhciInterruptAffinity");
        StringAssert.Contains(restore, "DeviceInterruptMutationTransaction");

        var recovery = File.ReadAllText(Path.Combine(root, "src", "LatencyPilot.Service", "DeviceInterruptRecoveryInspector.cs"));
        StringAssert.Contains(recovery, "Diverged");
        StringAssert.Contains(recovery, "ResumeAfterReboot");
        StringAssert.Contains(recovery, "ManualInterventionRequired");

        var harness = File.ReadAllText(Path.Combine(root, "tools", "LatencyPilot.PhysicalValidation", "Program.cs"));
        StringAssert.Contains(harness, "prepare-msi");
        StringAssert.Contains(harness, "prepare-xhci-affinity");
        StringAssert.Contains(harness, "resume-device-interrupt");
        StringAssert.Contains(harness, "recover-device-interrupt");
        StringAssert.Contains(harness, "restore-original-settings");
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LatencyPilot.slnx")))
            {
                return directory.FullName;
            }
        }
        throw new DirectoryNotFoundException();
    }

    private sealed class RecordingStageRunner(
        Func<AutomaticOptimizationStage, AutomaticOptimizationStageResult> resultFactory)
        : IAutomaticOptimizationStageRunner
    {
        internal List<AutomaticOptimizationStage> Calls { get; } = [];

        public ValueTask<AutomaticOptimizationStageResult> RunStageAsync(
            AutomaticOptimizationStage stage,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add(stage);
            return ValueTask.FromResult(resultFactory(stage));
        }
    }
}
''')


def apply_implementation() -> None:
    write("src/LatencyPilot.Service/AutomaticOptimizationWorkflow.cs", r'''namespace LatencyPilot.Service;

public enum AutomaticOptimizationStage
{
    OriginalMeasurement = 0,
    GpuAffinity = 1,
    Msi = 2,
    PrimaryInputUsbXhci = 3,
    FinalVerification = 4,
    Report = 5,
}

public enum AutomaticOptimizationStageDisposition
{
    Completed = 0,
    NotReady = 1,
    RebootPending = 2,
    Failed = 3,
}

public sealed record AutomaticOptimizationStageResult(
    AutomaticOptimizationStage Stage,
    AutomaticOptimizationStageDisposition Disposition,
    string Detail,
    string? BeforeEvidenceId,
    string? AfterEvidenceId);

public sealed record AutomaticOptimizationWorkflowResult(
    bool Completed,
    AutomaticOptimizationStage TerminalStage,
    IReadOnlyList<AutomaticOptimizationStageResult> Stages)
{
    public bool RebootPending =>
        Stages.LastOrDefault()?.Disposition == AutomaticOptimizationStageDisposition.RebootPending;
}

public interface IAutomaticOptimizationStageRunner
{
    ValueTask<AutomaticOptimizationStageResult> RunStageAsync(
        AutomaticOptimizationStage stage,
        CancellationToken cancellationToken);
}

/// <summary>
/// Internal sequencing contract for one-at-a-time optimization. It deliberately
/// does not arm public mutation; ServiceBoundary.MutationAvailable remains the
/// exact-revision physical-validation gate.
/// </summary>
public sealed class AutomaticOptimizationWorkflow
{
    private static readonly AutomaticOptimizationStage[] Stages =
    [
        AutomaticOptimizationStage.OriginalMeasurement,
        AutomaticOptimizationStage.GpuAffinity,
        AutomaticOptimizationStage.Msi,
        AutomaticOptimizationStage.PrimaryInputUsbXhci,
        AutomaticOptimizationStage.FinalVerification,
        AutomaticOptimizationStage.Report,
    ];

    public static IReadOnlyList<AutomaticOptimizationStage> OrderedStages => Stages;

    public async ValueTask<AutomaticOptimizationWorkflowResult> RunAsync(
        IAutomaticOptimizationStageRunner runner,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runner);
        var results = new List<AutomaticOptimizationStageResult>(Stages.Length);
        foreach (var stage in Stages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await runner.RunStageAsync(stage, cancellationToken).ConfigureAwait(false);
            if (result.Stage != stage)
            {
                throw new InvalidOperationException(
                    $"Optimize stage runner returned {result.Stage} while {stage} was requested.");
            }
            if (string.IsNullOrWhiteSpace(result.Detail))
            {
                throw new InvalidDataException($"Optimize stage {stage} returned no diagnostic detail.");
            }

            results.Add(result);
            if (result.Disposition != AutomaticOptimizationStageDisposition.Completed)
            {
                return new AutomaticOptimizationWorkflowResult(false, stage, results.AsReadOnly());
            }
        }

        return new AutomaticOptimizationWorkflowResult(
            true,
            AutomaticOptimizationStage.Report,
            results.AsReadOnly());
    }
}
''')

    write("src/LatencyPilot.Service/DeviceInterruptRecoveryInspector.cs", r'''using LatencyPilot.Persistence;
using LatencyPilot.Platform.Windows.Devices;

namespace LatencyPilot.Service;

internal enum DeviceInterruptRecoveryAction
{
    None = 0,
    AbortPreparedWithoutWrite = 1,
    ResumeAfterReboot = 2,
    RestoreOriginalState = 3,
    FinalizeOriginalState = 4,
    ManualInterventionRequired = 5,
}

internal sealed record DeviceInterruptRecoveryInspection(
    MutationJournalEntry Entry,
    MutationStoredStateRelation StoredStateRelation,
    bool TargetEnvironmentStable,
    DeviceInterruptRecoveryAction Action,
    string Reason);

internal static class DeviceInterruptRecoveryInspector
{
    internal static DeviceInterruptRecoveryInspection Inspect(MutationJournalEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (!DeviceInterruptMutationContract.IsSupportedKind(entry.Kind))
        {
            return Manual(entry, MutationStoredStateRelation.Unknown, false,
                $"Mutation kind '{entry.Kind}' is not a bounded MSI/xHCI transaction.");
        }

        try
        {
            var original = DeviceInterruptMutationJournalCodec.DeserializeOriginal(entry.OriginalStateJson);
            var candidate = DeviceInterruptMutationJournalCodec.DeserializeCandidate(entry.CandidateStateJson);
            if (!string.Equals(original.DeviceInstanceId, entry.TargetId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Device-interrupt journal target does not match its captured original snapshot.");
            }

            var current = DeviceInterruptConfigurationStore.Capture(entry.TargetId);
            var stable = string.Equals(current.DriverVersion, original.DriverVersion, StringComparison.OrdinalIgnoreCase);
            if (!stable)
            {
                return Manual(entry, MutationStoredStateRelation.Unknown, false,
                    "Target driver version changed after the original snapshot; automatic recovery will not restore stale policy.");
            }

            var originalMatches = DeviceInterruptConfigurationStore.MatchesOriginal(current, original, candidate.Operation);
            var candidateMatches = DeviceInterruptConfigurationStore.MatchesCandidate(current, original, candidate);
            var relation = (originalMatches, candidateMatches) switch
            {
                (true, true) => MutationStoredStateRelation.MatchesOriginalAndCandidate,
                (true, false) => MutationStoredStateRelation.MatchesOriginal,
                (false, true) => MutationStoredStateRelation.MatchesCandidate,
                _ => MutationStoredStateRelation.Diverged,
            };

            if (entry.IsTerminal)
            {
                return new DeviceInterruptRecoveryInspection(entry, relation, true, DeviceInterruptRecoveryAction.None,
                    "The mutation journal entry is already terminal.");
            }
            if (relation == MutationStoredStateRelation.Diverged)
            {
                return Manual(entry, relation, true,
                    "Actual interrupt configuration matches neither the captured original nor the experiment candidate; external or partial change is possible.");
            }
            if (entry.State == MutationJournalState.ApplyRebootPending ||
                entry.State == MutationJournalState.RollbackRebootPending)
            {
                return new DeviceInterruptRecoveryInspection(entry, relation, true,
                    DeviceInterruptRecoveryAction.ResumeAfterReboot,
                    "The journal is explicitly waiting for post-reboot state verification.");
            }
            if (entry.State == MutationJournalState.Prepared &&
                relation is MutationStoredStateRelation.MatchesOriginal or MutationStoredStateRelation.MatchesOriginalAndCandidate)
            {
                return new DeviceInterruptRecoveryInspection(entry, relation, true,
                    DeviceInterruptRecoveryAction.AbortPreparedWithoutWrite,
                    "Prepared experiment still matches the original state and can terminate without a machine write.");
            }
            if (relation == MutationStoredStateRelation.MatchesCandidate)
            {
                return new DeviceInterruptRecoveryInspection(entry, relation, true,
                    DeviceInterruptRecoveryAction.RestoreOriginalState,
                    "The experiment candidate is still stored; recovery is biased toward exact original-state rollback.");
            }
            if (relation is MutationStoredStateRelation.MatchesOriginal or MutationStoredStateRelation.MatchesOriginalAndCandidate)
            {
                return new DeviceInterruptRecoveryInspection(entry, relation, true,
                    DeviceInterruptRecoveryAction.FinalizeOriginalState,
                    "Stored state already matches the captured original; recovery may restart/verify and terminalize rollback without inventing a candidate write.");
            }

            return Manual(entry, relation, true, "No safe automatic recovery action was established.");
        }
        catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException or NotSupportedException or IOException or UnauthorizedAccessException)
        {
            return Manual(entry, MutationStoredStateRelation.Unknown, false,
                $"{exception.GetType().Name}: {exception.Message}");
        }
    }

    private static DeviceInterruptRecoveryInspection Manual(
        MutationJournalEntry entry,
        MutationStoredStateRelation relation,
        bool stable,
        string reason) =>
        new(entry, relation, stable, DeviceInterruptRecoveryAction.ManualInterventionRequired, reason);
}
''')

    store = "src/LatencyPilot.Platform.Windows/Devices/DeviceInterruptConfigurationStore.cs"
    replace_once(store,
'''    public static bool MatchesOriginal(DeviceInterruptConfigurationSnapshot current, DeviceInterruptConfigurationSnapshot original, DeviceInterruptMutationOperation operation)\n    {\n''',
'''    public static bool MatchesCandidate(\n        DeviceInterruptConfigurationSnapshot current,\n        DeviceInterruptConfigurationSnapshot original,\n        DeviceInterruptMutationCandidate candidate)\n    {\n        if (!string.Equals(current.DriverVersion, original.DriverVersion, StringComparison.OrdinalIgnoreCase) ||\n            !ValuesEqual(current.MessageNumberLimit, original.MessageNumberLimit))\n        {\n            return false;\n        }\n\n        return candidate.Operation == DeviceInterruptMutationOperation.EnableMsi\n            ? IsMsiEnabled(current)\n            : IsXhciAffinityStored(current, candidate.ToAffinityCandidate());\n    }\n\n    public static bool MatchesOriginal(DeviceInterruptConfigurationSnapshot current, DeviceInterruptConfigurationSnapshot original, DeviceInterruptMutationOperation operation)\n    {\n''')
    replace_once(store,
'''    public static void RestoreMsi(DeviceInterruptConfigurationSnapshot original)\n    {\n        using var tx = TransactionalRegistry.Begin("LatencyPilot MSI exact restore");\n''',
'''    public static void RestoreMsi(DeviceInterruptConfigurationSnapshot original)\n    {\n        var before = Capture(original.DeviceInstanceId);\n        if (!string.Equals(before.DriverVersion, original.DriverVersion, StringComparison.OrdinalIgnoreCase) ||\n            !ValuesEqual(before.MessageNumberLimit, original.MessageNumberLimit))\n        {\n            throw new InvalidOperationException("MSI restore refused because driver identity or MessageNumberLimit changed after the captured original state.");\n        }\n        using var tx = TransactionalRegistry.Begin("LatencyPilot MSI exact restore");\n''')
    replace_once(store,
'''    public static void RestoreXhciAffinity(DeviceInterruptConfigurationSnapshot original)\n    {\n        using var tx = TransactionalRegistry.Begin("LatencyPilot xHCI affinity exact restore");\n''',
'''    public static void RestoreXhciAffinity(DeviceInterruptConfigurationSnapshot original)\n    {\n        var before = Capture(original.DeviceInstanceId);\n        if (!string.Equals(before.DriverVersion, original.DriverVersion, StringComparison.OrdinalIgnoreCase))\n        {\n            throw new InvalidOperationException("xHCI affinity restore refused because driver identity changed after the captured original state.");\n        }\n        using var tx = TransactionalRegistry.Begin("LatencyPilot xHCI affinity exact restore");\n''')

    tx = "src/LatencyPilot.Service/DeviceInterruptMutationTransaction.cs"
    replace_once(tx,
'''internal static class DeviceInterruptMutationContract\n{\n    internal const string MsiKind = "device-msi-enable";\n    internal const string XhciAffinityKind = "xhci-interrupt-affinity";\n}\n''',
'''internal static class DeviceInterruptMutationContract\n{\n    internal const string MsiKind = "device-msi-enable";\n    internal const string XhciAffinityKind = "xhci-interrupt-affinity";\n\n    internal static bool IsSupportedKind(string kind) =>\n        string.Equals(kind, MsiKind, StringComparison.Ordinal) ||\n        string.Equals(kind, XhciAffinityKind, StringComparison.Ordinal);\n}\n''')
    replace_once(tx,
'''        var original = DeviceInterruptMutationJournalCodec.DeserializeOriginal(entry.OriginalStateJson);\n        var candidate = DeviceInterruptMutationJournalCodec.DeserializeCandidate(entry.CandidateStateJson);\n        if (entry.State is MutationJournalState.Reverted or MutationJournalState.AbortedBeforeApply) return new(entry, null, true);\n        var reverting = entry.State == MutationJournalState.Reverting ? entry :\n''',
'''        var original = DeviceInterruptMutationJournalCodec.DeserializeOriginal(entry.OriginalStateJson);\n        var candidate = DeviceInterruptMutationJournalCodec.DeserializeCandidate(entry.CandidateStateJson);\n        if (entry.State is MutationJournalState.Reverted or MutationJournalState.AbortedBeforeApply) return new(entry, null, true);\n\n        var currentBeforeRollback = DeviceInterruptConfigurationStore.Capture(original.DeviceInstanceId);\n        var matchesOriginal = DeviceInterruptConfigurationStore.MatchesOriginal(currentBeforeRollback, original, candidate.Operation);\n        var matchesCandidate = DeviceInterruptConfigurationStore.MatchesCandidate(currentBeforeRollback, original, candidate);\n        if (!matchesOriginal && !matchesCandidate)\n        {\n            throw new InvalidOperationException(\n                "Rollback refused because current interrupt configuration matches neither the captured original nor the LatencyPilot candidate.");\n        }\n        if (entry.State == MutationJournalState.Applying)\n        {\n            entry = journal.Transition(entry.ExperimentId, entry.Revision, entry.State, MutationJournalState.RecoveryRequired,\n                "Recovery entered while apply ownership was incomplete; actual state was re-read before rollback.");\n        }\n        var reverting = entry.State == MutationJournalState.Reverting ? entry :\n''')
    old_recover = '''    internal DeviceInterruptMutationStepResult Recover(Guid experimentId)\n    {\n        var entry = GetEntry(experimentId);\n        return entry.State switch\n        {\n            MutationJournalState.ApplyRebootPending or MutationJournalState.RollbackRebootPending => ResumeAfterReboot(experimentId),\n            MutationJournalState.Prepared or MutationJournalState.Applied or MutationJournalState.Measuring or MutationJournalState.AwaitingDecision or MutationJournalState.Kept or MutationJournalState.RecoveryRequired or MutationJournalState.Reverting => Rollback(experimentId),\n            MutationJournalState.Reverted or MutationJournalState.AbortedBeforeApply => new(entry, null, true),\n            _ => throw new InvalidOperationException($"Unsupported recovery state {entry.State}."),\n        };\n    }\n'''
    new_recover = '''    internal DeviceInterruptMutationStepResult Recover(Guid experimentId)\n    {\n        using var guard = MutationOperationLock.Acquire();\n        var entry = GetEntry(experimentId);\n        var inspection = DeviceInterruptRecoveryInspector.Inspect(entry);\n        return inspection.Action switch\n        {\n            DeviceInterruptRecoveryAction.None => new(entry, null, entry.State == MutationJournalState.Reverted),\n            DeviceInterruptRecoveryAction.AbortPreparedWithoutWrite => Rollback(experimentId),\n            DeviceInterruptRecoveryAction.ResumeAfterReboot => ResumeAfterReboot(experimentId),\n            DeviceInterruptRecoveryAction.RestoreOriginalState => Rollback(experimentId),\n            DeviceInterruptRecoveryAction.FinalizeOriginalState => Rollback(experimentId),\n            DeviceInterruptRecoveryAction.ManualInterventionRequired => throw new InvalidOperationException(\n                $"Automatic device-interrupt recovery refused: {inspection.Reason}"),\n            _ => throw new InvalidOperationException($"Unknown device-interrupt recovery action {inspection.Action}."),\n        };\n    }\n'''
    replace_once(tx, old_recover, new_recover)

    write("src/LatencyPilot.Service/GlobalRestoreBaseline.cs", r'''using LatencyPilot.Persistence;

namespace LatencyPilot.Service;

internal enum GlobalRestoreBaselineMutationKind
{
    GpuInterruptAffinity = 1,
    MsiEnable = 2,
    XhciInterruptAffinity = 3,
}

internal sealed record GlobalRestoreBaselineAction(
    Guid ExperimentId,
    GlobalRestoreBaselineMutationKind Kind,
    string TargetId,
    long ExpectedRevision);

internal sealed record GlobalRestoreBaselinePlan(IReadOnlyList<GlobalRestoreBaselineAction> Actions);
internal sealed record GlobalRestoreBaselineResult(IReadOnlyList<Guid> RestoredExperimentIds)
{
    internal int RestoredCount => RestoredExperimentIds.Count;
}

internal static class GlobalRestoreBaselinePlanner
{
    internal static GlobalRestoreBaselinePlan Create(IReadOnlyList<MutationJournalEntry> retainedChanges)
    {
        ArgumentNullException.ThrowIfNull(retainedChanges);
        var actions = new List<GlobalRestoreBaselineAction>(retainedChanges.Count);
        var seen = new HashSet<Guid>();
        DateTimeOffset? previous = null;
        foreach (var entry in retainedChanges)
        {
            if (entry.State != MutationJournalState.Kept)
                throw new InvalidOperationException($"Restore original settings accepts only Kept changes; {entry.ExperimentId:D} is {entry.State}.");
            if (!seen.Add(entry.ExperimentId))
                throw new ArgumentException($"Experiment {entry.ExperimentId:D} appears more than once.", nameof(retainedChanges));
            if (previous is { } p && entry.CreatedAtUtc > p)
                throw new ArgumentException("Retained changes must be supplied newest-first.", nameof(retainedChanges));
            previous = entry.CreatedAtUtc;
            var kind = entry.Kind switch
            {
                GpuInterruptAffinityMutationContract.Kind => GlobalRestoreBaselineMutationKind.GpuInterruptAffinity,
                DeviceInterruptMutationContract.MsiKind => GlobalRestoreBaselineMutationKind.MsiEnable,
                DeviceInterruptMutationContract.XhciAffinityKind => GlobalRestoreBaselineMutationKind.XhciInterruptAffinity,
                _ => throw new NotSupportedException($"Restore original settings does not know mutation kind '{entry.Kind}'. No changes were attempted."),
            };
            actions.Add(new GlobalRestoreBaselineAction(entry.ExperimentId, kind, entry.TargetId, entry.Revision));
        }
        return new GlobalRestoreBaselinePlan(actions.AsReadOnly());
    }
}

internal sealed class GlobalRestoreBaselineExecutor
{
    private readonly MutationJournal journal;
    private readonly GpuInterruptAffinityMutationTransaction gpuTransaction;
    private readonly DeviceInterruptMutationTransaction deviceTransaction;

    internal GlobalRestoreBaselineExecutor(MutationJournal journal)
    {
        this.journal = journal ?? throw new ArgumentNullException(nameof(journal));
        gpuTransaction = new GpuInterruptAffinityMutationTransaction(journal);
        deviceTransaction = new DeviceInterruptMutationTransaction(journal);
    }

    internal GlobalRestoreBaselineResult Restore()
    {
        using var operationLock = MutationOperationLock.Acquire();
        var unresolved = journal.GetUnresolved();
        if (unresolved.Count != 0)
            throw new InvalidOperationException("Restore original settings is blocked while any mutation experiment is unresolved. Recover or resume it first.");

        var plan = GlobalRestoreBaselinePlanner.Create(journal.GetRetainedChangesNewestFirst());
        var restored = new List<Guid>(plan.Actions.Count);
        foreach (var action in plan.Actions)
        {
            ValidateActionStillCurrent(action);
            switch (action.Kind)
            {
                case GlobalRestoreBaselineMutationKind.GpuInterruptAffinity:
                    RestoreGpu(action);
                    break;
                case GlobalRestoreBaselineMutationKind.MsiEnable:
                case GlobalRestoreBaselineMutationKind.XhciInterruptAffinity:
                    RestoreDeviceInterrupt(action);
                    break;
                default:
                    throw new NotSupportedException($"Restore action kind {action.Kind} is not supported.");
            }
            restored.Add(action.ExperimentId);
        }
        return new GlobalRestoreBaselineResult(restored.AsReadOnly());
    }

    private void ValidateActionStillCurrent(GlobalRestoreBaselineAction action)
    {
        var current = journal.TryGet(action.ExperimentId)
            ?? throw new InvalidOperationException($"Retained experiment {action.ExperimentId:D} disappeared before restore.");
        if (current.State != MutationJournalState.Kept || current.Revision != action.ExpectedRevision ||
            !string.Equals(current.TargetId, action.TargetId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Retained experiment {action.ExperimentId:D} changed after restore preflight; no write was attempted for this action.");
    }

    private void RestoreGpu(GlobalRestoreBaselineAction action)
    {
        var current = journal.TryGet(action.ExperimentId)!;
        if (!string.Equals(current.Kind, GpuInterruptAffinityMutationContract.Kind, StringComparison.Ordinal))
            throw new InvalidOperationException("Retained GPU action changed kind after preflight.");
        _ = journal.Transition(current.ExperimentId, current.Revision, MutationJournalState.Kept, MutationJournalState.Reverting);
        var rollback = gpuTransaction.RollbackAndActivate(current.ExperimentId);
        if (rollback.JournalEntry.State != MutationJournalState.Reverted || !rollback.OriginalStateRestored)
            throw new InvalidOperationException(rollback.JournalEntry.FailureReason ?? $"GPU experiment {current.ExperimentId:D} did not reach Reverted.");
    }

    private void RestoreDeviceInterrupt(GlobalRestoreBaselineAction action)
    {
        var rollback = deviceTransaction.Rollback(action.ExperimentId);
        if (rollback.JournalEntry.State == MutationJournalState.RollbackRebootPending)
            throw new InvalidOperationException($"Experiment {action.ExperimentId:D} restored stored policy but requires reboot verification before Restore original settings can continue.");
        if (rollback.JournalEntry.State != MutationJournalState.Reverted || !rollback.OriginalStateRestored)
            throw new InvalidOperationException(rollback.JournalEntry.FailureReason ?? $"Device-interrupt experiment {action.ExperimentId:D} did not reach Reverted.");
    }
}
''')

    program = "tools/LatencyPilot.PhysicalValidation/Program.cs"
    replace_once(program,
'''                "restore-original-settings" => RestoreOriginalSettings(args),\n                "--help" or "-h" or "help" => Help(args),\n''',
'''                "restore-original-settings" => RestoreOriginalSettings(args),\n                "prepare-msi" => PrepareMsi(args),\n                "prepare-xhci-affinity" => PrepareXhciAffinity(args),\n                "apply-device-interrupt" => ApplyDeviceInterrupt(args),\n                "resume-device-interrupt" => ResumeDeviceInterrupt(args),\n                "keep-device-interrupt" => KeepDeviceInterrupt(args),\n                "rollback-device-interrupt" => RollbackDeviceInterrupt(args),\n                "recover-device-interrupt" => RecoverDeviceInterrupt(args),\n                "--help" or "-h" or "help" => Help(args),\n''')
    methods = r'''
    private static int PrepareMsi(string[] args)
    {
        var options = ParseOptions(args, "--device");
        RequirePhysicalMutationAuthority(options);
        var result = new DeviceInterruptMutationTransaction(OpenJournal()).PrepareMsi(options.GetRequiredValue("--device"));
        Console.WriteLine(result.NoWriteRequired ? "MSI already enabled; no write required." : "Prepared bounded MSI experiment; no device write was attempted.");
        if (result.Entry is { } entry) PrintJournalEntry(entry);
        return 0;
    }

    private static int PrepareXhciAffinity(string[] args)
    {
        var options = ParseOptions(args, "--device", "--processor");
        RequirePhysicalMutationAuthority(options);
        if (!byte.TryParse(options.GetRequiredValue("--processor"), NumberStyles.None, CultureInfo.InvariantCulture, out var processorNumber) || processorNumber >= 64)
            throw new ArgumentException("--processor must identify a group-0 logical processor from 0 through 63.");
        var topology = ProcessorTopologyReader.Capture();
        var validated = GpuInterruptAffinityCandidate.Create(topology, new LogicalProcessorId(0, processorNumber));
        var candidate = new DeviceInterruptAffinityCandidate(validated.ProcessorGroup, validated.ProcessorNumber, validated.AffinityMask);
        var result = new DeviceInterruptMutationTransaction(OpenJournal()).PrepareXhciAffinity(options.GetRequiredValue("--device"), candidate);
        Console.WriteLine(result.NoWriteRequired ? "xHCI affinity already matches the candidate; no write required." : "Prepared bounded xHCI affinity experiment; no device write was attempted.");
        if (result.Entry is { } entry) PrintJournalEntry(entry);
        return 0;
    }

    private static int ApplyDeviceInterrupt(string[] args)
    {
        var options = ParseOptions(args, "--experiment");
        RequirePhysicalMutationAuthority(options);
        var result = new DeviceInterruptMutationTransaction(OpenJournal()).Apply(ParseExperimentId(options.GetRequiredValue("--experiment")));
        PrintDeviceMutationStep(result);
        return result.Entry.State == MutationJournalState.Applied ? 0 : 3;
    }

    private static int ResumeDeviceInterrupt(string[] args)
    {
        var options = ParseOptions(args, "--experiment");
        RequirePhysicalMutationAuthority(options);
        var result = new DeviceInterruptMutationTransaction(OpenJournal()).ResumeAfterReboot(ParseExperimentId(options.GetRequiredValue("--experiment")));
        PrintDeviceMutationStep(result);
        return result.Entry.State is MutationJournalState.Applied or MutationJournalState.Reverted ? 0 : 3;
    }

    private static int KeepDeviceInterrupt(string[] args)
    {
        var options = ParseOptions(args, "--experiment", "--measurement-verified");
        RequirePhysicalMutationAuthority(options);
        if (!bool.TryParse(options.GetRequiredValue("--measurement-verified"), out var verified) || !verified)
            throw new InvalidOperationException("keep-device-interrupt requires --measurement-verified true after scored before/after evidence has been reviewed.");
        var entry = new DeviceInterruptMutationTransaction(OpenJournal()).KeepVerified(
            ParseExperimentId(options.GetRequiredValue("--experiment")), verified);
        PrintJournalEntry(entry);
        return entry.State == MutationJournalState.Kept ? 0 : 3;
    }

    private static int RollbackDeviceInterrupt(string[] args)
    {
        var options = ParseOptions(args, "--experiment");
        RequirePhysicalMutationAuthority(options);
        var result = new DeviceInterruptMutationTransaction(OpenJournal()).Rollback(ParseExperimentId(options.GetRequiredValue("--experiment")));
        PrintDeviceMutationStep(result);
        return result.Entry.State == MutationJournalState.Reverted ? 0 : 3;
    }

    private static int RecoverDeviceInterrupt(string[] args)
    {
        var options = ParseOptions(args, "--experiment");
        RequirePhysicalMutationAuthority(options);
        var experimentId = ParseExperimentId(options.GetRequiredValue("--experiment"));
        var journal = OpenJournal();
        var entry = journal.TryGet(experimentId) ?? throw new InvalidOperationException($"Mutation journal entry {experimentId:D} was not found.");
        var inspection = DeviceInterruptRecoveryInspector.Inspect(entry);
        Console.WriteLine($"recovery-action={inspection.Action} relation={inspection.StoredStateRelation}");
        Console.WriteLine($"recovery-reason={inspection.Reason}");
        var result = new DeviceInterruptMutationTransaction(journal).Recover(experimentId);
        PrintDeviceMutationStep(result);
        return result.Entry.IsTerminal ? 0 : 3;
    }

    private static void PrintDeviceMutationStep(DeviceInterruptMutationStepResult result)
    {
        PrintJournalEntry(result.Entry);
        if (result.Restart is null)
        {
            Console.WriteLine("restart=not-attempted");
        }
        else
        {
            Console.WriteLine($"restart-in-place={result.Restart.RestartedInPlace}");
            Console.WriteLine($"system-restart-required={result.Restart.SystemRestartRequired}");
            Console.WriteLine($"device-started={result.Restart.DeviceStarted}");
            Console.WriteLine($"device-has-problem={result.Restart.DeviceHasProblem}");
        }
        Console.WriteLine($"original-state-restored={result.OriginalStateRestored}");
    }

'''
    replace_once(program,
'''    private static int RestoreOriginalSettings(string[] args)\n    {\n''',
methods + '''    private static int RestoreOriginalSettings(string[] args)\n    {\n''')
    replace_once(program,
'''        Console.WriteLine("  restore-original-settings --confirm-physical-mutation");\n''',
'''        Console.WriteLine("  restore-original-settings --confirm-physical-mutation");\n        Console.WriteLine("  prepare-msi --device <display-instance-id> --confirm-physical-mutation");\n        Console.WriteLine("  prepare-xhci-affinity --device <xhci-instance-id> --processor <group-0-cpu> --confirm-physical-mutation");\n        Console.WriteLine("  apply-device-interrupt --experiment <guid> --confirm-physical-mutation");\n        Console.WriteLine("  resume-device-interrupt --experiment <guid> --confirm-physical-mutation");\n        Console.WriteLine("  keep-device-interrupt --experiment <guid> --measurement-verified true --confirm-physical-mutation");\n        Console.WriteLine("  rollback-device-interrupt --experiment <guid> --confirm-physical-mutation");\n        Console.WriteLine("  recover-device-interrupt --experiment <guid> --confirm-physical-mutation");\n''')

    section = '''
## 2026-09-19 audit-closure semantics

LatencyPilot now treats automatic optimization as a one-at-a-time experiment pipeline: **Original measurement → GPU affinity → conservative MSI → primary-input/xHCI → final verification → report**. A stage that is NotReady, inconclusive, or requires reboot stops the pipeline; intent is never treated as activation proof.

MSI mutation is deliberately narrow: `MSISupported` may be enabled only when authoritative stored state makes the target applicable. `MessageNumberLimit` and interrupt priority are preserved/observed, not tuned automatically. xHCI affinity requires one explicit primary Raw Input identity, one exact USB route, clean capture evidence, and reversible journal ownership.

`Restore original settings` replays retained LatencyPilot changes newest-first from exact snapshots. It does **not** claim to restore Windows defaults. Public mutation remains fail-closed (`MutationAvailable = false`) until exact-revision physical GPU/MSI/xHCI validation is recorded; hosted CI proves source contracts only.
'''
    append_section("README.md", "## 2026-09-19 audit-closure semantics", section)
    append_section("SYSTEM_DESIGN.md", "## Audit-closure optimizer transaction model (2026-09-19)", section.replace("## 2026-09-19 audit-closure semantics", "## Audit-closure optimizer transaction model (2026-09-19)"))
    append_section("docs/BENCHMARK_METHODOLOGY.md", "## Audit-closure one-at-a-time workflow (2026-09-19)", section.replace("## 2026-09-19 audit-closure semantics", "## Audit-closure one-at-a-time workflow (2026-09-19)"))
    append_section("docs/adr/0004-automation-first-safe-optimizer.md", "## 2026-09-19 audit-closure addendum", section.replace("## 2026-09-19 audit-closure semantics", "## 2026-09-19 audit-closure addendum"))
    status_section = '''
## Audit closure status — 2026-09-19

Software/source closure is implemented for F1–F11 plus conservative MSI and reversible xHCI mutation substrate. Critical contracts cover owner-thread rendering, deterministic ranking, Original-vs-finalist decisions, serialized mutation/recovery, optional collector semantics, tri-state runtime placement, full eligible-core enumeration, primary-input USB identity, composite route correlation, capture-quality gating, driver-wide xHCI attribution, reboot-pending states, and exact rollback.

**Still not a physical-product completion claim:** public mutation remains disabled until the exact revision passes real Windows hardware validation across the required Intel/AMD and USB/xHCI scenarios. Hosted GitHub Actions cannot prove physical interrupt placement, reboot activation, or performance benefit.
'''
    append_section("PROJECT_STATUS.md", "## Audit closure status — 2026-09-19", status_section)
    roadmap_section = '''
## Audit closure milestone — 2026-09-19

- Source/CI: close F1–F11, MSI/xHCI reversible transactions, reboot resume, retained restore, and the internal Optimize sequence.
- Physical gate: run exact-revision Intel 16-LP, Intel >16-LP, AMD, primary-input/xHCI, MSI, reboot/resume, before/after, and restore validation before enabling public mutation.
- Out of scope for this milestone: NIC/RSS mutation, audio tuning, BIOS, HPET, broad power-plan tweaking, and arbitrary registry packs.
'''
    append_section("ROADMAP.md", "## Audit closure milestone — 2026-09-19", roadmap_section)

    for relative in ["README.md", "SYSTEM_DESIGN.md", "PROJECT_STATUS.md", "ROADMAP.md", "docs/BENCHMARK_METHODOLOGY.md"]:
        text = read(relative).replace("Restore Windows Defaults", "Restore original settings")
        write(relative, text)


def consolidate_tests_and_ci() -> None:
    tests_dir = ROOT / "tests/LatencyPilot.CriticalTests"
    data_methods = []
    replaced = 0
    for path in tests_dir.glob("*.cs"):
        if path.name == "ConsolidatedCriticalTests.cs":
            continue
        text = path.read_text(encoding="utf-8")
        if "[DataTestMethod]" in text or "[DataRow" in text:
            data_methods.append(path.name)
        count = text.count("[TestMethod]")
        if count:
            text = text.replace("[TestMethod]", "[AuditCase]")
            path.write_text(text, encoding="utf-8", newline="\n")
            replaced += count
    if data_methods:
        raise RuntimeError(f"Data-driven tests require explicit consolidation: {data_methods}")
    if replaced == 0:
        raise RuntimeError("No TestMethod attributes were found to consolidate.")

    write("tests/LatencyPilot.CriticalTests/ConsolidatedCriticalTests.cs", r'''using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LatencyPilot.CriticalTests;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
internal sealed class AuditCaseAttribute : Attribute;

[TestClass]
public sealed class ConsolidatedCriticalTests
{
    [TestMethod]
    public async Task RunAllCriticalContractsWithinPermanentTestBudget()
    {
        var failures = new List<string>();
        var cases = typeof(ConsolidatedCriticalTests).Assembly.GetTypes()
            .Where(static type => type.Namespace == "LatencyPilot.CriticalTests")
            .SelectMany(static type => type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(static method => method.GetCustomAttribute<AuditCaseAttribute>() is not null)
                .Select(method => (Type: type, Method: method)))
            .OrderBy(static item => item.Type.FullName, StringComparer.Ordinal)
            .ThenBy(static item => item.Method.Name, StringComparer.Ordinal)
            .ToArray();

        Assert.IsGreaterThan(0, cases.Length, "Consolidated test suite discovered no audit cases.");
        foreach (var item in cases)
        {
            try
            {
                if (item.Method.GetParameters().Length != 0)
                    throw new InvalidOperationException("AuditCase methods must be parameterless after consolidation.");
                var instance = item.Method.IsStatic ? null : Activator.CreateInstance(item.Type);
                var returned = item.Method.Invoke(instance, null);
                switch (returned)
                {
                    case Task task:
                        await task.ConfigureAwait(false);
                        break;
                    case ValueTask valueTask:
                        await valueTask.ConfigureAwait(false);
                        break;
                    case null:
                        break;
                    default:
                        throw new InvalidOperationException($"Unsupported audit-case return type {returned.GetType().FullName}.");
                }
            }
            catch (TargetInvocationException exception) when (exception.InnerException is not null)
            {
                failures.Add($"{item.Type.Name}.{item.Method.Name}: {exception.InnerException.GetType().Name}: {exception.InnerException.Message}");
            }
            catch (Exception exception)
            {
                failures.Add($"{item.Type.Name}.{item.Method.Name}: {exception.GetType().Name}: {exception.Message}");
            }
        }

        if (failures.Count > 0)
            Assert.Fail("Critical contract failures:\n" + string.Join("\n", failures));
    }
}
''')

    write(".github/workflows/ci.yml", r'''name: Tests

on:
  push:
    branches: [main]
  pull_request:
    branches: [main]
  workflow_dispatch:

permissions:
  contents: read

concurrency:
  group: tests-${{ github.ref }}
  cancel-in-progress: true

env:
  DOTNET_NOLOGO: true
  DOTNET_SKIP_FIRST_TIME_EXPERIENCE: true
  NUGET_XMLDOC_MODE: skip

jobs:
  critical-tests:
    runs-on: windows-latest
    timeout-minutes: 15
    steps:
      - name: Checkout
        uses: actions/checkout@d23441a48e516b6c34aea4fa41551a30e30af803 # v6
      - name: Setup .NET
        uses: actions/setup-dotnet@a98b56852c35b8e3190ac28c8c2271da59106c68 # v6
        with:
          dotnet-version: 10.0.401
      - name: Cache NuGet packages
        uses: actions/cache@55cc8345863c7cc4c66a329aec7e433d2d1c52a9 # v6.1.0
        with:
          path: ~/.nuget/packages
          key: ${{ runner.os }}-nuget-tests-${{ hashFiles('**/*.csproj', 'Directory.Build.props', 'Directory.Build.targets', 'Directory.Packages.props', 'global.json') }}
          restore-keys: |
            ${{ runner.os }}-nuget-tests-
            ${{ runner.os }}-nuget-
      - name: Restore
        run: dotnet restore LatencyPilot.slnx
      - name: Build solution
        run: dotnet build LatencyPilot.slnx --configuration Release --no-restore
      - name: Enforce permanent test budget
        shell: pwsh
        run: |
          $files = Get-ChildItem tests/LatencyPilot.CriticalTests -Filter *.cs -File
          $count = ($files | Select-String -Pattern '\[(TestMethod|DataTestMethod)\]').Count
          Write-Host "Permanent MSTest entrypoints: $count"
          if ($count -gt 10) { throw "Permanent test budget exceeded: $count > 10." }
      - name: Critical tests
        run: dotnet test tests/LatencyPilot.CriticalTests/LatencyPilot.CriticalTests.csproj --configuration Release --no-build
      - name: Diff whitespace check
        shell: pwsh
        run: git diff --check HEAD^
''')


if __name__ == "__main__":
    if len(sys.argv) != 2 or sys.argv[1] not in {"tests", "implementation", "consolidate"}:
        raise SystemExit("usage: audit-closure-task6-final.py tests|implementation|consolidate")
    if sys.argv[1] == "tests":
        apply_tests()
    elif sys.argv[1] == "implementation":
        apply_implementation()
    else:
        consolidate_tests_and_ci()
