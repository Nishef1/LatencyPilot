using LatencyPilot.Service;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LatencyPilot.CriticalTests;

[TestClass]
public sealed class GateAValidationWorkflowTests
{
    [TestMethod]
    public void OneClickGateACompletionRequiresEverySafetyProof()
    {
        var complete = new GateAValidationFacts(
            ExactRevision: true,
            BaselineEligible: true,
            InitialJournalClean: true,
            CandidatePrepared: true,
            PreparedStateSurvivedRestart: true,
            ApplySucceeded: true,
            RuntimePlacementVerified: true,
            RollbackVerified: true,
            RecoveryExerciseVerified: true,
            FinalUnresolvedExperimentCount: 0);

        Assert.IsTrue(GateAValidationCompletion.Evaluate(complete).Passed);

        var rejected = new[]
        {
            complete with { ExactRevision = false },
            complete with { BaselineEligible = false },
            complete with { InitialJournalClean = false },
            complete with { CandidatePrepared = false },
            complete with { PreparedStateSurvivedRestart = false },
            complete with { ApplySucceeded = false },
            complete with { RuntimePlacementVerified = false },
            complete with { RollbackVerified = false },
            complete with { RecoveryExerciseVerified = false },
            complete with { FinalUnresolvedExperimentCount = 1 },
        };

        foreach (var facts in rejected)
        {
            var result = GateAValidationCompletion.Evaluate(facts);
            Assert.IsFalse(result.Passed);
            Assert.IsFalse(string.IsNullOrWhiteSpace(result.Reason));
        }
    }
}
