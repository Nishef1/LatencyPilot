using System.Globalization;
using LatencyPilot.Platform.Windows.Devices;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace LatencyPilot.App;

public sealed partial class MainWindow
{
    private async void InspectDeviceEvidenceButton_Click(object sender, RoutedEventArgs e)
    {
        InspectDeviceEvidenceButton.IsEnabled = false;
        DeviceEvidenceStatusText.Text = "Reading present PnP devices and interrupt evidence…";

        try
        {
            var inventory = await Task.Run(DeviceInventoryReader.CapturePresentDevices);
            var representativeDevices = RepresentativeDeviceEvidenceSelector.Select(inventory);

            DeviceEvidenceStatusText.Text = string.Create(
                CultureInfo.InvariantCulture,
                $"{inventory.PresentDeviceCount:N0} present · {inventory.DevicesWithDriverMetadataCount:N0} with driver metadata · {inventory.DevicesWithReadableInterruptConfigurationCount:N0} with readable stored interrupt configuration · {inventory.DevicesWithAssignedInterruptsCount:N0} with allocated IRQ resources.");

            await ShowDeviceEvidenceDialogAsync(representativeDevices);
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Detailed device evidence inspection failed.");
            DeviceEvidenceStatusText.Text = "Detailed device evidence could not be read. See the diagnostics log for details.";
        }
        finally
        {
            InspectDeviceEvidenceButton.IsEnabled = true;
        }
    }

    private async Task ShowDeviceEvidenceDialogAsync(IReadOnlyList<RepresentativeDeviceEvidence> devices)
    {
        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(new TextBlock
        {
            Text = "Configuration evidence is not runtime evidence. MSISupported describes stored configuration; allocated IRQ entries describe Configuration Manager resources; actual DPC/ISR attribution remains in the observation results.",
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Foreground = ThemeBrush("MutedTextBrush"),
        });

        if (devices.Count == 0)
        {
            content.Children.Add(new TextBlock
            {
                Text = "No representative display, network or USBXHCI-bound device was found in the current present-device snapshot.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = ThemeBrush("TextBrush"),
            });
        }
        else
        {
            foreach (var item in devices)
            {
                content.Children.Add(BuildDeviceEvidencePanel(item));
            }
        }

        var scroll = new ScrollViewer
        {
            MaxHeight = 560,
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

    private Border BuildDeviceEvidencePanel(RepresentativeDeviceEvidence item)
    {
        var device = item.Device;
        var panel = new Border
        {
            Padding = new Thickness(14),
            CornerRadius = new CornerRadius(12),
            Background = ThemeBrush("SurfaceAltBrush"),
            BorderBrush = ThemeBrush("BorderBrush"),
            BorderThickness = new Thickness(1),
        };

        var stack = new StackPanel { Spacing = 6 };
        panel.Child = stack;
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

    private TextBlock CreateEvidenceLine(string label, string value) =>
        new()
        {
            Text = $"{label}: {value}",
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
}
