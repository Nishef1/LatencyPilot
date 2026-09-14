using System.ComponentModel;
using System.Security;
using System.Text.Json;
using LatencyPilot.Persistence;
using LatencyPilot.Platform.Windows.Devices;

namespace LatencyPilot.Service;

internal enum MutationStoredStateRelation
{
    Unknown = 0,
    MatchesOriginal = 1,
    MatchesCandidate = 2,
    MatchesOriginalAndCandidate = 3,
    Diverged = 4,
}

internal sealed record MutationRecoveryInspection(
    MutationJournalEntry Entry,
    bool ActualStateRead,
    bool TargetEnvironmentStable,
    MutationStoredStateRelation StoredStateRelation,
    MutationRecoveryPlan RecoveryPlan,
    GpuInterruptAffinitySnapshot? GpuInterruptAffinity,
    string? Error);

internal static class MutationRecoveryAssessment
{
    internal static MutationRecoveryInspection Inspect(MutationJournalEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (!string.Equals(entry.Kind, GpuInterruptAffinityMutationContract.Kind, StringComparison.Ordinal))
        {
            const string reason = "mutation kind is not supported by recovery assessment";
            var unsupportedPlan = MutationRecoveryPlanner.Create(
                entry,
                MutationStoredStateRelation.Unknown,
                targetEnvironmentStable: false);
            return new MutationRecoveryInspection(
                entry,
                false,
                false,
                MutationStoredStateRelation.Unknown,
                unsupportedPlan,
                null,
                reason);
        }

        try
        {
            var original = GpuInterruptAffinityJournalCodec.DeserializeOriginal(entry.OriginalStateJson);
            var candidate = GpuInterruptAffinityJournalCodec.DeserializeCandidate(entry.CandidateStateJson);
            if (!string.Equals(
                    original.DeviceInstanceId,
                    entry.TargetId,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "GPU affinity journal target does not match the original-state device instance ID.");
            }

            var actual = GpuInterruptAffinityPolicyStore.Capture(entry.TargetId);
            var matchesOriginal = GpuInterruptAffinityStateComparer.MatchesOriginal(actual, original);
            var matchesCandidate = GpuInterruptAffinityStateComparer.MatchesCandidate(actual, candidate);
            var relation = (matchesOriginal, matchesCandidate) switch
            {
                (true, true) => MutationStoredStateRelation.MatchesOriginalAndCandidate,
                (true, false) => MutationStoredStateRelation.MatchesOriginal,
                (false, true) => MutationStoredStateRelation.MatchesCandidate,
                _ => MutationStoredStateRelation.Diverged,
            };
            var targetEnvironmentStable = string.Equals(
                actual.DriverVersion,
                original.DriverVersion,
                StringComparison.OrdinalIgnoreCase);
            var plan = MutationRecoveryPlanner.Create(entry, relation, targetEnvironmentStable);

            return new MutationRecoveryInspection(
                entry,
                true,
                targetEnvironmentStable,
                relation,
                plan,
                actual,
                null);
        }
        catch (Exception exception) when (IsRecoverableActualStateFailure(exception))
        {
            var reason = $"{exception.GetType().Name}: {exception.Message}";
            var plan = MutationRecoveryPlanner.Create(
                entry,
                MutationStoredStateRelation.Unknown,
                targetEnvironmentStable: false);
            return new MutationRecoveryInspection(
                entry,
                false,
                false,
                MutationStoredStateRelation.Unknown,
                plan,
                null,
                reason);
        }
    }

    private static bool IsRecoverableActualStateFailure(Exception exception) =>
        exception is Win32Exception or
        IOException or
        UnauthorizedAccessException or
        SecurityException or
        InvalidDataException or
        InvalidOperationException or
        NotSupportedException or
        JsonException;
}
