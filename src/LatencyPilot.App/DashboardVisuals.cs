using System.Globalization;
using LatencyPilot.App.Services;
using LatencyPilot.App.ViewModels;
using LatencyPilot.Platform.Windows.Devices;
using LatencyPilot.Protocol;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace LatencyPilot.App;

public sealed partial class MainWindow
{
    private ComboBox? _dashboardScenarioSource;
    private bool _syncingDashboardScenario;

    internal void InitializeDashboardVisuals()
    {
        DashboardScenarioComboBox.SelectionChanged += DashboardScenarioComboBox_SelectionChanged;
        AutomationProperties.SetName(DashboardScenarioComboBox, "Measurement scenario");
        AutomationProperties.SetHelpText(
            DashboardScenarioComboBox,
            "Choose real-world workload, controlled idle, or before/after comparison. The same authoritative scenario is used by exported evidence.");

        RebindDashboardScenarioSource();
        ApplyDashboardCompaction();
        ClearDashboardCaptureVisuals();
        UpdateDashboardBaselineVisuals();

        if (_snapshotEvidenceBadgeText is not null)
        {
            _snapshotEvidenceBadgeText.RegisterPropertyChangedCallback(
                TextBlock.TextProperty,
                (_, _) =>
                {
                    if (_lastPremiumCapture is null)
                    {
                        ClearDashboardCaptureVisuals();
                    }
                    else
                    {
                        RenderDashboardCapture(_lastPremiumCapture);
                    }
                });
        }

        BaselineVerdictText.RegisterPropertyChangedCallback(
            TextBlock.TextProperty,
            (_, _) => UpdateDashboardBaselineVisuals());
        BaselineWindowsList.RegisterPropertyChangedCallback(
            ItemsControl.ItemsSourceProperty,
            (_, _) => UpdateDashboardBaselineVisuals());

        RootGrid.ActualThemeChanged += (_, _) =>
            DispatcherQueue.TryEnqueue(() =>
            {
                RebindDashboardScenarioSource();
                ApplyDashboardCompaction();
                if (_lastPremiumCapture is not null)
                {
                    RenderDashboardCapture(_lastPremiumCapture);
                }
                UpdateDashboardBaselineVisuals();
            });
        TryRegisterHighContrastChanged(() =>
            DispatcherQueue.TryEnqueue(() =>
            {
                ApplyDashboardCompaction();
                if (_lastPremiumCapture is not null)
                {
                    RenderDashboardCapture(_lastPremiumCapture);
                }
                UpdateDashboardBaselineVisuals();
            }));

    }

    private void DashboardScenarioComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingDashboardScenario || _measurementScenarioComboBox is null)
        {
            return;
        }

        _syncingDashboardScenario = true;
        try
        {
            if (_measurementScenarioComboBox.SelectedIndex != DashboardScenarioComboBox.SelectedIndex)
            {
                _measurementScenarioComboBox.SelectedIndex = DashboardScenarioComboBox.SelectedIndex;
            }
        }
        finally
        {
            _syncingDashboardScenario = false;
        }

        ApplyDashboardCompaction();
    }

    private void HiddenScenario_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingDashboardScenario || _measurementScenarioComboBox is null)
        {
            return;
        }

        _syncingDashboardScenario = true;
        try
        {
            DashboardScenarioComboBox.SelectedIndex = _measurementScenarioComboBox.SelectedIndex;
        }
        finally
        {
            _syncingDashboardScenario = false;
        }

        DispatcherQueue.TryEnqueue(ApplyDashboardCompaction);
    }

    private void RebindDashboardScenarioSource()
    {
        if (ReferenceEquals(_dashboardScenarioSource, _measurementScenarioComboBox))
        {
            return;
        }

        if (_dashboardScenarioSource is not null)
        {
            _dashboardScenarioSource.SelectionChanged -= HiddenScenario_SelectionChanged;
        }

        _dashboardScenarioSource = _measurementScenarioComboBox;
        if (_dashboardScenarioSource is null)
        {
            return;
        }

        _dashboardScenarioSource.SelectionChanged += HiddenScenario_SelectionChanged;
        if (DashboardScenarioComboBox.Items.Count == 0)
        {
            DashboardScenarioComboBox.Items.Add(EvidenceExportService.GetMeasurementDisplayName(MeasurementScenario.RealWorld));
            DashboardScenarioComboBox.Items.Add(EvidenceExportService.GetMeasurementDisplayName(MeasurementScenario.IdleBaseline));
            DashboardScenarioComboBox.Items.Add(EvidenceExportService.GetMeasurementDisplayName(MeasurementScenario.BeforeAfter));
        }

        DashboardScenarioComboBox.SelectedIndex = Math.Max(0, _dashboardScenarioSource.SelectedIndex);
    }

    private void ApplyDashboardCompaction()
    {
        RebindDashboardScenarioSource();

        if (_measurementScenarioCard is not null)
        {
            _measurementScenarioCard.Visibility = Visibility.Collapsed;
        }

        if (_snapshotEvidenceCard is not null)
        {
            _snapshotEvidenceCard.Visibility = Visibility.Collapsed;
        }

    }

    private void RenderDashboardDeviceContext(LatencyPilot.Core.Devices.DeviceInventorySnapshot inventory)
    {
        var devices = RepresentativeDeviceEvidenceSelector.Select(inventory);
        var display = devices.FirstOrDefault(static item => item.Kind == RepresentativeDeviceKind.DisplayAdapter)?.Device;
        PrimaryGpuText.Text = display?.DisplayName ?? "Display adapter unavailable";
        PrimaryGpuDriverText.Text = string.IsNullOrWhiteSpace(display?.Driver.Version)
            ? "Driver metadata unavailable"
            : $"Driver {display.Driver.Version}";
        DisplayEvidenceText.Text = display is null ? "Not found" : "Detected";
        NetworkEvidenceText.Text = devices.Any(static item => item.Kind == RepresentativeDeviceKind.NetworkAdapter) ? "Detected" : "Not found";
        UsbEvidenceText.Text = devices.Any(static item => item.Kind == RepresentativeDeviceKind.XhciController) ? "Detected" : "Not found";
    }

    private void RenderDashboardCapture(KernelLatencyCaptureResponse capture)
    {
        DpcP99SummaryText.Text = FormatMicroseconds(capture.Dpc.P99Microseconds);
        IsrP99SummaryText.Text = FormatMicroseconds(capture.Isr.P99Microseconds);
        DpcP99SummaryDetailText.Text = capture.Dpc.P999Microseconds is null
            ? $"max {FormatMicroseconds(capture.Dpc.MaximumMicroseconds)}"
            : $"p99.9 {FormatMicroseconds(capture.Dpc.P999Microseconds)}";
        IsrP99SummaryDetailText.Text = capture.Isr.P999Microseconds is null
            ? $"max {FormatMicroseconds(capture.Isr.MaximumMicroseconds)}"
            : $"p99.9 {FormatMicroseconds(capture.Isr.P999Microseconds)}";

        var totalEvents = capture.Processors.Sum(static processor => processor.Dpc.Count + processor.Isr.Count);
        var busiest = capture.Processors
            .OrderByDescending(static processor => processor.Dpc.Count + processor.Isr.Count)
            .FirstOrDefault();
        var busiestEvents = busiest is null ? 0 : busiest.Dpc.Count + busiest.Isr.Count;
        var concentration = totalEvents <= 0 ? 0d : busiestEvents * 100d / totalEvents;
        CpuConcentrationSummaryText.Text = totalEvents <= 0 ? "—" : $"{concentration:0.#}%";
        CpuConcentrationSummaryDetailText.Text = busiest is null
            ? "No processor evidence"
            : $"CPU {busiest.ProcessorNumber} · {busiestEvents:N0} events";

        var labels = new[] { "p50", "p95", "p99", "p99.9", "max" };
        var dpc = new[]
        {
            new ChartPoint("DPC p50", capture.Dpc.P50Microseconds),
            new ChartPoint("DPC p95", capture.Dpc.P95Microseconds),
            new ChartPoint("DPC p99", capture.Dpc.P99Microseconds),
            new ChartPoint("DPC p99.9", capture.Dpc.P999Microseconds),
            new ChartPoint("DPC max", capture.Dpc.MaximumMicroseconds),
        };
        var isr = new[]
        {
            new ChartPoint("ISR p50", capture.Isr.P50Microseconds),
            new ChartPoint("ISR p95", capture.Isr.P95Microseconds),
            new ChartPoint("ISR p99", capture.Isr.P99Microseconds),
            new ChartPoint("ISR p99.9", capture.Isr.P999Microseconds),
            new ChartPoint("ISR max", capture.Isr.MaximumMicroseconds),
        };
        LatencyProfileChart.SetSeries(
            dpc,
            isr,
            labels,
            $"Latest capture distribution. DPC p99 {FormatMicroseconds(capture.Dpc.P99Microseconds)}; ISR p99 {FormatMicroseconds(capture.Isr.P99Microseconds)}.");
        LatencyChartSubtitleText.Text = "Latest DPC / ISR percentile shape";

        var cpuBars = capture.Processors
            .OrderBy(static processor => processor.ProcessorNumber)
            .Select(processor =>
            {
                var events = processor.Dpc.Count + processor.Isr.Count;
                var share = totalEvents <= 0 ? 0d : events * 100d / totalEvents;
                return new ChartBar($"CPU {processor.ProcessorNumber}", share, $"{events:N0} events");
            })
            .ToArray();
        CpuDistributionChart.SetBars(
            cpuBars,
            busiest is null
                ? "No processor distribution was captured."
                : $"CPU {busiest.ProcessorNumber} was busiest at {concentration:0.#}% of observed DPC and ISR events.");

        var moduleDuration = capture.Modules.Sum(static module => module.TotalDurationMicroseconds);
        var moduleBars = capture.Modules
            .OrderByDescending(static module => module.TotalDurationMicroseconds)
            .Take(6)
            .Select(module => new ChartBar(
                module.ModuleName,
                moduleDuration <= 0d ? 0d : module.TotalDurationMicroseconds * 100d / moduleDuration,
                $"{module.TotalDurationMicroseconds:0.0} µs observed"))
            .ToArray();
        ModuleContributionChart.SetBars(
            moduleBars,
            moduleBars.Length == 0
                ? "No module attribution was available."
                : $"{moduleBars[0].Label} contributed the largest share of attributed kernel time at {moduleBars[0].Value:0.#}%.");

        var totalDpc = Math.Max(1, capture.Processors.Sum(static processor => processor.Dpc.Count));
        var totalIsr = Math.Max(1, capture.Processors.Sum(static processor => processor.Isr.Count));
        var mapRows = capture.Processors
            .OrderByDescending(static processor => processor.Dpc.Count + processor.Isr.Count)
            .Take(9)
            .Select(processor => new InterruptMapRow(
                $"CPU {processor.ProcessorNumber}",
                processor.Dpc.Count * 100d / totalDpc,
                processor.Isr.Count * 100d / totalIsr))
            .ToArray();
        CpuInterruptMap.SetRows(
            mapRows,
            "Intensity cells show each listed processor's real share of observed DPC events and ISR events. No time buckets are inferred.");

        var integrityIssue = GetCaptureIntegrityIssue(capture);
        RecentSnapshotStatusText.Text = integrityIssue is null ? "Snapshot ready" : "Capture warning";
        RecentSnapshotStatusText.Foreground = ThemeBrush(integrityIssue is null ? "SuccessBrush" : "WarningBrush");
        var dominantModule = moduleBars.FirstOrDefault()?.Label;
        RecentSnapshotSummaryText.Text = integrityIssue is not null
            ? integrityIssue
            : dominantModule is null || busiest is null
                ? "Capture is clean. Open exact evidence for the detailed distributions."
                : $"{dominantModule} led attributed kernel time · CPU {busiest.ProcessorNumber} handled {concentration:0.#}% of observed events.";
    }

    private void ClearDashboardCaptureVisuals()
    {
        DpcP99SummaryText.Text = "—";
        IsrP99SummaryText.Text = "—";
        DpcP99SummaryDetailText.Text = "Latest capture";
        IsrP99SummaryDetailText.Text = "Latest capture";
        CpuConcentrationSummaryText.Text = "—";
        CpuConcentrationSummaryDetailText.Text = "Busiest observed CPU";
        RecentSnapshotStatusText.Text = "No snapshot captured yet";
        RecentSnapshotStatusText.Foreground = ThemeBrush("TextBrush");
        RecentSnapshotSummaryText.Text = "Take a quick snapshot to reveal the current tail, CPU concentration, and dominant modules.";
        LatencyProfileChart.Clear("Capture evidence to reveal the latency profile.");
        CpuDistributionChart.Clear("Capture evidence to see where interrupt work concentrates.");
        ModuleContributionChart.Clear("Capture evidence to rank kernel modules by observed time.");
        CpuInterruptMap.Clear("Capture evidence to compare DPC and ISR intensity by processor.");
        LatencyChartSubtitleText.Text = "Latest DPC / ISR percentile shape";
    }

    private void UpdateDashboardBaselineVisuals()
    {
        var verdict = BaselineVerdictText.Text ?? "Not captured";
        BaselineSummaryText.Text = verdict;
        BaselineStatusText.Visibility = verdict == "Not captured" ? Visibility.Collapsed : Visibility.Visible;
        BaselineSummaryIcon.Glyph = verdict switch
        {
            "Valid" => "\uE73E",
            "Inconclusive" => "\uE7BA",
            _ => "\uE823",
        };
        BaselineSummaryText.Foreground = ThemeBrush(verdict.Equals("Valid", StringComparison.OrdinalIgnoreCase)
            ? "SuccessBrush"
            : verdict.Equals("Capturing", StringComparison.OrdinalIgnoreCase)
                ? "AccentBrush"
                : verdict.Equals("Inconclusive", StringComparison.OrdinalIgnoreCase)
                    ? "WarningBrush"
                    : "TextBrush");
        BaselineSummaryDetailText.Text = verdict switch
        {
            "Valid" => "Repeatable evidence ready",
            "Capturing" => $"{BaselineProgressBar.Value:0}% complete",
            "Inconclusive" => "Repeat before comparing",
            _ => "Build one for comparison",
        };

        if (BaselineWindowsList.ItemsSource is not IEnumerable<BaselineWindowRow> rows)
        {
            return;
        }

        var materialized = rows.ToArray();
        if (materialized.Length == 0)
        {
            return;
        }

        var labels = materialized.Select(static (row, index) => $"W{index + 1}").ToArray();
        var dpc = materialized
            .Select(static (row, index) => new ChartPoint($"Window {index + 1} DPC p99", ParseP99(row.DpcSummary)))
            .ToArray();
        var isr = materialized
            .Select(static (row, index) => new ChartPoint($"Window {index + 1} ISR p99", ParseP99(row.IsrSummary)))
            .ToArray();
        if (dpc.Any(static point => point.Value is not null) || isr.Any(static point => point.Value is not null))
        {
            LatencyProfileChart.SetSeries(
                dpc,
                isr,
                labels,
                $"Baseline stability across {materialized.Length} captured window(s), plotted from each real DPC p99 and ISR p99 value.");
            LatencyChartSubtitleText.Text = "Baseline stability · p99 across real windows";
        }
    }

    private static double? ParseP99(string summary)
    {
        const string marker = "p99 ";
        var start = summary.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return null;
        }

        start += marker.Length;
        var end = summary.IndexOf(" µs", start, StringComparison.OrdinalIgnoreCase);
        if (end < 0)
        {
            return null;
        }

        return double.TryParse(
            summary[start..end],
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var value)
            ? value
            : null;
    }

}
