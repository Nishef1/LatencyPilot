using LatencyPilot.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LatencyPilot.CriticalTests;

[TestClass]
public sealed class TemporaryReadOnlyJournalTests
{
    [TestMethod]
    public void ReadOnlyInspectorReadsExistingJournalWithoutWriteAccess()
    {
        var tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "LatencyPilot.CriticalTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        var databasePath = Path.Combine(tempDirectory, "journal.db");

        try
        {
            var writer = new MutationJournal(databasePath);
            writer.Initialize();
            var experimentId = Guid.NewGuid();
            writer.CreatePrepared(
                experimentId,
                "gpu-interrupt-affinity",
                "PCI\\VEN_TEST&DEV_TEST",
                "{}",
                "{}");

            SqliteConnection.ClearAllPools();
            var originalAttributes = File.GetAttributes(databasePath);
            File.SetAttributes(databasePath, originalAttributes | FileAttributes.ReadOnly);

            try
            {
                var unresolved = MutationJournalReadOnlyInspector.GetUnresolved(databasePath);
                Assert.HasCount(1, unresolved);
                Assert.AreEqual(experimentId, unresolved[0].ExperimentId);
                Assert.AreEqual(MutationJournalState.Prepared, unresolved[0].State);
            }
            finally
            {
                File.SetAttributes(databasePath, originalAttributes);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
            catch
            {
                // Best-effort cleanup for temporary SQLite files on Windows runners.
            }
        }
    }
}
