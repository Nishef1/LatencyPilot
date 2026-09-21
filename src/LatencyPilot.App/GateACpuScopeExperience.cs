using System.ComponentModel;
using LatencyPilot.Benchmarking.Candidates;
using LatencyPilot.Core.Benchmarking;
using LatencyPilot.Core.System;
using LatencyPilot.Platform.Windows.System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace LatencyPilot.App;

public sealed partial class MainWindow
{
    private Button? _gateACpuScopeButton;
    private GpuAutoAffinitySearchScope _gateASearchScope = GpuAutoAffinitySearchScope.Full;
    private LogicalProcessorId[] _gateASelectedProcessors = [];

    private void InitializeGateACpuScope()
    {
        _gateACpuScopeButton = new Button
        {
            Content = "All CPUs ▾",
            MinHeight = 36,
            Style = (Style)Application.Current.Resources["SecondaryButtonStyle"],
        };
        AutomationProperties.SetName(_gateACpuScopeButton, "GPU validation scope");
        _gateACpuScopeButton.Click += GateACpuScopeButton_Click;
        DeveloperValidationHost.Children.Add(_gateACpuScopeButton);
    }

    private async void GateACpuScopeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_measurementBusy || _gateAValidationRunning)
        {
            return;
        }
        try
        {
            var topology = ProcessorTopologyReader.Capture();
            var cpuSets = ProcessorCpuSetReader.Capture();
            var eligible = GpuAffinityCandidatePlanner.Create(topology, [], cpuSets)
                .Select(static candidate => candidate.Processor).ToHashSet();
            var mode = new ComboBox
            {
                Header = "Validation scope",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                ItemsSource = new[] { "Full search", "Selected CPUs · restore Original", "Original only · no system changes" },
                SelectedIndex = (int)_gateASearchScope,
            };
            var explanation = new TextBlock { TextWrapping = TextWrapping.Wrap };
            var choices = new StackPanel { Spacing = 8 };
            var selections = new List<(LogicalProcessorId Processor, CheckBox Choice)>();
            foreach (var core in topology.Cores)
            {
                var row = new StackPanel { Spacing = 8, Orientation = Orientation.Horizontal };
                row.Children.Add(new TextBlock { Text = $"Core {core.Index}", Width = 70, VerticalAlignment = VerticalAlignment.Center });
                foreach (var processor in core.LogicalProcessors)
                {
                    var available = processor.Group == 0 && eligible.Contains(processor);
                    var choice = new CheckBox
                    {
                        Content = $"CPU {processor.Number}" + (available ? "" : " · unavailable"),
                        IsEnabled = available,
                        IsChecked = available && _gateASelectedProcessors.Contains(processor),
                    };
                    ToolTipService.SetToolTip(choice, available
                        ? $"Processor group {processor.Group}, physical core {core.Index}."
                        : "Unavailable in the current topology or reserved/allocated CPU-set state.");
                    selections.Add((processor, choice));
                    row.Children.Add(choice);
                }
                choices.Children.Add(row);
            }
            var selectAll = new Button { Content = "Select all eligible" };
            var clear = new Button { Content = "Clear" };
            selectAll.Click += (_, _) => selections.ForEach(item => item.Choice.IsChecked = item.Choice.IsEnabled);
            clear.Click += (_, _) => selections.ForEach(item => item.Choice.IsChecked = false);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            actions.Children.Add(selectAll);
            actions.Children.Add(clear);
            var error = new TextBlock { TextWrapping = TextWrapping.Wrap };
            var content = new StackPanel { Spacing = 12, MinWidth = 360 };
            content.Children.Add(mode);
            content.Children.Add(explanation);
            content.Children.Add(actions);
            content.Children.Add(new ScrollViewer { Content = choices, MaxHeight = 330 });
            content.Children.Add(error);

            void UpdateMode()
            {
                var custom = mode.SelectedIndex == (int)GpuAutoAffinitySearchScope.Custom;
                choices.Visibility = actions.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
                explanation.Text = mode.SelectedIndex switch
                {
                    1 => "Short paired screening of the selected CPUs. Original is always restored; this cannot close Gate A.",
                    2 => "Five 10-second Original measurements retain every sample. No GPU restart, affinity write or candidate Keep. This checks measurement variability before a search.",
                    _ => "Physical-core screening, bounded SMT refinement and three independent finalist pairs. Keep requires repeatable improvement and final ISR placement proof.",
                };
                error.Text = string.Empty;
            }
            mode.SelectionChanged += (_, _) => UpdateMode();
            UpdateMode();
            var dialog = new ContentDialog
            {
                Title = "GPU validation scope",
                XamlRoot = ((FrameworkElement)Content).XamlRoot,
                Content = content,
                PrimaryButtonText = "Use selection",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
            };
            dialog.PrimaryButtonClick += (_, args) =>
            {
                if (mode.SelectedIndex == 1 && !selections.Any(static item => item.Choice.IsEnabled && item.Choice.IsChecked == true))
                {
                    error.Text = "Select at least one eligible CPU.";
                    args.Cancel = true;
                }
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }
            _gateASearchScope = (GpuAutoAffinitySearchScope)mode.SelectedIndex;
            _gateASelectedProcessors = _gateASearchScope == GpuAutoAffinitySearchScope.Custom
                ? selections.Where(static item => item.Choice.IsEnabled && item.Choice.IsChecked == true)
                    .Select(static item => item.Processor).ToArray()
                : [];
            if (_gateASourceAssessment is { } assessment)
            {
                ApplyGateASourceAssessmentUi(assessment);
            }
        }
        catch (Exception exception) when (exception is Win32Exception or IOException or InvalidOperationException or ArgumentException)
        {
            Logger.Warning(exception, "GPU CPU scope could not be selected.");
            SetGateAValidationStatus($"CPU selection unavailable: {exception.Message}");
        }
    }

    private void UpdateGateAScopeUi()
    {
        if (_gateACpuScopeButton is not null)
        {
            _gateACpuScopeButton.IsEnabled = !_measurementBusy && !_gateAValidationRunning;
            _gateACpuScopeButton.Content = _gateASearchScope switch
            {
                GpuAutoAffinitySearchScope.Custom => $"{_gateASelectedProcessors.Length} CPUs ▾",
                GpuAutoAffinitySearchScope.OriginalDiagnostics => "Original only ▾",
                _ => "All CPUs ▾",
            };
        }
        if (_gateAValidationButton is null || _gateASourceAssessment?.CanRun != true || _gateASearchScope == GpuAutoAffinitySearchScope.Full)
        {
            return;
        }
        _gateAValidationButton.Content = _gateASearchScope == GpuAutoAffinitySearchScope.Custom ? "Run selected CPUs" : "Check Original";
        AutomationProperties.SetName(_gateAValidationButton, _gateAValidationButton.Content.ToString());
        var description = _gateASearchScope == GpuAutoAffinitySearchScope.Custom
            ? $"Selected CPUs: {string.Join(", ", _gateASelectedProcessors.Select(static processor => processor.Number))}. Paired screening only; Original will be restored."
            : "Five Original measurements only. No GPU restart or affinity change; no candidate can be kept.";
        ToolTipService.SetToolTip(_gateAValidationButton, description);
        SetGateAValidationStatus(description + " Diagnostic scope cannot close Gate A.", syncEvidenceStatus: false);
    }
}
