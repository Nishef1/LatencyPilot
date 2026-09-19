using LatencyPilot.Persistence;
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
