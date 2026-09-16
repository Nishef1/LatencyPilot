using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LatencyPilot.CriticalTests;

[TestClass]
public sealed class OverviewActionContractTests
{
    [TestMethod]
    public void OverviewMustExposeBaselineAndExportActionsThroughCanonicalControls()
    {
        var repositoryRoot = FindRepositoryRoot();
        var mainWindowPath = Path.Combine(
            repositoryRoot,
            "src",
            "LatencyPilot.App",
            "MainWindow.xaml");
        var document = XDocument.Load(mainWindowPath);
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var baselineAction = document
            .Descendants()
            .SingleOrDefault(element =>
                string.Equals(
                    element.Attribute(x + "Name")?.Value,
                    "OverviewBaselineActionButton",
                    StringComparison.Ordinal));
        Assert.IsNotNull(baselineAction, "Overview must keep the baseline action visible in its header actions.");
        Assert.AreEqual("CaptureBaselineButton_Click", baselineAction.Attribute("Click")?.Value);
        Assert.AreEqual(
            "{Binding IsEnabled, ElementName=CaptureBaselineButton}",
            baselineAction.Attribute("IsEnabled")?.Value,
            "The Overview baseline action must mirror the canonical baseline control state.");

        var exportAction = document
            .Descendants()
            .SingleOrDefault(element =>
                string.Equals(
                    element.Attribute(x + "Name")?.Value,
                    "OverviewExportActionButton",
                    StringComparison.Ordinal));
        Assert.IsNotNull(exportAction, "Overview must keep the evidence export action visible in its header actions.");
        Assert.AreEqual("ExportEvidenceButton_Click", exportAction.Attribute("Click")?.Value);
        Assert.AreEqual(
            "{Binding IsEnabled, ElementName=ExportEvidenceButton}",
            exportAction.Attribute("IsEnabled")?.Value,
            "The Overview export action must mirror the canonical export control state.");
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
