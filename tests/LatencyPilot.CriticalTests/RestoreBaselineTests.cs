#pragma warning disable CA1822 // AuditCase methods are reflection-invoked by ConsolidatedCriticalTests.
using LatencyPilot.Persistence;
using LatencyPilot.Service;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LatencyPilot.CriticalTests;

[TestClass]
public sealed class RestoreBaselineTests
{
    [AuditCase]
    public void KeptChangesRestoreNewestFirstAndUnknownKindsFailClosed()
    {
        Assert.IsTrue(MutationJournalStateMachine.CanTransition(
            MutationJournalState.Kept,
            MutationJournalState.Reverting));

        var tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "LatencyPilot.CriticalTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
            var journal = new MutationJournal(Path.Combine(tempDirectory, "journal.db"));
            journal.Initialize();

            var first = CreateKept(
                journal,
                "gpu-interrupt-affinity",
                "PCI\\VEN_TEST&DEV_FIRST",
                DateTimeOffset.UnixEpoch);
            var second = CreateKept(
                journal,
                "gpu-interrupt-affinity",
                "PCI\\VEN_TEST&DEV_SECOND",
                DateTimeOffset.UnixEpoch.AddMinutes(1));

            var retained = journal.GetRetainedChangesNewestFirst();
            CollectionAssert.AreEqual(
                new[] { second.ExperimentId, first.ExperimentId },
                retained.Select(static entry => entry.ExperimentId).ToArray());

            var plan = GlobalRestoreBaselinePlanner.Create(retained);
            CollectionAssert.AreEqual(
                new[] { second.ExperimentId, first.ExperimentId },
                plan.Actions.Select(static action => action.ExperimentId).ToArray());
            Assert.IsTrue(plan.Actions.All(static action =>
                action.Kind == GlobalRestoreBaselineMutationKind.GpuInterruptAffinity));

            var unknown = retained[0] with { Kind = "unknown-mutation-kind" };
            Assert.ThrowsExactly<NotSupportedException>(() =>
                GlobalRestoreBaselinePlanner.Create([unknown, retained[1]]));
        }
        finally
        {
            try
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
            catch
            {
                // SQLite pooling can briefly retain file handles on some runners.
            }
        }
    }

    private static MutationJournalEntry CreateKept(
        MutationJournal journal,
        string kind,
        string targetId,
        DateTimeOffset createdAtUtc)
    {
        var prepared = journal.CreatePrepared(
            Guid.NewGuid(),
            kind,
            targetId,
            "{}",
            "{}",
            createdAtUtc);
        var applying = journal.Transition(
            prepared.ExperimentId,
            prepared.Revision,
            MutationJournalState.Prepared,
            MutationJournalState.Applying,
            nowUtc: createdAtUtc.AddSeconds(1));
        var applied = journal.Transition(
            prepared.ExperimentId,
            applying.Revision,
            MutationJournalState.Applying,
            MutationJournalState.Applied,
            nowUtc: createdAtUtc.AddSeconds(2));
        var measuring = journal.Transition(
            prepared.ExperimentId,
            applied.Revision,
            MutationJournalState.Applied,
            MutationJournalState.Measuring,
            nowUtc: createdAtUtc.AddSeconds(3));
        var decision = journal.Transition(
            prepared.ExperimentId,
            measuring.Revision,
            MutationJournalState.Measuring,
            MutationJournalState.AwaitingDecision,
            nowUtc: createdAtUtc.AddSeconds(4));
        return journal.Transition(
            prepared.ExperimentId,
            decision.Revision,
            MutationJournalState.AwaitingDecision,
            MutationJournalState.Kept,
            nowUtc: createdAtUtc.AddSeconds(5));
    }
}
