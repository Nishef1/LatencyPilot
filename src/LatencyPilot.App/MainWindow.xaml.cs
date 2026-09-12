using System.Globalization;
using System.Reflection;
using LatencyPilot.App.Services;
using LatencyPilot.App.ViewModels;
using LatencyPilot.Platform.Windows.Devices;
using LatencyPilot.Platform.Windows.System;
using LatencyPilot.Protocol;
using Microsoft.UI.Xaml;
using Serilog;

namespace LatencyPilot.App;

public sealed partial class MainWindow : Window
{
    private static readonly Serilog.ILogger Logger = Log.ForContext<MainWindow>();
    private bool _observationServiceReady;
    private bool _initialLoadStarted;

    public MainWindow()
    {
        InitializeComponent();
        Title = "LatencyPilot";
        VersionText.Text = $"v{GetProductVersion()}";
    }

    private async void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        if (_initialLoadStarted)
        {
            return;
        }

        _initialLoadStarted = true;
        await Task.Yield();
        await Task.WhenAll(
            CaptureSystemInventoryAsync(),
            CaptureProcessorTopologyAsync(),
            CaptureDeviceInventoryAsync());
        await RefreshObservationServiceStatusAsync();
    }

    private async void RefreshServiceButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshObservationServiceStatusAsync();
    }

    private async Task RefreshObservationServiceStatusAsync()
    {
        CaptureObservationButton.IsEnabled = false;
        RefreshServiceButton.IsEnabled = false;
        ServiceStatusBadgeText.Text = "Checking service";
        ServiceStatusText.Text = "Checking the local observation service…";

        try
        {
            var status = await ObservationServiceClient.GetStatusAsync();
            if (!status.PrivilegedObservationHostImplemented || status.MutationAvailable)
            {
                Logger.Warning(
                    "Observation service contract mismatch. HostImplemented={HostImplemented}, MutationAvailable={MutationAvailable}.",
                    status.PrivilegedObservationHostImplemented,
                    status.MutationAvailable);
                _observationServiceReady = false;
                ServiceStatusBadgeText.Text = "Contract mismatch";
                ServiceStatusText.Text = "Service contract mismatch. Read-only kernel capture is disabled.";
                return;
            }

            _observationServiceReady = true;
            CaptureObservationButton.IsEnabled = true;
            ServiceStatusBadgeText.Text = "Service connected";
            ServiceStatusText.Text = "Connected to the privileged read-only observation service. Mutation remains disabled.";
        }
        catch (TimeoutException exception)
        {
            Logger.Warning(exception, "Observation service status request timed out.");
            SetServiceUnavailable("Observation service is not running or did not respond in time.");
        }
        catch (IOException exception)
        {
            Logger.Warning(exception, "Observation service status connection failed.");
            SetServiceUnavailable("Observation service connection failed.");
        }
        catch (InvalidDataException exception)
        {
            Logger.Error(exception, "Observation service returned an invalid status response.");
            SetServiceUnavailable("Observation service returned an invalid protocol response.");
        }
        catch (InvalidOperationException exception)
        {
            Logger.Warning(exception, "Observation service rejected the status request.");
            SetServiceUnavailable(exception.Message);
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Unexpected failure while refreshing observation service status.");
            SetServiceUnavailable("Unexpected service error. See the diagnostics log for details.");
        }
        finally
        {
            RefreshServiceButton.IsEnabled = true;
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
        RefreshServiceButton.IsEnabled = false;
        KernelCaptureStatusText.Text = "Capturing DPC/ISR activity for 5 seconds…";
        ObservationQualityText.Text = "Capture in progress. No interpretation is made until the observation completes.";

        try
        {
            var capture = await ObservationServiceClient.CaptureKernelLatencyAsync(
                TimeSpan.FromSeconds(5),
                maximumEvents: 200_000);

            RenderCapture(capture);
        }
        catch (TimeoutException exception)
        {
            Logger.Warning(exception, "Kernel observation timed out.");
            ClearCaptureMetrics();
            SetServiceUnavailable("Observation service is not running or did not respond in time.");
            KernelCaptureStatusText.Text = "Kernel observation did not start or exceeded its deadline.";
        }
        catch (IOException exception)
        {
            Logger.Warning(exception, "Kernel observation pipe connection failed.");
            ClearCaptureMetrics();
            SetServiceUnavailable("Observation service connection failed.");
            KernelCaptureStatusText.Text = "Kernel observation did not complete.";
        }
        catch (InvalidDataException exception)
        {
            Logger.Error(exception, "Kernel observation returned an invalid protocol response.");
            ClearCaptureMetrics();
            KernelCaptureStatusText.Text = "Observation service returned an invalid protocol response.";
        }
        catch (InvalidOperationException exception)
        {
            Logger.Warning(exception, "Kernel observation request was rejected.");
            ClearCaptureMetrics();
            KernelCaptureStatusText.Text = exception.Message;
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Unexpected kernel observation failure.");
            ClearCaptureMetrics();
            KernelCaptureStatusText.Text = "Unexpected observation error. See the diagnostics log for details.";
        }
        finally
        {
            CaptureObservationButton.IsEnabled = _observationServiceReady;
            RefreshServiceButton.IsEnabled = true;
        }
    }

    private void RenderCapture(KernelLatencyCaptureResponse capture)
    {
        DpcCountText.Text = capture.Dpc.Count.ToString("N0", CultureInfo.InvariantCulture);
        IsrCountText.Text = capture.Isr.Count.ToString("N0", CultureInfo.InvariantCulture);
        DpcP99Text.Text = $"p99 {FormatMicroseconds(capture.Dpc.P99Microseconds)}";
        DpcP999Text.Text = FormatMicroseconds(capture.Dpc.P999Microseconds);
        IsrP99Text.Text = $"p99 {FormatMicroseconds(capture.Isr.P99Microseconds)}";
        IsrP999Text.Text = FormatMicroseconds(capture.Isr.P999Microseconds);
        ObservedProcessorCountText.Text = capture.Processors.Count.ToString(CultureInfo.InvariantCulture);

        var totalAttributedEvents = capture.ResolvedModuleEventCount + capture.UnresolvedModuleEventCount;
        var resolvedPercent = totalAttributedEvents == 0
            ? 0d
            : capture.ResolvedModuleEventCount * 100d / totalAttributedEvents;
        ModuleCoverageBar.Value = Math.Clamp(resolvedPercent, 0d, 100d);

        var truncationSuffix = capture.ModuleContributorListTruncated || capture.UnresolvedRoutineListTruncated
            ? " Contributor lists reached protocol bounds."
            : string.Empty;

        ModuleAttributionCoverageText.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"{capture.ResolvedModuleEventCount:N0} resolved · {capture.UnresolvedModuleEventCount:N0} unresolved · {resolvedPercent:F1}% coverage.{truncationSuffix}");

        TopModulesList.ItemsSource = capture.Modules
            .Take(8)
            .Select(module => new ModuleObservationRow(
                module.ModuleName,
                $"DPC {module.Dpc.Count:N0} · ISR {module.Isr.Count:N0}",
                string.Create(CultureInfo.InvariantCulture, $"{module.TotalDurationMicroseconds:F1} µs")))
            .ToArray();

        var topModule = capture.Modules.Count == 0 ? null : capture.Modules[0];
        TopModuleText.Text = topModule is null
            ? "No routine address was resolved to an authoritative image range."
            : $"Dominant resolved module: {topModule.ModuleName}";

        TopProcessorsList.ItemsSource = capture.Processors
            .OrderByDescending(processor => processor.Dpc.Count + processor.Isr.Count)
            .Take(8)
            .Select(processor => new ProcessorObservationRow(
                $"CPU {processor.ProcessorNumber}",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{processor.Dpc.Count + processor.Isr.Count:N0} events · DPC {processor.Dpc.Count:N0} / ISR {processor.Isr.Count:N0}"),
                $"p99 {FormatLargestP99(processor)}"))
            .ToArray();

        var etwLossCountKnown = capture.EventsLost >= 0;
        var cleanCapture = etwLossCountKnown &&
            capture.InvalidEventCount == 0 &&
            capture.InvalidImageEventCount == 0 &&
            !capture.EventLimitReached;

        KernelCaptureStatusText.Text = cleanCapture
            ? $"Observation complete in {capture.ActualDurationMilliseconds:F0} ms with no ETW loss detected."
            : !etwLossCountKnown
                ? $"Observation completed with quality warning: ETW loss count unavailable, invalidLatency={capture.InvalidEventCount}, invalidImages={capture.InvalidImageEventCount}, limitReached={capture.EventLimitReached}."
                : $"Observation completed with quality warnings: lost={capture.EventsLost}, invalidLatency={capture.InvalidEventCount}, invalidImages={capture.InvalidImageEventCount}, limitReached={capture.EventLimitReached}.";

        ObservationQualityText.Text = cleanCapture
            ? "Capture integrity looks clean. This is still a single observation, not a validated baseline."
            : "Treat this observation as incomplete evidence. Stage C will reject noisy or incomplete windows when building a baseline.";
    }

    private void SetServiceUnavailable(string message)
    {
        _observationServiceReady = false;
        CaptureObservationButton.IsEnabled = false;
        ServiceStatusBadgeText.Text = "Service unavailable";
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
        ModuleAttributionCoverageText.Text = "—";
        ModuleCoverageBar.Value = 0;
        TopModuleText.Text = "No observation yet.";
        TopModulesList.ItemsSource = null;
        TopProcessorsList.ItemsSource = null;
        ObservationQualityText.Text = "Quality evidence will appear after capture.";
    }

    private static string GetProductVersion()
    {
        var assembly = typeof(MainWindow).Assembly;
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString(3)
            ?? "unknown";
    }

    private static string FormatLargestP99(ProcessorLatencyDistribution processor)
    {
        var dpc = processor.Dpc.P99Microseconds;
        var isr = processor.Isr.P99Microseconds;
        if (dpc is null)
        {
            return FormatMicroseconds(isr);
        }

        if (isr is null)
        {
            return FormatMicroseconds(dpc);
        }

        return FormatMicroseconds(Math.Max(dpc.Value, isr.Value));
    }

    private static string FormatMicroseconds(double? value) =>
        value is null
            ? "—"
            : value.Value.ToString("F1", CultureInfo.InvariantCulture) + " µs";

    private async Task CaptureSystemInventoryAsync()
    {
        try
        {
            var system = await Task.Run(() => SystemInventoryReader.Capture());
            OperatingSystemText.Text = system.OperatingSystem;
            OsArchitectureText.Text = system.OsArchitecture;
            ProcessArchitectureText.Text = system.ProcessArchitecture;
            ProcessAvailableProcessorCountText.Text = system.ProcessAvailableProcessorCount.ToString(CultureInfo.InvariantCulture);
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "System inventory capture failed.");
            OperatingSystemText.Text = "Unavailable";
            OsArchitectureText.Text = "—";
            ProcessArchitectureText.Text = "—";
            ProcessAvailableProcessorCountText.Text = "—";
            TopologyStatusText.Text = "System inventory failed. See the diagnostics log for details.";
        }
    }

    private async Task CaptureProcessorTopologyAsync()
    {
        try
        {
            var topology = await Task.Run(() => ProcessorTopologyReader.Capture());
            HardwareLogicalProcessorCountText.Text = topology.LogicalProcessorCount.ToString(CultureInfo.InvariantCulture);
            PhysicalCoreCountText.Text = topology.PhysicalCoreCount.ToString(CultureInfo.InvariantCulture);
            PackageCountText.Text = topology.Packages.Count.ToString(CultureInfo.InvariantCulture);
            ProcessorGroupCountText.Text = topology.ProcessorGroupCount.ToString(CultureInfo.InvariantCulture);
            SmtCoreCountText.Text = topology.SmtCoreCount.ToString(CultureInfo.InvariantCulture);
            TopologyStatusText.Text = "CPU topology captured through GetLogicalProcessorInformationEx.";
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Processor topology capture failed.");
            HardwareLogicalProcessorCountText.Text = "Unavailable";
            PhysicalCoreCountText.Text = "Unavailable";
            PackageCountText.Text = "Unavailable";
            ProcessorGroupCountText.Text = "Unavailable";
            SmtCoreCountText.Text = "Unavailable";
            TopologyStatusText.Text = "Topology capture failed. See the diagnostics log for details.";
        }
    }

    private async Task CaptureDeviceInventoryAsync()
    {
        try
        {
            var inventory = await Task.Run(() => DeviceInventoryReader.CapturePresentDevices());
            PresentDeviceCountText.Text = inventory.PresentDeviceCount.ToString(CultureInfo.InvariantCulture);
            DeviceInventoryStatusText.Text = "Present devices captured through SetupAPI using stable device instance IDs.";
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Device inventory capture failed.");
            PresentDeviceCountText.Text = "Unavailable";
            DeviceInventoryStatusText.Text = "Device inventory failed. See the diagnostics log for details.";
        }
    }
}
