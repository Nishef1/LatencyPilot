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
        StringAssert.Contains(
            File.ReadAllText(Path.Combine(
                repositoryRoot,
                "src",
                "LatencyPilot.App",
                "GateAValidationExperience.cs")),
            "if (_gateAValidationButton is not null)",
            "Gate A startup must be idempotent so a future startup call cannot create duplicate controls or handlers.");

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
