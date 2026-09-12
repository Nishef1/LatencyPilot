using System.Windows;
using LatencyPilot.Platform.Windows.System;

namespace LatencyPilot.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var snapshot = SystemInventoryReader.Capture();
        OperatingSystemText.Text = snapshot.OperatingSystem;
        OsArchitectureText.Text = snapshot.OsArchitecture;
        ProcessArchitectureText.Text = snapshot.ProcessArchitecture;
        LogicalProcessorCountText.Text = snapshot.LogicalProcessorCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
