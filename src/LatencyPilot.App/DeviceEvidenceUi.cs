using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using LatencyPilot.Benchmarking.Optimization;
using LatencyPilot.Core.Devices;
using LatencyPilot.Platform.Windows.Devices;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace LatencyPilot.App;

public sealed partial class MainWindow
{
    private const int MaximumInspectorRowsPerSection = 12;
    private static readonly TimeSpan InputTimingInspectionDuration = TimeSpan.FromSeconds(5);

    private async void InspectDeviceEvidenceButton_Click(object sender, RoutedEventArgs e)
    {
        InspectDeviceEvidenceButton.IsEnabled = false;
        DeviceEvidenceStatusText.Text = "Reading present PnP, USB route and RSS evidence…";

        try
        {
            var inspection = await Task.Run(CaptureDeviceEvidenceInspection);

            var rssSummary = inspection.NetworkRss.IsAvailable
                ? string.Create(CultureInfo.InvariantCulture, $"{inspection.NetworkRss.Adapters.Count:N0} RSS row(s)")
                : $"RSS {inspection.NetworkRss.Status}";
            var warningSummary = inspection.Warnings.Count == 0
                ? string.Empty
                : string.Create(CultureInfo.InvariantCulture, $" · {inspection.Warnings.Count:N0} partial-read warning(s)");
            DeviceEvidenceStatusText.Text = string.Create(
                CultureInfo.InvariantCulture,
                $"{inspection.Inventory.PresentDeviceCount:N0} present · {inspection.Inventory.DevicesWithDriverMetadataCount:N0} with driver metadata · {inspection.InputRoutes.ExactUsbPortRouteCount:N0} exact USB input port route(s) · {rssSummary}{warningSummary}.");

            await ShowDeviceEvidenceDialogAsync(inspection);
        }
        catch (Exception exception) when (IsRecoverableDeviceEvidenceException(exception))
        {
            Logger.Error(exception, "Required present-device inventory could not be read for detailed evidence inspection.");
            DeviceEvidenceStatusText.Text =
                $"Present-device inventory could not be read: {exception.Message} See the diagnostics log for details.";
        }
        finally
        {
            InspectDeviceEvidenceButton.IsEnabled = true;
        }
    }

    private static DeviceEvidenceInspection CaptureDeviceEvidenceInspection()
    {
        // Present PnP inventory is the required authority for this view. Optional
        // evidence layers are isolated below so one unavailable provider does not
        // discard otherwise trustworthy device evidence.
        var inventory = DeviceInventoryReader.CapturePresentDevices();
        var representativeDevices = RepresentativeDeviceEvidenceSelector.Select(inventory);
        var warnings = new List<string>();

        var usbTopology = CaptureUsbTopologyOrUnavailable(inventory, warnings);
        foreach (var error in usbTopology.Errors)
        {
            warnings.Add($"USB topology: {error}");
        }

        var inputRoutes = CaptureInputRoutesOrUnavailable(inventory, usbTopology, warnings);
        var rss = CaptureNetworkRssOrUnavailable(warnings);

        return new DeviceEvidenceInspection(
            inventory,
            representativeDevices,
            usbTopology,
            inputRoutes,
            rss,
            warnings.AsReadOnly());
    }

    private static UsbTopologySnapshot CaptureUsbTopologyOrUnavailable(
        DeviceInventorySnapshot inventory,
        List<string> warnings)
    {
        try
        {
            return UsbTopologyReader.Capture(inventory);
        }
        catch (Exception exception) when (IsRecoverableDeviceEvidenceException(exception))
        {
            warnings.Add($"USB topology unavailable: {exception.Message}");
            return new UsbTopologySnapshot(
                [],
                DateTimeOffset.UtcNow,
                [$"USB topology capture failed: {exception.Message}"]);
        }
    }

    private static UserInputRouteInventory CaptureInputRoutesOrUnavailable(
        DeviceInventorySnapshot inventory,
        UsbTopologySnapshot usbTopology,
        List<string> warnings)
    {
        try
        {
            return InputDeviceRouteReader.Capture(inventory, usbTopology);
        }
        catch (Exception exception) when (IsRecoverableDeviceEvidenceException(exception))
        {
            warnings.Add($"Raw Input route evidence unavailable: {exception.Message}");
            return new UserInputRouteInventory([], DateTimeOffset.UtcNow);
        }
    }

    private static NetworkRssSnapshot CaptureNetworkRssOrUnavailable(List<string> warnings)
    {
        try
        {
            var rss = NetworkRssReader.Capture();
            if (!rss.IsAvailable && !string.IsNullOrWhiteSpace(rss.Error))
            {
                warnings.Add($"RSS provider: {rss.Error}");
            }

            return rss;
        }
        catch (Exception exception) when (IsRecoverableDeviceEvidenceException(exception))
        {
            warnings.Add($"RSS provider unavailable: {exception.Message}");
            return new NetworkRssSnapshot(
                NetworkRssReadStatus.ReadFailed,
                [],
                DateTimeOffset.UtcNow,
                exception.Message);
        }
    }

    private static bool IsRecoverableDeviceEvidenceException(Exception exception) =>
        exception is ArgumentException or
            InvalidDataException or
            InvalidOperationException or
            Win32Exception or
            IOException or
            UnauthorizedAccessException or
            OverflowException or
            COMException;

    private async Task ShowDeviceEvidenceDialogAsync(DeviceEvidenceInspection inspection)
    {
        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(new TextBlock
        {
            Text = "Configuration evidence is not runtime evidence. Stored interrupt policy, allocated resources, exact USB route evidence, RSS provider state and host-observable input timing remain separate evidence layers.",
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Foreground = ThemeBrush("MutedTextBrush"),
        });

        if (inspection.Warnings.Count > 0)
        {
            AddSectionHeading(content, "Partial-read warnings");
            content.Children.Add(CreateMutedText(
                "Available sections remain usable. An unavailable optional provider is not silently promoted to evidence."));
            foreach (var warning in inspection.Warnings.Take(MaximumInspectorRowsPerSection))
            {
                content.Children.Add(CreateMutedText($"• {warning}"));
            }
            if (inspection.Warnings.Count > MaximumInspectorRowsPerSection)
            {
                content.Children.Add(CreateMutedText(
                    $"Showing the first {MaximumInspectorRowsPerSection} warnings; {inspection.Warnings.Count} were recorded."));
            }
        }

        AddSectionHeading(content, "Representative interrupt devices");
        if (inspection.RepresentativeDevices.Count == 0)
        {
            content.Children.Add(CreateMutedText(
                "No representative display, network or USBXHCI-bound device was found in the current present-device snapshot."));
        }
        else
        {
            foreach (var item in inspection.RepresentativeDevices)
            {
                content.Children.Add(BuildDeviceEvidencePanel(item));
            }
        }

        AddSectionHeading(content, "Input routes and USB ports");
        content.Children.Add(CreateMutedText(string.Create(
            CultureInfo.InvariantCulture,
            $"{inspection.InputRoutes.Routes.Count:N0} Raw Input route(s) · {inspection.InputRoutes.UsbBackedRouteCount:N0} USB-backed · {inspection.InputRoutes.ExactUsbPortRouteCount:N0} exact hub/port match(es). Timing actions below measure host-observable Raw Input dispatch intervals, not physical click-to-photon latency.")));
        foreach (var route in inspection.InputRoutes.Routes.Take(MaximumInspectorRowsPerSection))
        {
            content.Children.Add(BuildInputRoutePanel(route));
        }
        if (inspection.InputRoutes.Routes.Count > MaximumInspectorRowsPerSection)
        {
            content.Children.Add(CreateMutedText(
                $"Showing the first {MaximumInspectorRowsPerSection} input routes; the captured inventory contains {inspection.InputRoutes.Routes.Count}."));
        }

        AddSectionHeading(content, "Network RSS provider evidence");
        if (!inspection.NetworkRss.IsAvailable)
        {
            content.Children.Add(CreateMutedText(
                $"RSS provider evidence is {inspection.NetworkRss.Status}: {inspection.NetworkRss.Error ?? "no additional provider detail"}."));
        }
        else if (inspection.NetworkRss.Adapters.Count == 0)
        {
            content.Children.Add(CreateMutedText("StandardCimv2 returned no RSS setting rows."));
        }
        else
        {
            foreach (var adapter in inspection.NetworkRss.Adapters.Take(MaximumInspectorRowsPerSection))
            {
                content.Children.Add(BuildNetworkRssPanel(adapter));
            }
            if (inspection.NetworkRss.Adapters.Count > MaximumInspectorRowsPerSection)
            {
                content.Children.Add(CreateMutedText(
                    $"Showing the first {MaximumInspectorRowsPerSection} RSS rows; the provider returned {inspection.NetworkRss.Adapters.Count}."));
            }
        }

        var scroll = new ScrollViewer
        {
            MaxHeight = 620,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = content,
        };

        var dialog = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            Title = "Read-only device evidence",
            Content = scroll,
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close,
        };
        await dialog.ShowAsync();
    }

    private Border BuildInputRoutePanel(InputDeviceRouteSnapshot route)
    {
        var panel = CreateEvidencePanel();
        var stack = (StackPanel)panel.Child;
        var raw = route.RawInputDevice;

        stack.Children.Add(new TextBlock
        {
            Text = $"{raw.Kind} input route",
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = ThemeBrush("AccentBrush"),
        });
        stack.Children.Add(new TextBlock
        {
            Text = route.PnPDisplayName ?? raw.PnPInstanceId ?? "Unresolved Raw Input device",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Foreground = ThemeBrush("TextBrush"),
        });
        stack.Children.Add(CreateSelectableEvidenceText(
            $"PnP: {raw.PnPInstanceId ?? "—"}\nResolution: {raw.ResolutionStatus}"));

        var transport = route.IsBluetoothBacked
            ? $"Bluetooth ancestor {route.BluetoothAncestorInstanceId}"
            : route.IsUsbBacked
                ? $"USB via xHCI {route.UsbHostControllerInstanceId}"
                : "No authoritative USB/Bluetooth transport resolved";
        stack.Children.Add(CreateEvidenceLine("Transport", transport));

        if (route.UsbPortRoute is { } usbRoute)
        {
            if (usbRoute.IsAvailable && usbRoute.Port is { } port)
            {
                stack.Children.Add(CreateEvidenceLine(
                    "Exact USB port",
                    $"port {port.ConnectionIndex} · {port.Speed} · hub {port.HubInstanceId ?? port.HubDevicePath} · connection {port.ConnectionStatus}"));
            }
            else
            {
                stack.Children.Add(CreateEvidenceLine(
                    "Exact USB port",
                    $"{usbRoute.Status} · {usbRoute.Reason ?? "no unique documented driver-key match"}"));
            }
        }

        var timingText = CreateMutedText(
            "Host timing not measured. Keep the device active during the five-second capture.");
        stack.Children.Add(timingText);

        if (!string.IsNullOrWhiteSpace(raw.DeviceInterfacePath))
        {
            var measureButton = new Button
            {
                Content = "Measure 5 s host timing",
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            AutomationProperties.SetName(measureButton, $"Measure host timing for {route.PnPDisplayName ?? raw.Kind.ToString()}");
            AutomationProperties.SetHelpText(
                measureButton,
                "Measures Raw Input report dispatch intervals on this PC for five seconds. This is not click-to-photon latency.");
            measureButton.Click += async (_, _) =>
                await MeasureInputTimingAsync(route, measureButton, timingText);
            stack.Children.Add(measureButton);
        }

        return panel;
    }

    private static async Task MeasureInputTimingAsync(
        InputDeviceRouteSnapshot route,
        Button button,
        TextBlock resultText)
    {
        button.IsEnabled = false;
        resultText.Text = "Capturing host-observable Raw Input reports for five seconds…";
        try
        {
            var capture = await RawInputTimingCapture.CaptureAsync(
                route.RawInputDevice,
                InputTimingInspectionDuration);
            if (!capture.IsAvailable || capture.TimestampSeries is null)
            {
                resultText.Text = $"Timing unavailable: {capture.Status}. {capture.Error ?? "No timing series was produced."}";
                return;
            }

            var timing = InputTimingAnalyzer.Analyze(capture.TimestampSeries);
            if (timing.Status != InputTimingAnalysisStatus.Available)
            {
                resultText.Text =
                    $"Timing inconclusive: {timing.ReportCount:N0} report(s). {timing.Reason ?? "More host-observed reports are required."}";
                return;
            }

            resultText.Text = string.Create(
                CultureInfo.InvariantCulture,
                $"Host timing: {timing.ReportCount:N0} reports · median {FormatTimingMilliseconds(timing.MedianIntervalMilliseconds)} · p95 {FormatTimingMilliseconds(timing.P95IntervalMilliseconds)} · p99 {FormatTimingMilliseconds(timing.P99IntervalMilliseconds)} · observed {FormatTimingRate(timing.ObservedReportRateHz)} · tail jitter {FormatTimingMilliseconds(timing.TailJitterMilliseconds)} · long gaps {timing.LongGapCount:N0} · burst/coalescing intervals {timing.BurstIntervalCount:N0}. Scope: host Raw Input dispatch timing only.");
        }
        catch (Exception exception) when (exception is
            ArgumentException or
            InvalidDataException or
            InvalidOperationException or
            Win32Exception or
            IOException or
            UnauthorizedAccessException)
        {
            Logger.Warning(exception, "Host-observable Raw Input timing capture failed.");
            resultText.Text = $"Timing capture failed: {exception.Message}";
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    private Border BuildNetworkRssPanel(NetworkRssAdapterSnapshot adapter)
    {
        var panel = CreateEvidencePanel();
        var stack = (StackPanel)panel.Child;
        stack.Children.Add(new TextBlock
        {
            Text = "StandardCimv2 RSS",
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = ThemeBrush("AccentBrush"),
        });
        stack.Children.Add(new TextBlock
        {
            Text = adapter.Name ?? adapter.InterfaceDescription ?? "Unnamed network adapter",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Foreground = ThemeBrush("TextBrush"),
        });
        stack.Children.Add(CreateEvidenceLine(
            "Provider state",
            $"RSS {FormatNullableBoolean(adapter.Enabled)} · MSI {FormatNullableBoolean(adapter.MsiSupported)} · MSI-X supported {FormatNullableBoolean(adapter.MsiXSupported)} · MSI-X enabled {FormatNullableBoolean(adapter.MsiXEnabled)}"));
        stack.Children.Add(CreateEvidenceLine(
            "Capacity",
            $"queues {FormatNullableNumber(adapter.NumberOfReceiveQueues)} · interrupt messages {FormatNullableNumber(adapter.NumberOfInterruptMessages)} · max processors {FormatNullableNumber(adapter.MaxProcessors)} · profile {FormatNullableNumber(adapter.Profile)}"));
        stack.Children.Add(CreateEvidenceLine(
            "Processor range",
            $"base {FormatProcessor(adapter.BaseProcessorGroup, adapter.BaseProcessorNumber)} · max {FormatProcessor(adapter.MaxProcessorGroup, adapter.MaxProcessorNumber)} · NUMA {FormatNullableNumber(adapter.NumaNode)}"));
        stack.Children.Add(CreateSelectableEvidenceText(
            $"PnP correlation: {adapter.PnpCorrelation.Status} · {adapter.PnpCorrelation.PnpInstanceId ?? adapter.PnpCorrelation.Reason ?? "—"}"));

        var processors = adapter.RssProcessorArray.Take(16).ToArray();
        if (processors.Length > 0)
        {
            var suffix = adapter.RssProcessorArray.Count > processors.Length
                ? $" · +{adapter.RssProcessorArray.Count - processors.Length} more"
                : string.Empty;
            stack.Children.Add(CreateEvidenceLine("RSS processors", string.Join(", ", processors) + suffix));
        }

        return panel;
    }

    private Border BuildDeviceEvidencePanel(RepresentativeDeviceEvidence item)
    {
        var device = item.Device;
        var panel = CreateEvidencePanel();
        var stack = (StackPanel)panel.Child;
        stack.Children.Add(new TextBlock
        {
            Text = GetRepresentativeDeviceLabel(item.Kind),
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = ThemeBrush("AccentBrush"),
        });
        stack.Children.Add(new TextBlock
        {
            Text = device.DisplayName,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Foreground = ThemeBrush("TextBrush"),
        });
        stack.Children.Add(CreateSelectableEvidenceText(
            $"Instance: {device.InstanceId}\nService: {device.ServiceName ?? "—"} · Class: {device.ClassGuid:D}"));
        stack.Children.Add(CreateEvidenceLine("Driver", FormatDriverEvidence(item)));
        stack.Children.Add(CreateEvidenceLine("Stored configuration", FormatStoredInterruptEvidence(item)));
        stack.Children.Add(CreateEvidenceLine("Allocated resources", FormatAllocatedInterruptEvidence(item)));
        return panel;
    }

    private Border CreateEvidencePanel()
    {
        var panel = new Border
        {
            Padding = new Thickness(14),
            CornerRadius = new CornerRadius(12),
            Background = ThemeBrush("SurfaceAltBrush"),
            BorderBrush = ThemeBrush("BorderBrush"),
            BorderThickness = new Thickness(1),
        };
        panel.Child = new StackPanel { Spacing = 6 };
        return panel;
    }

    private void AddSectionHeading(StackPanel content, string title) =>
        content.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = ThemeBrush("TextBrush"),
            Margin = new Thickness(0, 8, 0, 0),
        });

    private TextBlock CreateEvidenceLine(string label, string value) =>
        new()
        {
            Text = $"{label}: {value}",
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Foreground = ThemeBrush("MutedTextBrush"),
        };

    private TextBlock CreateMutedText(string value) =>
        new()
        {
            Text = value,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Foreground = ThemeBrush("MutedTextBrush"),
        };

    private TextBlock CreateSelectableEvidenceText(string value) =>
        new()
        {
            Text = value,
            FontSize = 10,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
            Foreground = ThemeBrush("MutedTextBrush"),
        };

    private static string FormatTimingMilliseconds(double? value) =>
        value is null
            ? "—"
            : string.Create(CultureInfo.InvariantCulture, $"{value.Value:F3} ms");

    private static string FormatTimingRate(double? value) =>
        value is null
            ? "—"
            : string.Create(CultureInfo.InvariantCulture, $"{value.Value:F1} Hz");

    private static string FormatNullableBoolean(bool? value) => value switch
    {
        true => "on",
        false => "off",
        null => "—",
    };

    private static string FormatNullableNumber<T>(T? value)
        where T : struct =>
        value?.ToString() ?? "—";

    private static string FormatProcessor(ushort? group, byte? number) =>
        group is null || number is null
            ? "—"
            : string.Create(CultureInfo.InvariantCulture, $"{group.Value}:{number.Value}");

    private static string GetRepresentativeDeviceLabel(RepresentativeDeviceKind kind) =>
        kind switch
        {
            RepresentativeDeviceKind.DisplayAdapter => "Display / GPU class",
            RepresentativeDeviceKind.NetworkAdapter => "Network adapter class",
            RepresentativeDeviceKind.XhciController => "xHCI controller (USBXHCI service)",
            _ => "Representative device",
        };

    private static string FormatDriverEvidence(RepresentativeDeviceEvidence item)
    {
        var driver = item.Device.Driver;
        if (!driver.IsAvailable)
        {
            return "metadata unavailable";
        }

        return $"provider {driver.Provider ?? "—"} · version {driver.Version ?? "—"} · INF {driver.InfPath ?? "—"}";
    }

    private static string FormatStoredInterruptEvidence(RepresentativeDeviceEvidence item)
    {
        var configuration = item.Device.InterruptConfiguration;
        if (!item.StoredInterruptConfigurationAvailable)
        {
            var native = configuration.NativeErrorCode is null
                ? string.Empty
                : string.Create(CultureInfo.InvariantCulture, $" · native {configuration.NativeErrorCode.Value}");
            return configuration.ReadStatus + native;
        }

        var msi = configuration.MsiSupported switch
        {
            1 => "MSISupported=1 (stored configuration only)",
            0 => "MSISupported=0 (stored configuration only)",
            _ => "MSISupported unavailable",
        };
        var messageLimit = configuration.MessageNumberLimit is null
            ? "message limit —"
            : string.Create(CultureInfo.InvariantCulture, $"message limit {configuration.MessageNumberLimit.Value}");
        var policy = configuration.DevicePolicy is null
            ? "affinity policy —"
            : string.Create(CultureInfo.InvariantCulture, $"affinity policy {configuration.DevicePolicy.Value}");
        var overrideMask = configuration.AssignmentSetOverrideMask is null
            ? "override mask —"
            : string.Create(CultureInfo.InvariantCulture, $"override mask 0x{configuration.AssignmentSetOverrideMask.Value:X}");
        return $"{msi} · {messageLimit} · {policy} · {overrideMask}";
    }

    private static string FormatAllocatedInterruptEvidence(RepresentativeDeviceEvidence item)
    {
        var resources = item.Device.InterruptResources;
        if (!item.AllocatedInterruptResourcesAvailable)
        {
            var native = resources.NativeStatusCode is null
                ? string.Empty
                : string.Create(CultureInfo.InvariantCulture, $" · native {resources.NativeStatusCode.Value}");
            return resources.ReadStatus + native;
        }

        if (resources.Resources.Count == 0)
        {
            return "readable, no IRQ resource entries";
        }

        return string.Join(
            " | ",
            resources.Resources.Select(static resource =>
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"IRQ {resource.Irq} · group {resource.ProcessorGroup} · affinity 0x{resource.AffinityMask:X} · raw ConfigMgr flags 0x{resource.RawFlags:X4}")));
    }

    private sealed record DeviceEvidenceInspection(
        DeviceInventorySnapshot Inventory,
        IReadOnlyList<RepresentativeDeviceEvidence> RepresentativeDevices,
        UsbTopologySnapshot UsbTopology,
        UserInputRouteInventory InputRoutes,
        NetworkRssSnapshot NetworkRss,
        IReadOnlyList<string> Warnings);
}
