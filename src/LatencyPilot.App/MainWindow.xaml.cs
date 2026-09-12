using System.Windows;
using LatencyPilot.Platform.Windows.System;

namespace LatencyPilot.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var system = SystemInventoryReader.Capture();
        OperatingSystemText.Text = system.OperatingSystem;
        OsArchitectureText.Text = system.OsArchitecture;
        ProcessArchitectureText.Text = system.ProcessArchitecture;
        ProcessAvailableProcessorCountText.Text = system.ProcessAvailableProcessorCount.ToString(System.Globalization.CultureInfo.InvariantCulture);

        try
        {
            var topology = ProcessorTopologyReader.Capture();
            HardwareLogicalProcessorCountText.Text = topology.LogicalProcessorCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
            PhysicalCoreCountText.Text = topology.PhysicalCoreCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
            PackageCountText.Text = topology.Packages.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            ProcessorGroupCountText.Text = topology.ProcessorGroupCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
            SmtCoreCountText.Text = topology.SmtCoreCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
            TopologyStatusText.Text = "Captured from GetLogicalProcessorInformationEx. No system settings were changed.";
        }
        catch (Exception exception)
        {
            HardwareLogicalProcessorCountText.Text = "Unavailable";
            PhysicalCoreCountText.Text = "Unavailable";
            PackageCountText.Text = "Unavailable";
            ProcessorGroupCountText.Text = "Unavailable";
            SmtCoreCountText.Text = "Unavailable";
            TopologyStatusText.Text = $"Topology capture failed: {exception.Message}";
        }
    }
}
