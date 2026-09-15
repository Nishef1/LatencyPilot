using LatencyPilot.Service;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LatencyPilot.CriticalTests;

[TestClass]
public sealed class SourceRevisionIdentityTests
{
    [TestMethod]
    public void SourceRevisionAndGateACompletionMustFailClosed()
    {
        const string expected = "4f06d190ee8c0262c2e73799063bc93a7d9caf71";

        Assert.IsTrue(SourceRevisionIdentity.MatchesExpectedCommit(
            "0.0.2+4f06d190ee8c0262c2e73799063bc93a7d9caf71",
            expected));
        Assert.IsTrue(SourceRevisionIdentity.MatchesExpectedCommit(
            "0.0.2+build.4f06d190ee8c0262c2e73799063bc93a7d9caf71",
            expected));

        Assert.IsFalse(SourceRevisionIdentity.MatchesExpectedCommit("0.0.2", expected));
        Assert.IsFalse(SourceRevisionIdentity.MatchesExpectedCommit(
            "0.0.2+4f06d190ee8c0262c2e73799063bc93a7d9caf70",
            expected));
        Assert.IsFalse(SourceRevisionIdentity.MatchesExpectedCommit(
            "0.0.2+prefix4f06d190ee8c0262c2e73799063bc93a7d9caf71suffix",
            expected));
        Assert.IsFalse(SourceRevisionIdentity.MatchesExpectedCommit(null, expected));
        Assert.ThrowsExactly<ArgumentException>(() =>
            SourceRevisionIdentity.MatchesExpectedCommit("0.0.2+abc", "abc"));

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
