using LatencyPilot.App.Services;
using Microsoft.UI.Text;
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
        if (_measurementScenarioCard?.Child is not StackPanel scenarioStack)
        {
            return;
        }

        if (_measurementReadinessCard is not null &&
            _measurementReadinessCard.Parent is Panel oldParent)
        {
            oldParent.Children.Remove(_measurementReadinessCard);
        }

        RebindMeasurementScenarioSource();

        var card = new Border
        {
            Padding = new Thickness(14),
            CornerRadius = new CornerRadius(12),
            Background = ThemeBrush("SurfaceBrush"),
            BorderBrush = ThemeBrush("BorderBrush"),
            BorderThickness = new Thickness(1),
        };
        _measurementReadinessCard = card;

        var root = new StackPanel { Spacing = 8 };
        card.Child = root;

        var header = new Grid { ColumnSpacing = 10 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock
        {
            Text = "Baseline preparation",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = ThemeBrush("TextBrush"),
        });

        var badge = new Border
        {
            Padding = new Thickness(8, 3, 8, 3),
            CornerRadius = new CornerRadius(9),
            Background = ThemeBrush("AccentSoftBrush"),
        };
        badge.Child = new TextBlock
        {
            Text = "2 checks",
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = ThemeBrush("AccentBrush"),
        };
        Grid.SetColumn(badge, 1);
        header.Children.Add(badge);
        root.Children.Add(header);

        root.Children.Add(new TextBlock
        {
            Text = "These checks only gate the five-window repeated baseline. A single 5-second observation stays available whenever the service is ready.",
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Foreground = ThemeBrush("MutedTextBrush"),
        });

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

        var insertionIndex = Math.Max(0, scenarioStack.Children.Count - 1);
        scenarioStack.Children.Insert(insertionIndex, card);
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
                "I will leave the PC otherwise idle and keep the same power state through all five windows.",
                "Controlled idle is intentionally quiet. Close unnecessary applications; normal background Windows activity does not invalidate the run by itself.",
                "Keep user activity and power conditions stable so inter-window variation reflects the machine rather than a changing workload."),
            MeasurementScenario.BeforeAfter => (
                "I can reproduce the same apps or game, workload and power state before and after the change.",
                "I will keep background activity as similar as practical on both sides of the comparison.",
                "Before/after evidence is only comparable when the foreground workload and power conditions are reproduced.",
                "Background apps do not have to be closed; they should be kept as consistent as practical between both sides."),
            _ => (
                "The apps or game that reproduce the issue are open and I will use them normally during the baseline.",
                "I will keep the workload and background activity roughly consistent through all five windows.",
                "For a real-world run, do not close the apps that are part of the problem just to make the latency numbers look better.",
                "Other applications may remain open. Consistency matters more than artificially making the system idle."),
        };

        _measurementContextReadyCheckBox.Content = contextText;
        _measurementConsistencyReadyCheckBox.Content = consistencyText;
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
                _measurementReadinessStatusText.Text = "Measurement in progress. Preparation choices are frozen until capture completes.";
                _measurementReadinessStatusText.Foreground = ThemeBrush("AccentBrush");
            }
            else if (!prepared)
            {
                _measurementReadinessStatusText.Text =
                    "Repeated baseline locked until both checks are confirmed. Single 5-second capture remains available.";
                _measurementReadinessStatusText.Foreground = ThemeBrush("MutedTextBrush");
            }
            else if (!serviceConnected)
            {
                _measurementReadinessStatusText.Text =
                    "Preparation confirmed. Repeated baseline will unlock when the read-only observation service is connected.";
                _measurementReadinessStatusText.Foreground = ThemeBrush("AccentBrush");
            }
            else
            {
                _measurementReadinessStatusText.Text =
                    "Ready for repeated baseline. Keep the confirmed conditions stable through all five windows.";
                _measurementReadinessStatusText.Foreground = ThemeBrush("SuccessBrush");
            }
        }
        finally
        {
            _updatingMeasurementReadiness = false;
        }
    }
}
