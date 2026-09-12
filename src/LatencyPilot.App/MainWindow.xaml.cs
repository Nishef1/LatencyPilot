using System.Globalization;
using LatencyPilot.Platform.Windows.Devices;
using LatencyPilot.Platform.Windows.System;
using Microsoft.UI.Xaml;

namespace LatencyPilot.App;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Title = "LatencyPilot";

        var system = SystemInventoryReader.Capture();
        OperatingSystemText.Text = system.OperatingSystem;
        OsArchitectureText.Text = system.OsArchitecture;
        ProcessArchitectureText.Text = system.ProcessArchitecture;
        ProcessAvailableProcessorCountText.Text = system.ProcessAvailableProcessorCount.ToString(CultureInfo.InvariantCulture);

        CaptureProcessorTopology();
        CaptureDeviceInventory();
    }

    private void CaptureProcessorTopology()
    {
        try
        {
            var topology = ProcessorTopologyReader.Capture();
            HardwareLogicalProcessorCountText.Text = topology.LogicalProcessorCount.ToString(CultureInfo.InvariantCulture);
            PhysicalCoreCountText.Text = topology.PhysicalCoreCount.ToString(CultureInfo.InvariantCulture);
            PackageCountText.Text = topology.Packages.Count.ToString(CultureInfo.InvariantCulture);
            ProcessorGroupCountText.Text = topology.ProcessorGroupCount.ToString(CultureInfo.InvariantCulture);
            SmtCoreCountText.Text = topology.SmtCoreCount.ToString(CultureInfo.InvariantCulture);
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

    private void CaptureDeviceInventory()
    {
        try
        {
            var inventory = DeviceInventoryReader.CapturePresentDevices();
            PresentDeviceCountText.Text = inventory.PresentDeviceCount.ToString(CultureInfo.InvariantCulture);
            DeviceInventoryStatusText.Text = "Present devices captured through SetupAPI using stable device instance IDs.";
        }
        catch (Exception exception)
        {
            PresentDeviceCountText.Text = "Unavailable";
            DeviceInventoryStatusText.Text = $"Device inventory failed: {exception.Message}";
        }
    }
}
