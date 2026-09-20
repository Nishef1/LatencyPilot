#pragma warning disable CA1822 // AuditCase methods are reflection-invoked by ConsolidatedCriticalTests.
using System.IO.Compression;
using LatencyPilot.Benchmarking.Optimization;
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
            "The v1 benchmark must keep synthetic CPU simulation at its fixed minimum instead of calibrating scheduler pressure into the scored workload.");
        Assert.IsFalse(
            benchmarkWorkload.Contains("TuneSimulationIterations(", StringComparison.Ordinal),
            "The v1 benchmark must not adaptively increase CPU simulation based on CPU frame time.");

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
    }

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
