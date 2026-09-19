#pragma warning disable CA1822 // AuditCase methods are reflection-invoked by ConsolidatedCriticalTests.
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
            AutomaticOptimizationStage.Msi,
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
            stage == AutomaticOptimizationStage.Msi
                ? new AutomaticOptimizationStageResult(
                    stage,
                    AutomaticOptimizationStageDisposition.RebootPending,
                    "Windows restart required",
                    "before:msi",
                    "stored:msi")
                : new AutomaticOptimizationStageResult(
                    stage,
                    AutomaticOptimizationStageDisposition.Completed,
                    $"{stage} complete",
                    $"before:{stage}",
                    $"after:{stage}"));
        var reboot = await AutomaticOptimizationWorkflow.RunAsync(rebootRunner);
        Assert.IsFalse(reboot.Completed);
        Assert.AreEqual(AutomaticOptimizationStage.Msi, reboot.TerminalStage);
        Assert.AreEqual(3, rebootRunner.Calls.Count);
        Assert.IsFalse(rebootRunner.Calls.Contains(AutomaticOptimizationStage.PrimaryInputUsbXhci));

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
