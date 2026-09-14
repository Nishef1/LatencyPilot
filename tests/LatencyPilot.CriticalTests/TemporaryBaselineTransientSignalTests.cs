using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LatencyPilot.CriticalTests;

[TestClass]
public sealed class TemporaryPhysicalValidationHarnessTests
{
    [TestMethod]
    public void HarnessExposesSafeCandidatePlanningAndRuntimePlacementVerification()
    {
        var root = FindRepositoryRoot();
        var harness = Path.Combine(root, "tools", "LatencyPilot.PhysicalValidation");
        var program = File.ReadAllText(Path.Combine(harness, "Program.cs"));
        var project = File.ReadAllText(Path.Combine(harness, "LatencyPilot.PhysicalValidation.csproj"));

        StringAssert.Contains(program, "plan-gpu-affinity");
        StringAssert.Contains(program, "verify-gpu-placement");
        StringAssert.Contains(program, "RequireKernelCaptureAuthority");
        StringAssert.Contains(program, "GpuInterruptRuntimePlacementVerifier.Analyze");
        Assert.IsTrue(File.Exists(Path.Combine(harness, "BaselineEvidenceCandidatePlan.cs")));
        StringAssert.Contains(project, "LatencyPilot.Benchmarking");
        StringAssert.Contains(project, "LatencyPilot.Protocol");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LatencyPilot.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root was not found from the test output directory.");
    }
}
