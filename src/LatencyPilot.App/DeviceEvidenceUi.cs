using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using LatencyPilot.App.Services;
using LatencyPilot.Benchmarking.Optimization;
using LatencyPilot.Core.Devices;
using LatencyPilot.Platform.Windows.Devices;
using LatencyPilot.Protocol;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace LatencyPilot.App;

public sealed partial class MainWindow
{
    private const int MaximumInspectorRowsPerSection = 12;
    private static readonly TimeSpan InputTimingInspectionDuration = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan NetworkInspectionDuration = TimeSpan.FromSeconds(5);
    private bool _networkSubsystemRunning;

    private async void InspectDeviceEvidenceButton_Click(object sender, RoutedEventArgs e)
    {
        InspectDeviceEvidenceButton.IsEnabled = false;
        DeviceEvidenceStatusText.Text = "Reading present PnP, USB route and RSS evidence…";

        try
        {
            var inspection = await Task.Run(CaptureDeviceEvidenceInspection);

            var rssSummary = inspection.NetworkRss.IsAvailable
                ? $"{inspection.NetworkRss.Adapters.Count(static adapter => adapter.RssSettingsAvailable):N0} RSS row(s) · " +
                  $"{inspection.NetworkRss.Adapters.Count(static adapter => !adapter.RssSettingsAvailable && adapter.PnpCorrelation.IsAvailable):N0} physical adapter(s) without RSS settings"
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

    private async void UsbSubsystemButton_Click(object sender, RoutedEventArgs e) =>
        await RunUsbXhciWorkflowAsync();

    private async void NetworkSubsystemButton_Click(object sender, RoutedEventArgs e)
    {
        if (_networkSubsystemRunning ||
            _usbSubsystemRunning ||
            _gateAValidationRunning ||
            _measurementBusy ||
            _manualDeviceAffinityBusy)
        {
            NetworkEvidenceText.Text =
                "Network/RSS analysis is unavailable while another measurement or mutation session owns the hardware.";
            return;
        }

        _networkSubsystemRunning = true;
        NetworkSubsystemButton.IsEnabled = false;
        UsbSubsystemButton.IsEnabled = false;
        GpuGateAEntryButton.IsEnabled = false;
        ManualDeviceAffinityButton.IsEnabled = false;
        SetObservationControlsBusy(_measurementBusy);
        UpdateScenarioSelectionEnabledState();

        try
        {
            NetworkEvidenceText.Text = "Resolving the active physical network adapter…";
            var inspection = await Task.Run(CaptureDeviceEvidenceInspection);
            var target = await SelectNetworkRssTargetAsync(inspection.NetworkRss);
            if (target is null)
            {
                NetworkEvidenceText.Text =
                    "Network/RSS analysis unavailable · no physical network adapter was selected.";
                return;
            }

            var targetDevice = inspection.Inventory.Devices.FirstOrDefault(device =>
                target.PnpCorrelation.IsAvailable &&
                string.Equals(
                    device.InstanceId,
                    target.PnpCorrelation.PnpInstanceId,
                    StringComparison.OrdinalIgnoreCase));

            var before = NetworkEnvironmentContinuity.CaptureForTarget(
                target.PnpCorrelation.PnpInstanceId!);
            NetworkRuntimeSummary? attribution = null;
            NetworkEnvironmentContinuityResult? continuity = null;
            NetworkTrafficDelta? traffic = null;
            string? runtimeReason = null;

            if (targetDevice is null)
            {
                runtimeReason =
                    "The selected RSS row no longer maps to a present PnP device; configuration evidence remains available.";
            }
            else if (!await EnsureObservationServiceReadyAsync())
            {
                runtimeReason =
                    "The observation service is unavailable, so only current RSS configuration evidence can be shown.";
            }
            else
            {
                NetworkEvidenceText.Text =
                    $"Capturing {NetworkInspectionDuration.TotalSeconds:F0} s of read-only miniport DPC/ISR evidence on {target.Name ?? target.InterfaceDescription ?? "the physical NIC"}…";
                var capture = await ObservationServiceClient.CaptureKernelLatencyAsync(
                    NetworkInspectionDuration,
                    ObservationMaximumEvents);

                attribution = AnalyzeNetworkRuntime(capture, targetDevice);
                var after = NetworkEnvironmentContinuity.CaptureForTarget(
                    target.PnpCorrelation.PnpInstanceId!);
                if (before.IsAvailable && before.Snapshot is not null &&
                    after.IsAvailable && after.Snapshot is not null)
                {
                    continuity = NetworkEnvironmentContinuity.Evaluate(
                        before.Snapshot,
                        after.Snapshot);
                    traffic = NetworkEnvironmentContinuity.MeasureTraffic(
                        before.Snapshot,
                        after.Snapshot);
                }
                else
                {
                    runtimeReason =
                        before.Reason ??
                        after.Reason ??
                        "Network continuity could not be proven across the runtime capture.";
                }
            }

            NetworkEvidenceText.Text = attribution is null
                ? $"Network/RSS configuration ready · {target.Name ?? target.InterfaceDescription ?? "physical NIC"}."
                : attribution.HasTargetEvidence && continuity?.IsStable == true
                    ? $"Network/RSS runtime evidence captured · {target.Name ?? target.InterfaceDescription ?? "physical NIC"}."
                    : traffic is { IsAvailable: true, HasTraffic: false }
                        ? $"Network/RSS capture complete · interface was idle during the sample."
                        : $"Network/RSS capture complete · target miniport runtime evidence was not trusted.";

            await ShowNetworkSubsystemDialogAsync(
                target,
                targetDevice,
                attribution,
                continuity,
                traffic,
                runtimeReason);
        }
        catch (Exception exception) when (IsRecoverableDeviceEvidenceException(exception))
        {
            Logger.Error(exception, "Network/RSS analysis failed.");
            NetworkEvidenceText.Text = $"Network/RSS analysis unavailable · {exception.Message}";
        }
        finally
        {
            _networkSubsystemRunning = false;
            NetworkSubsystemButton.IsEnabled = true;
            UsbSubsystemButton.IsEnabled = true;
            GpuGateAEntryButton.IsEnabled = true;
            ManualDeviceAffinityButton.IsEnabled = true;
            SetObservationControlsBusy(_measurementBusy);
            UpdateScenarioSelectionEnabledState();
            if (_gateASourceAssessment is not null)
            {
                ApplyGateASourceAssessmentUi(_gateASourceAssessment);
            }
        }
    }

    private async Task<NetworkRssAdapterSnapshot?> SelectNetworkRssTargetAsync(
        NetworkRssSnapshot snapshot)
    {
        if (!snapshot.IsAvailable)
        {
            await ShowSimpleNetworkMessageAsync(
                "Network / RSS unavailable",
                $"Windows RSS provider evidence is {snapshot.Status}: {snapshot.Error ?? "no additional detail"}.");
            return null;
        }

        var physical = NetworkRssPhysicalAdapterSelector.Select(snapshot).ToArray();
        if (physical.Length == 0)
        {
            await ShowSimpleNetworkMessageAsync(
                "No physical network adapter",
                "Windows did not expose a PnP-correlated physical network adapter. RSS settings are optional here; virtual switches, debug adapters and other software interfaces remain excluded.");
            return null;
        }

        var candidates = physical
            .Select(adapter => (
                Adapter: adapter,
                Continuity: NetworkEnvironmentContinuity.Capture(adapter)))
            .ToArray();
        var active = candidates
            .Where(static candidate => candidate.Continuity.IsAvailable)
            .ToArray();

        if (active.Length == 1)
        {
            return active[0].Adapter;
        }

        if (physical.Length == 1)
        {
            return physical[0];
        }

        var source = active.Length > 0 ? active : candidates;
        var picker = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinWidth = 420d,
        };
        foreach (var candidate in source)
        {
            picker.Items.Add(new ComboBoxItem
            {
                Content =
                    $"{candidate.Adapter.Name ?? candidate.Adapter.InterfaceDescription ?? "Network adapter"}" +
                    (candidate.Continuity.IsAvailable ? " · active" : " · configuration only"),
                Tag = candidate.Adapter.PnpCorrelation.PnpInstanceId,
            });
        }
        picker.SelectedIndex = 0;

        var dialog = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            Title = "Select physical network adapter",
            Content = new StackPanel
            {
                Spacing = 10d,
                Children =
                {
                    CreateMutedText(
                        "LatencyPilot resolves the physical NIC first and treats RSS as optional multi-CPU receive-steering evidence. Virtual switches and debug/software adapters are excluded from this subsystem action."),
                    picker,
                },
            },
            PrimaryButtonText = "Analyze this adapter",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary ||
            picker.SelectedItem is not ComboBoxItem selected ||
            selected.Tag is not string selectedPnpId)
        {
            return null;
        }

        return source
            .Select(static candidate => candidate.Adapter)
            .Single(adapter =>
                string.Equals(
                    adapter.PnpCorrelation.PnpInstanceId,
                    selectedPnpId,
                    StringComparison.OrdinalIgnoreCase));
    }

    private async Task ShowNetworkSubsystemDialogAsync(
        NetworkRssAdapterSnapshot adapter,
        PnPDeviceSnapshot? device,
        NetworkRuntimeSummary? attribution,
        NetworkEnvironmentContinuityResult? continuity,
        NetworkTrafficDelta? traffic,
        string? runtimeReason)
    {
        var processorSet = adapter.RssProcessorArray
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var runtimeVerified =
            attribution?.HasTargetEvidence == true &&
            continuity?.IsStable == true;
        var runtimeIdle =
            attribution is not null &&
            traffic is { IsAvailable: true, HasTraffic: false };
        var runtimeLabel = runtimeVerified
            ? "runtime evidence captured"
            : attribution?.HasTargetEvidence == true
                ? continuity is { IsStable: false }
                    ? "runtime evidence invalidated"
                    : "runtime continuity unproven"
                : runtimeIdle
                    ? "runtime sample idle"
                    : attribution is not null
                        ? "runtime attribution unproven"
                        : "configuration evidence";
        var headline = !adapter.RssSettingsAvailable
            ? $"Physical NIC · RSS settings unavailable · {runtimeLabel}"
            : adapter.Enabled == false
                ? $"Physical NIC · RSS disabled · {runtimeLabel}"
                : $"Physical NIC · {runtimeLabel}";

        var content = new StackPanel { Spacing = 10d };
        content.Children.Add(new TextBlock
        {
            Text = headline,
            FontWeight = FontWeights.SemiBold,
            Foreground = ThemeBrush(
                adapter.Enabled == false
                    ? "SemanticAttentionBrush"
                    : runtimeVerified
                        ? "SemanticGoodBrush"
                        : "TextBrush"),
        });
        content.Children.Add(CreateEvidenceLine(
            "Adapter",
            adapter.Name ?? adapter.InterfaceDescription ?? "Unnamed physical adapter"));
        content.Children.Add(CreateEvidenceLine(
            "RSS",
            !adapter.RssSettingsAvailable
                ? "Settings not exposed by Windows/driver"
                : adapter.Enabled switch
                {
                    true => "Enabled",
                    false => "Disabled",
                    null => "State unavailable",
                }));
        content.Children.Add(CreateEvidenceLine(
            "Receive steering",
            !adapter.RssSettingsAvailable
                ? "RSS queue/processor settings unavailable; runtime miniport analysis still runs"
                : $"{FormatNullableNumber(adapter.NumberOfReceiveQueues)} queue(s) · " +
                  $"{(processorSet.Length == 0 ? "processor set unavailable" : $"{processorSet.Length} RSS processor(s)")}" +
                  (adapter.MaxProcessors is { } max ? $" · max {max}" : string.Empty)));
        content.Children.Add(CreateEvidenceLine(
            "MSI-X",
            adapter.MsiXEnabled == true
                ? "Enabled"
                : adapter.MsiXSupported == true
                    ? "Supported · not reported enabled"
                    : adapter.MsiXSupported == false
                        ? "Not supported"
                        : adapter.MsiSupported == true
                            ? "MSI supported · MSI-X state not reported"
                            : adapter.MsiSupported == false
                                ? "MSI not supported · MSI-X state not reported"
                                : adapter.HardwareInfoAvailable
                                    ? "Hardware-info provider available · MSI/MSI-X fields not reported"
                                    : "Unavailable"));

        if (attribution is not null)
        {
            content.Children.Add(CreateEvidenceLine(
                "Network activity",
                traffic?.IsAvailable == true
                    ? $"{FormatNetworkBytes(traffic.BytesReceived)} received · {FormatNetworkBytes(traffic.BytesSent)} sent"
                    : "Unavailable"));
            content.Children.Add(CreateEvidenceLine(
                "Miniport runtime",
                $"{attribution.MatchingDpcEventCount} DPC · {attribution.MatchingIsrEventCount} ISR · {attribution.TotalDurationMicroseconds:F1} us total"));
            content.Children.Add(CreateEvidenceLine(
                "Miniport tail",
                $"DPC p99 {FormatNetworkMicroseconds(attribution.DpcP99Microseconds)} · ISR p99 {FormatNetworkMicroseconds(attribution.IsrP99Microseconds)}"));
            content.Children.Add(CreateEvidenceLine(
                "Capture integrity",
                attribution.CaptureIntegrityValid
                    ? attribution.ModuleListTruncated
                        ? "Valid · module contributor list truncated"
                        : "Valid"
                    : "Invalid"));
        }

        content.Children.Add(CreateEvidenceLine(
            "Environment continuity",
            continuity is not null
                ? continuity.IsStable
                    ? "Stable during capture"
                    : "Changed during capture"
                : "Not proven"));

        content.Children.Add(CreateMutedText(
            adapter.RssSettingsAvailable
                ? "No network settings were changed. RSS is intentionally multi-CPU; v1 does not force the NIC onto a single CPU or rewrite its RSS profile."
                : "No network settings were changed. This driver did not expose an RSS settings row, but LatencyPilot still analyzes the physical NIC and its miniport runtime evidence."));

        if (attribution is not null && !attribution.HasTargetEvidence)
        {
            content.Children.Add(CreateMutedText(
                traffic is { IsAvailable: true, HasTraffic: false }
                    ? "No interface traffic was observed during the capture. A zero DPC/ISR sample is therefore idle evidence, not proof that this NIC has no interrupt cost; re-run while network traffic is active."
                    : traffic is { IsAvailable: true, HasTraffic: true }
                        ? "Interface traffic occurred, but the target miniport module was not observed in the bounded kernel contributor sample. Runtime attribution remains unproven."
                        : "The bounded capture did not prove target-miniport runtime activity, and interface traffic counters were unavailable."));
        }

        var technical = new StackPanel { Spacing = 7d };
        technical.Children.Add(CreateSelectableEvidenceText(
            $"PnP: {adapter.PnpCorrelation.PnpInstanceId ?? "—"}\n" +
            $"Interface: {adapter.InterfaceDescription ?? "—"}\n" +
            $"Hardware interface: {FormatNullableBoolean(adapter.HardwareInterface)} · connector present: {FormatNullableBoolean(adapter.ConnectorPresent)}"));
        technical.Children.Add(CreateEvidenceLine(
            "RSS profile",
            FormatNullableNumber(adapter.Profile)));
        technical.Children.Add(CreateEvidenceLine(
            "NIC hardware info",
            adapter.HardwareInfoAvailable
                ? "MSFT_NetAdapterHardwareInfoSettingData available"
                : "Not exposed"));
        technical.Children.Add(CreateEvidenceLine(
            "Processor range",
            $"base {FormatProcessor(adapter.BaseProcessorGroup, adapter.BaseProcessorNumber)} · " +
            $"max {FormatProcessor(adapter.MaxProcessorGroup, adapter.MaxProcessorNumber)} · " +
            $"NUMA {FormatNullableNumber(adapter.NumaNode)}"));
        technical.Children.Add(CreateEvidenceLine(
            "RSS processors",
            processorSet.Length == 0 ? "—" : string.Join(", ", processorSet.Take(24))));
        if (adapter.IndirectionTable.Count > 0)
        {
            technical.Children.Add(CreateEvidenceLine(
                "Indirection table",
                string.Join(", ", adapter.IndirectionTable.Take(24)) +
                (adapter.IndirectionTable.Count > 24
                    ? $" · +{adapter.IndirectionTable.Count - 24} more"
                    : string.Empty)));
        }

        if (device is not null)
        {
            technical.Children.Add(CreateEvidenceLine(
                "Miniport service",
                device.ServiceName ?? "—"));
            technical.Children.Add(CreateEvidenceLine(
                "Driver",
                device.Driver.IsAvailable
                    ? $"{device.Driver.Provider ?? "—"} · {device.Driver.Version ?? "—"} · {device.Driver.InfPath ?? "—"}"
                    : "metadata unavailable"));
        }

        if (attribution is not null)
        {
            technical.Children.Add(CreateEvidenceLine(
                "Generic NDIS events",
                attribution.GenericNdisEventCount.ToString(CultureInfo.InvariantCulture)));
            technical.Children.Add(CreateEvidenceLine(
                "Unresolved interrupt events",
                attribution.UnresolvedInterruptEventCount.ToString(CultureInfo.InvariantCulture)));
        }

        if (continuity is { IsStable: false })
        {
            technical.Children.Add(CreateMutedText(
                "Continuity warnings:" + Environment.NewLine +
                string.Join(Environment.NewLine, continuity.Reasons.Select(static reason => $"• {reason}"))));
        }
        if (!string.IsNullOrWhiteSpace(runtimeReason))
        {
            technical.Children.Add(CreateMutedText(runtimeReason));
        }

        content.Children.Add(new Expander
        {
            Header = "Technical details",
            Content = technical,
            IsExpanded = attribution is null || continuity is { IsStable: false },
            HorizontalAlignment = HorizontalAlignment.Stretch,
        });

        await ShowFocusedDeviceEvidenceDialogAsync("Network / RSS analysis", content);
    }

    private async Task ShowSimpleNetworkMessageAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            Title = title,
            Content = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
            },
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close,
        };
        await dialog.ShowAsync();
    }

    private static NetworkRuntimeSummary AnalyzeNetworkRuntime(
        KernelLatencyCaptureResponse capture,
        PnPDeviceSnapshot adapter)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(adapter);

        if (string.IsNullOrWhiteSpace(adapter.ServiceName))
        {
            return new NetworkRuntimeSummary(
                0,
                0,
                0,
                capture.UnresolvedModuleEventCount,
                0d,
                null,
                null,
                IsCaptureIntegrityValid(capture),
                capture.ModuleContributorListTruncated,
                false);
        }

        var serviceName = NormalizeModuleStem(adapter.ServiceName);
        var matching = capture.Modules
            .Where(module =>
                string.Equals(
                    NormalizeModuleStem(module.ModuleName),
                    serviceName,
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    NormalizeModuleStem(module.ImagePath),
                    serviceName,
                    StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var ndis = capture.Modules
            .Where(module =>
                string.Equals(
                    NormalizeModuleStem(module.ModuleName),
                    "ndis",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    NormalizeModuleStem(module.ImagePath),
                    "ndis",
                    StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return new NetworkRuntimeSummary(
            matching.Sum(static module => module.Dpc.Count),
            matching.Sum(static module => module.Isr.Count),
            ndis.Sum(static module => module.Dpc.Count + module.Isr.Count),
            capture.UnresolvedModuleEventCount,
            matching.Sum(static module => module.TotalDurationMicroseconds),
            MaxNullable(matching.Select(static module => module.Dpc.P99Microseconds)),
            MaxNullable(matching.Select(static module => module.Isr.P99Microseconds)),
            IsCaptureIntegrityValid(capture),
            capture.ModuleContributorListTruncated,
            matching.Length > 0);
    }

    private static bool IsCaptureIntegrityValid(KernelLatencyCaptureResponse capture) =>
        capture.EventsLost == 0 &&
        capture.InvalidEventCount == 0 &&
        capture.InvalidImageEventCount == 0 &&
        !capture.EventLimitReached;

    private static string NormalizeModuleStem(string value)
    {
        var fileName = Path.GetFileName(value.Trim());
        return Path.GetFileNameWithoutExtension(fileName);
    }

    private static double? MaxNullable(IEnumerable<double?> values)
    {
        var materialized = values
            .Where(static value => value is not null)
            .Select(static value => value!.Value)
            .ToArray();
        return materialized.Length == 0 ? null : materialized.Max();
    }

    private static string FormatNetworkMicroseconds(double? value) =>
        value is null
            ? "—"
            : string.Create(CultureInfo.InvariantCulture, $"{value.Value:F1} us");

    private static string FormatNetworkBytes(long? value)
    {
        if (value is null)
        {
            return "—";
        }

        var bytes = value.Value;
        if (bytes < 1024)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{bytes} B");
        }

        var kibibytes = bytes / 1024d;
        if (kibibytes < 1024d)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{kibibytes:F1} KiB");
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{kibibytes / 1024d:F1} MiB");
    }

    private sealed record NetworkRuntimeSummary(
        int MatchingDpcEventCount,
        int MatchingIsrEventCount,
        int GenericNdisEventCount,
        int UnresolvedInterruptEventCount,
        double TotalDurationMicroseconds,
        double? DpcP99Microseconds,
        double? IsrP99Microseconds,
        bool CaptureIntegrityValid,
        bool ModuleListTruncated,
        bool ServiceModuleObserved)
    {
        public bool HasTargetEvidence =>
            CaptureIntegrityValid &&
            ServiceModuleObserved &&
            MatchingDpcEventCount + MatchingIsrEventCount > 0;
    }

    private async Task ShowFocusedDeviceEvidenceDialogAsync(string title, StackPanel content)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            Title = title,
            Content = new ScrollViewer
            {
                MaxHeight = 620,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = content,
            },
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close,
        };
        await dialog.ShowAsync();
    }

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

        AddSectionHeading(content, "Network adapter / RSS evidence");
        if (!inspection.NetworkRss.IsAvailable)
        {
            content.Children.Add(CreateMutedText(
                $"RSS provider evidence is {inspection.NetworkRss.Status}: {inspection.NetworkRss.Error ?? "no additional provider detail"}."));
        }
        else if (inspection.NetworkRss.Adapters.Count == 0)
        {
            content.Children.Add(CreateMutedText("StandardCimv2 returned no physical adapter or RSS setting evidence."));
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
                    $"Showing the first {MaximumInspectorRowsPerSection} network evidence rows; {inspection.NetworkRss.Adapters.Count} were captured."));
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
            Text = adapter.RssSettingsAvailable
                ? "StandardCimv2 RSS"
                : "Physical network adapter",
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
            "RSS settings",
            adapter.RssSettingsAvailable
                ? $"RSS {FormatNullableBoolean(adapter.Enabled)} · MSI {FormatNullableBoolean(adapter.MsiSupported)} · MSI-X supported {FormatNullableBoolean(adapter.MsiXSupported)} · MSI-X enabled {FormatNullableBoolean(adapter.MsiXEnabled)}"
                : "Not exposed by Windows/driver for this physical adapter"));
        if (adapter.RssSettingsAvailable)
        {
            stack.Children.Add(CreateEvidenceLine(
                "Capacity",
                $"queues {FormatNullableNumber(adapter.NumberOfReceiveQueues)} · interrupt messages {FormatNullableNumber(adapter.NumberOfInterruptMessages)} · max processors {FormatNullableNumber(adapter.MaxProcessors)} · profile {FormatNullableNumber(adapter.Profile)}"));
            stack.Children.Add(CreateEvidenceLine(
                "Processor range",
                $"base {FormatProcessor(adapter.BaseProcessorGroup, adapter.BaseProcessorNumber)} · max {FormatProcessor(adapter.MaxProcessorGroup, adapter.MaxProcessorNumber)} · NUMA {FormatNullableNumber(adapter.NumaNode)}"));
        }
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
