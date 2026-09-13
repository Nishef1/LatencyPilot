using System.Globalization;
using LatencyPilot.Core.Devices;
using LatencyPilot.Platform.Windows.Devices;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace LatencyPilot.App;

public sealed partial class MainWindow
{
    private static readonly Guid DisplayDeviceClass = new("4D36E968-E325-11CE-BFC1-08002BE10318");
    private static readonly Guid NetworkDeviceClass = new("4D36E972-E325-11CE-BFC1-08002BE10318");

    private Button? _inspectDeviceEvidenceButton;
    private TextBlock? _deviceEvidenceStatusText;

    private void InitializeDeviceEvidenceUi()
    {
        var card = new Border
        {
            Padding = new Thickness(18),
            CornerRadius = new CornerRadius(18),
            Background = ThemeBrush("SurfaceBrush"),
            BorderBrush = ThemeBrush("BorderBrush"),
            BorderThickness = new Thickness(1),
        };

        var root = new StackPanel { Spacing = 10 };
        card.Child = root;

        var heading = new Grid { ColumnSpacing = 10 };
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        heading.Children.Add(new TextBlock
        {
            Text = "Device evidence",
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = ThemeBrush("TextBrush"),
        });

        var readOnlyBadge = new Border
        {
            Padding = new Thickness(8, 2, 8, 2),
            CornerRadius = new CornerRadius(9),
            Background = ThemeBrush("SuccessSoftBrush"),
        };
        readOnlyBadge.Child = new TextBlock
        {
            Text = "READ ONLY",
            FontSize = 9,
            FontWeight = FontWeights.SemiBold,
            Foreground = ThemeBrush("SuccessBrush"),
        };
        Grid.SetColumn(readOnlyBadge, 1);
        heading.Children.Add(readOnlyBadge);
        root.Children.Add(heading);

        root.Children.Add(new TextBlock
        {
            Text = "Inspect representative display, network and xHCI devices without changing configuration. Stored MSI/affinity settings, allocated IRQ resources and runtime DPC/ISR behavior remain separate evidence layers.",
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Foreground = ThemeBrush("MutedTextBrush"),
        });

        _inspectDeviceEvidenceButton = new Button
        {
            Content = "Inspect device evidence",
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        _inspectDeviceEvidenceButton.Click += InspectDeviceEvidenceButton_Click;
        AutomationProperties.SetName(_inspectDeviceEvidenceButton, "Inspect read-only device evidence");
        AutomationProperties.SetHelpText(
            _inspectDeviceEvidenceButton,
            "Read driver metadata, stored interrupt configuration and allocated IRQ resources for representative devices.");
        root.Children.Add(_inspectDeviceEvidenceButton);

        _deviceEvidenceStatusText = new TextBlock
        {
            Text = "No detailed device evidence has been inspected yet.",
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Foreground = ThemeBrush("MutedTextBrush"),
        };
        AutomationProperties.SetName(_deviceEvidenceStatusText, "Device evidence status");
        root.Children.Add(_deviceEvidenceStatusText);

        LeftRail.Children.Insert(Math.Min(1, LeftRail.Children.Count), card);
    }

    private async void InspectDeviceEvidenceButton_Click(object sender, RoutedEventArgs e)
    {
        if (_inspectDeviceEvidenceButton is null || _deviceEvidenceStatusText is null)
        {
            return;
        }

        _inspectDeviceEvidenceButton.IsEnabled = false;
        _deviceEvidenceStatusText.Text = "Reading present PnP devices and interrupt evidence…";

        try
        {
            var inventory = await Task.Run(DeviceInventoryReader.CapturePresentDevices);
            var representativeDevices = SelectRepresentativeDevices(inventory);

            _deviceEvidenceStatusText.Text = string.Create(
                CultureInfo.InvariantCulture,
                $"{inventory.PresentDeviceCount:N0} present · {inventory.DevicesWithDriverMetadataCount:N0} with driver metadata · " +
                $"{inventory.DevicesWithReadableInterruptConfigurationCount:N0} with readable stored interrupt configuration · " +
                $"{inventory.DevicesWithAssignedInterruptsCount:N0} with allocated IRQ resources.");

            await ShowDeviceEvidenceDialogAsync(representativeDevices);
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Detailed device evidence inspection failed.");
            _deviceEvidenceStatusText.Text = "Detailed device evidence could not be read. See the diagnostics log for details.";
        }
        finally
        {
            _inspectDeviceEvidenceButton.IsEnabled = true;
        }
    }

    private async Task ShowDeviceEvidenceDialogAsync(IReadOnlyList<RepresentativeDevice> devices)
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

    private Border BuildDeviceEvidencePanel(RepresentativeDevice item)
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
            Text = item.Category,
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
        stack.Children.Add(CreateEvidenceLine("Driver", FormatDriverEvidence(device.Driver)));
        stack.Children.Add(CreateEvidenceLine("Stored configuration", FormatStoredInterruptEvidence(device.InterruptConfiguration)));
        stack.Children.Add(CreateEvidenceLine("Allocated resources", FormatAllocatedInterruptEvidence(device.InterruptResources)));
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

    private static IReadOnlyList<RepresentativeDevice> SelectRepresentativeDevices(DeviceInventorySnapshot inventory)
    {
        var selected = new List<RepresentativeDevice>();
        AddCategory(
            selected,
            inventory.Devices.Where(static device => device.ClassGuid == DisplayDeviceClass),
            "Display / GPU class",
            maximum: 2);
        AddCategory(
            selected,
            inventory.Devices.Where(static device => device.ClassGuid == NetworkDeviceClass),
            "Network adapter class",
            maximum: 3);
        AddCategory(
            selected,
            inventory.Devices.Where(static device =>
                string.Equals(device.ServiceName, "USBXHCI", StringComparison.OrdinalIgnoreCase)),
            "xHCI controller (USBXHCI service)",
            maximum: 3);
        return selected;
    }

    private static void AddCategory(
        List<RepresentativeDevice> destination,
        IEnumerable<PnPDeviceSnapshot> source,
        string category,
        int maximum)
    {
        foreach (var device in source
                     .OrderBy(static device => device.DisplayName, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(static device => device.InstanceId, StringComparer.OrdinalIgnoreCase)
                     .Take(maximum))
        {
            destination.Add(new RepresentativeDevice(category, device));
        }
    }

    private static string FormatDriverEvidence(DriverMetadataSnapshot driver)
    {
        if (!driver.IsAvailable)
        {
            return "metadata unavailable";
        }

        return $"provider {driver.Provider ?? "—"} · version {driver.Version ?? "—"} · INF {driver.InfPath ?? "—"}";
    }

    private static string FormatStoredInterruptEvidence(InterruptConfigurationSnapshot configuration)
    {
        if (configuration.ReadStatus != InterruptConfigurationReadStatus.Available)
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

    private static string FormatAllocatedInterruptEvidence(InterruptResourceSnapshot resources)
    {
        if (resources.ReadStatus != InterruptResourceReadStatus.Available)
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

    private sealed record RepresentativeDevice(string Category, PnPDeviceSnapshot Device);
}
