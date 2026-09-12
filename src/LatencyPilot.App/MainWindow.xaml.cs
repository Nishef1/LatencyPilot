using System.Globalization;
using LatencyPilot.App.Services;
using LatencyPilot.Platform.Windows.Devices;
using LatencyPilot.Platform.Windows.System;
using Microsoft.UI.Xaml;

namespace LatencyPilot.App;

public sealed partial class MainWindow : Window
{
    private bool _observationServiceReady;

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

    private async void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        await RefreshObservationServiceStatusAsync();
    }

    private async Task RefreshObservationServiceStatusAsync()
    {
        CaptureObservationButton.IsEnabled = false;
        ServiceStatusText.Text = "Checking service…";

        try
        {
            var status = await ObservationServiceClient.GetStatusAsync();
            if (!status.PrivilegedObservationHostImplemented || status.MutationAvailable)
            {
                _observationServiceReady = false;
                ServiceStatusText.Text = "Service contract mismatch. Read-only kernel capture is disabled.";
                return;
            }

            _observationServiceReady = true;
            CaptureObservationButton.IsEnabled = true;
            ServiceStatusText.Text = "Connected. Privileged observation is available; mutation remains disabled.";
        }
        catch (TimeoutException)
        {
            SetServiceUnavailable("Observation service is not running or did not respond in time.");
        }
        catch (IOException)
        {
            SetServiceUnavailable("Observation service connection failed.");
        }
        catch (InvalidDataException)
        {
            SetServiceUnavailable("Observation service returned an invalid protocol response.");
        }
        catch (InvalidOperationException exception)
        {
            SetServiceUnavailable(exception.Message);
        }
    }

    private async void CaptureObservationButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_observationServiceReady)
        {
            await RefreshObservationServiceStatusAsync();
            if (!_observationServiceReady)
            {
                return;
            }
        }

        CaptureObservationButton.IsEnabled = false;
        KernelCaptureStatusText.Text = "Capturing DPC/ISR activity for 5 seconds…";

        try
        {
            var capture = await ObservationServiceClient.CaptureKernelLatencyAsync(
                TimeSpan.FromSeconds(5),
                maximumEvents: 200_000);

            DpcCountText.Text = capture.Dpc.Count.ToString("N0", CultureInfo.InvariantCulture);
            IsrCountText.Text = capture.Isr.Count.ToString("N0", CultureInfo.InvariantCulture);
            DpcP99Text.Text = FormatMicroseconds(capture.Dpc.P99Microseconds);
            DpcP999Text.Text = FormatMicroseconds(capture.Dpc.P999Microseconds);
            IsrP99Text.Text = FormatMicroseconds(capture.Isr.P99Microseconds);
            IsrP999Text.Text = FormatMicroseconds(capture.Isr.P999Microseconds);
            ObservedProcessorCountText.Text = capture.Processors.Count.ToString(CultureInfo.InvariantCulture);

            KernelCaptureStatusText.Text = capture.EventsLost == 0 &&
                capture.InvalidEventCount == 0 &&
                !capture.EventLimitReached
                ? $"Observation complete in {capture.ActualDurationMilliseconds:F0} ms with no ETW loss detected."
                : $"Observation incomplete: lost={capture.EventsLost}, invalid={capture.InvalidEventCount}, limitReached={capture.EventLimitReached}.";
        }
        catch (TimeoutException)
        {
            ClearCaptureMetrics();
            SetServiceUnavailable("Observation service is not running or did not respond in time.");
            KernelCaptureStatusText.Text = "Kernel observation did not start or exceeded its deadline.";
        }
        catch (IOException)
        {
            ClearCaptureMetrics();
            SetServiceUnavailable("Observation service connection failed.");
            KernelCaptureStatusText.Text = "Kernel observation did not complete.";
        }
        catch (InvalidDataException)
        {
            ClearCaptureMetrics();
            KernelCaptureStatusText.Text = "Observation service returned an invalid protocol response.";
        }
        catch (InvalidOperationException exception)
        {
            ClearCaptureMetrics();
            KernelCaptureStatusText.Text = exception.Message;
        }
        finally
        {
            CaptureObservationButton.IsEnabled = _observationServiceReady;
        }
    }

    private void SetServiceUnavailable(string message)
    {
        _observationServiceReady = false;
        CaptureObservationButton.IsEnabled = false;
        ServiceStatusText.Text = message;
    }

    private void ClearCaptureMetrics()
    {
        DpcCountText.Text = "—";
        IsrCountText.Text = "—";
        DpcP99Text.Text = "—";
        DpcP999Text.Text = "—";
        IsrP99Text.Text = "—";
        IsrP999Text.Text = "—";
        ObservedProcessorCountText.Text = "—";
    }

    private static string FormatMicroseconds(double? value) =>
        value is null
            ? "—"
            : value.Value.ToString("F1", CultureInfo.InvariantCulture) + " µs";

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
