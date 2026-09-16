using LatencyPilot.Protocol;
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

        Assert.AreEqual(
            false,
            typeof(ServiceBoundary).GetField(nameof(ServiceBoundary.MutationAvailable))?.GetRawConstantValue());
        Assert.AreEqual(
            6,
            typeof(ProtocolVersion).GetField(nameof(ProtocolVersion.Current))?.GetRawConstantValue());

        var repositoryRoot = FindRepositoryRoot();
        var gateASource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "LatencyPilot.App",
            "GateAValidationExperience.cs"));
        StringAssert.Contains(gateASource, "Run GPU Gate A");
        StringAssert.Contains(gateASource, "IsDevelopmentGateAAvailable");
        Assert.IsFalse(gateASource.Contains("Validate GPU · one click", StringComparison.Ordinal));
        Assert.IsFalse(gateASource.Contains("Auto-optimize GPU", StringComparison.Ordinal));

        var progressWindowSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "LatencyPilot.App",
            "GpuOptimizationProgressWindow.xaml.cs"));
        StringAssert.Contains(progressWindowSource, "terminalSnapshotReceived");
        StringAssert.Contains(progressWindowSource, "Failed safely · original state verified");
        StringAssert.Contains(progressWindowSource, "Stopped safely · original state verified");

        var gateAProgressSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "tools",
            "LatencyPilot.GateAValidation",
            "GpuGateAProgressFile.cs"));
        StringAssert.Contains(gateAProgressSource, "\"failed-safely\"");
        StringAssert.Contains(gateAProgressSource, "\"stopped-safely\"");

        var benchmarkControlClientSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "tools",
            "LatencyPilot.GateAValidation",
            "GpuBenchmarkControlClient.cs"));
        var connectAwaitIndex = benchmarkControlClientSource.IndexOf(
            "await pipe.ConnectAsync",
            StringComparison.Ordinal);
        var clientConstructionIndex = benchmarkControlClientSource.IndexOf(
            "new GpuBenchmarkControlClient",
            StringComparison.Ordinal);
        Assert.IsTrue(
            connectAwaitIndex >= 0 && clientConstructionIndex > connectAwaitIndex,
            "GPU benchmark control streams must not be created before the named pipe is connected.");

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

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LatencyPilot.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("LatencyPilot repository root could not be resolved from the test output directory.");
    }
}
