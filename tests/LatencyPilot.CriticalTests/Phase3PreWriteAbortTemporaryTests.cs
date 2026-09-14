using LatencyPilot.Persistence;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LatencyPilot.CriticalTests;

[TestClass]
public sealed class Phase3PreWriteAbortTemporaryTests
{
    [TestMethod]
    public void Applying_can_terminalize_as_aborted_before_owned_write()
    {
        Assert.IsTrue(MutationJournalStateMachine.CanTransition(
            MutationJournalState.Applying,
            MutationJournalState.AbortedBeforeApply));
    }
}
