using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LatencyPilot.CriticalTests;

[TestClass]
public sealed class GateAUiContractTests
{
    [TestMethod]
    public void MainWindowMustInitializeDevelopmentGateAExperience()
    {
        var repositoryRoot = FindRepositoryRoot();
        var mainWindowSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "LatencyPilot.App",
            "MainWindow.xaml.cs"));

        StringAssert.Contains(
            mainWindowSource,
            "InitializeGateAValidationExperience();",
            "The development Gate A surface must be initialized from MainWindow startup or its button remains collapsed and is never created.");

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
