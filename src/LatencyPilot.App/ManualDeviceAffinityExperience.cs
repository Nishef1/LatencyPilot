using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using LatencyPilot.Core.Devices;
using LatencyPilot.Core.System;
using LatencyPilot.Platform.Windows.Devices;
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
        if (_manualDeviceAffinityBusy)
        {
            return;
        }

        if (_gateAValidationRunning)
        {
            ManualDeviceAffinityStatusText.Text =
                "Manual affinity is blocked while GPU Gate A owns the mutation/measurement session.";
            return;
        }

        ManualDeviceAffinityButton.IsEnabled = false;
        ManualDeviceAffinityStatusText.Text = "Reading current interrupt-affinity policy and allocated resources…";
        try
        {
            var snapshot = await Task.Run(CaptureManualAffinitySnapshot);
            var host = new StackPanel { Spacing = 10d };
            RenderManualAffinityRows(host, snapshot);

            var dialog = new ContentDialog
            {
                XamlRoot = RootGrid.XamlRoot,
                Title = "Manual device affinity",
                CloseButtonText = "Close",
                DefaultButton = ContentDialogButton.Close,
                Content = new ScrollViewer
                {
                    MaxHeight = 700d,
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

    private void RenderManualAffinityRows(StackPanel host, ManualAffinitySnapshot snapshot)
    {
        host.Children.Clear();

        host.Children.Add(new TextBlock
        {
            Text = "Current policy is read directly from each present PnP hardware key; allocated resources are a separate active Windows view. An explicit policy does not imply LatencyPilot owns it.",
            TextWrapping = TextWrapping.Wrap,
            Style = AppStyle("CaptionTextStyle"),
        });
        host.Children.Add(new TextBlock
        {
            Text = "Manual writes are intentionally limited to the display adapter and USBXHCI controllers. Apply is journaled and kept only after allocated interrupt resources verify the requested CPU; verification failure triggers exact rollback. Existing explicit policies are not claimed as LatencyPilot-owned unless the journal owns them.",
            TextWrapping = TextWrapping.Wrap,
            Style = AppStyle("CaptionTextStyle"),
        });

        if (snapshot.Rows.Count == 0)
        {
            host.Children.Add(new TextBlock
            {
                Text = "No latency-sensitive device with interrupt evidence is currently available.",
                Style = AppStyle("MutedBodyTextStyle"),
            });
            return;
        }

        foreach (var row in snapshot.Rows)
        {
            host.Children.Add(BuildManualAffinityRow(host, snapshot, row));
        }
    }

    private Border BuildManualAffinityRow(
        StackPanel host,
        ManualAffinitySnapshot snapshot,
        ManualAffinityDeviceRow row)
    {
        var title = new TextBlock
        {
            Text = row.Device.DisplayName,
            TextWrapping = TextWrapping.Wrap,
            Style = AppStyle("SubsectionTitleTextStyle"),
        };
        var identity = new TextBlock
        {
            Text = $"{row.Kind?.ToString() ?? "Other"} · {row.Device.ServiceName ?? "service unavailable"}",
            TextWrapping = TextWrapping.Wrap,
            Style = AppStyle("CaptionTextStyle"),
        };
        var stored = new TextBlock
        {
            Text = $"Stored policy: {row.StoredAffinity}",
            TextWrapping = TextWrapping.Wrap,
            Style = AppStyle("CaptionTextStyle"),
        };
        var active = new TextBlock
        {
            Text = $"Allocated resources: {row.AllocatedAffinity}",
            TextWrapping = TextWrapping.Wrap,
            Style = AppStyle("CaptionTextStyle"),
        };
        var ownership = new TextBlock
        {
            Text = row.HasExplicitOverride
                ? "Explicit processor override is currently stored."
                : "No explicit specified-processors override is currently stored.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = row.HasExplicitOverride
                ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SemanticAttentionBrush"]
                : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SubtleTextBrush"],
            Style = AppStyle("CaptionTextStyle"),
        };

        var details = new StackPanel { Spacing = 3d };
        details.Children.Add(title);
        details.Children.Add(identity);
        details.Children.Add(stored);
        details.Children.Add(active);
        details.Children.Add(ownership);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8d,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };

        if (row.TargetKind is not null)
        {
            var picker = new ComboBox
            {
                MinWidth = 150d,
                PlaceholderText = "Choose CPU",
                ItemsSource = snapshot.CpuOptions,
            };
            if (row.StoredSingleCpu is { } currentCpu)
            {
                picker.SelectedItem = snapshot.CpuOptions.FirstOrDefault(
                    option => option.Processor.Number == currentCpu);
            }

            var applyButton = new Button
            {
                Content = "Apply & verify",
                Style = AppStyle("SecondaryButtonStyle"),
                IsEnabled = picker.SelectedItem is not null,
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
                    row,
                    "Apply",
                    cpu.Processor.Number);
            };

            var restoreButton = new Button
            {
                Content = "Restore all LatencyPilot changes",
                Style = AppStyle("QuietButtonStyle"),
            };
            AutomationProperties.SetName(
                restoreButton,
                $"Restore journal-owned original affinity for {row.Device.DisplayName}");
            restoreButton.Click += async (_, _) =>
                await RunManualAffinityActionAsync(host, row, "Restore", null);

            actions.Children.Add(picker);
            actions.Children.Add(applyButton);
            actions.Children.Add(restoreButton);
        }
        else
        {
            actions.Children.Add(new TextBlock
            {
                Text = "Read only",
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.SemiBold,
                Style = AppStyle("CaptionTextStyle"),
            });
        }

        var grid = new Grid { ColumnSpacing = 16d, RowSpacing = 8d };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(details);
        Grid.SetColumn(actions, 1);
        grid.Children.Add(actions);

        return new Border
        {
            Style = AppStyle("SubtleCardStyle"),
            Child = grid,
        };
    }

    private async Task RunManualAffinityActionAsync(
        StackPanel host,
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
            ManualDeviceAffinityStatusText.Text =
                "Manual affinity is blocked while GPU Gate A owns the mutation/measurement session.";
            return;
        }

        _manualDeviceAffinityBusy = true;
        ManualDeviceAffinityButton.IsEnabled = false;
        ManualDeviceAffinityStatusText.Text =
            action == "Apply"
                ? $"Applying CPU {processorNumber?.ToString(CultureInfo.InvariantCulture)} to {row.Device.DisplayName}; Windows may briefly restart the device…"
                : $"Restoring journal-owned original state for {row.Device.DisplayName}…";

        try
        {
            var report = await RunManualAffinityHelperAsync(
                action,
                row.TargetKind,
                row.Device.InstanceId,
                processorNumber);
            ManualDeviceAffinityStatusText.Text =
                report.Status == "RebootRequired"
                    ? $"{row.Device.DisplayName}: Windows requires a reboot. Reboot, reopen this panel, and apply the same CPU again to resume the same journaled experiment."
                    : $"{row.Device.DisplayName}: {report.Message}";

            var refreshed = await Task.Run(CaptureManualAffinitySnapshot);
            RenderManualAffinityRows(host, refreshed);
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            ManualDeviceAffinityStatusText.Text = "Manual affinity was cancelled at the Windows elevation prompt; no new change was requested.";
        }
        catch (Exception exception) when (exception is
            InvalidOperationException or
            IOException or
            UnauthorizedAccessException or
            Win32Exception or
            JsonException)
        {
            Logger.Error(exception, "Manual affinity action failed for {DeviceInstanceId}.", row.Device.InstanceId);
            ManualDeviceAffinityStatusText.Text =
                $"Manual affinity action failed: {exception.Message}";
        }
        finally
        {
            _manualDeviceAffinityBusy = false;
            ManualDeviceAffinityButton.IsEnabled = true;
        }
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
            FileName = ResolveDotNetExecutable(),
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
            var value => $"Policy {value.ToString(CultureInfo.InvariantCulture)}",
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
