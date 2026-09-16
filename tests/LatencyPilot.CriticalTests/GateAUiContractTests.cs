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
        StringAssert.Contains(gateASource, "private bool _gateAValidationRunning;");
        StringAssert.Contains(gateASource, "SetGateAValidationBusy(true);");
        StringAssert.Contains(gateASource, "SetGateAValidationBusy(false);");
        StringAssert.Contains(gateASource, "_measurementBusy || _gateAValidationRunning");
        StringAssert.Contains(gateASource, "Content = \"Run GPU Gate A\"");
        StringAssert.Contains(gateASource, "DeveloperValidationCard.Visibility = Visibility.Visible;");
        StringAssert.Contains(gateASource, "DeveloperValidationHost.Children.Add(_gateAValidationButton);");

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
