using LatencyPilot.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace LatencyPilot.App;

public sealed partial class MainWindow
{
    private Border? _measurementReadinessCard;
    private ComboBox? _measurementReadinessScenarioSource;
    private CheckBox? _measurementContextReadyCheckBox;
    private CheckBox? _measurementConsistencyReadyCheckBox;
    private TextBlock? _measurementReadinessStatusText;
    private bool _measurementContextAcknowledged;
    private bool _measurementConsistencyAcknowledged;
    private bool _updatingMeasurementReadiness;
    private bool _measurementReadinessInitialized;

    internal void InitializeMeasurementReadinessExperience()
    {
        if (_measurementReadinessInitialized)
        {
            return;
        }

        _measurementReadinessInitialized = true;
        RebuildMeasurementReadinessCard();

        RootGrid.ActualThemeChanged += (_, _) => RebuildMeasurementReadinessCard();
        TryRegisterHighContrastChanged(RebuildMeasurementReadinessCard);

        ServiceStatusBadgeText.RegisterPropertyChangedCallback(
            TextBlock.TextProperty,
            (_, _) => UpdateMeasurementReadinessState());
        CaptureBaselineButton.RegisterPropertyChangedCallback(
            Control.IsEnabledProperty,
            (_, _) => UpdateMeasurementReadinessState());

        UpdateMeasurementReadinessState();
    }

    private bool IsRepeatedBaselinePrepared =>
        _measurementContextAcknowledged && _measurementConsistencyAcknowledged;

    private void RebuildMeasurementReadinessCard()
    {
        // This host owns preparation from construction onward, including before Loaded.
        // Reparenting via FrameworkElement.Parent is unreliable before the tree is loaded.
        BaselinePreparationHost.Children.Clear();

        RebindMeasurementScenarioSource();

        var card = new Border
        {
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
        };
        _measurementReadinessCard = card;

        var root = new StackPanel { Spacing = 8 };
        card.Child = root;

        _measurementContextReadyCheckBox = new CheckBox
        {
            IsChecked = _measurementContextAcknowledged,
            IsEnabled = !_measurementBusy,
        };
        _measurementContextReadyCheckBox.Checked += MeasurementReadinessCheckBox_Changed;
        _measurementContextReadyCheckBox.Unchecked += MeasurementReadinessCheckBox_Changed;
        AutomationProperties.SetName(_measurementContextReadyCheckBox, "Measurement context preparation confirmed");
        root.Children.Add(_measurementContextReadyCheckBox);

        _measurementConsistencyReadyCheckBox = new CheckBox
        {
            IsChecked = _measurementConsistencyAcknowledged,
            IsEnabled = !_measurementBusy,
        };
        _measurementConsistencyReadyCheckBox.Checked += MeasurementReadinessCheckBox_Changed;
        _measurementConsistencyReadyCheckBox.Unchecked += MeasurementReadinessCheckBox_Changed;
        AutomationProperties.SetName(_measurementConsistencyReadyCheckBox, "Measurement consistency preparation confirmed");
        root.Children.Add(_measurementConsistencyReadyCheckBox);

        _measurementReadinessStatusText = new TextBlock
        {
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
        };
        AutomationProperties.SetName(_measurementReadinessStatusText, "Repeated baseline readiness");
        root.Children.Add(_measurementReadinessStatusText);

        UpdateMeasurementReadinessContent();

        BaselinePreparationHost.Children.Add(card);
        UpdateMeasurementReadinessState();
    }

    private void RebindMeasurementScenarioSource()
    {
        if (ReferenceEquals(_measurementReadinessScenarioSource, _measurementScenarioComboBox))
        {
            return;
        }

        if (_measurementReadinessScenarioSource is not null)
        {
            _measurementReadinessScenarioSource.SelectionChanged -= MeasurementReadinessScenarioChanged;
        }

        _measurementReadinessScenarioSource = _measurementScenarioComboBox;
        if (_measurementReadinessScenarioSource is not null)
        {
            _measurementReadinessScenarioSource.SelectionChanged += MeasurementReadinessScenarioChanged;
        }
    }

    private void MeasurementReadinessScenarioChanged(object sender, SelectionChangedEventArgs e)
    {
        _measurementContextAcknowledged = false;
        _measurementConsistencyAcknowledged = false;

        if (_measurementContextReadyCheckBox is not null)
        {
            _measurementContextReadyCheckBox.IsChecked = false;
        }

        if (_measurementConsistencyReadyCheckBox is not null)
        {
            _measurementConsistencyReadyCheckBox.IsChecked = false;
        }

        UpdateMeasurementReadinessContent();
        UpdateMeasurementReadinessState();
    }

    private void MeasurementReadinessCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        _measurementContextAcknowledged = _measurementContextReadyCheckBox?.IsChecked == true;
        _measurementConsistencyAcknowledged = _measurementConsistencyReadyCheckBox?.IsChecked == true;
        UpdateMeasurementReadinessState();
    }

    private void UpdateMeasurementReadinessContent()
    {
        if (_measurementContextReadyCheckBox is null || _measurementConsistencyReadyCheckBox is null)
        {
            return;
        }

        var (contextText, consistencyText, contextHelp, consistencyHelp) = SelectedMeasurementScenario switch
        {
            MeasurementScenario.IdleBaseline => (
                "Unnecessary apps are closed and I will not start unrelated work during the baseline.",
                "I will leave the PC otherwise idle and keep the same power state through all five 20-second windows.",
                "Controlled idle is intentionally quiet. Close unnecessary applications; normal background Windows activity does not invalidate the run by itself.",
                "Keep user activity and power conditions stable for the full decision baseline so inter-window variation reflects the machine rather than a changing workload."),
            MeasurementScenario.BeforeAfter => (
                "The same app or game is warmed for both sides.",
                "The same scene, power state and background load will be reproduced.",
                "The five-second LatencyPilot settle period is not workload warm-up. Finish loading, shader compilation, startup transitions or other one-time work before starting the baseline unless those transitions are intentionally the workload being tested.",
                "Before/after evidence is comparable only when the foreground workload, power conditions and background state are reproduced closely enough on both sides."),
            _ => (
                "The real workload is warmed and at a repeatable point.",
                "The workload and background activity will stay consistent.",
                "For a real-world run, keep the applications that are part of the problem. Do not close them merely to improve the numbers. Finish one-time startup/loading work first unless it is intentionally what you are measuring.",
                "Other applications may remain open. Repeatability matters more than artificially making the machine idle; use the same scene, action loop or workload pattern through the full sequence."),
        };

        _measurementContextReadyCheckBox.Content = new TextBlock { Text = contextText, TextWrapping = TextWrapping.Wrap, FontSize = 12 };
        _measurementConsistencyReadyCheckBox.Content = new TextBlock { Text = consistencyText, TextWrapping = TextWrapping.Wrap, FontSize = 12 };
        AutomationProperties.SetHelpText(_measurementContextReadyCheckBox, contextHelp);
        AutomationProperties.SetHelpText(_measurementConsistencyReadyCheckBox, consistencyHelp);
    }

    private void UpdateMeasurementReadinessState()
    {
        if (_updatingMeasurementReadiness)
        {
            return;
        }

        _updatingMeasurementReadiness = true;
        try
        {
            var prepared = IsRepeatedBaselinePrepared;
            var serviceConnected = _observationServiceReady &&
                string.Equals(ServiceStatusBadgeText.Text, "Service connected", StringComparison.OrdinalIgnoreCase);
            var baselineAvailable = prepared && serviceConnected && !_measurementBusy;

            CaptureBaselineButton.IsEnabled = baselineAvailable;

            if (_measurementContextReadyCheckBox is not null)
            {
                _measurementContextReadyCheckBox.IsEnabled = !_measurementBusy;
            }

            if (_measurementConsistencyReadyCheckBox is not null)
            {
                _measurementConsistencyReadyCheckBox.IsEnabled = !_measurementBusy;
            }

            if (_measurementReadinessStatusText is null)
            {
                return;
            }

            if (_measurementBusy)
            {
                _measurementReadinessStatusText.Text = "Capture in progress · preparation locked.";
                _measurementReadinessStatusText.Foreground = ThemeBrush("AccentBrush");
            }
            else if (!prepared)
            {
                _measurementReadinessStatusText.Text =
                    $"Preparation {(_measurementContextAcknowledged ? 1 : 0) + (_measurementConsistencyAcknowledged ? 1 : 0)}/2 · confirm both checks to unlock baseline.";
                _measurementReadinessStatusText.Foreground = ThemeBrush("MutedTextBrush");
            }
            else if (!serviceConnected)
            {
                _measurementReadinessStatusText.Text =
                    "Prepared · waiting for the observation service.";
                _measurementReadinessStatusText.Foreground = ThemeBrush("AccentBrush");
            }
            else
            {
                _measurementReadinessStatusText.Text =
                    "Prepared · ready for five 20-second windows.";
                _measurementReadinessStatusText.Foreground = ThemeBrush("SuccessBrush");
            }
        }
        finally
        {
            _updatingMeasurementReadiness = false;
        }
    }
}
