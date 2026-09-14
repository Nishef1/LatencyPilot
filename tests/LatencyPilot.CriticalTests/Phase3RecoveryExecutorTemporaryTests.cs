using System.Reflection;
using LatencyPilot.Persistence;
using LatencyPilot.Service;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LatencyPilot.CriticalTests;

[TestClass]
public sealed class Phase3RecoveryExecutorTemporaryTests
{
    [TestMethod]
    public void RecoveryExecutorRefusesUnsupportedMutationWithoutChangingJournal()
    {
        var tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "LatencyPilot.RecoveryExecutorTemporaryTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
            var journal = new MutationJournal(Path.Combine(tempDirectory, "journal.db"));
            journal.Initialize();
            var prepared = journal.CreatePrepared(
                Guid.NewGuid(),
                "unsupported-test-kind",
                "unsupported-target",
                "{}",
                "{}");

            var serviceAssembly = typeof(ServiceBoundary).Assembly;
            var executorType = serviceAssembly.GetType("LatencyPilot.Service.MutationRecoveryExecutor");
            Assert.IsNotNull(executorType, "The rollback-biased recovery executor has not been implemented yet.");

            var constructor = executorType.GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                [typeof(MutationJournal)],
                modifiers: null);
            Assert.IsNotNull(constructor);

            var executor = constructor.Invoke([journal]);
            var execute = executorType.GetMethod(
                "Execute",
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                [typeof(Guid)],
                modifiers: null);
            Assert.IsNotNull(execute);

            var invocation = Assert.ThrowsExactly<TargetInvocationException>(() =>
                execute.Invoke(executor, [prepared.ExperimentId]));
            Assert.IsInstanceOfType<InvalidOperationException>(invocation.InnerException);

            var unchanged = journal.TryGet(prepared.ExperimentId);
            Assert.IsNotNull(unchanged);
            Assert.AreEqual(MutationJournalState.Prepared, unchanged.State);
            Assert.AreEqual(prepared.Revision, unchanged.Revision);
            Assert.IsTrue(journal.HasUnresolved());
        }
        finally
        {
            try
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
            catch (IOException)
            {
                // Temporary SQLite handles may close asynchronously on some runners.
            }
        }
    }
}
