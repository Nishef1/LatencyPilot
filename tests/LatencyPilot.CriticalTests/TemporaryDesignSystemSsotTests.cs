using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LatencyPilot.CriticalTests;

[TestClass]
public sealed class TemporaryDesignSystemSsotTests
{
    [TestMethod]
    public void AppConsumesCentralDesignSystemResources()
    {
        var repoRoot = FindRepoRoot();
        var appDirectory = Path.Combine(repoRoot, "src", "LatencyPilot.App");
        var appXaml = File.ReadAllText(Path.Combine(appDirectory, "App.xaml"));

        var designTokensPath = Path.Combine(appDirectory, "Design", "DesignTokens.xaml");
        var componentStylesPath = Path.Combine(appDirectory, "Design", "ComponentStyles.xaml");

        Assert.IsTrue(File.Exists(designTokensPath), "DesignTokens.xaml must be the SSOT for visual tokens.");
        Assert.IsTrue(File.Exists(componentStylesPath), "ComponentStyles.xaml must centralize reusable component styles.");
        StringAssert.Contains(appXaml, "Design/DesignTokens.xaml");
        StringAssert.Contains(appXaml, "Design/ComponentStyles.xaml");
        Assert.IsFalse(appXaml.Contains("<SolidColorBrush x:Key=\"CanvasBrush\"", StringComparison.Ordinal),
            "App.xaml should compose the design system instead of owning theme colors directly.");
    }

    private static string FindRepoRoot()
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

        throw new InvalidOperationException("Could not locate the LatencyPilot repository root.");
    }
}
