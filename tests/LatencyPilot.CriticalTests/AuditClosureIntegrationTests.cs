#pragma warning disable CA1822 // AuditCase methods are reflection-invoked by ConsolidatedCriticalTests.
using System.IO.Compression;
using LatencyPilot.Benchmarking.Optimization;
using LatencyPilot.Core.Benchmarking;
using LatencyPilot.Core.System;
using LatencyPilot.Service;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LatencyPilot.CriticalTests;

[TestClass]
public sealed class AuditClosureIntegrationTests
{
    [AuditCase]
    public async Task OptimizeWorkflowRecoveryRestoreAndPublicArmingStayFailClosed()
    {
        var expected = new[]
        {
            AutomaticOptimizationStage.OriginalMeasurement,
            AutomaticOptimizationStage.GpuAffinity,
            AutomaticOptimizationStage.PrimaryInputUsbXhci,
            AutomaticOptimizationStage.FinalVerification,
            AutomaticOptimizationStage.Report,
        };
        CollectionAssert.AreEqual(expected, AutomaticOptimizationWorkflow.OrderedStages.ToArray());

        var successRunner = new RecordingStageRunner(static stage =>
            new AutomaticOptimizationStageResult(
                stage,
                AutomaticOptimizationStageDisposition.Completed,
                $"{stage} complete",
                $"before:{stage}",
                $"after:{stage}"));
        var success = await AutomaticOptimizationWorkflow.RunAsync(successRunner);
        Assert.IsTrue(success.Completed);
        CollectionAssert.AreEqual(expected, successRunner.Calls.ToArray());
        Assert.AreEqual(AutomaticOptimizationStage.Report, success.TerminalStage);

        var rebootRunner = new RecordingStageRunner(static stage =>
            stage == AutomaticOptimizationStage.PrimaryInputUsbXhci
                ? new AutomaticOptimizationStageResult(
                    stage,
                    AutomaticOptimizationStageDisposition.RebootPending,
                    "Windows restart required",
                    "before:xhci",
                    "stored:xhci")
                : new AutomaticOptimizationStageResult(
                    stage,
                    AutomaticOptimizationStageDisposition.Completed,
                    $"{stage} complete",
                    $"before:{stage}",
                    $"after:{stage}"));
        var reboot = await AutomaticOptimizationWorkflow.RunAsync(rebootRunner);
        Assert.IsFalse(reboot.Completed);
        Assert.AreEqual(AutomaticOptimizationStage.PrimaryInputUsbXhci, reboot.TerminalStage);
        Assert.AreEqual(3, rebootRunner.Calls.Count);
        Assert.IsFalse(rebootRunner.Calls.Contains(AutomaticOptimizationStage.FinalVerification));

        const string revision = "4f06d190ee8c0262c2e73799063bc93a7d9caf71";
        var assessSource = typeof(GpuOptimizationSourceRevisionPolicy).GetMethod(
            "Assess",
            [typeof(string), typeof(string), typeof(bool), typeof(string)]);
        Assert.IsNotNull(assessSource, "Gate A source classification must be centralized instead of duplicating clean/dirty checks in App and helper.");

        var evidenceReady = assessSource.Invoke(null, [revision, "main", false, revision]);
        var developmentOnly = assessSource.Invoke(null, [revision, "main", true, revision]);
        var wrongBranch = assessSource.Invoke(null, [revision, "dev", false, revision]);
        var mismatch = assessSource.Invoke(null, [revision, "main", false, "5f06d190ee8c0262c2e73799063bc93a7d9caf71"]);
        Assert.AreEqual("EvidenceReady", ReadProperty(evidenceReady, "State")?.ToString());
        Assert.AreEqual(true, ReadProperty(evidenceReady, "CanRun"));
        Assert.AreEqual(true, ReadProperty(evidenceReady, "IsClosureEligible"));
        Assert.AreEqual("DevelopmentOnly", ReadProperty(developmentOnly, "State")?.ToString());
        Assert.AreEqual(true, ReadProperty(developmentOnly, "CanRun"));
        Assert.AreEqual(false, ReadProperty(developmentOnly, "IsClosureEligible"));
        Assert.AreEqual("Blocked", ReadProperty(wrongBranch, "State")?.ToString());
        Assert.AreEqual(false, ReadProperty(wrongBranch, "CanRun"));
        Assert.AreEqual("Blocked", ReadProperty(mismatch, "State")?.ToString());

        var root = FindRepositoryRoot();
        var boundary = File.ReadAllText(Path.Combine(root, "src", "LatencyPilot.Service", "ServiceBoundary.cs"));
        StringAssert.Contains(boundary, "MutationAvailable = false");

        var restore = File.ReadAllText(Path.Combine(root, "src", "LatencyPilot.Service", "GlobalRestoreBaseline.cs"));
        StringAssert.Contains(restore, "MsiEnable");
        StringAssert.Contains(restore, "XhciInterruptAffinity");
        StringAssert.Contains(restore, "DeviceInterruptMutationTransaction");

        var recovery = File.ReadAllText(Path.Combine(root, "src", "LatencyPilot.Service", "DeviceInterruptRecoveryInspector.cs"));
        StringAssert.Contains(recovery, "Diverged");
        StringAssert.Contains(recovery, "ResumeAfterReboot");
        StringAssert.Contains(recovery, "ManualInterventionRequired");

        var harness = File.ReadAllText(Path.Combine(root, "tools", "LatencyPilot.PhysicalValidation", "Program.cs"));
        StringAssert.Contains(harness, "prepare-msi");
        StringAssert.Contains(harness, "prepare-xhci-affinity");
        StringAssert.Contains(harness, "resume-device-interrupt");
        StringAssert.Contains(harness, "recover-device-interrupt");
        StringAssert.Contains(harness, "restore-original-settings");

        var gateAUi = File.ReadAllText(Path.Combine(root, "src", "LatencyPilot.App", "GateAValidationExperience.cs"));
        StringAssert.Contains(gateAUi, "Run development validation");
        StringAssert.Contains(gateAUi, "--allow-dirty-development-source");
        StringAssert.Contains(gateAUi, "GateAClosureEligible");
        Assert.IsFalse(gateAUi.Contains("ReadCleanSourceRevisionAsync", StringComparison.Ordinal));

        var gateARunner = File.ReadAllText(Path.Combine(root, "tools", "LatencyPilot.GateAValidation", "GpuAutoAffinityGateARunner.cs"));
        StringAssert.Contains(gateARunner, "AllowDirtyDevelopmentSource");
        StringAssert.Contains(gateARunner, "GateAClosureEligible");
        StringAssert.Contains(gateARunner, "DevelopmentOnly");

        var reportContract = File.ReadAllText(Path.Combine(root, "src", "LatencyPilot.Core", "Benchmarking", "GpuAutoAffinityReport.cs"));
        StringAssert.Contains(reportContract, "GateAClosureEligible");
        StringAssert.Contains(reportContract, "SourceState");

        var benchmarkWorkload = File.ReadAllText(Path.Combine(root, "src", "LatencyPilot.GpuBenchmark", "BenchmarkWorkload.cs"));
        StringAssert.Contains(
            benchmarkWorkload,
            "var simulationIterations = MinimumSimulationIterations;",
            "The controlled benchmark must keep synthetic CPU simulation at its fixed minimum instead of calibrating scheduler pressure into the scored workload.");
        Assert.IsFalse(
            benchmarkWorkload.Contains("TuneSimulationIterations(", StringComparison.Ordinal),
            "The controlled benchmark must not adaptively increase CPU simulation based on CPU frame time.");

        var bundleRoot = Path.Combine(Path.GetTempPath(), $"latencypilot-gatea-bundle-{Guid.NewGuid():N}");
        var sessionDirectory = Path.Combine(bundleRoot, "gpu-auto-affinity-session");
        Directory.CreateDirectory(Path.Combine(sessionDirectory, "benchmark"));
        await File.WriteAllTextAsync(Path.Combine(sessionDirectory, "gpu-auto-affinity-report.json"), "{\"schema\":\"test\"}");
        await File.WriteAllTextAsync(Path.Combine(sessionDirectory, "benchmark", "trial.json"), "{\"trial\":1}");
        try
        {
            var export = await GateAEvidenceBundleExporter.TryCreateAsync(sessionDirectory);
            Assert.IsTrue(export.Succeeded, export.Error);
            Assert.IsNotNull(export.ZipPath);
            Assert.IsTrue(File.Exists(export.ZipPath));
            Assert.IsTrue(Directory.Exists(sessionDirectory), "Packaging must preserve the authoritative uncompressed session directory.");

            using var archive = ZipFile.OpenRead(export.ZipPath);
            var sessionName = Path.GetFileName(sessionDirectory);
            Assert.IsTrue(archive.Entries.Any(entry => string.Equals(
                entry.FullName,
                $"{sessionName}/gpu-auto-affinity-report.json",
                StringComparison.Ordinal)));
            Assert.IsTrue(archive.Entries.Any(entry => string.Equals(
                entry.FullName,
                $"{sessionName}/benchmark/trial.json",
                StringComparison.Ordinal)));
        }
        finally
        {
            if (Directory.Exists(bundleRoot))
            {
                Directory.Delete(bundleRoot, recursive: true);
            }
        }

        var cpu6 = new LogicalProcessorId(0, 6);
        var cpu7 = new LogicalProcessorId(0, 7);
        var now = DateTimeOffset.UtcNow;
        var originalTrials = new[]
        {
            CreateOriginalTrial(1, 149d, 160d, 6.30d, 99d),
            CreateOriginalTrial(2, 151d, 161d, 6.20d, 101d),
            CreateOriginalTrial(3, 150d, 159d, 6.25d, 100d),
        };
        var screeningLeader = new GpuAutoAffinityCandidateReport(
            "screening",
            3,
            cpu7,
            1,
            "Ranked",
            null,
            [],
            "CPU 7 led the short screen before finalist confirmation.",
            164d,
            171d,
            5.95d,
            111d,
            0.03d,
            false,
            DecisionRank: 1)
        {
            DecisionOnePercentLowEffect = 0.08d,
            DecisionAvgEffect = 0.07d,
            DecisionFrameP99Effect = 0.06d,
            DecisionLow01PctEffect = 0.03d,
        };
        var executionFirstButRankedSecond = new GpuAutoAffinityCandidateReport(
            "screening-finalists",
            2,
            cpu6,
            3,
            "Ranked",
            null,
            [],
            "Measured before the decision leader because finalist order is shuffled.",
            162d,
            170d,
            6.00d,
            109d,
            0.03d,
            false,
            DecisionRank: 2)
        {
            DecisionOnePercentLowEffect = 0.09d,
            DecisionAvgEffect = 0.06d,
            DecisionFrameP99Effect = 0.05d,
            DecisionLow01PctEffect = 0.02d,
        };
        var decisionLeader = new GpuAutoAffinityCandidateReport(
            "screening-finalists",
            3,
            cpu7,
            3,
            "Ranked",
            null,
            [],
            "Three local finalist pairs established the persisted decision leader.",
            165d,
            172d,
            5.90d,
            112d,
            0.02d,
            false,
            DecisionRank: 1)
        {
            DecisionOnePercentLowEffect = 0.12d,
            DecisionAvgEffect = 0.09d,
            DecisionFrameP99Effect = 0.08d,
            DecisionLow01PctEffect = 0.04d,
        };
        var decisionBaseline = new GpuAutoAffinityDecisionBaselineReport(
            145d,
            158d,
            6.40d,
            95d,
            0.04d,
            0.02d,
            0.03d,
            0.05d,
            3,
            4,
            false);
        var finalist = new GpuAutoAffinityFinalistReport(
            cpu7,
            3,
            [101, 102, 103],
            0.12d,
            0.09d,
            0.08d,
            0.04d,
            "ImprovementCapable",
            "Three valid paired observations cleared the decision floor.")
        {
            DecisionFloor = 0.025d,
        };
        var keepReport = new GpuAutoAffinityReport(
            GpuAutoAffinityReport.SchemaId,
            Guid.NewGuid(),
            now,
            now.AddMinutes(12),
            42,
            [screeningLeader, executionFirstButRankedSecond, decisionLeader],
            originalTrials,
            GpuOptimizationRecommendation.KeepCandidate.ToString(),
            cpu7,
            true,
            false,
            ["CPU 7 cleared paired improvement/noise and guardrail checks."],
            DecisionBaseline: decisionBaseline)
        {
            GateAClosureEligible = true,
            SourceState = "evidence-ready",
            Finalists = [finalist],
        };
        var keepPresentation = GateAResultPresentation.Create(
            keepReport,
            "C:\\evidence\\session",
            "C:\\evidence\\session\\gpu-auto-affinity-report.json",
            new GateAEvidenceBundleExportResult("C:\\evidence\\session.zip", null));
        Assert.AreEqual("Best observed this run · CPU 7 kept", keepPresentation.Title);
        Assert.AreEqual("Evidence eligible", keepPresentation.EligibilityLabel,
            "Source/evidence eligibility must not be presented as if the whole physical Gate A were already closed.");
        Assert.AreEqual(cpu7, keepPresentation.ComparedProcessor);
        Assert.IsTrue(keepPresentation.BundleAvailable);
        Assert.AreEqual(4, keepPresentation.Metrics.Count);
        var keptLow1 = keepPresentation.Metrics.Single(metric => metric.Key == "low1");
        Assert.AreEqual(0.12d, keptLow1.ImprovementFraction,
            "Presentation must use the optimizer-persisted finalist paired effect rather than reconstructing an absolute-baseline delta.");
        Assert.AreEqual((165d - 145d) / 145d, keptLow1.ObservedChangeFraction!.Value, 0.0001d,
            "The user-visible raw percentage must be derived directly from the same displayed Original and Candidate medians, independently of the drift-adjusted paired ranking effect.");
        Assert.AreEqual(0.025d, keptLow1.UncertaintyFraction,
            "Presentation uncertainty must carry the persisted finalist decision floor.");
        Assert.AreEqual(GateAMetricState.Improved, keptLow1.State,
            "A verified Keep proves that the primary paired 1%-low effect cleared the optimizer's authoritative threshold.");
        Assert.AreEqual(GateAMetricState.DecisionGuardrailSatisfied, keepPresentation.Metrics.Single(metric => metric.Key == "p99").State,
            "A secondary metric may be shown as measured paired effect, but the presentation must not independently invent statistical significance; verified Keep only proves that this guardrail stayed inside the authority's allowed envelope.");

        var restoreReport = keepReport with
        {
            FinalRecommendation = GpuOptimizationRecommendation.RestoreOriginal.ToString(),
            FinalProcessor = null,
            FinalStateVerified = true,
            OriginalStateRestored = true,
            GateAClosureEligible = false,
            SourceState = "development-only",
        };
        var restorePresentation = GateAResultPresentation.Create(
            restoreReport,
            "C:\\evidence\\session",
            "C:\\evidence\\session\\gpu-auto-affinity-report.json",
            new GateAEvidenceBundleExportResult(null, "ZIP destination is locked."));
        Assert.AreEqual("Best observed this run · CPU 7 · Original restored", restorePresentation.Title,
            "Restoring Original must not erase a structurally valid best-observed CPU.");
        Assert.AreEqual("Development evidence", restorePresentation.EligibilityLabel);
        Assert.AreEqual(cpu7, restorePresentation.ComparedProcessor,
            "Diagnostic comparison must follow the optimizer's persisted decision rank, not candidate execution order.");
        Assert.AreEqual(0.12d, restorePresentation.Metrics.Single(metric => metric.Key == "low1").ImprovementFraction,
            "When both a short-screen and a finalist aggregate exist for the same rank, diagnostic presentation must prefer the repeated finalist authority.");
        Assert.IsTrue(restorePresentation.ComparedCandidateIsDiagnosticOnly);
        StringAssert.Contains(restorePresentation.ComparedCandidateLabel, "comparison only");
        Assert.IsTrue(restorePresentation.Metrics.All(metric =>
            metric.State is GateAMetricState.DiagnosticOnly or GateAMetricState.Unavailable),
            "A restored run may display measured paired effects, but it must not turn them into independent pass/fail decisions.");
        var restoredGuardrail = restorePresentation.DecisionEvidence.Single(row => row.Label == "Keep guardrails");
        Assert.AreEqual("Not kept", restoredGuardrail.State,
            "A ranked CPU that was restored must not be presented as having passed the separate Keep decision.");
        Assert.IsFalse(restorePresentation.BundleAvailable);
        StringAssert.Contains(restorePresentation.BundleStatus, "could not be packaged");

        var customPresentation = GateAResultPresentation.Create(
            restoreReport with { SearchScope = GpuAutoAffinitySearchScope.Custom },
            "C:\\evidence\\session",
            "C:\\evidence\\session\\gpu-auto-affinity-report.json",
            new GateAEvidenceBundleExportResult(null, "ZIP destination is locked."));
        Assert.AreEqual("Best observed · CPU 7 · diagnostic only", customPresentation.Title);
        StringAssert.Contains(customPresentation.ComparedCandidateLabel, "Best within selected CPUs");
        StringAssert.Contains(customPresentation.Summary, "Best observed within the selected CPUs");
    }

    private static GpuAutoAffinityTrialReport CreateOriginalTrial(
        int runNumber,
        double low1,
        double avg,
        double p99,
        double low01) =>
        new(
            runNumber,
            "screening-original",
            "Original",
            null,
            Guid.NewGuid(),
            "Ready",
            true,
            true,
            null,
            p99,
            low1,
            30_000d,
            30_000d,
            [],
            null,
            null,
            avg,
            low01);

    private static object? ReadProperty(object? value, string name) =>
        value?.GetType().GetProperty(name)?.GetValue(value);

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LatencyPilot.slnx")))
            {
                return directory.FullName;
            }
        }
        throw new DirectoryNotFoundException();
    }

    private sealed class RecordingStageRunner(
        Func<AutomaticOptimizationStage, AutomaticOptimizationStageResult> resultFactory)
        : IAutomaticOptimizationStageRunner
    {
        internal List<AutomaticOptimizationStage> Calls { get; } = [];

        public ValueTask<AutomaticOptimizationStageResult> RunStageAsync(
            AutomaticOptimizationStage stage,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add(stage);
            return ValueTask.FromResult(resultFactory(stage));
        }
    }
}
