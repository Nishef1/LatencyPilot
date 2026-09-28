using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using LatencyPilot.Core.Devices;
using LatencyPilot.Core.System;
using LatencyPilot.Persistence;
using LatencyPilot.Platform.Windows.Devices;
using LatencyPilot.Platform.Windows.System;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace LatencyPilot.App;

public sealed partial class MainWindow
{
    private bool _manualDeviceAffinityBusy;
    private bool _manualDeviceAffinityDialogOpening;
    private Window? _manualDeviceAffinityWindow;
    private readonly Dictionary<string, ulong> _manualAffinityDraftMasks =
        new(StringComparer.OrdinalIgnoreCase);

    internal void InitializeManualDeviceAffinityExperience()
    {
        ManualDeviceAffinityCard.Visibility =
            IsDevelopmentGateAAvailable(_gateARepositoryRoot) &&
            File.Exists(Path.Combine(
                _gateARepositoryRoot!,
                "tools",
                "LatencyPilot.GateAValidation",
                "LatencyPilot.GateAValidation.csproj"))
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    private async void ManualDeviceAffinityButton_Click(object sender, RoutedEventArgs e)
    {
        if (_manualDeviceAffinityWindow is not null)
        {
            _manualDeviceAffinityWindow.Activate();
            return;
        }

        if (_manualDeviceAffinityBusy || _manualDeviceAffinityDialogOpening)
        {
            return;
        }

        if (_gateAValidationRunning)
        {
            ManualDeviceAffinityStatusText.Text =
                "Manual affinity is blocked while GPU Gate A owns the mutation/measurement session.";
            return;
        }

        _manualDeviceAffinityDialogOpening = true;
        ManualDeviceAffinityButton.IsEnabled = false;
        ManualDeviceAffinityStatusText.Text = "Reading current interrupt-affinity policy and allocated resources…";
        try
        {
            var snapshot = await Task.Run(CaptureManualAffinitySnapshot);
            var windowStatusText = new TextBlock
            {
                Text = "Inspection complete · No system changes made.",
                TextWrapping = TextWrapping.Wrap,
                Style = AppStyle("CaptionTextStyle"),
                Foreground = ThemeBrush("MutedTextBrush"),
                VerticalAlignment = VerticalAlignment.Center,
            };
            var host = new StackPanel
            {
                Spacing = 12d,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                MaxWidth = 1140d,
            };
            RenderManualAffinityWorkspace(host, snapshot, windowStatusText);
            OpenManualAffinityWindow(host);
            ManualDeviceAffinityStatusText.Text = "Inspection complete. No system changes have been made.";
        }
        catch (Exception exception) when (exception is
            InvalidOperationException or
            IOException or
            UnauthorizedAccessException or
            Win32Exception or
            JsonException)
        {
            Logger.Error(exception, "Manual device affinity UI failed.");
            ManualDeviceAffinityStatusText.Text =
                $"Manual affinity could not be opened: {exception.Message}";
        }
        finally
        {
            _manualDeviceAffinityDialogOpening = false;
            ManualDeviceAffinityButton.IsEnabled = true;
        }
    }

    private void OpenManualAffinityWindow(StackPanel host)
    {
        if (_manualDeviceAffinityWindow is not null)
        {
            _manualDeviceAffinityWindow.Activate();
            return;
        }

        var root = new Grid
        {
            RequestedTheme = RootGrid.ActualTheme,
            Background = ThemeBrush("CanvasBrush"),
        };
        root.Children.Add(new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new Border
            {
                Padding = new Thickness(24d, 20d, 24d, 24d),
                Child = host,
            },
        });

        var window = new Window
        {
            Title = "Interrupt affinity policy",
            Content = root,
            SystemBackdrop = new MicaBackdrop(),
        };

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (File.Exists(iconPath))
        {
            window.AppWindow.SetIcon(iconPath);
        }

        var workArea = DisplayArea
            .GetFromWindowId(window.AppWindow.Id, DisplayAreaFallback.Primary)
            .WorkArea;
        var width = Math.Min(1280, workArea.Width);
        var height = Math.Min(860, workArea.Height);
        window.AppWindow.MoveAndResize(new RectInt32(
            workArea.X + (workArea.Width - width) / 2,
            workArea.Y + (workArea.Height - height) / 2,
            width,
            height));

        ApplyManualAffinityCaptionTheme(window);
        root.ActualThemeChanged += (_, _) => ApplyManualAffinityCaptionTheme(window);

        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_manualDeviceAffinityWindow, window))
            {
                _manualDeviceAffinityWindow = null;
                _manualAffinityDraftMasks.Clear();
            }
        };

        _manualDeviceAffinityWindow = window;
        window.Activate();
    }

    private void ApplyManualAffinityCaptionTheme(Window window)
    {
        if (!AppWindowTitleBar.IsCustomizationSupported())
        {
            return;
        }

        var titleBar = window.AppWindow.TitleBar;
        var foreground = ((SolidColorBrush)ThemeBrush("TextBrush")).Color;
        var background = ((SolidColorBrush)ThemeBrush("SurfaceStrongBrush")).Color;
        var hoverBackground = ((SolidColorBrush)ThemeBrush("SurfaceHoverBrush")).Color;
        var pressedBackground = ((SolidColorBrush)ThemeBrush("SurfaceAltBrush")).Color;
        var mutedForeground = ((SolidColorBrush)ThemeBrush("MutedTextBrush")).Color;

        titleBar.ForegroundColor = foreground;
        titleBar.BackgroundColor = background;
        titleBar.InactiveForegroundColor = mutedForeground;
        titleBar.InactiveBackgroundColor = background;
        titleBar.ButtonForegroundColor = foreground;
        titleBar.ButtonBackgroundColor = background;
        titleBar.ButtonHoverForegroundColor = foreground;
        titleBar.ButtonHoverBackgroundColor = hoverBackground;
        titleBar.ButtonPressedForegroundColor = foreground;
        titleBar.ButtonPressedBackgroundColor = pressedBackground;
        titleBar.ButtonInactiveForegroundColor = mutedForeground;
        titleBar.ButtonInactiveBackgroundColor = background;
    }
    private ManualAffinitySnapshot CaptureManualAffinitySnapshot()
    {
        var inventory = DeviceInventoryReader.CapturePresentDevices();
        var topology = ProcessorTopologyReader.Capture();
        var (pendingByTarget, journalInspectionError) = ReadPendingManualAffinityEntries();
        var classified = LatencySensitiveDeviceSelector.Select(inventory).Devices
            .ToDictionary(
                static evidence => evidence.Device.InstanceId,
                static evidence => evidence.Kind,
                StringComparer.OrdinalIgnoreCase);

        var cpuOptions = topology.Cores
            .SelectMany(core => core.LogicalProcessors.Select(
                processor => new ManualAffinityCpuOption(processor, core.Index)))
            .Where(static option => option.Processor.Group == 0 && option.Processor.Number < 64)
            .OrderBy(static option => option.Processor.Number)
            .ToArray();

        var rows = inventory.Devices
            .Select(device =>
            {
                classified.TryGetValue(device.InstanceId, out var kind);
                var targetKind = ManualDeviceAffinityPolicyEligibility.ClassifyTarget(device) switch
                {
                    ManualDeviceAffinityPolicyTargetKind.Gpu => "Gpu",
                    ManualDeviceAffinityPolicyTargetKind.Xhci => "Xhci",
                    _ => "Device",
                };
                pendingByTarget.TryGetValue(device.InstanceId, out var pendingRecovery);
                var pendingMask = TryGetPendingAffinityMask(pendingRecovery);
                var canStartNewPolicyMutation =
                    ManualDeviceAffinityPolicyEligibility.CanStartNewPolicyMutation(
                        device,
                        out var inspectionOnlyReason);
                return new ManualAffinityDeviceRow(
                    device,
                    classified.ContainsKey(device.InstanceId) ? kind : null,
                    targetKind,
                    FormatStoredAffinity(device.InterruptConfiguration),
                    FormatAllocatedAffinity(device.InterruptResources),
                    device.InterruptConfiguration.AssignmentSetOverrideMask,
                    device.InterruptConfiguration.DevicePolicy == 4 &&
                    device.InterruptConfiguration.AssignmentSetOverrideMask is not null,
                    pendingRecovery,
                    pendingMask,
                    canStartNewPolicyMutation,
                    inspectionOnlyReason);
            })
            .OrderByDescending(static row => row.CanStartNewPolicyMutation)
            .ThenByDescending(static row => row.HasPendingRecovery)
            .ThenByDescending(static row => row.HasExplicitOverride)
            .ThenBy(static row => row.Kind?.ToString(), StringComparer.OrdinalIgnoreCase)
            .ThenBy(static row => row.Device.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new ManualAffinitySnapshot(rows, cpuOptions, journalInspectionError);
    }

    private static ulong? TryGetPendingAffinityMask(MutationJournalEntry? entry)
    {
        if (entry is null ||
            entry.State != MutationJournalState.ApplyRebootPending)
        {
            return null;
        }

        try
        {
            var candidate = DeviceInterruptMutationJournalCodec
                .DeserializeCandidate(entry.CandidateStateJson);
            return candidate.Operation is
                DeviceInterruptMutationOperation.DeviceAffinity or
                DeviceInterruptMutationOperation.XhciAffinity
                    ? candidate.AffinityMask
                    : null;
        }
        catch (Exception exception) when (exception is
            InvalidDataException or
            JsonException or
            FormatException or
            OverflowException)
        {
            Logger.Warning(
                exception,
                "Could not decode pending manual affinity mask for {TargetId}.",
                entry.TargetId);
            return null;
        }
    }

    private static (IReadOnlyDictionary<string, MutationJournalEntry> Entries, string? Error)
        ReadPendingManualAffinityEntries()
    {
        try
        {
            var unresolved = MutationJournalReadOnlyInspector.GetUnresolved(
                MutationJournal.GetDefaultDatabasePath());
            var entries = unresolved
                .GroupBy(static entry => entry.TargetId, StringComparer.OrdinalIgnoreCase)
                .Where(static group => group.Count() == 1)
                .ToDictionary(
                    static group => group.Key,
                    static group => group.Single(),
                    StringComparer.OrdinalIgnoreCase);
            return (entries, null);
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Manual affinity UI could not inspect the mutation journal read-only.");
            return (
                new Dictionary<string, MutationJournalEntry>(StringComparer.OrdinalIgnoreCase),
                $"Recovery journal unavailable: {exception.Message}");
        }
    }

    private void RenderManualAffinityWorkspace(
        StackPanel host,
        ManualAffinitySnapshot snapshot,
        TextBlock dialogStatusText,
        string? selectedDeviceInstanceId = null)
    {
        host.Children.Clear();

        var titleGrid = new Grid { ColumnSpacing = 12d };
        titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1d, GridUnitType.Star) });
        titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        titleGrid.Children.Add(BuildManualAffinityIconTile("\uE950", 44d, 19d));

        var titleText = new StackPanel { Spacing = 2d, VerticalAlignment = VerticalAlignment.Center };
        titleText.Children.Add(new TextBlock
        {
            Text = "Interrupt affinity policy",
            Style = AppStyle("SectionTitleTextStyle"),
        });
        titleText.Children.Add(new TextBlock
        {
            Text = "Inspect interrupt-affinity evidence for present devices and edit the documented Windows policy only for supported device classes.",
            TextWrapping = TextWrapping.Wrap,
            Style = AppStyle("CaptionTextStyle"),
        });
        Grid.SetColumn(titleText, 1);
        titleGrid.Children.Add(titleText);

        var experimentalBadge = BuildManualAffinityPill(
            "Experimental",
            "AccentBrush",
            "PremiumOverviewQuietBrush");
        experimentalBadge.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(experimentalBadge, 2);
        titleGrid.Children.Add(experimentalBadge);
        host.Children.Add(titleGrid);

        host.Children.Add(BuildManualAffinityStatusBanner(dialogStatusText));

        if (snapshot.JournalInspectionError is { } journalInspectionError)
        {
            host.Children.Add(new Border
            {
                Padding = new Thickness(10d),
                CornerRadius = new CornerRadius(8d),
                Background = ThemeBrush("SurfaceAltBrush"),
                BorderBrush = ThemeBrush("BorderBrush"),
                BorderThickness = new Thickness(1d),
                Child = new TextBlock
                {
                    Text = $"{journalInspectionError} No recovery action is enabled until the journal can be read safely.",
                    TextWrapping = TextWrapping.Wrap,
                    Style = AppStyle("CaptionTextStyle"),
                    Foreground = ThemeBrush("SemanticAttentionBrush"),
                },
            });
        }

        if (snapshot.Rows.Count == 0)
        {
            host.Children.Add(new Border
            {
                Style = AppStyle("SubtleCardStyle"),
                Child = new TextBlock
                {
                    Text = "No device with interrupt-affinity evidence is currently available.",
                    TextWrapping = TextWrapping.Wrap,
                    Style = AppStyle("MutedBodyTextStyle"),
                },
            });
            return;
        }

        var supportedCount = snapshot.Rows.Count(static row => row.CanStartNewPolicyMutation);
        var detailHost = new StackPanel
        {
            Spacing = 0d,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var deviceList = new ListView
        {
            Height = 180d,
            SelectionMode = ListViewSelectionMode.Single,
            IsItemClickEnabled = false,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0d),
            Padding = new Thickness(0d),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
        AutomationProperties.SetName(deviceList, "Devices with interrupt affinity evidence");

        var searchBox = new TextBox
        {
            PlaceholderText = "Search devices…",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        AutomationProperties.SetName(searchBox, "Search interrupt-affinity devices");

        var devicesTitleRow = new Grid { ColumnSpacing = 8d };
        devicesTitleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        devicesTitleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1d, GridUnitType.Star) });
        devicesTitleRow.Children.Add(new FontIcon
        {
            FontFamily = new FontFamily("Segoe Fluent Icons"),
            Glyph = "\uE8B7",
            FontSize = 17d,
            Foreground = ThemeBrush("AccentBrush"),
            VerticalAlignment = VerticalAlignment.Center,
        });
        var devicesHeading = new StackPanel { Spacing = 1d };
        devicesHeading.Children.Add(new TextBlock
        {
            Text = "Devices",
            Style = AppStyle("SubsectionTitleTextStyle"),
        });
        devicesHeading.Children.Add(new TextBlock
        {
            Text = $"{supportedCount.ToString(CultureInfo.InvariantCulture)} editable · {snapshot.Rows.Count.ToString(CultureInfo.InvariantCulture)} present devices",
            Style = AppStyle("CaptionTextStyle"),
            Foreground = ThemeBrush("MutedTextBrush"),
        });
        Grid.SetColumn(devicesHeading, 1);
        devicesTitleRow.Children.Add(devicesHeading);

        var devicesContent = new StackPanel { Spacing = 10d };
        devicesContent.Children.Add(devicesTitleRow);
        devicesContent.Children.Add(searchBox);
        devicesContent.Children.Add(new Border
        {
            Height = 1d,
            Background = ThemeBrush("DividerBrush"),
        });
        devicesContent.Children.Add(deviceList);

        var masterCard = new Border
        {
            Style = AppStyle("DashboardCardStyle"),
            VerticalAlignment = VerticalAlignment.Top,
            Child = devicesContent,
        };

        var workspaceGrid = new Grid
        {
            ColumnSpacing = 14d,
            RowSpacing = 14d,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        workspaceGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(400d) });
        workspaceGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1d, GridUnitType.Star) });
        workspaceGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        workspaceGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        workspaceGrid.Children.Add(masterCard);
        Grid.SetColumn(detailHost, 1);
        workspaceGrid.Children.Add(detailHost);
        host.Children.Add(workspaceGrid);

        var compactLayout = false;
        var visibleDeviceCount = 0;

        void RefreshDeviceListHeight()
        {
            var desired = 12d + (visibleDeviceCount * 48d);
            deviceList.Height = Math.Clamp(
                desired,
                120d,
                compactLayout ? 210d : 520d);
        }

        void ApplyWorkspaceLayout(double width)
        {
            compactLayout = width > 0d && width < 940d;
            workspaceGrid.ColumnDefinitions[0].Width = compactLayout
                ? new GridLength(1d, GridUnitType.Star)
                : new GridLength(400d);
            workspaceGrid.ColumnDefinitions[1].Width = compactLayout
                ? new GridLength(0d)
                : new GridLength(1d, GridUnitType.Star);
            Grid.SetColumn(masterCard, 0);
            Grid.SetRow(masterCard, 0);
            Grid.SetColumn(detailHost, compactLayout ? 0 : 1);
            Grid.SetRow(detailHost, compactLayout ? 1 : 0);
            RefreshDeviceListHeight();
        }

        workspaceGrid.SizeChanged += (_, args) => ApplyWorkspaceLayout(args.NewSize.Width);
        ApplyWorkspaceLayout(host.ActualWidth > 0d ? host.ActualWidth : 880d);

        void RenderSelectedDevice()
        {
            detailHost.Children.Clear();
            if (deviceList.SelectedItem is not ListViewItem { Tag: ManualAffinityDeviceRow row })
            {
                return;
            }

            detailHost.Children.Add(
                BuildManualAffinityDeviceDetail(
                    host,
                    snapshot,
                    row,
                    dialogStatusText));
        }

        void PopulateDevices(string query, string? preferredId)
        {
            var normalized = query.Trim();
            var previousId = preferredId;
            if (previousId is null &&
                deviceList.SelectedItem is ListViewItem { Tag: ManualAffinityDeviceRow selected })
            {
                previousId = selected.Device.InstanceId;
            }

            deviceList.Items.Clear();
            var filtered = snapshot.Rows
                .Where(row =>
                    normalized.Length == 0 ||
                    row.Device.DisplayName.Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
                    row.Device.InstanceId.Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
                    (row.Device.ServiceName?.Contains(normalized, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (row.Kind?.ToString().Contains(normalized, StringComparison.OrdinalIgnoreCase) ?? false))
                .ToArray();
            visibleDeviceCount = filtered.Length;
            RefreshDeviceListHeight();

            ListViewItem? preferredItem = null;
            foreach (var row in filtered)
            {
                var item = new ListViewItem
                {
                    Tag = row,
                    Padding = new Thickness(8d, 6d, 8d, 6d),
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Content = BuildManualAffinityDeviceListItem(row),
                };
                AutomationProperties.SetName(
                    item,
                    $"{row.Device.DisplayName}, {(row.TargetKind is null ? "read only" : "editable")}");
                ToolTipService.SetToolTip(item, row.Device.DisplayName);
                deviceList.Items.Add(item);
                if (string.Equals(
                    row.Device.InstanceId,
                    previousId,
                    StringComparison.OrdinalIgnoreCase))
                {
                    preferredItem = item;
                }
            }

            if (deviceList.Items.Count == 0)
            {
                detailHost.Children.Clear();
                detailHost.Children.Add(new Border
                {
                    Style = AppStyle("SubtleCardStyle"),
                    Child = new TextBlock
                    {
                        Text = "No devices match this search.",
                        TextWrapping = TextWrapping.Wrap,
                        Style = AppStyle("MutedBodyTextStyle"),
                    },
                });
                return;
            }

            deviceList.SelectedItem = preferredItem ?? deviceList.Items[0];
            RenderSelectedDevice();
        }

        deviceList.SelectionChanged += (_, _) => RenderSelectedDevice();
        searchBox.TextChanged += (_, _) => PopulateDevices(searchBox.Text, null);

        var initialId = selectedDeviceInstanceId
            ?? snapshot.Rows.FirstOrDefault(static row => row.CanStartNewPolicyMutation || row.HasPendingRecovery)?.Device.InstanceId
            ?? snapshot.Rows[0].Device.InstanceId;
        PopulateDevices(string.Empty, initialId);
    }
    private Grid BuildManualAffinityDeviceListItem(ManualAffinityDeviceRow row)
    {
        var grid = new Grid { ColumnSpacing = 10d };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1d, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = BuildManualAffinityIconTile(GetManualAffinityDeviceGlyph(row.Kind), 30d, 14d);
        icon.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(icon);

        var text = new StackPanel { Spacing = 1d, VerticalAlignment = VerticalAlignment.Center };
        var deviceNameText = new TextBlock
        {
            Text = row.Device.DisplayName,
            MaxLines = 1,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Style = AppStyle("BodyTextStyle"),
        };
        ToolTipService.SetToolTip(deviceNameText, row.Device.DisplayName);
        text.Children.Add(deviceNameText);
        text.Children.Add(new TextBlock
        {
            Text = FormatManualAffinityDeviceType(row.Kind, row.Device.ServiceName),
            MaxLines = 1,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Style = AppStyle("CaptionTextStyle"),
            Foreground = ThemeBrush("MutedTextBrush"),
        });
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var badge = BuildManualAffinityPill(
            row.CanStartNewPolicyMutation
                ? row.TargetKind switch
                {
                    "Gpu" => "GPU",
                    "Xhci" => "xHCI",
                    _ => "Policy",
                }
                : row.HasPendingRecovery
                    ? "Recovery"
                    : "Read-only",
            row.CanStartNewPolicyMutation ? "SemanticGoodBrush" : "MutedTextBrush",
            "PremiumOverviewQuietBrush");
        badge.VerticalAlignment = VerticalAlignment.Center;
        ToolTipService.SetToolTip(
            badge,
            row.CanStartNewPolicyMutation
                ? row.TargetKind switch
                {
                    "Gpu" => "GPU interrupt affinity · Supported with runtime ISR verification",
                    "Xhci" => "USB xHCI interrupt affinity · Supported with runtime ISR verification",
                    _ => "Windows device interrupt-affinity policy · Active placement is verified when Windows exposes allocated interrupt resources",
                }
                : row.HasPendingRecovery
                    ? "New affinity changes are blocked for this device class; a previously journaled LatencyPilot change can still be restored."
                    : row.InspectionOnlyReason ?? "Inspection only.");
        Grid.SetColumn(badge, 2);
        grid.Children.Add(badge);

        return grid;
    }
    private Border BuildManualAffinityIconTile(string glyph, double size, double fontSize) =>
        new()
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(Math.Min(12d, size / 3d)),
            Background = ThemeBrush("PremiumOverviewQuietBrush"),
            Child = new FontIcon
            {
                FontFamily = new FontFamily("Segoe Fluent Icons"),
                Glyph = glyph,
                FontSize = fontSize,
                Foreground = ThemeBrush("AccentBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };

    private static string GetManualAffinityDeviceGlyph(LatencySensitiveDeviceKind? kind) =>
        kind switch
        {
            LatencySensitiveDeviceKind.DisplayAdapter => "\uE7F4",
            LatencySensitiveDeviceKind.AudioAdapter or
                LatencySensitiveDeviceKind.AudioEndpoint => "\uE767",
            LatencySensitiveDeviceKind.NetworkAdapter => "\uE968",
            LatencySensitiveDeviceKind.UsbHostController => "\uE88E",
            LatencySensitiveDeviceKind.Bluetooth => "\uE702",
            LatencySensitiveDeviceKind.Keyboard or
                LatencySensitiveDeviceKind.Mouse or
                LatencySensitiveDeviceKind.HumanInterface => "\uE765",
            LatencySensitiveDeviceKind.StorageController or
                LatencySensitiveDeviceKind.StorageDevice => "\uE7C3",
            _ => "\uE770",
        };

    private static string FormatManualAffinityDeviceType(
        LatencySensitiveDeviceKind? kind,
        string? serviceName) =>
        kind switch
        {
            LatencySensitiveDeviceKind.DisplayAdapter => "Display adapter",
            LatencySensitiveDeviceKind.AudioAdapter => "Audio adapter",
            LatencySensitiveDeviceKind.AudioEndpoint => "Audio endpoint",
            LatencySensitiveDeviceKind.NetworkAdapter => "Network adapter",
            LatencySensitiveDeviceKind.UsbHostController => "USB host controller",
            LatencySensitiveDeviceKind.HumanInterface => "Human interface device",
            LatencySensitiveDeviceKind.Keyboard => "Keyboard",
            LatencySensitiveDeviceKind.Mouse => "Mouse",
            LatencySensitiveDeviceKind.Bluetooth => "Bluetooth",
            LatencySensitiveDeviceKind.StorageController => "Storage controller",
            LatencySensitiveDeviceKind.StorageDevice => "Storage device",
            LatencySensitiveDeviceKind.SystemDevice => "System device",
            _ => serviceName ?? "Device",
        };

    private Border BuildManualAffinityDeviceDetail(
        StackPanel host,
        ManualAffinitySnapshot snapshot,
        ManualAffinityDeviceRow row,
        TextBlock dialogStatusText)
    {
        var content = new StackPanel { Spacing = 14d };
        var header = new Grid { ColumnSpacing = 12d };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1d, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        header.Children.Add(BuildManualAffinityIconTile(
            GetManualAffinityDeviceGlyph(row.Kind),
            38d,
            17d));

        var heading = new StackPanel { Spacing = 2d, VerticalAlignment = VerticalAlignment.Center };
        heading.Children.Add(new TextBlock
        {
            Text = row.Device.DisplayName,
            TextWrapping = TextWrapping.Wrap,
            Style = AppStyle("SubsectionTitleTextStyle"),
        });
        heading.Children.Add(new TextBlock
        {
            Text = FormatManualAffinityDeviceType(row.Kind, row.Device.ServiceName),
            Style = AppStyle("CaptionTextStyle"),
            Foreground = ThemeBrush("MutedTextBrush"),
        });
        Grid.SetColumn(heading, 1);
        header.Children.Add(heading);

        var advancedButton = new Button
        {
            Style = AppStyle("QuietButtonStyle"),
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6d,
                Children =
                {
                    new FontIcon
                    {
                        FontFamily = new FontFamily("Segoe Fluent Icons"),
                        Glyph = "\uE713",
                        FontSize = 13d,
                    },
                    new TextBlock { Text = "Details" },
                },
            },
        };
        AutomationProperties.SetName(
            advancedButton,
            $"Show advanced interrupt details for {row.Device.DisplayName}");
        advancedButton.Click += (_, _) => ShowManualAffinityAdvancedFlyout(advancedButton, row);

        var headerActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6d,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var capabilityLabel = !row.CanStartNewPolicyMutation
            ? row.HasPendingRecovery ? "Inspection only · recovery" : "Inspection only"
            : row.TargetKind == "Gpu" &&
                row.HasExplicitOverride &&
                row.Device.InterruptResources.ReadStatus != InterruptResourceReadStatus.Available
                    ? "Policy set · runtime unverified"
                    : row.TargetKind is "Gpu" or "Xhci"
                        ? "Verification capable"
                        : "Manual policy";
        var capabilityBrush = !row.CanStartNewPolicyMutation
            ? "MutedTextBrush"
            : capabilityLabel == "Policy set · runtime unverified"
                ? "SemanticAttentionBrush"
                : row.TargetKind is "Gpu" or "Xhci"
                    ? "SemanticGoodBrush"
                    : "AccentBrush";
        headerActions.Children.Add(BuildManualAffinityPill(
            capabilityLabel,
            capabilityBrush,
            "PremiumOverviewQuietBrush"));
        headerActions.Children.Add(advancedButton);
        Grid.SetColumn(headerActions, 2);
        header.Children.Add(headerActions);

        content.Children.Add(header);
        content.Children.Add(new Border
        {
            Height = 1d,
            Background = ThemeBrush("DividerBrush"),
        });
        content.Children.Add(BuildManualAffinityMaskPanel(
            host,
            snapshot,
            row,
            dialogStatusText));

        return new Border
        {
            Style = AppStyle("DashboardCardStyle"),
            Child = content,
        };
    }
    private StackPanel BuildManualAffinityMaskPanel(
        StackPanel host,
        ManualAffinitySnapshot snapshot,
        ManualAffinityDeviceRow row,
        TextBlock dialogStatusText)
    {
        var panel = new StackPanel { Spacing = 12d };

        var title = new StackPanel { Spacing = 2d };
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8d };
        titleRow.Children.Add(new FontIcon
        {
            FontFamily = new FontFamily("Segoe Fluent Icons"),
            Glyph = "\uE950",
            FontSize = 17d,
            Foreground = ThemeBrush("AccentBrush"),
        });
        titleRow.Children.Add(new TextBlock
        {
            Text = "Interrupt affinity",
            Style = AppStyle("SubsectionTitleTextStyle"),
        });
        title.Children.Add(titleRow);
        title.Children.Add(new TextBlock
        {
            Text = row.CanStartNewPolicyMutation
                ? "Choose the logical processors that may service this device interrupt."
                : row.HasPendingRecovery
                    ? "This device class is inspection-only for new changes. Restore remains available for its existing journal-owned LatencyPilot change."
                    : row.InspectionOnlyReason ?? "This device is available for inspection only.",
            TextWrapping = TextWrapping.Wrap,
            Style = AppStyle("CaptionTextStyle"),
        });
        panel.Children.Add(title);

        if (row.Kind == LatencySensitiveDeviceKind.NetworkAdapter)
        {
            panel.Children.Add(new Border
            {
                Padding = new Thickness(10d, 8d, 10d, 8d),
                CornerRadius = new CornerRadius(8d),
                Background = ThemeBrush("SurfaceAltBrush"),
                BorderBrush = ThemeBrush("BorderBrush"),
                BorderThickness = new Thickness(1d),
                Child = new TextBlock
                {
                    Text = "Network note · This control sets the Windows device interrupt-affinity policy. Receive Side Scaling (RSS) is a separate NIC/NDIS processor policy and can still distribute receive processing across its RSS CPU set.",
                    TextWrapping = TextWrapping.Wrap,
                    Style = AppStyle("CaptionTextStyle"),
                    Foreground = ThemeBrush("MutedTextBrush"),
                },
            });
        }

        var metrics = new Grid { ColumnSpacing = 8d };
        for (var index = 0; index < 3; index++)
        {
            metrics.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1d, GridUnitType.Star) });
        }
        var policyTile = BuildManualAffinityMetricTile(
            "Current policy",
            FormatManualAffinityPolicy(row.Device.InterruptConfiguration));
        var maskTile = BuildManualAffinityMetricTile(
            "Specified mask",
            FormatManualSpecifiedMask(row.Device.InterruptConfiguration));
        var assignmentTile = BuildManualAffinityMetricTile(
            "Current assignment",
            row.AllocatedAffinity);
        metrics.Children.Add(policyTile);
        Grid.SetColumn(maskTile, 1);
        metrics.Children.Add(maskTile);
        Grid.SetColumn(assignmentTile, 2);
        metrics.Children.Add(assignmentTile);
        panel.Children.Add(metrics);

        if (row.PendingRecovery?.State == MutationJournalState.ApplyRebootPending &&
            row.PendingMask is { } pendingMask)
        {
            panel.Children.Add(new Border
            {
                Padding = new Thickness(10d, 8d, 10d, 8d),
                CornerRadius = new CornerRadius(8d),
                Background = ThemeBrush("SurfaceAltBrush"),
                BorderBrush = ThemeBrush("AccentBrush"),
                BorderThickness = new Thickness(1d),
                Child = new TextBlock
                {
                    Text = row.CanStartNewPolicyMutation
                        ? $"Pending verification · {FormatMask(pendingMask)} · mask 0x{pendingMask:X}. This is the journal-owned selection waiting to be resumed; Apply & verify continues it, while Restore original cancels it."
                        : $"Pending recovery · {FormatMask(pendingMask)} · mask 0x{pendingMask:X}. New mutations are now blocked for this device class; Restore journal-owned original is the supported recovery path.",
                    TextWrapping = TextWrapping.Wrap,
                    Style = AppStyle("CaptionTextStyle"),
                    Foreground = ThemeBrush("AccentBrush"),
                },
            });
        }

        if (row.Device.InterruptResources.ReadStatus != InterruptResourceReadStatus.Available)
        {
            var warningContent = new Grid { ColumnSpacing = 8d };
            warningContent.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            warningContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1d, GridUnitType.Star) });
            warningContent.Children.Add(new FontIcon
            {
                FontFamily = new FontFamily("Segoe Fluent Icons"),
                Glyph = "\uE7BA",
                FontSize = 14d,
                Foreground = ThemeBrush("SemanticAttentionBrush"),
                VerticalAlignment = VerticalAlignment.Top,
            });
            var warningText = new StackPanel { Spacing = 2d };
            warningText.Children.Add(new TextBlock
            {
                Text = "Assignment unavailable",
                FontWeight = FontWeights.SemiBold,
                Style = AppStyle("CaptionTextStyle"),
                Foreground = ThemeBrush("SemanticAttentionBrush"),
            });
            warningText.Children.Add(new TextBlock
            {
                Text = !row.CanStartNewPolicyMutation
                    ? "The current allocation is not readable. This device class is inspection-only, so LatencyPilot will not start a new affinity mutation from this row."
                    : row.TargetKind == "Xhci"
                        ? "Apply & verify re-reads allocation from the elevated helper and keeps nothing unless active allocation and controller-attributed ISR placement are both proven."
                        : row.TargetKind == "Gpu"
                            ? "The GPU allocation is not readable. Manual policy can still be retained after the stored mask and restart are verified, but LatencyPilot will label active placement as unverified instead of claiming runtime proof."
                            : "The current allocation is not readable. Manual policy editing is still available, matching Windows IntPolicy behavior; LatencyPilot will verify the stored policy and device restart, but will label active placement as unverified until Windows exposes interrupt allocation.",
                TextWrapping = TextWrapping.Wrap,
                Style = AppStyle("CaptionTextStyle"),
                Foreground = ThemeBrush("MutedTextBrush"),
            });
            Grid.SetColumn(warningText, 1);
            warningContent.Children.Add(warningText);

            panel.Children.Add(new Border
            {
                Padding = new Thickness(10d, 8d, 10d, 8d),
                CornerRadius = new CornerRadius(8d),
                Background = ThemeBrush("SurfaceAltBrush"),
                BorderBrush = ThemeBrush("BorderBrush"),
                BorderThickness = new Thickness(1d),
                Child = warningContent,
            });
        }

        if (row.CanStartNewPolicyMutation)
        {
            var selectedProcessors = new HashSet<byte>();
            var initialMask = _manualAffinityDraftMasks.TryGetValue(
                row.Device.InstanceId,
                out var draftMask)
                    ? draftMask
                    : row.PendingMask ??
                        row.StoredMask.GetValueOrDefault();
            if (initialMask != 0)
            {
                foreach (var option in snapshot.CpuOptions)
                {
                    if ((initialMask & (1UL << option.Processor.Number)) != 0)
                    {
                        selectedProcessors.Add(option.Processor.Number);
                    }
                }
            }

            var cpuHeader = new Grid { ColumnSpacing = 10d };
            cpuHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1d, GridUnitType.Star) });
            cpuHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var cpuTitle = new StackPanel { Spacing = 1d };
            cpuTitle.Children.Add(new TextBlock
            {
                Text = "Processor mask",
                Style = AppStyle("MetricLabelTextStyle"),
                Foreground = ThemeBrush("TextBrush"),
            });
            cpuTitle.Children.Add(new TextBlock
            {
                Text = "Logical CPUs are grouped by physical core so SMT siblings stay visible.",
                TextWrapping = TextWrapping.Wrap,
                Style = AppStyle("CaptionTextStyle"),
            });
            cpuHeader.Children.Add(cpuTitle);

            var selectAllButton = new Button { Content = "Select all", Style = AppStyle("QuietButtonStyle") };
            var clearButton = new Button { Content = "Clear", Style = AppStyle("QuietButtonStyle") };
            var selectionActions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4d,
                VerticalAlignment = VerticalAlignment.Center,
            };
            selectionActions.Children.Add(selectAllButton);
            selectionActions.Children.Add(clearButton);
            Grid.SetColumn(selectionActions, 1);
            cpuHeader.Children.Add(selectionActions);
            panel.Children.Add(cpuHeader);

            var applyButton = new Button
            {
                Style = AppStyle("PrimaryButtonStyle"),
                MinWidth = 150d,
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 7d,
                    Children =
                    {
                        new FontIcon
                        {
                            FontFamily = new FontFamily("Segoe Fluent Icons"),
                            Glyph = "\uE73E",
                            FontSize = 14d,
                        },
                        new TextBlock { Text = "Apply & verify" },
                    },
                },
            };
            var selectionSummary = new TextBlock
            {
                Style = AppStyle("CaptionTextStyle"),
                Foreground = ThemeBrush("MutedTextBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
            };

            var coreGroups = snapshot.CpuOptions
                .GroupBy(static option => option.PhysicalCoreIndex)
                .OrderBy(static group => group.Key)
                .ToArray();
            var coreGrid = new Grid { ColumnSpacing = 8d, RowSpacing = 8d };
            var coreColumnCount = Math.Min(3, Math.Max(1, coreGroups.Length));
            for (var column = 0; column < coreColumnCount; column++)
            {
                coreGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1d, GridUnitType.Star) });
            }
            var coreRowCount = (int)Math.Ceiling(coreGroups.Length / (double)coreColumnCount);
            for (var rowIndex = 0; rowIndex < coreRowCount; rowIndex++)
            {
                coreGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }

            var cpuButtons = new List<ToggleButton>();

            ulong BuildSelectedMask()
            {
                ulong mask = 0;
                foreach (var processor in selectedProcessors)
                {
                    mask |= 1UL << processor;
                }
                return mask;
            }

            var synchronizingCpuSelection = false;

            void RefreshSelection()
            {
                var mask = BuildSelectedMask();
                _manualAffinityDraftMasks[row.Device.InstanceId] = mask;
                applyButton.IsEnabled = mask != 0 && !_manualDeviceAffinityBusy;
                selectionSummary.Text = mask == 0
                    ? "No CPUs selected."
                    : $"{selectedProcessors.Count.ToString(CultureInfo.InvariantCulture)} CPU(s) · {FormatMask(mask)} · mask 0x{mask:X}";
            }

            for (var coreIndex = 0; coreIndex < coreGroups.Length; coreIndex++)
            {
                var coreGroup = coreGroups[coreIndex];
                var coreContent = new StackPanel { Spacing = 6d };
                coreContent.Children.Add(new TextBlock
                {
                    Text = $"Core {coreGroup.Key.ToString(CultureInfo.InvariantCulture)}",
                    Style = AppStyle("MetricLabelTextStyle"),
                    Foreground = ThemeBrush("MutedTextBrush"),
                });

                var siblingGrid = new Grid { ColumnSpacing = 6d };
                var siblings = coreGroup
                    .OrderBy(static option => option.Processor.Number)
                    .ToArray();
                for (var siblingIndex = 0; siblingIndex < siblings.Length; siblingIndex++)
                {
                    siblingGrid.ColumnDefinitions.Add(new ColumnDefinition
                    {
                        Width = new GridLength(1d, GridUnitType.Star),
                    });

                    var option = siblings[siblingIndex];
                    var cpuButton = new ToggleButton
                    {
                        Content = $"CPU {option.Processor.Number.ToString(CultureInfo.InvariantCulture)}",
                        Tag = option,
                        IsChecked = selectedProcessors.Contains(option.Processor.Number),
                        MinHeight = 36d,
                        MinWidth = 56d,
                        Padding = new Thickness(8d, 4d, 8d, 4d),
                        CornerRadius = new CornerRadius(8d),
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        BorderThickness = new Thickness(1d),
                        FontWeight = FontWeights.SemiBold,
                    };
                    AutomationProperties.SetName(
                        cpuButton,
                        $"CPU {option.Processor.Number.ToString(CultureInfo.InvariantCulture)}, physical core {option.PhysicalCoreIndex.ToString(CultureInfo.InvariantCulture)}");
                    ToolTipService.SetToolTip(
                        cpuButton,
                        $"Logical processor {option.Processor.Number.ToString(CultureInfo.InvariantCulture)} · physical core {option.PhysicalCoreIndex.ToString(CultureInfo.InvariantCulture)}");
                    cpuButton.Checked += (_, _) =>
                    {
                        if (synchronizingCpuSelection)
                        {
                            return;
                        }

                        try
                        {
                            selectedProcessors.Add(option.Processor.Number);
                            RefreshSelection();
                        }
                        catch (Exception exception)
                        {
                            Logger.Error(
                                exception,
                                "CPU selection failed for {DeviceInstanceId} processor {Processor}.",
                                row.Device.InstanceId,
                                option.Processor.Number);
                            SetManualAffinityStatus(
                                dialogStatusText,
                                $"CPU selection failed: {exception.Message}",
                                "SemanticFailureBrush");
                        }
                    };
                    cpuButton.Unchecked += (_, _) =>
                    {
                        if (synchronizingCpuSelection)
                        {
                            return;
                        }

                        try
                        {
                            selectedProcessors.Remove(option.Processor.Number);
                            RefreshSelection();
                        }
                        catch (Exception exception)
                        {
                            Logger.Error(
                                exception,
                                "CPU deselection failed for {DeviceInstanceId} processor {Processor}.",
                                row.Device.InstanceId,
                                option.Processor.Number);
                            SetManualAffinityStatus(
                                dialogStatusText,
                                $"CPU deselection failed: {exception.Message}",
                                "SemanticFailureBrush");
                        }
                    };
                    Grid.SetColumn(cpuButton, siblingIndex);
                    siblingGrid.Children.Add(cpuButton);
                    cpuButtons.Add(cpuButton);
                }

                coreContent.Children.Add(siblingGrid);
                var coreCard = new Border
                {
                    Padding = new Thickness(8d),
                    CornerRadius = new CornerRadius(10d),
                    Background = ThemeBrush("SurfaceAltBrush"),
                    BorderBrush = ThemeBrush("BorderBrush"),
                    BorderThickness = new Thickness(1d),
                    Child = coreContent,
                };
                Grid.SetRow(coreCard, coreIndex / coreColumnCount);
                Grid.SetColumn(coreCard, coreIndex % coreColumnCount);
                coreGrid.Children.Add(coreCard);
            }

            selectAllButton.Click += (_, _) =>
            {
                synchronizingCpuSelection = true;
                try
                {
                    selectedProcessors.Clear();
                    foreach (var option in snapshot.CpuOptions)
                    {
                        selectedProcessors.Add(option.Processor.Number);
                    }

                    foreach (var button in cpuButtons)
                    {
                        button.IsChecked = true;
                    }
                }
                finally
                {
                    synchronizingCpuSelection = false;
                }

                RefreshSelection();
            };
            clearButton.Click += (_, _) =>
            {
                synchronizingCpuSelection = true;
                try
                {
                    selectedProcessors.Clear();
                    foreach (var button in cpuButtons)
                    {
                        button.IsChecked = false;
                    }
                }
                finally
                {
                    synchronizingCpuSelection = false;
                }

                RefreshSelection();
            };

            panel.Children.Add(coreGrid);

            var restoreMayBeNeeded =
                row.HasPendingRecovery ||
                row.HasExplicitOverride ||
                row.Device.InterruptConfiguration.ReadStatus != InterruptConfigurationReadStatus.Available;
            var restoreButton = new Button
            {
                Style = AppStyle("SecondaryButtonStyle"),
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 7d,
                    Children =
                    {
                        new FontIcon
                        {
                            FontFamily = new FontFamily("Segoe Fluent Icons"),
                            Glyph = "\uE777",
                            FontSize = 14d,
                        },
                        new TextBlock { Text = "Restore original" },
                    },
                },
                MinWidth = 150d,
                IsEnabled = restoreMayBeNeeded && !_manualDeviceAffinityBusy,
            };
            AutomationProperties.SetName(
                restoreButton,
                $"Restore journal-owned original affinity for {row.Device.DisplayName}");
            ToolTipService.SetToolTip(
                restoreButton,
                row.HasPendingRecovery
                    ? $"A journal-owned {row.PendingRecovery!.State} recovery is pending. Restore only this device to resolve it before another mask can be saved."
                    : row.HasExplicitOverride
                    ? "Restores only an original state previously journaled by LatencyPilot; a non-owned explicit policy remains untouched."
                    : row.Device.InterruptConfiguration.ReadStatus != InterruptConfigurationReadStatus.Available
                        ? "Current policy could not be fully inspected. Restore remains available so journal-owned recovery is never blocked."
                        : "No explicit affinity override is active for this device.");
            restoreButton.Click += async (_, _) =>
            {
                var choice = await ConfirmManualAffinityActionAsync(host, row, "Restore", null);
                if (choice == ManualAffinityActionChoice.Cancel)
                {
                    return;
                }

                restoreButton.IsEnabled = false;
                applyButton.IsEnabled = false;
                await RunManualAffinityActionAsync(
                    host,
                    dialogStatusText,
                    row,
                    "Restore",
                    null,
                    choice == ManualAffinityActionChoice.RestartDeviceOnly);
            };

            applyButton.Click += async (_, _) =>
            {
                var mask = BuildSelectedMask();
                if (mask == 0)
                {
                    return;
                }

                var choice = await ConfirmManualAffinityActionAsync(host, row, "Apply", mask);
                if (choice == ManualAffinityActionChoice.Cancel)
                {
                    return;
                }

                restoreButton.IsEnabled = false;
                applyButton.IsEnabled = false;
                await RunManualAffinityActionAsync(
                    host,
                    dialogStatusText,
                    row,
                    choice == ManualAffinityActionChoice.RestoreOnlyDevice
                        ? "Restore"
                        : "Apply",
                    choice == ManualAffinityActionChoice.RestoreOnlyDevice
                        ? null
                        : mask,
                    choice == ManualAffinityActionChoice.RestartDeviceOnly);
            };

            var actions = new Grid { ColumnSpacing = 8d };
            actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1d, GridUnitType.Star) });
            actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            actions.Children.Add(selectionSummary);
            Grid.SetColumn(restoreButton, 1);
            actions.Children.Add(restoreButton);
            Grid.SetColumn(applyButton, 2);
            actions.Children.Add(applyButton);
            panel.Children.Add(actions);

            RefreshSelection();
        }
        else
        {
            var inspectionPanel = new StackPanel { Spacing = 8d };
            inspectionPanel.Children.Add(new TextBlock
            {
                Text = row.InspectionOnlyReason ?? "No new mutation control is exposed for this device.",
                TextWrapping = TextWrapping.Wrap,
                Style = AppStyle("CaptionTextStyle"),
                Foreground = ThemeBrush("MutedTextBrush"),
            });

            var recoveryMayBeNeeded =
                row.HasPendingRecovery ||
                row.HasExplicitOverride ||
                row.Device.InterruptConfiguration.ReadStatus != InterruptConfigurationReadStatus.Available;
            if (recoveryMayBeNeeded)
            {
                var recoveryButton = new Button
                {
                    Style = AppStyle("SecondaryButtonStyle"),
                    Content = "Restore journal-owned original",
                    HorizontalAlignment = HorizontalAlignment.Left,
                };
                recoveryButton.Click += async (_, _) =>
                {
                    var choice = await ConfirmManualAffinityActionAsync(host, row, "Restore", null);
                    if (choice == ManualAffinityActionChoice.Cancel)
                    {
                        return;
                    }

                    recoveryButton.IsEnabled = false;
                    await RunManualAffinityActionAsync(
                        host,
                        dialogStatusText,
                        row,
                        "Restore",
                        null,
                        restartDeviceOnly: false);
                };
                inspectionPanel.Children.Add(recoveryButton);
            }

            panel.Children.Add(new Border
            {
                Padding = new Thickness(10d, 8d, 10d, 8d),
                CornerRadius = new CornerRadius(8d),
                Background = ThemeBrush("SurfaceAltBrush"),
                Child = inspectionPanel,
            });
        }

        panel.Children.Add(new TextBlock
        {
            Text = !row.CanStartNewPolicyMutation
                ? "Inspection-only rows never start a new affinity mutation. Existing journal-owned state can still be restored so recovery is not stranded."
                : row.TargetKind == "Xhci"
                    ? "xHCI is kept only when Windows allocation and live controller-attributed ISR execution stay inside the requested mask."
                    : row.TargetKind == "Gpu"
                        ? "GPU manual policy is retained when the stored mask/restart are verified. Full runtime-verified status additionally requires readable translated allocation plus requested-mask-only GPU ISR evidence."
                        : "For generic devices, LatencyPilot verifies the stored Windows affinity policy and restart. When translated interrupt allocation is readable it must also stay inside the requested mask; otherwise the result is retained as policy-only and clearly marked as active-placement unverified.",
            TextWrapping = TextWrapping.Wrap,
            Style = AppStyle("CaptionTextStyle"),
            Foreground = ThemeBrush("MutedTextBrush"),
        });

        return panel;
    }
    private Border BuildManualAffinityMetricTile(string label, string value)
    {
        var stack = new StackPanel { Spacing = 4d };
        stack.Children.Add(new TextBlock
        {
            Text = label,
            Style = AppStyle("MetricLabelTextStyle"),
            Foreground = ThemeBrush("MutedTextBrush"),
        });
        stack.Children.Add(new TextBlock
        {
            Text = value,
            MaxLines = 3,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Style = AppStyle("CaptionTextStyle"),
            Foreground = ThemeBrush("TextBrush"),
        });
        ToolTipService.SetToolTip(stack, value);
        return new Border
        {
            Style = AppStyle("MetricTileStyle"),
            MinHeight = 68d,
            Child = stack,
        };
    }

    private Grid BuildManualAffinityPropertyRow(
        string label,
        string value,
        string? valueBrushKey = null)
    {
        var grid = new Grid { ColumnSpacing = 10d };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130d) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1d, GridUnitType.Star) });

        grid.Children.Add(new TextBlock
        {
            Text = label,
            Style = AppStyle("CaptionTextStyle"),
            Foreground = ThemeBrush("MutedTextBrush"),
        });
        var valueText = new TextBlock
        {
            Text = value,
            TextWrapping = TextWrapping.Wrap,
            Style = AppStyle("CaptionTextStyle"),
            Foreground = ThemeBrush(valueBrushKey ?? "TextBrush"),
        };
        Grid.SetColumn(valueText, 1);
        grid.Children.Add(valueText);
        return grid;
    }



    private void ShowManualAffinityAdvancedFlyout(
        Button anchor,
        ManualAffinityDeviceRow row)
    {
        var configuration = row.Device.InterruptConfiguration;
        var resources = row.Device.InterruptResources;
        var details = new StackPanel
        {
            Spacing = 8d,
            MinWidth = 420d,
            MaxWidth = 520d,
        };
        details.Children.Add(new TextBlock
        {
            Text = "Advanced device details",
            Style = AppStyle("SubsectionTitleTextStyle"),
        });
        details.Children.Add(BuildManualAffinityPropertyRow(
            "Device ID",
            row.Device.InstanceId));
        details.Children.Add(BuildManualAffinityPropertyRow(
            "Parent device",
            row.Device.Parent.ReadStatus == DeviceParentReadStatus.Available &&
            !string.IsNullOrWhiteSpace(row.Device.Parent.ParentInstanceId)
                ? row.Device.Parent.ParentInstanceId!
                : "N/A"));
        details.Children.Add(BuildManualAffinityPropertyRow(
            "Service",
            row.Device.ServiceName ?? "N/A"));
        details.Children.Add(BuildManualAffinityPropertyRow(
            "Manufacturer",
            row.Device.Manufacturer ?? "N/A"));
        details.Children.Add(BuildManualAffinityPropertyRow(
            "Driver version",
            row.Device.Driver.Version ?? "N/A"));
        details.Children.Add(BuildManualAffinityPropertyRow(
            "Driver provider",
            row.Device.Driver.Provider ?? "N/A"));
        details.Children.Add(BuildManualAffinityPropertyRow(
            "INF",
            row.Device.Driver.InfPath ?? "N/A"));
        details.Children.Add(BuildManualAffinityPropertyRow(
            "MSI configured",
            configuration.MsiSupported is null
                ? "N/A"
                : configuration.IsMsiConfiguredEnabled ? "Yes" : "No"));
        details.Children.Add(BuildManualAffinityPropertyRow(
            "Message limit",
            configuration.MessageNumberLimit?.ToString(CultureInfo.InvariantCulture) ?? "N/A"));
        details.Children.Add(BuildManualAffinityPropertyRow(
            "Policy read",
            configuration.ReadStatus.ToString()));
        details.Children.Add(BuildManualAffinityPropertyRow(
            "Policy native status",
            configuration.NativeErrorCode is { } policyStatus
                ? $"0x{policyStatus:X8}"
                : "N/A"));
        details.Children.Add(BuildManualAffinityPropertyRow(
            "Resource read",
            resources.ReadStatus.ToString()));
        details.Children.Add(BuildManualAffinityPropertyRow(
            "Resource native status",
            resources.NativeStatusCode is { } resourceStatus
                ? $"0x{resourceStatus:X8}"
                : "N/A"));
        details.Children.Add(BuildManualAffinityPropertyRow(
            "Stored summary",
            row.StoredAffinity));

        var flyout = new Flyout
        {
            Content = new ScrollViewer
            {
                MaxHeight = 520d,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = details,
            },
        };
        flyout.ShowAt(anchor);
    }



    private Border BuildManualAffinityStatusBanner(TextBlock statusText)
    {
        var grid = new Grid { ColumnSpacing = 10d };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(
            new ColumnDefinition { Width = new GridLength(1d, GridUnitType.Star) });

        grid.Children.Add(new TextBlock
        {
            Text = "Status",
            Style = AppStyle("MetricLabelTextStyle"),
            Foreground = ThemeBrush("MutedTextBrush"),
        });
        Grid.SetColumn(statusText, 1);
        grid.Children.Add(statusText);

        return new Border
        {
            Padding = new Thickness(10d),
            CornerRadius = new CornerRadius(8d),
            Background = ThemeBrush("SurfaceAltBrush"),
            BorderBrush = ThemeBrush("BorderBrush"),
            BorderThickness = new Thickness(1d),
            Child = grid,
        };
    }

    private Border BuildManualAffinityPill(string text, string foregroundKey, string backgroundKey) =>
        new()
        {
            Padding = new Thickness(7d, 3d, 7d, 3d),
            CornerRadius = new CornerRadius(999d),
            Background = ThemeBrush(backgroundKey),
            Child = new TextBlock
            {
                Text = text,
                FontSize = 10d,
                FontWeight = FontWeights.SemiBold,
                Foreground = ThemeBrush(foregroundKey),
            },
        };

    private static string FormatManualAffinityPolicy(InterruptConfigurationSnapshot configuration)
    {
        if (configuration.ReadStatus != InterruptConfigurationReadStatus.Available)
        {
            return configuration.ReadStatus.ToString();
        }

        return configuration.DevicePolicy switch
        {
            null => "N/A · Windows/driver default",
            0 => "Machine default",
            1 => "All close processors",
            2 => "One close processor",
            3 => "All processors",
            4 => "Specified processors",
            5 => "Spread MSI messages",
            var value => $"Policy {Convert.ToString(value, CultureInfo.InvariantCulture)}",
        };
    }

    private static string FormatManualSpecifiedMask(InterruptConfigurationSnapshot configuration)
    {
        if (configuration.ReadStatus != InterruptConfigurationReadStatus.Available ||
            configuration.AssignmentSetOverrideMask is not { } affinity)
        {
            return "N/A";
        }

        return $"{FormatMask(affinity)} · 0x{affinity:X}";
    }

    private async Task<ManualAffinityActionChoice> ConfirmManualAffinityActionAsync(
        StackPanel host,
        ManualAffinityDeviceRow row,
        string action,
        ulong? affinityMask)
    {
        var pendingRecovery = row.PendingRecovery;
        var isPendingApply =
            action == "Apply" &&
            pendingRecovery?.State == MutationJournalState.ApplyRebootPending;
        var isOtherPendingRecovery =
            pendingRecovery is not null &&
            !isPendingApply;
        var restoreOnly = action == "Restore" || isOtherPendingRecovery;

        var dialog = new ContentDialog
        {
            XamlRoot = host.XamlRoot,
            Title = isPendingApply
                ? $"Resume {row.Device.DisplayName}?"
                : restoreOnly
                    ? $"Recover {row.Device.DisplayName}?"
                    : $"Save affinity for {row.Device.DisplayName}?",
            Content = new StackPanel
            {
                Spacing = 8d,
                Children =
                {
                    new TextBlock
                    {
                        Text = isPendingApply
                            ? $"Resume the journal-owned pending affinity {FormatMask(row.PendingMask ?? affinityMask ?? 0)} and verify it. Restore original remains a separate cancellation choice."
                            : restoreOnly
                                ? pendingRecovery is not null
                                    ? $"This device has a journal-owned {pendingRecovery.State} recovery. Only this device will be restored to its exact captured original policy."
                                    : "Only this device will be restored to its exact LatencyPilot-owned original policy. A non-owned policy will not be overwritten."
                                : $"Save {FormatMask(affinityMask ?? 0)} as the Windows interrupt-affinity policy for this device.",
                        TextWrapping = TextWrapping.Wrap,
                        Style = AppStyle("BodyTextStyle"),
                    },
                    new TextBlock
                    {
                        Text = isPendingApply
                            ? "The selected mask is reconstructed from the durable journal, so the same CPU selection remains visible after restart even when current allocation cannot be read."
                            : restoreOnly
                                ? "If Windows requires another reboot, the dialog will report it and you can reboot manually."
                                : row.TargetKind == "Gpu"
                                    ? "You can save for a full reboot, or explicitly choose the optional device-only restart path."
                                    : "Windows may restart the device in place; if it requires a system reboot, LatencyPilot will stop and report that requirement.",
                        TextWrapping = TextWrapping.Wrap,
                        Style = AppStyle("CaptionTextStyle"),
                        Foreground = ThemeBrush("MutedTextBrush"),
                    },
                    new TextBlock
                    {
                        Text = "The optional device-only path restarts only this display adapter/driver. The screen may flicker, and Windows can still require a full reboot if the driver does not accept an in-place restart.",
                        TextWrapping = TextWrapping.Wrap,
                        Style = AppStyle("CaptionTextStyle"),
                        Foreground = ThemeBrush("MutedTextBrush"),
                        Visibility = row.TargetKind == "Gpu" && !isPendingApply
                            ? Visibility.Visible
                            : Visibility.Collapsed,
                    },
                },
            },
            PrimaryButtonText = isPendingApply
                ? "Resume & verify"
                : restoreOnly
                    ? "Restore only this device"
                    : "Save policy; I will reboot manually",
            SecondaryButtonText = isPendingApply
                ? "Restore original"
                : row.TargetKind == "Gpu"
                    ? restoreOnly
                        ? "Restore and restart only this GPU driver/device"
                        : "Restart only this GPU driver/device"
                    : null,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Secondary)
        {
            if (isPendingApply)
            {
                return ManualAffinityActionChoice.RestoreOnlyDevice;
            }

            if (row.TargetKind == "Gpu")
            {
                return ManualAffinityActionChoice.RestartDeviceOnly;
            }
        }

        if (result != ContentDialogResult.Primary)
        {
            return ManualAffinityActionChoice.Cancel;
        }

        return restoreOnly && action != "Restore"
            ? ManualAffinityActionChoice.RestoreOnlyDevice
            : ManualAffinityActionChoice.ConfirmRequestedAction;
    }

    private async Task RunManualAffinityActionAsync(
        StackPanel host,
        TextBlock dialogStatusText,
        ManualAffinityDeviceRow row,
        string action,
        ulong? affinityMask,
        bool restartDeviceOnly)
    {
        if (_manualDeviceAffinityBusy || string.IsNullOrWhiteSpace(row.TargetKind))
        {
            return;
        }

        if (_gateAValidationRunning)
        {
            SetManualAffinityStatus(
                dialogStatusText,
                "Manual affinity is blocked while GPU Gate A owns the mutation/measurement session.",
                "SemanticAttentionBrush");
            return;
        }

        _manualDeviceAffinityBusy = true;
        ManualDeviceAffinityButton.IsEnabled = false;
        var actionStatus = action == "Apply" && affinityMask is { } requestedMask
            ? row.TargetKind switch
            {
                "Xhci" => $"Applying {FormatMask(requestedMask)} to {row.Device.DisplayName}. After UAC, keep moving the USB mouse/using USB input during the ~10 s ETW verification; Windows may briefly restart the controller…",
                "Gpu" when restartDeviceOnly => $"Applying {FormatMask(requestedMask)} to {row.Device.DisplayName}. LatencyPilot will restart only this GPU driver/device; the screen may flicker, and Windows may still require a full reboot.",
                "Gpu" => $"Storing {FormatMask(requestedMask)} for {row.Device.DisplayName}. Reboot Windows after this step, then reopen the panel and apply the same mask again to complete allocation + ETW verification.",
                _ => $"Applying {FormatMask(requestedMask)} to {row.Device.DisplayName}. Windows will restart the device when possible and verify the stored affinity policy; active allocation is also verified when Windows exposes it.",
            }
            : restartDeviceOnly
                ? $"Restoring journal-owned original state for {row.Device.DisplayName} by restarting only this GPU driver/device…"
                : $"Restoring journal-owned original state for {row.Device.DisplayName}…";

        SetManualAffinityStatus(
            dialogStatusText,
            actionStatus,
            "SemanticAttentionBrush");

        try
        {
            var report = await RunManualAffinityHelperAsync(
                action,
                row.TargetKind,
                row.Device.InstanceId,
                affinityMask,
                restartDeviceOnly);
            var status = report.Status is "RebootRequired" or "AppliedPolicyKept" or "AlreadyStoredPolicy"
                ? "SemanticAttentionBrush"
                : report.Status is "AppliedAndKept" or "AlreadyConfigured" or "Restored" or "NoLatencyPilotChange"
                    ? "SemanticGoodBrush"
                    : "TextBrush";
            var message = report.Status == "RebootRequired"
                ? restartDeviceOnly
                    ? $"{row.Device.DisplayName}: Windows could not complete the device-only restart safely. A full system reboot is required to finish this journaled {action.ToLowerInvariant()} operation."
                    : action == "Restore"
                    ? $"{row.Device.DisplayName}: Windows requires a reboot to finish restoring the journal-owned original state. Reboot, reopen this panel, and choose Restore again."
                    : $"{row.Device.DisplayName}: Windows requires a reboot. Reboot, reopen this panel, and select the same processor mask again to resume the journaled experiment."
                : $"{row.Device.DisplayName}: {report.Message}";
            SetManualAffinityStatus(
                dialogStatusText,
                message,
                status);

            _manualAffinityDraftMasks.Remove(row.Device.InstanceId);
            var refreshed = await Task.Run(CaptureManualAffinitySnapshot);
            RenderManualAffinityWorkspace(
                host,
                refreshed,
                dialogStatusText,
                row.Device.InstanceId);
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            SetManualAffinityStatus(
                dialogStatusText,
                "Manual affinity was cancelled at the Windows elevation prompt; no new change was requested.",
                "SemanticAttentionBrush");
        }
        catch (Exception exception) when (exception is
            InvalidOperationException or
            IOException or
            UnauthorizedAccessException or
            Win32Exception or
            JsonException)
        {
            Logger.Error(exception, "Manual affinity action failed for {DeviceInstanceId}.", row.Device.InstanceId);
            SetManualAffinityStatus(
                dialogStatusText,
                $"Manual affinity action failed: {exception.Message}",
                "SemanticFailureBrush");
        }
        finally
        {
            _manualDeviceAffinityBusy = false;
            ManualDeviceAffinityButton.IsEnabled = true;
        }
    }

    private void SetManualAffinityStatus(TextBlock dialogStatusText, string message, string foregroundKey)
    {
        dialogStatusText.Text = message;
        dialogStatusText.Foreground = ThemeBrush(foregroundKey);
        ManualDeviceAffinityStatusText.Text = message;
    }

    private async Task<ManualAffinityHelperReport> RunManualAffinityHelperAsync(
        string action,
        string targetKind,
        string deviceInstanceId,
        ulong? affinityMask,
        bool restartDeviceOnly)
    {
        if (string.IsNullOrWhiteSpace(_gateARepositoryRoot))
        {
            throw new InvalidOperationException("Manual affinity is available only from a development checkout.");
        }

        var helperProject = Path.Combine(
            _gateARepositoryRoot,
            "tools",
            "LatencyPilot.GateAValidation",
            "LatencyPilot.GateAValidation.csproj");
        if (!File.Exists(helperProject))
        {
            throw new FileNotFoundException("The elevated Gate A helper project was not found.", helperProject);
        }

        var outputDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LatencyPilot",
            "ManualAffinity");
        Directory.CreateDirectory(outputDirectory);
        var reportPath = Path.Combine(
            outputDirectory,
            $"manual-affinity-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}.json");

        var startInfo = new ProcessStartInfo
        {
            FileName = ResolveDotnetExecutable(),
            WorkingDirectory = _gateARepositoryRoot,
            UseShellExecute = true,
            Verb = "runas",
        };
        foreach (var argument in new[]
                 {
                     "run",
                     "--project", helperProject,
                     "--configuration", "Release",
                     "--",
                     "--manual-device-affinity",
                     "--action", action,
                     "--target-kind", targetKind,
                     "--device", deviceInstanceId,
                     "--output", reportPath,
                     "--confirm-physical-mutation",
                 })
        {
            startInfo.ArgumentList.Add(argument);
        }
        if (affinityMask is { } mask)
        {
            startInfo.ArgumentList.Add("--mask");
            startInfo.ArgumentList.Add($"0x{mask:X}");
        }
        if (restartDeviceOnly)
        {
            startInfo.ArgumentList.Add("--restart-device-only");
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The elevated manual-affinity helper could not be started.");
        await process.WaitForExitAsync();

        if (!File.Exists(reportPath))
        {
            throw new InvalidOperationException(
                $"Manual affinity helper exited with code {process.ExitCode.ToString(CultureInfo.InvariantCulture)} without producing its report.");
        }

        await using var stream = new FileStream(
            reportPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 4096,
            useAsync: true);
        var report = await JsonSerializer.DeserializeAsync<ManualAffinityHelperReport>(
            stream,
            GateAJsonOptions)
            ?? throw new InvalidDataException("Manual affinity helper returned an empty report.");

        if (!string.Equals(report.Schema, "latencypilot-manual-device-affinity-v2", StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Unsupported manual affinity report schema '{report.Schema}'.");
        }

        return report;
    }

    private static string FormatStoredAffinity(InterruptConfigurationSnapshot configuration)
    {
        if (configuration.ReadStatus != InterruptConfigurationReadStatus.Available)
        {
            return configuration.ReadStatus.ToString();
        }

        if (configuration.DevicePolicy is null && configuration.AssignmentSetOverrideMask is null)
        {
            return "Windows/driver default";
        }

        var policy = configuration.DevicePolicy switch
        {
            null => "policy unspecified",
            0 => "Machine default",
            1 => "All close processors",
            2 => "One close processor",
            3 => "All processors",
            4 => "Specified processors",
            5 => "Spread MSI messages",
            var value => $"Policy {Convert.ToString(value, CultureInfo.InvariantCulture)}",
        };
        var mask = configuration.AssignmentSetOverrideMask is { } affinity
            ? $" · {FormatMask(affinity)} · mask 0x{affinity:X}"
            : string.Empty;
        return policy + mask;
    }

    private static string FormatAllocatedAffinity(InterruptResourceSnapshot resources)
    {
        if (resources.ReadStatus != InterruptResourceReadStatus.Available)
        {
            return resources.ReadStatus.ToString();
        }
        if (resources.Resources.Count == 0)
        {
            return "No allocated interrupt resources";
        }

        return string.Join(
            " · ",
            resources.Resources
                .Select(resource =>
                    $"G{resource.ProcessorGroup} {FormatMask(resource.AffinityMask)}")
                .Distinct(StringComparer.Ordinal));
    }

    private static string FormatMask(ulong mask)
    {
        var cpus = Enumerable.Range(0, 64)
            .Where(cpu => (mask & (1UL << cpu)) != 0)
            .ToArray();
        return cpus.Length switch
        {
            0 => "no CPUs",
            1 => $"CPU {cpus[0].ToString(CultureInfo.InvariantCulture)}",
            _ => $"CPUs {string.Join(",", cpus)}",
        };
    }

    private sealed record ManualAffinitySnapshot(
        IReadOnlyList<ManualAffinityDeviceRow> Rows,
        IReadOnlyList<ManualAffinityCpuOption> CpuOptions,
        string? JournalInspectionError);

    private enum ManualAffinityActionChoice
    {
        Cancel = 0,
        ConfirmRequestedAction = 1,
        RestoreOnlyDevice = 2,
        RestartDeviceOnly = 3,
    }

    private sealed record ManualAffinityDeviceRow(
        PnPDeviceSnapshot Device,
        LatencySensitiveDeviceKind? Kind,
        string? TargetKind,
        string StoredAffinity,
        string AllocatedAffinity,
        ulong? StoredMask,
        bool HasExplicitOverride,
        MutationJournalEntry? PendingRecovery,
        ulong? PendingMask,
        bool CanStartNewPolicyMutation,
        string? InspectionOnlyReason)
    {
        public bool HasPendingRecovery => PendingRecovery is not null;
    }

    private sealed record ManualAffinityCpuOption(
        LogicalProcessorId Processor,
        int PhysicalCoreIndex)
    {
        public override string ToString() =>
            $"CPU {Processor.Number.ToString(CultureInfo.InvariantCulture)} · core {PhysicalCoreIndex.ToString(CultureInfo.InvariantCulture)}";
    }

    private sealed record ManualAffinityHelperReport(
        string Schema,
        string Action,
        string Status,
        bool Succeeded,
        string DeviceInstanceId,
        string? DisplayName,
        string? TargetKind,
        byte? ProcessorNumber,
        ulong? RequestedMask,
        Guid? ExperimentId,
        bool RestartRequired,
        ulong? StoredMask,
        IReadOnlyList<string> AllocatedMasks,
        string? Verification,
        string Message);
}
