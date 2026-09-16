using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LatencyPilot.CriticalTests;

[TestClass]
public sealed class GateATerminalLifecycleTests
{
    [TestMethod]
    public void FailureTerminalizationMustAttemptReportAndProgressIndependently()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "tools",
            "LatencyPilot.GateAValidation",
            "GpuAutoAffinityGateARunner.cs"));

        var failureCatchStart = source.IndexOf(
            "catch (Exception exception)",
            source.IndexOf("catch (OperationCanceledException)", StringComparison.Ordinal),
            StringComparison.Ordinal);
        var finallyStart = source.IndexOf("finally", failureCatchStart, StringComparison.Ordinal);
        Assert.IsTrue(
            failureCatchStart >= 0 && finallyStart > failureCatchStart,
            "Gate A failure terminal path could not be located.");

        var failurePath = source[failureCatchStart..finallyStart];
        StringAssert.Contains(
            failurePath,
            "await TryWriteTerminalReportAsync(options.OutputPath, fallback)");
        StringAssert.Contains(
            failurePath,
            "await TryReportTerminalAsync(");
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

        throw new DirectoryNotFoundException(
            "LatencyPilot repository root could not be resolved from the test output directory.");
    }
}
