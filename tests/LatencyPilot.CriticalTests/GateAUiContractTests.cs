using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LatencyPilot.CriticalTests;

[TestClass]
public sealed class GateAUiContractTests
{
    [TestMethod]
    public void StartupHardeningMustInitializeDevelopmentGateAExperience()
    {
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

        var gateASource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "LatencyPilot.App",
            "GateAValidationExperience.cs"));
        StringAssert.Contains(gateASource, "Content = \"Run GPU Gate A\"");
        StringAssert.Contains(gateASource, "DeveloperValidationCard.Visibility = Visibility.Visible;");
        StringAssert.Contains(gateASource, "DeveloperValidationHost.Children.Add(_gateAValidationButton);");
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
