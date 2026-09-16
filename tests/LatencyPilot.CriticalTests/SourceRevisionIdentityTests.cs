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

        var premiumOverviewPath = Path.Combine(
            repositoryRoot,
            "src",
            "LatencyPilot.App",
            "PremiumOverviewExperience.cs");
        Assert.IsTrue(
            File.Exists(premiumOverviewPath),
            "The approved premium Overview must be implemented as a maintained experience instead of a one-off mockup.");
        var premiumOverviewSource = File.ReadAllText(premiumOverviewPath);
        StringAssert.Contains(premiumOverviewSource, "LatencyProfileChart");
        StringAssert.Contains(premiumOverviewSource, "CpuDistributionChart");
        StringAssert.Contains(premiumOverviewSource, "ModuleContributionChart");
        StringAssert.Contains(premiumOverviewSource, "CpuInterruptMap");
        StringAssert.Contains(premiumOverviewSource, "SemanticGoodBrush");
        StringAssert.Contains(premiumOverviewSource, "SemanticAttentionBrush");
        StringAssert.Contains(premiumOverviewSource, "SemanticFailureBrush");
        StringAssert.Contains(premiumOverviewSource, "BrandActionBrush");

        var premiumTokensPath = Path.Combine(
            repositoryRoot,
            "src",
            "LatencyPilot.App",
            "Design",
            "PremiumOverviewTokens.xaml");
        Assert.IsTrue(File.Exists(premiumTokensPath), "Premium Overview semantic color resources must be centralized.");
        var premiumTokensSource = File.ReadAllText(premiumTokensPath);
        StringAssert.Contains(premiumTokensSource, "BrandActionBrush");
        StringAssert.Contains(premiumTokensSource, "SemanticGoodBrush");
        StringAssert.Contains(premiumTokensSource, "SemanticAttentionBrush");
        StringAssert.Contains(premiumTokensSource, "SemanticFailureBrush");
        StringAssert.Contains(premiumTokensSource, "DpcCategoryBrush");
        StringAssert.Contains(premiumTokensSource, "IsrCategoryBrush");

        var gateASource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "LatencyPilot.App",
            "GateAValidationExperience.cs"));
        StringAssert.Contains(gateASource, "Run GPU Gate A");
        StringAssert.Contains(gateASource, "IsDevelopmentGateAAvailable");
        StringAssert.Contains(gateASource, "BuildGateATerminalSummary");
        StringAssert.Contains(gateASource, "GPU Gate A failed safely");
        StringAssert.Contains(gateASource, "GPU Gate A stopped safely");
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

        var gateARunnerSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "tools",
            "LatencyPilot.GateAValidation",
            "GpuAutoAffinityGateARunner.cs"));
        var originalSnapshotIndex = gateARunnerSource.IndexOf(
            "preMutationOriginalState = GpuInterruptAffinityPolicyStore.Capture",
            StringComparison.Ordinal);
        var benchmarkConnectIndex = gateARunnerSource.IndexOf(
            "GpuBenchmarkControlClient.ConnectAsync",
            StringComparison.Ordinal);
        Assert.IsTrue(
            originalSnapshotIndex >= 0 && benchmarkConnectIndex > originalSnapshotIndex,
            "Gate A must capture exact original GPU affinity before benchmark-control startup can fail.");
        StringAssert.Contains(gateARunnerSource, "TryStopBenchmarkAsync");
        StringAssert.Contains(gateARunnerSource, "benchmark.StopAsync(deadline.Token)");
        StringAssert.Contains(gateARunnerSource, "safe = IsVerifiedOriginalTerminalState(stoppedReport);");
        StringAssert.Contains(gateARunnerSource, "safe = IsVerifiedOriginalTerminalState(fallback);");

        var cancellationCatchStart = gateARunnerSource.IndexOf(
            "catch (OperationCanceledException)",
            StringComparison.Ordinal);
        var failureCatchStart = gateARunnerSource.IndexOf(
            "catch (Exception exception)",
            cancellationCatchStart,
            StringComparison.Ordinal);
        Assert.IsTrue(
            cancellationCatchStart >= 0 && failureCatchStart > cancellationCatchStart,
            "Gate A cancellation terminal path could not be located.");
        var cancellationCatchSource = gateARunnerSource[cancellationCatchStart..failureCatchStart];
        StringAssert.Contains(
            cancellationCatchSource,
            "await TryWriteTerminalReportAsync(options.OutputPath, stoppedReport)");
        StringAssert.Contains(
            cancellationCatchSource,
            "await TryReportTerminalAsync(");

        var gateABackendSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "tools",
            "LatencyPilot.GateAValidation",
            "GpuAutoAffinityGateABackend.cs"));
        var keepStartIndex = gateABackendSource.IndexOf(
            "public Task KeepAsync",
            StringComparison.Ordinal);
        var keepEndIndex = gateABackendSource.IndexOf(
            "public Task<bool> VerifyOriginalStateAsync",
            keepStartIndex,
            StringComparison.Ordinal);
        Assert.IsTrue(
            keepStartIndex >= 0 && keepEndIndex > keepStartIndex,
            "Gate A Keep lifecycle source could not be located.");
        var keepSource = gateABackendSource[keepStartIndex..keepEndIndex];
        var keepVerificationIndex = keepSource.IndexOf(
            "GpuInterruptAffinityPolicyStore.Capture",
            StringComparison.Ordinal);
        var keepCommitIndex = keepSource.IndexOf(
            "mutation.KeepCandidate(experimentId)",
            StringComparison.Ordinal);
        Assert.IsTrue(
            keepVerificationIndex >= 0 && keepCommitIndex > keepVerificationIndex,
            "Gate A must perform throwing stored-state verification before the journal terminalizes Keep.");
        Assert.IsFalse(
            keepSource[(keepCommitIndex + "mutation.KeepCandidate(experimentId)".Length)..]
                .Contains("GpuInterruptAffinityPolicyStore.Capture", StringComparison.Ordinal),
            "Gate A must not perform throwing external state reads after Keep becomes terminal.");
        StringAssert.Contains(keepSource, "pre-keep verification failed");

        var applyCandidateIndex = gateABackendSource.IndexOf(
            "public Task<Guid> ApplyCandidateAsync",
            StringComparison.Ordinal);
        var postApplyRollbackIndex = gateABackendSource.IndexOf(
            "RollbackCandidateAfterPostApplyFailure",
            StringComparison.Ordinal);
        Assert.IsTrue(
            applyCandidateIndex >= 0 && postApplyRollbackIndex > applyCandidateIndex,
            "Gate A must retain rollback ownership when post-apply verification fails before the session receives the experiment id.");
        StringAssert.Contains(gateABackendSource, "post-apply verification failed");

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
