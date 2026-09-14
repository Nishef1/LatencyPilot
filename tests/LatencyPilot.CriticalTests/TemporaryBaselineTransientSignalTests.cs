using LatencyPilot.Persistence;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LatencyPilot.CriticalTests;

[TestClass]
public sealed class TemporaryReadOnlyJournalLookupTests
{
    [TestMethod]
    public void ReadOnlyInspectorCanRetrieveExactExperiment()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"LatencyPilot-readonly-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "latencypilot.db");
        var experimentId = Guid.NewGuid();

        try
        {
            var journal = new MutationJournal(databasePath);
            journal.Initialize();
            _ = journal.CreatePrepared(
                experimentId,
                "gpu-interrupt-affinity",
                "PCI\\VEN_TEST&DEV_TEST",
                "{\"snapshot\":true}",
                "{\"candidate\":true}");

            File.SetAttributes(databasePath, File.GetAttributes(databasePath) | FileAttributes.ReadOnly);

            var entry = MutationJournalReadOnlyInspector.TryGet(databasePath, experimentId);
            Assert.IsNotNull(entry);
            Assert.AreEqual(experimentId, entry.ExperimentId);
            Assert.AreEqual(MutationJournalState.Prepared, entry.State);
            Assert.AreEqual("PCI\\VEN_TEST&DEV_TEST", entry.TargetId);
            Assert.IsNull(MutationJournalReadOnlyInspector.TryGet(databasePath, Guid.NewGuid()));
        }
        finally
        {
            if (File.Exists(databasePath))
            {
                File.SetAttributes(databasePath, FileAttributes.Normal);
            }

            Directory.Delete(directory, recursive: true);
        }
    }
}
