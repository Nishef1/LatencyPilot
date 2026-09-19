using LatencyPilot.Benchmarking.Candidates;
using LatencyPilot.Persistence;
using LatencyPilot.Platform.Windows.Devices;

namespace LatencyPilot.Service;

/// <summary>
/// Owns the journaled GPU interrupt-affinity mutation lifecycle used by Gate A.
/// Benchmark collection and ranking intentionally live elsewhere.
/// </summary>
internal sealed class GpuAffinityMutationBackend
{
    private readonly MutationJournal journal;
    private readonly GpuInterruptAffinityMutationTransaction transaction;

    internal GpuAffinityMutationBackend(MutationJournal journal)
    {
        this.journal = journal ?? throw new ArgumentNullException(nameof(journal));
        transaction = new GpuInterruptAffinityMutationTransaction(journal);
    }

    internal static GpuInterruptAffinitySnapshot CaptureOriginal(string deviceInstanceId) =>
        GpuInterruptAffinityPolicyStore.Capture(deviceInstanceId);

    internal Guid ApplyCandidate(string deviceInstanceId, GpuAffinityCandidate candidate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceInstanceId);
        ArgumentNullException.ThrowIfNull(candidate);
        var processor = candidate.Processor;
        if (processor.Group != 0 || processor.Number >= 64)
        {
            throw new NotSupportedException(
                "GPU optimization v1 supports only a group-0 x64 affinity candidate.");
        }

        var mutationCandidate = new GpuInterruptAffinityCandidate(
            processor.Group,
            processor.Number,
            1UL << processor.Number);
        var prepared = transaction.Prepare(deviceInstanceId, mutationCandidate);
        var applied = transaction.ApplyAndActivate(prepared.ExperimentId);
        if (applied.JournalEntry.State != MutationJournalState.Applied)
        {
            throw new InvalidOperationException(
                applied.JournalEntry.FailureReason ??
                "GPU affinity candidate did not reach the verified Applied state.");
        }

        return prepared.ExperimentId;
    }

    internal void BeginMeasurement(Guid experimentId)
    {
        using var operationLock = MutationOperationLock.Acquire();
        var entry = GetRequiredEntry(experimentId, MutationJournalState.Applied);
        _ = journal.Transition(
            entry.ExperimentId,
            entry.Revision,
            MutationJournalState.Applied,
            MutationJournalState.Measuring);
    }

    internal void AwaitDecision(Guid experimentId)
    {
        using var operationLock = MutationOperationLock.Acquire();
        var entry = GetRequiredEntry(experimentId, MutationJournalState.Measuring);
        _ = journal.Transition(
            entry.ExperimentId,
            entry.Revision,
            MutationJournalState.Measuring,
            MutationJournalState.AwaitingDecision);
    }

    internal void KeepCandidate(Guid experimentId)
    {
        using var operationLock = MutationOperationLock.Acquire();
        var entry = GetRequiredEntry(experimentId, MutationJournalState.AwaitingDecision);
        var original = GpuInterruptAffinityJournalCodec.DeserializeOriginal(entry.OriginalStateJson);
        var candidate = GpuInterruptAffinityJournalCodec.DeserializeCandidate(entry.CandidateStateJson);
        var current = GpuInterruptAffinityPolicyStore.Capture(original.DeviceInstanceId);
        var driverMatches = string.Equals(
            current.DriverVersion,
            original.DriverVersion,
            StringComparison.OrdinalIgnoreCase);
        if (!driverMatches || !GpuInterruptAffinityStateComparer.MatchesCandidate(current, candidate))
        {
            const string reason =
                "GPU affinity candidate changed before Keep could be finalized; recovery is required.";
            _ = journal.Transition(
                entry.ExperimentId,
                entry.Revision,
                MutationJournalState.AwaitingDecision,
                MutationJournalState.RecoveryRequired,
                reason);
            throw new InvalidOperationException(reason);
        }

        _ = journal.Transition(
            entry.ExperimentId,
            entry.Revision,
            MutationJournalState.AwaitingDecision,
            MutationJournalState.Kept);
    }

    internal void Rollback(Guid experimentId)
    {
        var rollback = transaction.RollbackAndActivate(experimentId);
        if (rollback.JournalEntry.State != MutationJournalState.Reverted ||
            !rollback.OriginalStateRestored)
        {
            throw new InvalidOperationException(
                rollback.JournalEntry.FailureReason ??
                "GPU affinity rollback did not reach a verified exact-original state.");
        }
    }

    private MutationJournalEntry GetRequiredEntry(
        Guid experimentId,
        MutationJournalState requiredState)
    {
        var entry = journal.TryGet(experimentId)
            ?? throw new InvalidOperationException($"GPU affinity experiment {experimentId:D} was not found.");
        if (!string.Equals(entry.Kind, GpuInterruptAffinityMutationContract.Kind, StringComparison.Ordinal) ||
            entry.State != requiredState)
        {
            throw new InvalidOperationException(
                $"GPU affinity experiment {experimentId:D} must be {requiredState}; actual state is {entry.State}.");
        }

        return entry;
    }
}
