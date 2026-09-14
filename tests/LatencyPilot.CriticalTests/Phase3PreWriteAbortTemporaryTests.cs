using LatencyPilot.Persistence;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LatencyPilot.CriticalTests;

[TestClass]
public sealed class Phase3PreWriteAbortTemporaryTests
{
    [TestMethod]
    public void ApplyingCanTerminalizeAsAbortedBeforeOwnedWrite()
    {
        Assert.IsTrue(MutationJournalStateMachine.CanTransition(
            MutationJournalState.Applying,
            MutationJournalState.AbortedBeforeApply));
    }
}
