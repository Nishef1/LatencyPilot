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
                Spacing = 14d,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                MaxWidth = 620d,
            };
            RenderManualAffinityRows(host, snapshot, dialogStatusText);

            var dialog = new ContentDialog
            {
                XamlRoot = RootGrid.XamlRoot,
                Title = "Manage interrupt affinity",
                CloseButtonText = "Close",
                DefaultButton = ContentDialogButton.Close,
                Content = new ScrollViewer
                {
                    MaxHeight = 720d,
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

    private void RenderManualAffinityRows(
        StackPanel host,
        ManualAffinitySnapshot snapshot,
        TextBlock dialogStatusText)
    {
        host.Children.Clear();

        var intro = new StackPanel { Spacing = 4d };
        intro.Children.Add(new TextBlock
        {
            Text = "Development-only surface · exact rollback available",
            Style = AppStyle("HeroEyebrowTextStyle"),
            Foreground = ThemeBrush("AccentBrush"),
        });
        intro.Children.Add(new TextBlock
        {
            Text = "Inspect the current Windows state, select a supported target CPU, then let the journaled helper apply, verify and roll back the change if evidence is insufficient.",
            TextWrapping = TextWrapping.Wrap,
            Style = AppStyle("BodyTextStyle"),
        });
        host.Children.Add(intro);
        host.Children.Add(BuildManualAffinityInfoBanner());
        host.Children.Add(BuildManualAffinityStatusBanner(dialogStatusText));

        var editableRows = snapshot.Rows.Where(static row => row.TargetKind is not null).ToArray();
        var readOnlyRows = snapshot.Rows.Where(static row => row.TargetKind is null).ToArray();

        if (snapshot.Rows.Count == 0)
        {
            host.Children.Add(new Border
            {
                Style = AppStyle("SubtleCardStyle"),
                Child = new TextBlock
                {
                    Text = "No latency-sensitive device with interrupt evidence is currently available.",
                    TextWrapping = TextWrapping.Wrap,
                    Style = AppStyle("MutedBodyTextStyle"),
                },
            });
            return;
        }

        if (editableRows.Length > 0)
        {
            host.Children.Add(BuildManualAffinitySectionHeader(
                "Editable devices",
                "Supported mutation targets: display adapters and USBXHCI controllers."));
            foreach (var row in editableRows)
            {
                host.Children.Add(BuildManualAffinityRow(host, snapshot, row, dialogStatusText));
            }
        }

        if (readOnlyRows.Length > 0)
        {
            host.Children.Add(BuildManualAffinitySectionHeader(
                "Read-only devices",
                "Observed for context only. No mutation control is exposed for these devices."));
            foreach (var row in readOnlyRows)
            {
                host.Children.Add(BuildManualAffinityRow(host, snapshot, row, dialogStatusText));
            }
        }
    }

    private Border BuildManualAffinityInfoBanner() =>
        new()
        {
            Padding = new Thickness(12d),
            CornerRadius = new CornerRadius(12d),
            Background = ThemeBrush("PremiumOverviewQuietBrush"),
            BorderBrush = ThemeBrush("BorderBrush"),
            BorderThickness = new Thickness(1d),
            Child = new StackPanel
            {
                Spacing = 3d,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Stored policy ≠ active assignment ≠ runtime proof",
                        Style = AppStyle("SubsectionTitleTextStyle"),
                        Foreground = ThemeBrush("TextBrush"),
                    },
                    new TextBlock
                    {
                        Text = "The panel keeps these evidence layers separate. A new setting is retained only after translated allocation and clean target-only ETW ISR verification succeed.",
                        TextWrapping = TextWrapping.Wrap,
                        Style = AppStyle("CaptionTextStyle"),
                        Foreground = ThemeBrush("MutedTextBrush"),
                    },
                },
            },
        };

    private Border BuildManualAffinityStatusBanner(TextBlock statusText) =>
        new()
        {
            Padding = new Thickness(12d),
            CornerRadius = new CornerRadius(10d),
            Background = ThemeBrush("SurfaceAltBrush"),
            BorderBrush = ThemeBrush("BorderBrush"),
            BorderThickness = new Thickness(1d),
            Child = new StackPanel
            {
                Spacing = 3d,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Session status",
                        Style = AppStyle("MetricLabelTextStyle"),
                        Foreground = ThemeBrush("MutedTextBrush"),
                    },
                    statusText,
                },
            },
        };

    private static Border BuildManualAffinitySectionHeader(string title, string description) =>
        new()
        {
            Margin = new Thickness(0d, 4d, 0d, -4d),
            Child = new StackPanel
            {
                Spacing = 2d,
                Children =
                {
                    new TextBlock
                    {
                        Text = title,
                        Style = AppStyle("SubsectionTitleTextStyle"),
                    },
                    new TextBlock
                    {
                        Text = description,
                        TextWrapping = TextWrapping.Wrap,
                        Style = AppStyle("CaptionTextStyle"),
                    },
                },
            },
        };

    private Border BuildManualAffinityRow(
        StackPanel host,
        ManualAffinitySnapshot snapshot,
        ManualAffinityDeviceRow row,
        TextBlock dialogStatusText)
    {
        var title = new TextBlock
        {
            Text = row.Device.DisplayName,
            TextWrapping = TextWrapping.Wrap,
            Style = AppStyle("SubsectionTitleTextStyle"),
        };
        var identity = new TextBlock
        {
            Text = $"{row.Kind?.ToString() ?? "Other"} · {row.Device.ServiceName ?? "service unavailable"} · {row.Device.InstanceId}",
            TextWrapping = TextWrapping.Wrap,
            Style = AppStyle("CaptionTextStyle"),
            Foreground = ThemeBrush("MutedTextBrush"),
        };

        var badges = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6d,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        badges.Children.Add(BuildManualAffinityPill(
            row.TargetKind == "Gpu" ? "GPU" : row.TargetKind == "Xhci" ? "USBXHCI" : "Other",
            row.TargetKind is null ? "MutedTextBrush" : "AccentBrush",
            row.TargetKind is null ? "SurfaceAltBrush" : "PremiumOverviewQuietBrush"));
        badges.Children.Add(BuildManualAffinityPill(
            row.TargetKind is null ? "Read-only" : "Editable",
            row.TargetKind is null ? "MutedTextBrush" : "SemanticGoodBrush",
            row.TargetKind is null ? "SurfaceAltBrush" : "SemanticGoodSoftBrush"));

        var header = new Grid { ColumnSpacing = 12d };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1d, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var heading = new StackPanel { Spacing = 2d };
        heading.Children.Add(title);
        heading.Children.Add(identity);
        header.Children.Add(heading);
        Grid.SetColumn(badges, 1);
        header.Children.Add(badges);

        var metrics = new Grid { ColumnSpacing = 8d, RowSpacing = 8d };
        metrics.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1d, GridUnitType.Star) });
        metrics.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1d, GridUnitType.Star) });
        metrics.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        metrics.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var storedMetric = BuildManualAffinityMetric("Stored policy", row.StoredAffinity);
        var activeMetric = BuildManualAffinityMetric("Active assignment", row.AllocatedAffinity);
        var ownershipMetric = BuildManualAffinityMetric(
            "Ownership",
            row.HasExplicitOverride
                ? "Explicit override stored; ownership is only claimed when the journal owns it."
                : "No explicit specified-processors override stored.",
            row.HasExplicitOverride ? "SemanticAttentionBrush" : "MutedTextBrush");
        metrics.Children.Add(storedMetric);
        Grid.SetColumn(activeMetric, 1);
        metrics.Children.Add(activeMetric);
        Grid.SetRow(ownershipMetric, 1);
        Grid.SetColumnSpan(ownershipMetric, 2);
        metrics.Children.Add(ownershipMetric);

        var cardContent = new StackPanel { Spacing = 12d };
        cardContent.Children.Add(header);
        cardContent.Children.Add(metrics);

        if (row.TargetKind is not null)
        {
            var picker = new ComboBox
            {
                PlaceholderText = "Choose target CPU",
                ItemsSource = snapshot.CpuOptions,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                MinHeight = 40d,
            };
            if (row.StoredSingleCpu is { } currentCpu)
            {
                picker.SelectedItem = snapshot.CpuOptions.FirstOrDefault(
                    option => option.Processor.Number == currentCpu);
            }

            var applyButton = new Button
            {
                Content = "Apply & verify",
                Style = AppStyle("PrimaryButtonStyle"),
                IsEnabled = picker.SelectedItem is not null,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            AutomationProperties.SetName(
                applyButton,
                $"Apply manual interrupt affinity for {row.Device.DisplayName}");
            picker.SelectionChanged += (_, _) =>
                applyButton.IsEnabled = !_manualDeviceAffinityBusy && picker.SelectedItem is not null;
            applyButton.Click += async (_, _) =>
            {
                if (picker.SelectedItem is not ManualAffinityCpuOption cpu)
                {
                    return;
                }

                await RunManualAffinityActionAsync(
                    host,
                    dialogStatusText,
                    row,
                    "Apply",
                    cpu.Processor.Number);
            };

            var restoreButton = new Button
            {
                Content = "Restore this device",
                Style = AppStyle("QuietButtonStyle"),
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            AutomationProperties.SetName(
                restoreButton,
                $"Restore journal-owned original affinity for {row.Device.DisplayName}");
            restoreButton.Click += async (_, _) =>
                await RunManualAffinityActionAsync(host, dialogStatusText, row, "Restore", null);

            cardContent.Children.Add(new TextBlock
            {
                Text = "Target processor",
                Style = AppStyle("MetricLabelTextStyle"),
                Foreground = ThemeBrush("MutedTextBrush"),
            });
            cardContent.Children.Add(picker);

            var actionGrid = new Grid { ColumnSpacing = 8d };
            actionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1d, GridUnitType.Star) });
            actionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1d, GridUnitType.Star) });
            actionGrid.Children.Add(applyButton);
            Grid.SetColumn(restoreButton, 1);
            actionGrid.Children.Add(restoreButton);
            cardContent.Children.Add(actionGrid);
        }
        else
        {
            cardContent.Children.Add(new Border
            {
                Padding = new Thickness(10d, 8d, 10d, 8d),
                CornerRadius = new CornerRadius(8d),
                Background = ThemeBrush("SurfaceAltBrush"),
                Child = new TextBlock
                {
                    Text = "Read only · observation only; no mutation control is available for this device.",
                    TextWrapping = TextWrapping.Wrap,
                    Style = AppStyle("CaptionTextStyle"),
                    Foreground = ThemeBrush("MutedTextBrush"),
                },
            });
        }

        return new Border
        {
            Style = AppStyle("SubtleCardStyle"),
            Child = cardContent,
        };
    }

    private Border BuildManualAffinityPill(string text, string foregroundKey, string backgroundKey) =>
        new()
        {
            Padding = new Thickness(8d, 4d, 8d, 4d),
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

    private Border BuildManualAffinityMetric(string label, string value, string? valueBrushKey = null) =>
        new()
        {
            Padding = new Thickness(10d, 8d, 10d, 8d),
            CornerRadius = new CornerRadius(8d),
            Background = ThemeBrush("SurfaceAltBrush"),
            Child = new StackPanel
            {
                Spacing = 3d,
                Children =
                {
                    new TextBlock
                    {
                        Text = label,
                        Style = AppStyle("MetricLabelTextStyle"),
                        Foreground = ThemeBrush("MutedTextBrush"),
                    },
                    new TextBlock
                    {
                        Text = value,
                        TextWrapping = TextWrapping.Wrap,
                        Style = AppStyle("CaptionTextStyle"),
                        Foreground = ThemeBrush(valueBrushKey ?? "TextBrush"),
                    },
                },
            },
        };

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
                : report.Status is "Kept" or "Restored"
                    ? "SemanticGoodBrush"
                    : "TextBrush";
            SetManualAffinityStatus(
                dialogStatusText,
                report.Status == "RebootRequired"
                    ? $"{row.Device.DisplayName}: Windows requires a reboot. Reboot, reopen this panel, and apply the same CPU again to resume the same journaled experiment."
                    : $"{row.Device.DisplayName}: {report.Message}",
                status);

            var refreshed = await Task.Run(CaptureManualAffinitySnapshot);
            RenderManualAffinityRows(host, refreshed, dialogStatusText);
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
