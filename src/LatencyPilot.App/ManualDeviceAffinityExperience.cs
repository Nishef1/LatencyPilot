using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using LatencyPilot.Core.Devices;
using LatencyPilot.Core.System;
using LatencyPilot.Platform.Windows.Devices;
using LatencyPilot.Platform.Windows.System;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace LatencyPilot.App;

public sealed partial class MainWindow
{
    private static readonly Guid ManualAffinityDisplayClass =
        new("4D36E968-E325-11CE-BFC1-08002BE10318");
    private bool _manualDeviceAffinityBusy;
    private bool _manualDeviceAffinityDialogOpening;

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
            var dialogStatusText = new TextBlock
            {
                Text = "Inspection complete. No system changes have been made.",
                TextWrapping = TextWrapping.Wrap,
                Style = AppStyle("CaptionTextStyle"),
                Foreground = ThemeBrush("MutedTextBrush"),
            };
            var host = new StackPanel
            {
                Spacing = 12d,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                MinWidth = 620d,
                MaxWidth = 720d,
            };
            RenderManualAffinityWorkspace(host, snapshot, dialogStatusText);

            var dialog = new ContentDialog
            {
                XamlRoot = RootGrid.XamlRoot,
                Title = "Interrupt affinity policy",
                CloseButtonText = "Done",
                DefaultButton = ContentDialogButton.Close,
                MinWidth = 660d,
                MaxWidth = 760d,
                Content = new ScrollViewer
                {
                    MaxHeight = 740d,
                    Padding = new Thickness(0, 4, 8, 8),
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = host,
                },
            };
            AutomationProperties.SetName(dialog, "Manual device interrupt affinity development lab");
            await dialog.ShowAsync();
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

        var intro = new StackPanel { Spacing = 3d };
        intro.Children.Add(new TextBlock
        {
            Text = "DEVELOPMENT LAB",
            Style = AppStyle("HeroEyebrowTextStyle"),
            Foreground = ThemeBrush("AccentBrush"),
        });
        intro.Children.Add(new TextBlock
        {
            Text = "Choose a device, inspect its interrupt-affinity policy, then set a verified processor mask for supported targets.",
            TextWrapping = TextWrapping.Wrap,
            Style = AppStyle("BodyTextStyle"),
        });
        host.Children.Add(intro);
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

        host.Children.Add(new TextBlock
        {
            Text = "Devices",
            Style = AppStyle("MetricLabelTextStyle"),
            Foreground = ThemeBrush("MutedTextBrush"),
        });

        var deviceList = new ListView
        {
            Height = 270d,
            SelectionMode = ListViewSelectionMode.Single,
            IsItemClickEnabled = false,
            Background = ThemeBrush("SurfaceAltBrush"),
            BorderBrush = ThemeBrush("BorderBrush"),
            BorderThickness = new Thickness(1d),
            Padding = new Thickness(4d),
        };
        AutomationProperties.SetName(deviceList, "Devices with interrupt affinity evidence");

        var detailHost = new StackPanel { Spacing = 10d };
        ListViewItem? selectedItem = null;
        var preferredRow = snapshot.Rows.FirstOrDefault(row =>
            string.Equals(
                row.Device.InstanceId,
                selectedDeviceInstanceId,
                StringComparison.OrdinalIgnoreCase))
            ?? snapshot.Rows.FirstOrDefault(static row => row.TargetKind is not null)
            ?? snapshot.Rows[0];

        foreach (var row in snapshot.Rows)
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
            if (ReferenceEquals(row, preferredRow))
            {
                selectedItem = item;
            }
        }

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

        deviceList.SelectionChanged += (_, _) => RenderSelectedDevice();
        host.Children.Add(deviceList);
        host.Children.Add(detailHost);
        host.Children.Add(BuildManualAffinityInfoBanner());

        deviceList.SelectedItem = selectedItem ?? deviceList.Items[0];
        RenderSelectedDevice();
    }

    private Grid BuildManualAffinityDeviceListItem(ManualAffinityDeviceRow row)
    {
        var grid = new Grid { ColumnSpacing = 12d };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1d, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var text = new StackPanel { Spacing = 1d };
        text.Children.Add(new TextBlock
        {
            Text = row.Device.DisplayName,
            MaxLines = 1,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Style = AppStyle("BodyTextStyle"),
        });
        text.Children.Add(new TextBlock
        {
            Text = row.Device.ServiceName ?? row.Kind?.ToString() ?? "Device",
            MaxLines = 1,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Style = AppStyle("CaptionTextStyle"),
            Foreground = ThemeBrush("MutedTextBrush"),
        });
        grid.Children.Add(text);

        var badge = BuildManualAffinityPill(
            row.TargetKind == "Gpu"
                ? "GPU"
                : row.TargetKind == "Xhci"
                    ? "USBXHCI"
                    : "Read only",
            row.TargetKind is null ? "MutedTextBrush" : "AccentBrush",
            row.TargetKind is null ? "SurfaceAltBrush" : "PremiumOverviewQuietBrush");
        badge.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(badge, 1);
        grid.Children.Add(badge);
        return grid;
    }

    private Border BuildManualAffinityDeviceDetail(
        StackPanel host,
        ManualAffinitySnapshot snapshot,
        ManualAffinityDeviceRow row,
        TextBlock dialogStatusText)
    {
        var content = new StackPanel { Spacing = 10d };

        var header = new Grid { ColumnSpacing = 12d };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1d, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var heading = new StackPanel { Spacing = 2d };
        heading.Children.Add(new TextBlock
        {
            Text = row.Device.DisplayName,
            TextWrapping = TextWrapping.Wrap,
            Style = AppStyle("SubsectionTitleTextStyle"),
        });
        heading.Children.Add(new TextBlock
        {
            Text = $"{row.Kind?.ToString() ?? "Other"} · {row.Device.ServiceName ?? "service unavailable"}",
            TextWrapping = TextWrapping.Wrap,
            Style = AppStyle("CaptionTextStyle"),
            Foreground = ThemeBrush("MutedTextBrush"),
        });
        header.Children.Add(heading);

        var advancedButton = new Button
        {
            Content = "Advanced…",
            Style = AppStyle("QuietButtonStyle"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(
            advancedButton,
            $"Show advanced interrupt details for {row.Device.DisplayName}");
        advancedButton.Click += (_, _) => ShowManualAffinityAdvancedFlyout(advancedButton, row);
        Grid.SetColumn(advancedButton, 1);
        header.Children.Add(advancedButton);
        content.Children.Add(header);

        content.Children.Add(BuildManualAffinityPropertyRow(
            "DevObj name",
            row.Device.InstanceId));
        if (row.Device.Parent.ReadStatus == DeviceParentReadStatus.Available &&
            !string.IsNullOrWhiteSpace(row.Device.Parent.ParentInstanceId))
        {
            content.Children.Add(BuildManualAffinityPropertyRow(
                "Parent device",
                row.Device.Parent.ParentInstanceId!));
        }

        content.Children.Add(
            BuildManualAffinityMaskPanel(
                host,
                snapshot,
                row,
                dialogStatusText));

        return new Border
        {
            Style = AppStyle("SubtleCardStyle"),
            Child = content,
        };
    }

    private Border BuildManualAffinityMaskPanel(
        StackPanel host,
        ManualAffinitySnapshot snapshot,
        ManualAffinityDeviceRow row,
        TextBlock dialogStatusText)
    {
        var panel = new StackPanel { Spacing = 9d };
        panel.Children.Add(new TextBlock
        {
            Text = "Interrupt affinity mask",
            Style = AppStyle("SubsectionTitleTextStyle"),
        });

        var body = new Grid { ColumnSpacing = 14d };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(185d) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1d, GridUnitType.Star) });

        var actions = new StackPanel { Spacing = 8d };
        var setMaskButton = new Button
        {
            Content = "Set mask",
            Style = AppStyle("SecondaryButtonStyle"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsEnabled = row.TargetKind is not null && !_manualDeviceAffinityBusy,
        };
        AutomationProperties.SetName(
            setMaskButton,
            $"Set processor affinity mask for {row.Device.DisplayName}");
        setMaskButton.Click += (_, _) =>
            ShowManualAffinityProcessorFlyout(
                setMaskButton,
                host,
                snapshot,
                row,
                dialogStatusText);

        var restoreButton = new Button
        {
            Content = "Restore LatencyPilot original",
            Style = AppStyle("QuietButtonStyle"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsEnabled = row.TargetKind is not null && !_manualDeviceAffinityBusy,
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

        actions.Children.Add(setMaskButton);
        actions.Children.Add(restoreButton);
        if (row.TargetKind is null)
        {
            actions.Children.Add(new TextBlock
            {
                Text = "Read only",
                TextWrapping = TextWrapping.Wrap,
                Style = AppStyle("CaptionTextStyle"),
                Foreground = ThemeBrush("MutedTextBrush"),
            });
        }
        else
        {
            actions.Children.Add(new TextBlock
            {
                Text = "One CPU per verified manual experiment.",
                TextWrapping = TextWrapping.Wrap,
                Style = AppStyle("CaptionTextStyle"),
                Foreground = ThemeBrush("MutedTextBrush"),
            });
        }

        body.Children.Add(actions);

        var values = new StackPanel { Spacing = 7d };
        values.Children.Add(BuildManualAffinityPropertyRow(
            "Current policy",
            FormatManualAffinityPolicy(row.Device.InterruptConfiguration)));
        values.Children.Add(BuildManualAffinityPropertyRow(
            "Specified mask",
            FormatManualSpecifiedMask(row.Device.InterruptConfiguration)));
        values.Children.Add(BuildManualAffinityPropertyRow(
            "Current assignment",
            row.AllocatedAffinity));
        values.Children.Add(BuildManualAffinityPropertyRow(
            "Ownership",
            row.HasExplicitOverride
                ? "Explicit override stored; LatencyPilot only owns it when its mutation journal says so."
                : "No explicit specified-processors override stored.",
            row.HasExplicitOverride ? "SemanticAttentionBrush" : "MutedTextBrush"));

        Grid.SetColumn(values, 1);
        body.Children.Add(values);
        panel.Children.Add(body);

        return new Border
        {
            Padding = new Thickness(12d),
            CornerRadius = new CornerRadius(10d),
            Background = ThemeBrush("SurfaceAltBrush"),
            BorderBrush = ThemeBrush("BorderBrush"),
            BorderThickness = new Thickness(1d),
            Child = panel,
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

    private void ShowManualAffinityProcessorFlyout(
        Button anchor,
        StackPanel host,
        ManualAffinitySnapshot snapshot,
        ManualAffinityDeviceRow row,
        TextBlock dialogStatusText)
    {
        if (row.TargetKind is null || _manualDeviceAffinityBusy)
        {
            return;
        }

        ManualAffinityCpuOption? selectedCpu = row.StoredSingleCpu is { } storedCpu
            ? snapshot.CpuOptions.FirstOrDefault(option => option.Processor.Number == storedCpu)
            : null;

        var title = new TextBlock
        {
            Text = "Processor affinity policy",
            Style = AppStyle("SubsectionTitleTextStyle"),
        };
        var description = new TextBlock
        {
            Text = "Choose the CPU that may service this device's interrupts. LatencyPilot verifies one processor at a time and rolls back if runtime evidence does not match.",
            TextWrapping = TextWrapping.Wrap,
            Style = AppStyle("CaptionTextStyle"),
            Foreground = ThemeBrush("MutedTextBrush"),
        };

        var cpuGrid = new Grid { ColumnSpacing = 18d, RowSpacing = 5d };
        var columnCount = snapshot.CpuOptions.Count >= 12
            ? 4
            : snapshot.CpuOptions.Count >= 6
                ? 2
                : 1;
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

        var applyButton = new Button
        {
            Content = "Apply & verify runtime",
            Style = AppStyle("PrimaryButtonStyle"),
            IsEnabled = selectedCpu is not null,
        };
        var groupName = $"ManualAffinityCpu-{Guid.NewGuid():N}";
        for (var index = 0; index < snapshot.CpuOptions.Count; index++)
        {
            var option = snapshot.CpuOptions[index];
            var radio = new RadioButton
            {
                Content = $"CPU {option.Processor.Number.ToString(CultureInfo.InvariantCulture)}",
                GroupName = groupName,
                IsChecked = selectedCpu?.Processor.Number == option.Processor.Number,
                Tag = option,
                MinWidth = 90d,
            };
            ToolTipService.SetToolTip(
                radio,
                $"Physical core {option.PhysicalCoreIndex.ToString(CultureInfo.InvariantCulture)}");
            radio.Checked += (_, _) =>
            {
                selectedCpu = option;
                applyButton.IsEnabled = !_manualDeviceAffinityBusy;
            };
            Grid.SetRow(radio, index % rowCount);
            Grid.SetColumn(radio, index / rowCount);
            cpuGrid.Children.Add(radio);
        }

        var flyout = new Flyout();
        var cancelButton = new Button
        {
            Content = "Cancel",
            Style = AppStyle("QuietButtonStyle"),
        };
        cancelButton.Click += (_, _) => flyout.Hide();
        applyButton.Click += async (_, _) =>
        {
            if (selectedCpu is null)
            {
                return;
            }

            var cpu = selectedCpu;
            flyout.Hide();
            await RunManualAffinityActionAsync(
                host,
                dialogStatusText,
                row,
                "Apply",
                cpu.Processor.Number);
        };

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8d,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        buttonRow.Children.Add(cancelButton);
        buttonRow.Children.Add(applyButton);

        var content = new StackPanel
        {
            Spacing = 12d,
            MinWidth = 420d,
            MaxWidth = 520d,
        };
        content.Children.Add(title);
        content.Children.Add(description);
        content.Children.Add(new Border
        {
            Padding = new Thickness(10d),
            CornerRadius = new CornerRadius(8d),
            Background = ThemeBrush("SurfaceAltBrush"),
            BorderBrush = ThemeBrush("BorderBrush"),
            BorderThickness = new Thickness(1d),
            Child = cpuGrid,
        });
        content.Children.Add(buttonRow);

        flyout.Content = content;
        flyout.ShowAt(anchor);
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

    private Border BuildManualAffinityInfoBanner() =>
        new()
        {
            Padding = new Thickness(10d),
            CornerRadius = new CornerRadius(10d),
            Background = ThemeBrush("PremiumOverviewQuietBrush"),
            BorderBrush = ThemeBrush("BorderBrush"),
            BorderThickness = new Thickness(1d),
            Child = new StackPanel
            {
                Spacing = 2d,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Stored policy ≠ current assignment ≠ runtime proof",
                        Style = AppStyle("MetricLabelTextStyle"),
                        Foreground = ThemeBrush("TextBrush"),
                    },
                    new TextBlock
                    {
                        Text = "Set mask is available only for GPU and USBXHCI. A new setting is kept only after Windows assignment and target-only ETW ISR verification both succeed.",
                        TextWrapping = TextWrapping.Wrap,
                        Style = AppStyle("CaptionTextStyle"),
                        Foreground = ThemeBrush("MutedTextBrush"),
                    },
                },
            },
        };

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
