using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LatencyPilot.CriticalTests;

[TestClass]
public sealed class TemporaryPremiumDashboardTests
{
    [TestMethod]
    public void PremiumDashboardUsesTruthfulNativeVisualsAndSsot()
    {
        var root = FindRepositoryRoot();
        var app = Path.Combine(root, "src", "LatencyPilot.App");

        var requiredFiles = new[]
        {
            Path.Combine(app, "Controls", "LatencyProfileChart.cs"),
            Path.Combine(app, "Controls", "CpuDistributionChart.cs"),
            Path.Combine(app, "Controls", "ModuleContributionChart.cs"),
            Path.Combine(app, "Controls", "CpuInterruptMap.cs"),
            Path.Combine(app, "DashboardChartModels.cs"),
            Path.Combine(app, "DashboardVisuals.cs"),
        };

        foreach (var file in requiredFiles)
        {
            Assert.IsTrue(File.Exists(file), $"Missing premium dashboard artifact: {file}");
        }

        var tokens = File.ReadAllText(Path.Combine(app, "Design", "DesignTokens.xaml"));
        StringAssert.Contains(tokens, "NavigationRailWidth");
        StringAssert.Contains(tokens, "ChartGridBrush");
        StringAssert.Contains(tokens, "ChartAccentSecondaryBrush");

        var mainWindow = File.ReadAllText(Path.Combine(app, "MainWindow.xaml"));
        StringAssert.Contains(mainWindow, "LatencyProfileChart");
        StringAssert.Contains(mainWindow, "CpuDistributionChart");
        StringAssert.Contains(mainWindow, "ModuleContributionChart");
        StringAssert.Contains(mainWindow, "CpuInterruptMap");
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
