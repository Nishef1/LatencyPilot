using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using LatencyPilot.Core.Devices;
using LatencyPilot.Core.System;
using LatencyPilot.Platform.Windows.Devices;
using LatencyPilot.Platform.Windows.System;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace LatencyPilot.App;

public sealed partial class MainWindow
{
    private static readonly Guid ManualAffinityDisplayClass =
        new("4D36E968-E325-11CE-BFC1-08002BE10318");
    private bool _manualDeviceAffinityBusy;
    private bool _manualDeviceAffinityDialogOpening;
    private Window? _manualDeviceAffinityWindow;

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
                Spacing = 14d,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                MaxWidth = 820d,
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
                Padding = new Thickness(24d, 22d, 24d, 24d),
                Child = host,
            },
        });

        var window = new Window
        {
            Title = "Interrupt affinity policy",
            Content = root,
            SystemBackdrop = new DesktopAcrylicBackdrop(),
        };

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (File.Exists(iconPath))
        {
            window.AppWindow.SetIcon(iconPath);
        }

        var workArea = DisplayArea
            .GetFromWindowId(window.AppWindow.Id, DisplayAreaFallback.Primary)
            .WorkArea;
        var width = Math.Min(900, workArea.Width);
        var height = Math.Min(900, workArea.Height);
        window.AppWindow.MoveAndResize(new RectInt32(
            workArea.X + (workArea.Width - width) / 2,
            workArea.Y + (workArea.Height - height) / 2,
            width,
            height));

        window.AppWindow.TitleBar.ButtonForegroundColor =
            ((SolidColorBrush)ThemeBrush("TextBrush")).Color;
        window.AppWindow.TitleBar.ButtonHoverForegroundColor =
            ((SolidColorBrush)ThemeBrush("TextBrush")).Color;
        window.AppWindow.TitleBar.ButtonHoverBackgroundColor =
            ((SolidColorBrush)ThemeBrush("SurfaceHoverBrush")).Color;
        window.AppWindow.TitleBar.ButtonPressedForegroundColor =
            ((SolidColorBrush)ThemeBrush("TextBrush")).Color;
        window.AppWindow.TitleBar.ButtonPressedBackgroundColor =
            ((SolidColorBrush)ThemeBrush("SurfaceStrongBrush")).Color;

        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_manualDeviceAffinityWindow, window))
            {
                _manualDeviceAffinityWindow = null;
            }
        };

        _manualDeviceAffinityWindow = window;
        window.Activate();
    }

    private ManualAffinitySnapshot CaptureManualAffinitySnapshot()
    {
        var inventory = DeviceInventoryReader.CapturePresentDevices();
        var topology = ProcessorTopologyReader.Capture();
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
            .Where(device =>
                classified.ContainsKey(device.InstanceId) ||
                device.InterruptConfiguration.HasAnyConfiguration ||
                device.InterruptResources.HasAssignedInterrupts)
            .Select(device =>
            {
                classified.TryGetValue(device.InstanceId, out var kind);
                var targetKind = device.ClassGuid == ManualAffinityDisplayClass
                    ? "Gpu"
                    : string.Equals(device.ServiceName, "USBXHCI", StringComparison.OrdinalIgnoreCase)
                        ? "Xhci"
                        : null;
                return new ManualAffinityDeviceRow(
                    device,
                    classified.ContainsKey(device.InstanceId) ? kind : null,
                    targetKind,
                    FormatStoredAffinity(device.InterruptConfiguration),
                    FormatAllocatedAffinity(device.InterruptResources),
                    TrySingleCpu(device.InterruptConfiguration.AssignmentSetOverrideMask),
                    device.InterruptConfiguration.DevicePolicy == 4 &&
                    device.InterruptConfiguration.AssignmentSetOverrideMask is not null);
            })
            .OrderByDescending(static row => row.TargetKind is not null)
            .ThenByDescending(static row => row.HasExplicitOverride)
            .ThenBy(static row => row.Kind?.ToString(), StringComparer.OrdinalIgnoreCase)
            .ThenBy(static row => row.Device.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new ManualAffinitySnapshot(rows, cpuOptions);
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

        titleGrid.Children.Add(BuildManualAffinityIconTile("\uE950", 46d, 20d));

        var titleText = new StackPanel { Spacing = 2d, VerticalAlignment = VerticalAlignment.Center };
        titleText.Children.Add(new TextBlock
        {
            Text = "Interrupt affinity policy",
            Style = AppStyle("SectionTitleTextStyle"),
        });
        titleText.Children.Add(new TextBlock
        {
            Text = "Inspect supported devices and assign a verified interrupt target.",
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

        var detailHost = new StackPanel { Spacing = 14d };
        var deviceList = new ListView
        {
            Height = 230d,
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
            MinWidth = 250d,
            MaxWidth = 320d,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        AutomationProperties.SetName(searchBox, "Search interrupt-affinity devices");

        var devicesHeader = new Grid { ColumnSpacing = 14d };
        devicesHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1d, GridUnitType.Star) });
        devicesHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var devicesTitle = new StackPanel { Spacing = 2d };
        var devicesTitleRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8d,
        };
        devicesTitleRow.Children.Add(new FontIcon
        {
            FontFamily = new FontFamily("Segoe Fluent Icons"),
            Glyph = "\uE8B7",
            FontSize = 17d,
            Foreground = ThemeBrush("AccentBrush"),
        });
        devicesTitleRow.Children.Add(new TextBlock
        {
            Text = "Devices",
            Style = AppStyle("SubsectionTitleTextStyle"),
        });
        devicesTitle.Children.Add(devicesTitleRow);
        devicesTitle.Children.Add(new TextBlock
        {
            Text = "Select a device to inspect its current policy and supported actions.",
            Style = AppStyle("CaptionTextStyle"),
        });
        devicesHeader.Children.Add(devicesTitle);
        Grid.SetColumn(searchBox, 1);
        devicesHeader.Children.Add(searchBox);

        var devicesContent = new StackPanel { Spacing = 10d };
        devicesContent.Children.Add(devicesHeader);
        devicesContent.Children.Add(deviceList);
        host.Children.Add(new Border
        {
            Style = AppStyle("DashboardCardStyle"),
            Child = devicesContent,
        });
        host.Children.Add(detailHost);

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
                    (row.Device.ServiceName?.Contains(normalized, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (row.Kind?.ToString().Contains(normalized, StringComparison.OrdinalIgnoreCase) ?? false))
                .ToArray();

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
            ?? snapshot.Rows.FirstOrDefault(static row => row.TargetKind is not null)?.Device.InstanceId
            ?? snapshot.Rows[0].Device.InstanceId;
        PopulateDevices(string.Empty, initialId);
    }

    private Grid BuildManualAffinityDeviceListItem(ManualAffinityDeviceRow row)
    {
        var grid = new Grid { ColumnSpacing = 10d };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1d, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = BuildManualAffinityIconTile(GetManualAffinityDeviceGlyph(row.Kind), 32d, 15d);
        icon.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(icon);

        var text = new StackPanel { Spacing = 1d, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock
        {
            Text = row.Device.DisplayName,
            MaxLines = 1,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Style = AppStyle("BodyTextStyle"),
        });
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

        if (row.TargetKind is not null)
        {
            var badge = BuildManualAffinityPill(
                row.TargetKind == "Gpu" ? "GPU · Supported" : "USBXHCI · Supported",
                "SemanticGoodBrush",
                "PremiumOverviewQuietBrush");
            badge.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(badge, 2);
            grid.Children.Add(badge);
        }

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

    private StackPanel BuildManualAffinityDeviceDetail(
        StackPanel host,
        ManualAffinitySnapshot snapshot,
        ManualAffinityDeviceRow row,
        TextBlock dialogStatusText)
    {
        var stack = new StackPanel { Spacing = 14d };

        var summary = new StackPanel { Spacing = 12d };
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

        var supportBadge = BuildManualAffinityPill(
            row.TargetKind is null ? "Inspection only" : "Supported",
            row.TargetKind is null ? "MutedTextBrush" : "SemanticGoodBrush",
            row.TargetKind is null ? "SurfaceAltBrush" : "PremiumOverviewQuietBrush");
        supportBadge.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(supportBadge, 2);
        header.Children.Add(supportBadge);
        summary.Children.Add(header);

        summary.Children.Add(BuildManualAffinityIdentityRow(
            "Device ID",
            row.Device.InstanceId));
        if (row.Device.Parent.ReadStatus == DeviceParentReadStatus.Available &&
            !string.IsNullOrWhiteSpace(row.Device.Parent.ParentInstanceId))
        {
            summary.Children.Add(BuildManualAffinityIdentityRow(
                "Parent device",
                row.Device.Parent.ParentInstanceId!));
        }

        var advancedButton = new Button
        {
            Style = AppStyle("QuietButtonStyle"),
            HorizontalAlignment = HorizontalAlignment.Left,
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 7d,
                Children =
                {
                    new FontIcon
                    {
                        FontFamily = new FontFamily("Segoe Fluent Icons"),
                        Glyph = "\uE713",
                        FontSize = 14d,
                    },
                    new TextBlock { Text = "Advanced details" },
                },
            },
        };
        AutomationProperties.SetName(
            advancedButton,
            $"Show advanced interrupt details for {row.Device.DisplayName}");
        advancedButton.Click += (_, _) => ShowManualAffinityAdvancedFlyout(advancedButton, row);
        summary.Children.Add(advancedButton);

        stack.Children.Add(new Border
        {
            Style = AppStyle("DashboardCardStyle"),
            Child = summary,
        });
        stack.Children.Add(BuildManualAffinityMaskPanel(
            host,
            snapshot,
            row,
            dialogStatusText));
        return stack;
    }

    private Grid BuildManualAffinityIdentityRow(string label, string value)
    {
        var grid = new Grid { ColumnSpacing = 12d };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110d) });
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
            MaxLines = 1,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Style = AppStyle("CaptionTextStyle"),
            Foreground = ThemeBrush("TextBrush"),
        };
        ToolTipService.SetToolTip(valueText, value);
        Grid.SetColumn(valueText, 1);
        grid.Children.Add(valueText);
        return grid;
    }

    private Border BuildManualAffinityMaskPanel(
        StackPanel host,
        ManualAffinitySnapshot snapshot,
        ManualAffinityDeviceRow row,
        TextBlock dialogStatusText)
    {
        var panel = new StackPanel { Spacing = 12d };

        var header = new Grid { ColumnSpacing = 10d };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1d, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var title = new StackPanel { Spacing = 2d };
        var titleRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8d,
        };
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
            Text = row.TargetKind is null
                ? "This device is available for inspection only."
                : "Choose one CPU for this verified manual experiment.",
            Style = AppStyle("CaptionTextStyle"),
        });
        header.Children.Add(title);

        var stateBadge = BuildManualAffinityPill(
            row.TargetKind is null ? "Read only" : "Ready",
            row.TargetKind is null ? "MutedTextBrush" : "SemanticGoodBrush",
            row.TargetKind is null ? "SurfaceAltBrush" : "PremiumOverviewQuietBrush");
        stateBadge.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(stateBadge, 1);
        header.Children.Add(stateBadge);
        panel.Children.Add(header);

        var metrics = new Grid { ColumnSpacing = 8d };
        for (var index = 0; index < 3; index++)
        {
            metrics.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1d, GridUnitType.Star),
            });
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

        if (row.TargetKind is not null)
        {
            ManualAffinityCpuOption? selectedCpu = row.StoredSingleCpu is { } storedCpu
                ? snapshot.CpuOptions.FirstOrDefault(option => option.Processor.Number == storedCpu)
                : null;

            var cpuHeader = new Grid { ColumnSpacing = 10d };
            cpuHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1d, GridUnitType.Star) });
            cpuHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var cpuTitle = new StackPanel { Spacing = 1d };
            cpuTitle.Children.Add(new TextBlock
            {
                Text = "Processor target",
                Style = AppStyle("MetricLabelTextStyle"),
                Foreground = ThemeBrush("TextBrush"),
            });
            cpuTitle.Children.Add(new TextBlock
            {
                Text = "One processor is tested at a time; physical-core mapping is shown on hover.",
                Style = AppStyle("CaptionTextStyle"),
            });
            cpuHeader.Children.Add(cpuTitle);

            var clearButton = new Button
            {
                Content = "Clear",
                Style = AppStyle("QuietButtonStyle"),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(clearButton, 1);
            cpuHeader.Children.Add(clearButton);
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
                IsEnabled = selectedCpu is not null && !_manualDeviceAffinityBusy,
            };

            var cpuGrid = new Grid { ColumnSpacing = 6d, RowSpacing = 6d };
            var columnCount = Math.Min(8, Math.Max(1, snapshot.CpuOptions.Count));
            for (var column = 0; column < columnCount; column++)
            {
                cpuGrid.ColumnDefinitions.Add(
                    new ColumnDefinition { Width = new GridLength(1d, GridUnitType.Star) });
            }
            var rowCount = (int)Math.Ceiling(snapshot.CpuOptions.Count / (double)columnCount);
            for (var rowIndex = 0; rowIndex < rowCount; rowIndex++)
            {
                cpuGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }

            var cpuButtons = new List<ToggleButton>();
            void SelectCpu(ManualAffinityCpuOption? option)
            {
                selectedCpu = option;
                foreach (var button in cpuButtons)
                {
                    button.IsChecked =
                        option is not null &&
                        button.Tag is ManualAffinityCpuOption candidate &&
                        candidate.Processor.Equals(option.Processor);
                }
                applyButton.IsEnabled = selectedCpu is not null && !_manualDeviceAffinityBusy;
            }

            for (var index = 0; index < snapshot.CpuOptions.Count; index++)
            {
                var option = snapshot.CpuOptions[index];
                var cpuButton = new ToggleButton
                {
                    Content = $"CPU {option.Processor.Number.ToString(CultureInfo.InvariantCulture)}",
                    Tag = option,
                    IsChecked = selectedCpu?.Processor.Equals(option.Processor) == true,
                    MinHeight = 36d,
                    MinWidth = 70d,
                    Padding = new Thickness(8d, 5d, 8d, 5d),
                    CornerRadius = new CornerRadius(8d),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                };
                ToolTipService.SetToolTip(
                    cpuButton,
                    $"Physical core {option.PhysicalCoreIndex.ToString(CultureInfo.InvariantCulture)}");
                cpuButton.Click += (_, _) => SelectCpu(option);
                Grid.SetRow(cpuButton, index / columnCount);
                Grid.SetColumn(cpuButton, index % columnCount);
                cpuGrid.Children.Add(cpuButton);
                cpuButtons.Add(cpuButton);
            }

            clearButton.Click += (_, _) => SelectCpu(null);
            panel.Children.Add(new Border
            {
                Padding = new Thickness(8d),
                CornerRadius = new CornerRadius(10d),
                Background = ThemeBrush("SurfaceAltBrush"),
                BorderBrush = ThemeBrush("BorderBrush"),
                BorderThickness = new Thickness(1d),
                Child = new ScrollViewer
                {
                    MaxHeight = 230d,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = cpuGrid,
                },
            });

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
            };
            AutomationProperties.SetName(
                restoreButton,
                $"Restore journal-owned original affinity for {row.Device.DisplayName}");

            restoreButton.Click += async (_, _) =>
                await RunManualAffinityActionAsync(
                    host,
                    dialogStatusText,
                    row,
                    "Restore",
                    null);

            applyButton.Click += async (_, _) =>
            {
                if (selectedCpu is null)
                {
                    return;
                }

                await RunManualAffinityActionAsync(
                    host,
                    dialogStatusText,
                    row,
                    "Apply",
                    selectedCpu.Processor.Number);
            };

            var actions = new Grid { ColumnSpacing = 8d };
            actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1d, GridUnitType.Star) });
            actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            actions.Children.Add(new TextBlock
            {
                Text = row.HasExplicitOverride
                    ? "An explicit mask is stored. LatencyPilot only owns it when the mutation journal says so."
                    : "No explicit processor mask is currently stored.",
                Style = AppStyle("CaptionTextStyle"),
                Foreground = ThemeBrush(row.HasExplicitOverride ? "SemanticAttentionBrush" : "MutedTextBrush"),
                VerticalAlignment = VerticalAlignment.Center,
            });
            Grid.SetColumn(restoreButton, 1);
            actions.Children.Add(restoreButton);
            Grid.SetColumn(applyButton, 2);
            actions.Children.Add(applyButton);
            panel.Children.Add(actions);
        }
        else
        {
            panel.Children.Add(new Border
            {
                Padding = new Thickness(10d, 8d, 10d, 8d),
                CornerRadius = new CornerRadius(8d),
                Background = ThemeBrush("SurfaceAltBrush"),
                Child = new TextBlock
                {
                    Text = "No mutation control is exposed for this device.",
                    Style = AppStyle("CaptionTextStyle"),
                    Foreground = ThemeBrush("MutedTextBrush"),
                },
            });
        }

        panel.Children.Add(new TextBlock
        {
            Text = "Stored policy, Windows assignment, and runtime ISR proof are separate evidence. Supported changes are kept only after verification succeeds.",
            TextWrapping = TextWrapping.Wrap,
            Style = AppStyle("CaptionTextStyle"),
            Foreground = ThemeBrush("MutedTextBrush"),
        });

        return new Border
        {
            Style = AppStyle("DashboardCardStyle"),
            Child = panel,
        };
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
            MinHeight = 78d,
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
            "Resource read",
            resources.ReadStatus.ToString()));
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

    private async Task RunManualAffinityActionAsync(
        StackPanel host,
        TextBlock dialogStatusText,
        ManualAffinityDeviceRow row,
        string action,
        byte? processorNumber)
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
        SetManualAffinityStatus(
            dialogStatusText,
            action == "Apply"
                ? row.TargetKind == "Xhci"
                    ? $"Applying CPU {processorNumber?.ToString(CultureInfo.InvariantCulture)} to {row.Device.DisplayName}. After UAC, keep moving the USB mouse/using USB input during the ~10 s ETW verification; Windows may briefly restart the controller…"
                    : $"Applying CPU {processorNumber?.ToString(CultureInfo.InvariantCulture)} to {row.Device.DisplayName}. After UAC, keep representative graphics activity running during the ~10 s ETW verification; Windows may briefly restart the device…"
                : $"Restoring journal-owned original state for {row.Device.DisplayName}…",
            "SemanticAttentionBrush");

        try
        {
            var report = await RunManualAffinityHelperAsync(
                action,
                row.TargetKind,
                row.Device.InstanceId,
                processorNumber);
            var status = report.Status == "RebootRequired"
                ? "SemanticAttentionBrush"
                : report.Status is "AppliedAndKept" or "AlreadyConfigured" or "Restored" or "NoLatencyPilotChange"
                    ? "SemanticGoodBrush"
                    : "TextBrush";
            var message = report.Status == "RebootRequired"
                ? action == "Restore"
                    ? $"{row.Device.DisplayName}: Windows requires a reboot to finish restoring the journal-owned original state. Reboot, reopen this panel, and choose Restore again."
                    : $"{row.Device.DisplayName}: Windows requires a reboot. Reboot, reopen this panel, and select the same CPU again to resume the journaled experiment."
                : $"{row.Device.DisplayName}: {report.Message}";
            SetManualAffinityStatus(
                dialogStatusText,
                message,
                status);

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
        byte? processorNumber)
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
        if (processorNumber is { } cpu)
        {
            startInfo.ArgumentList.Add("--processor");
            startInfo.ArgumentList.Add(cpu.ToString(CultureInfo.InvariantCulture));
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

        if (!string.Equals(report.Schema, "latencypilot-manual-device-affinity-v1", StringComparison.Ordinal))
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

    private static byte? TrySingleCpu(ulong? mask)
    {
        if (mask is not { } value || value == 0 || (value & (value - 1)) != 0)
        {
            return null;
        }

        for (byte cpu = 0; cpu < 64; cpu++)
        {
            if ((value & (1UL << cpu)) != 0)
            {
                return cpu;
            }
        }

        return null;
    }

    private sealed record ManualAffinitySnapshot(
        IReadOnlyList<ManualAffinityDeviceRow> Rows,
        IReadOnlyList<ManualAffinityCpuOption> CpuOptions);

    private sealed record ManualAffinityDeviceRow(
        PnPDeviceSnapshot Device,
        LatencySensitiveDeviceKind? Kind,
        string? TargetKind,
        string StoredAffinity,
        string AllocatedAffinity,
        byte? StoredSingleCpu,
        bool HasExplicitOverride);

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
        Guid? ExperimentId,
        bool RestartRequired,
        ulong? StoredMask,
        IReadOnlyList<string> AllocatedMasks,
        string? Verification,
        string Message);
}
