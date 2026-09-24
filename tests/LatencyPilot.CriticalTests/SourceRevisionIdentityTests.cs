#pragma warning disable CA1822 // AuditCase methods are reflection-invoked by ConsolidatedCriticalTests.
using LatencyPilot.Protocol;
using LatencyPilot.Service;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LatencyPilot.CriticalTests;

[TestClass]
public sealed class SourceRevisionIdentityTests
{
    [AuditCase]
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
        StringAssert.Contains(
            progressWindowSource,
            "Elapsed {FormatDuration(snapshot.ElapsedMilliseconds)} · finished",
            "A terminal Gate A snapshot must not continue presenting an estimated remaining time.");

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

        var rendererOwnerPath = Path.Combine(
            repositoryRoot,
            "src",
            "LatencyPilot.GpuBenchmark",
            "BenchmarkRendererOwner.cs");
        Assert.IsTrue(
            File.Exists(rendererOwnerPath),
            "F1 requires one dedicated renderer owner thread rather than touching HWND/D3D12 state from pipe continuations.");
        var rendererOwnerSource = File.ReadAllText(rendererOwnerPath);
        StringAssert.Contains(rendererOwnerSource, "new Thread(ThreadMain)");
        StringAssert.Contains(rendererOwnerSource, "PumpMessages");
        StringAssert.Contains(rendererOwnerSource, "RecreateRendererAsync");
        var recreateRendererIndex = rendererOwnerSource.IndexOf(
            "internal Task RecreateRendererAsync",
            StringComparison.Ordinal);
        var disposeOldRendererIndex = rendererOwnerSource.IndexOf(
            "active?.Dispose();",
            recreateRendererIndex,
            StringComparison.Ordinal);
        var createReplacementRendererIndex = rendererOwnerSource.IndexOf(
            "renderer = rendererFactory();",
            recreateRendererIndex,
            StringComparison.Ordinal);
        Assert.IsTrue(
            recreateRendererIndex >= 0 &&
            disposeOldRendererIndex > recreateRendererIndex &&
            createReplacementRendererIndex > disposeOldRendererIndex,
            "D3D12 recovery must release the removed device/resources before creating the replacement renderer.");

        var benchmarkWindowSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "LatencyPilot.GpuBenchmark",
            "BenchmarkWindow.cs"));
        StringAssert.Contains(benchmarkWindowSource, "ownerThreadId");
        StringAssert.Contains(benchmarkWindowSource, "EnsureOwnerThread();");
        StringAssert.Contains(benchmarkWindowSource, "DestroyWindow failed on the benchmark-window owner thread");

        var benchmarkProgramSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "LatencyPilot.GpuBenchmark",
            "Program.cs"));
        StringAssert.Contains(benchmarkProgramSource, "BenchmarkRendererOwner.CreateAsync");

        var benchmarkOwnerControlServerSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "LatencyPilot.GpuBenchmark",
            "BenchmarkControlServer.cs"));
        StringAssert.Contains(benchmarkOwnerControlServerSource, "BenchmarkRendererOwner rendererOwner");
        Assert.IsFalse(
            benchmarkOwnerControlServerSource.Contains("D3D12BenchmarkRenderer renderer", StringComparison.Ordinal),
            "The async pipe server must not directly own or dispose the renderer/window.");

        var gpuMutationBackendSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "LatencyPilot.Service",
            "GpuAffinityMutationBackend.cs"));
        StringAssert.Contains(
            gpuMutationBackendSource,
            "transaction.Prepare(deviceInstanceId, mutationCandidate, expectedOriginal)",
            "The GPU mutation backend must carry the exact session-original snapshot into transaction preparation.");

        var gpuMutationTransactionSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "LatencyPilot.Service",
            "GpuInterruptAffinityMutationTransaction.cs"));
        StringAssert.Contains(
            gpuMutationTransactionSource,
            "MatchesOriginal(original, expectedOriginal)",
            "Mutation preparation must reject TOCTOU drift from the session-original state before journaling/writing.");

        var mutationLockPath = Path.Combine(
            repositoryRoot,
            "src",
            "LatencyPilot.Service",
            "MutationOperationLock.cs");
        Assert.IsTrue(
            File.Exists(mutationLockPath),
            "F3 requires one shared crash-released named mutex for mutation/recovery/restore entry points.");
        var mutationLockSource = File.ReadAllText(mutationLockPath);
        StringAssert.Contains(mutationLockSource, "Global\\LatencyPilot.MutationOperation.v1");
        StringAssert.Contains(mutationLockSource, "AbandonedMutexException");

        var gpuMutationSource = File.ReadAllText(Path.Combine(
            repositoryRoot, "src", "LatencyPilot.Service", "GpuInterruptAffinityMutationTransaction.cs"));
        Assert.IsTrue(
            CountOccurrences(gpuMutationSource, "MutationOperationLock.Acquire()") >= 3,
            "Prepare/apply/rollback must all participate in the shared mutation lock.");

        var recoveryExecutorSource = File.ReadAllText(Path.Combine(
            repositoryRoot, "src", "LatencyPilot.Service", "MutationRecoveryExecutor.cs"));
        StringAssert.Contains(recoveryExecutorSource, "MutationOperationLock.Acquire()");

        var restoreSource = File.ReadAllText(Path.Combine(
            repositoryRoot, "src", "LatencyPilot.Service", "GlobalRestoreBaseline.cs"));
        StringAssert.Contains(restoreSource, "MutationOperationLock.Acquire()");

        var physicalValidationSource = File.ReadAllText(Path.Combine(
            repositoryRoot, "tools", "LatencyPilot.PhysicalValidation", "Program.cs"));
        StringAssert.Contains(physicalValidationSource, "\"restore-original-settings\" => RestoreOriginalSettings(args)");
        StringAssert.Contains(physicalValidationSource, "Restore original settings completed");

        var collectorBackendSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "tools",
            "LatencyPilot.GateAValidation",
            "GpuAutoAffinityGateABackend.cs"));
        StringAssert.Contains(collectorBackendSource, "TryStartOptionalPresentMonAsync");
        StringAssert.Contains(collectorBackendSource, "CollectorTailSlack");
        StringAssert.Contains(collectorBackendSource, "PresentMon startup unavailable");
        Assert.IsFalse(
            collectorBackendSource.Contains("request.Duration + TimeSpan.FromSeconds(8)", StringComparison.Ordinal),
            "Scored ETW capture must not add an unconditional eight-second tail to every benchmark block.");
        StringAssert.Contains(collectorBackendSource, "GPU ISR attribution is unavailable for this trial");

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
        StringAssert.Contains(
            benchmarkControlClientSource,
            "IsRecoverableRendererFailure",
            "A transient renderer/window/device-loss after GPU affinity change must have one bounded recovery path.");
        StringAssert.Contains(
            benchmarkControlClientSource,
            "0x887A0005",
            "DXGI_ERROR_DEVICE_REMOVED must be recognized as one bounded renderer-recreation retry.");
        StringAssert.Contains(
            benchmarkControlClientSource,
            "0x887A0007",
            "DXGI_ERROR_DEVICE_RESET must be recognized as one bounded renderer-recreation retry.");
        StringAssert.Contains(
            benchmarkControlClientSource,
            "0x887A0020",
            "DXGI_ERROR_DRIVER_INTERNAL_ERROR must be recognized as one bounded renderer-recreation retry.");
        StringAssert.Contains(
            benchmarkControlClientSource,
            "await RecreateRendererAsync(cancellationToken).ConfigureAwait(false);",
            "The benchmark client must recreate the renderer before retrying a recoverable renderer trial.");

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
        StringAssert.Contains(
            gateASessionSource,
            "MinimumFinalistPairs = 2",
            "Adaptive v4 finalist confirmation must collect two independent local pairs before it can stop.");
        StringAssert.Contains(
            gateASessionSource,
            "MaximumFinalistPairs = 3",
            "Adaptive v4 may spend one additional pair only while the top two remain inside measured uncertainty.");
        StringAssert.Contains(
            gateASessionSource,
            "for (var round = 0; round < MaximumFinalistPairs; round++)",
            "Finalist confirmation must remain explicitly bounded.");
        StringAssert.Contains(
            gateASessionSource,
            "FinalistsNeedMoreEvidence(",
            "The third finalist pair must be conditional on measured top-two uncertainty.");
        StringAssert.Contains(
            gateASessionSource,
            "ShuffleDeterministically(",
            "Finalist round order must be deterministically shuffled to reduce time/thermal ordering bias.");
        StringAssert.Contains(
            gateASessionSource,
            "CandidateMetricEquivalenceTolerance",
            "GPU ranking must use an explicit practical-equivalence margin instead of false precision.");
        StringAssert.Contains(
            gateASessionSource,
            "SelectFinalists(",
            "GPU adaptive paired screening must reduce the search to bounded finalists instead of exhaustively re-running every logical processor.");
        StringAssert.Contains(
            gateASessionSource,
            "MeasureScreeningPairAsync(",
            "GPU v4 ranking must be derived from measured local Original-Candidate-Original pairs.");

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

        var benchmarkControlServerSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "LatencyPilot.GpuBenchmark",
            "BenchmarkControlServer.cs"));
        StringAssert.Contains(
            benchmarkControlServerSource,
            "exception is not OperationCanceledException",
            "A renderer/window cancellation must be considered separately from an authenticated session cancellation.");
        StringAssert.Contains(
            benchmarkControlServerSource,
            "!cancellationToken.IsCancellationRequested",
            "A renderer/window cancellation that is not the authenticated session cancellation must be returned as a failed control response instead of closing the pipe without a response.");

        var gateAExperienceSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "LatencyPilot.App",
            "GateAValidationExperience.cs"));
        StringAssert.Contains(
            gateAExperienceSource,
            "var benchmarkStandardOutput",
            "Gate A must retain benchmark stdout so a child-process failure is diagnosable from the App run.");
        StringAssert.Contains(
            gateAExperienceSource,
            "benchmarkStandardError",
            "Gate A must retain benchmark stderr so a child-process failure is diagnosable from the App run.");
        StringAssert.Contains(
            gateAExperienceSource,
            "WaitForGateAHelperAsync",
            "The App must not wait indefinitely for an elevated wrapper after a terminal Gate A report is already available.");
        StringAssert.Contains(
            gateAExperienceSource,
            "TryReadTerminalProgressAsync",
            "The App must use the terminal progress snapshot as an independent completion signal for Gate A.");

        StringAssert.Contains(gateASessionSource, "\"final-verification-warmup\"");
        StringAssert.Contains(gateASessionSource, "\"final-verification\"");
        StringAssert.Contains(
            gateASessionSource,
            "\"screening-original-control\"",
            "Paired v2 must capture a fresh scored Original control after qualification before candidate comparison.");
        StringAssert.Contains(
            gateASessionSource,
            "\"finalist-original-control\"",
            "Paired v2 must begin finalist confirmation from a fresh long-window Original control.");
        Assert.IsFalse(
            gateASessionSource.Contains("NormalizeScreeningEvaluations", StringComparison.Ordinal) ||
            gateASessionSource.Contains("ControlPoint.Interpolate", StringComparison.Ordinal),
            "Paired v2 must not regress to temporal interpolation or pseudo-normalized decision FPS.");
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
            "ApplyCandidateRefusedExternalDrift",
            "Gate A must refuse an affinity write when stored policy drifted outside the active session.");
        StringAssert.Contains(
            gateABackendSource,
            "MatchesOriginal(currentBefore, originalState)",
            "Gate A must compare current stored policy to the exact session-original snapshot before candidate apply.");
        StringAssert.Contains(
            gateABackendSource,
            "await benchmark.RecreateRendererAsync(cancellationToken).ConfigureAwait(false);",
            "The D3D12 renderer must be recreated after GPU affinity activation so warm-up and score can share the warmed renderer.");
        StringAssert.Contains(
            gateABackendSource,
            "RecreateBenchmarkRendererAfterRollback",
            "Rollback activation must recreate the benchmark renderer before the next Original/candidate block.");

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