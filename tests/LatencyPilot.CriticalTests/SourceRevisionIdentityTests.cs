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

        var startupHardeningSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "LatencyPilot.App",
            "ObservationExperienceHardening.cs"));
        StringAssert.Contains(
            startupHardeningSource,
            "InitializeGateAValidationExperience();",
            "The development Gate A surface must be initialized from the post-XAML startup seam or its card remains collapsed and its button is never created.");

        var appSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "LatencyPilot.App",
            "App.xaml.cs"));
        Assert.AreEqual(
            1,
            CountOccurrences(startupHardeningSource, "InitializeGateAValidationExperience();") +
                CountOccurrences(appSource, "InitializeGateAValidationExperience();"),
            "The Gate A surface must be initialized exactly once; duplicate startup initialization creates duplicate controls and handlers.");

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
        StringAssert.Contains(gateASource, "Directory.Exists(Path.Combine(directory.FullName, \".git\"))");
        StringAssert.Contains(gateASource, "File.Exists(Path.Combine(directory.FullName, \".git\"))");
        StringAssert.Contains(gateASource, "if (_gateAValidationButton is not null)");
        StringAssert.Contains(gateASource, "private bool _gateAValidationRunning;");
        StringAssert.Contains(gateASource, "SetGateAValidationBusy(true);");
        StringAssert.Contains(gateASource, "SetGateAValidationBusy(false);");
        StringAssert.Contains(gateASource, "_measurementBusy || _gateAValidationRunning");
        StringAssert.Contains(gateASource, "Content = \"Run GPU Gate A\"");
        StringAssert.Contains(gateASource, "DeveloperValidationCard.Visibility = Visibility.Visible;");
        StringAssert.Contains(gateASource, "DeveloperValidationHost.Children.Add(_gateAValidationButton);");
        Assert.IsFalse(gateASource.Contains("Validate GPU · one click", StringComparison.Ordinal));
        Assert.IsFalse(gateASource.Contains("Auto-optimize GPU", StringComparison.Ordinal));

        var measurementSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "LatencyPilot.App",
            "MeasurementExperience.cs"));
        Assert.IsTrue(
            CountOccurrences(measurementSource, "if (_measurementBusy || _gateAValidationRunning)") >= 2,
            "Quick observation and repeated baseline must both reject entry while Gate A is running.");
        StringAssert.Contains(measurementSource, "IsEnabled = !_measurementBusy && !_gateAValidationRunning");

        var readinessSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "LatencyPilot.App",
            "MeasurementReadinessExperience.cs"));
        StringAssert.Contains(readinessSource, "!_measurementBusy && !_gateAValidationRunning");

        var mainWindowSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "LatencyPilot.App",
            "MainWindow.xaml.cs"));
        StringAssert.Contains(mainWindowSource, "var controlsBusy = busy || _gateAValidationRunning;");
        StringAssert.Contains(mainWindowSource, "SetObservationControlsBusy(true);");
        StringAssert.Contains(mainWindowSource, "SetObservationControlsBusy(false);");
        StringAssert.Contains(mainWindowSource, "SetObservationControlsBusy(_measurementBusy);");

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
        StringAssert.Contains(gateAProgressSource, "\"screening-warmup\"");

        var autoAffinitySessionSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "LatencyPilot.Benchmarking",
            "Optimization",
            "GpuAutoAffinitySession.cs"));
        StringAssert.Contains(
            autoAffinitySessionSource,
            "\"screening-warmup\"",
            "The startup warm-up must have a distinct phase so it cannot silently become a decision control.");
        StringAssert.Contains(
            autoAffinitySessionSource,
            "resolved single-adapter ISR placement",
            "Gate A diagnostics must not call WDDM dxgkrnl fallback evidence direct GPU-driver ISR evidence.");

        var placementVerifierSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "LatencyPilot.Platform.Windows",
            "Devices",
            "GpuInterruptRuntimePlacementVerifier.cs"));
        StringAssert.Contains(placementVerifierSource, "WddmGraphicsKernelModule");
        StringAssert.Contains(placementVerifierSource, "wddm-graphics-kernel-dispatch");
        StringAssert.Contains(placementVerifierSource, "exactly one present display adapter");

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
        StringAssert.Contains(gateARunnerSource, "var stage = \"argument parsing\";");
        StringAssert.Contains(
            gateARunnerSource,
            "Gate A failed during {stage}: {exception}",
            "Gate A failure reports must preserve the execution stage and full exception details for native/runtime diagnosis.");
        StringAssert.Contains(
            benchmarkControlClientSource,
            "Preserve the handshake/connection failure.",
            "Benchmark pipe cleanup must not replace the original connection or handshake failure.");

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

        var finallyStart = gateARunnerSource.IndexOf("finally", failureCatchStart, StringComparison.Ordinal);
        Assert.IsTrue(
            finallyStart > failureCatchStart,
            "Gate A failure terminal path could not be located.");
        var failureCatchSource = gateARunnerSource[failureCatchStart..finallyStart];
        StringAssert.Contains(
            failureCatchSource,
            "await TryWriteTerminalReportAsync(options.OutputPath, fallback)");
        StringAssert.Contains(
            failureCatchSource,
            "await TryReportTerminalAsync(");

        var gateABackendSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "tools",
            "LatencyPilot.GateAValidation",
            "GpuAutoAffinityGateABackend.cs"));
        StringAssert.Contains(
            gateABackendSource,
            "var isWarmup = request.Phase.EndsWith(\"-warmup\", StringComparison.Ordinal);",
            "Warm-up must remain explicitly distinguishable from scored/final evidence.");
        StringAssert.Contains(
            gateABackendSource,
            "var attributionAttempted = !isWarmup && kernel.IsValid && storedBefore && storedAfter;",
            "Non-scored warm-up must not pretend to carry runtime ISR placement evidence.");

        var gateASessionSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "LatencyPilot.Benchmarking",
            "Optimization",
            "GpuAutoAffinitySession.cs"));
        StringAssert.Contains(gateASessionSource, "repetitions: 1");
        StringAssert.Contains(
            gateASessionSource,
            "for (var round = 0; round < 2; round++)",
            "Top finalists must be re-tested in two independent transition rounds rather than two back-to-back scores under one affinity activation.");
        StringAssert.Contains(
            gateASessionSource,
            "ShuffleDeterministically(roundCandidates, roundSeed);",
            "Finalist round order must be deterministically shuffled to reduce time/thermal ordering bias.");
        StringAssert.Contains(
            gateASessionSource,
            "CandidateMetricEquivalenceTolerance",
            "GPU ranking must use an explicit practical-equivalence margin instead of false precision.");
        StringAssert.Contains(
            gateASessionSource,
            "CreateAdaptiveShortlist",
            "GPU screening must re-test candidates inside the primary-noise margin of the third-place cutoff.");

        var rendererSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "LatencyPilot.GpuBenchmark",
            "D3D12BenchmarkRenderer.cs"));
        StringAssert.Contains(rendererSource, "private const int BufferCount = 3;");
        StringAssert.Contains(rendererSource, "private const int FrameContextCount = 2;");
        StringAssert.Contains(rendererSource, "DrainFrames");
        var renderFrameStart = rendererSource.IndexOf(
            "internal BenchmarkFrameTelemetry? RenderFrame",
            StringComparison.Ordinal);
        var captureChecksumsStart = rendererSource.IndexOf(
            "internal IReadOnlyList<ulong> CaptureWorkerChecksums",
            renderFrameStart,
            StringComparison.Ordinal);
        Assert.IsTrue(renderFrameStart >= 0 && captureChecksumsStart > renderFrameStart);
        Assert.IsFalse(
            rendererSource[renderFrameStart..captureChecksumsStart]
                .Contains("WaitForGpu();", StringComparison.Ordinal),
            "The benchmark must not fully drain the GPU after every submitted frame.");

        StringAssert.Contains(gateASessionSource, "\"final-verification-warmup\"");
        StringAssert.Contains(gateASessionSource, "\"final-verification\"");
        Assert.IsFalse(
            gateASessionSource.Contains("screening-control", StringComparison.Ordinal) ||
            gateASessionSource.Contains("smt-refinement", StringComparison.Ordinal) ||
            gateASessionSource.Contains("ABBA", StringComparison.Ordinal) ||
            gateASessionSource.Contains("BAAB", StringComparison.Ordinal),
            "The simplified v1 GPU session must not regress to legacy control/SMT/ABBA paths.");
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
            "public async Task<Guid> ApplyCandidateAsync",
            StringComparison.Ordinal);
        StringAssert.Contains(
            gateABackendSource,
            "await benchmark.RecreateRendererAsync(cancellationToken).ConfigureAwait(false);",
            "The D3D12 renderer must be recreated exactly after GPU affinity activation so warm-up and score can share the warmed renderer.");

        var postApplyRollbackIndex = gateABackendSource.IndexOf(
            "RollbackCandidateAfterPostApplyFailure",
            StringComparison.Ordinal);
        Assert.IsTrue(
            applyCandidateIndex >= 0 && postApplyRollbackIndex > applyCandidateIndex,
            "Gate A must retain rollback ownership when post-apply verification fails before the session receives the experiment id.");
        StringAssert.Contains(gateABackendSource, "post-apply verification failed");

        var gateAHarnessSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "tools",
            "LatencyPilot.GateAValidation",
            "Program.cs"));
        var restartStartIndex = gateAHarnessSource.IndexOf(
            "private static async Task RestartServiceAsync",
            StringComparison.Ordinal);
        var restartEndIndex = gateAHarnessSource.IndexOf(
            "private static async Task<GateAStepReport> RunStepAsync",
            restartStartIndex,
            StringComparison.Ordinal);
        Assert.IsTrue(restartStartIndex >= 0 && restartEndIndex > restartStartIndex);
        var restartSource = gateAHarnessSource[restartStartIndex..restartEndIndex];
        StringAssert.Contains(restartSource, "ServiceControllerStatus.StartPending");
        StringAssert.Contains(restartSource, "ServiceControllerStatus.StopPending");
        StringAssert.Contains(restartSource, "ServiceControllerStatus.PausePending");
        StringAssert.Contains(restartSource, "ServiceControllerStatus.ContinuePending");
        Assert.IsFalse(
            restartSource.Contains("string workingDirectory", StringComparison.Ordinal),
            "ServiceController restart no longer launches a child process and must not retain a dead working-directory parameter.");

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

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        for (var index = 0; (index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0; index += value.Length)
        {
            count++;
        }

        return count;
    }
}
