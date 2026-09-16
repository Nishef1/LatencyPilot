using System.Globalization;
using LatencyPilot.Benchmarking.Baselines;
using LatencyPilot.Protocol;
using Microsoft.UI.Xaml.Controls;

namespace LatencyPilot.App;

public sealed partial class MainWindow
{
    private BaselineTransientSignalSummary? _latestBaselineTransientSignals;
    private bool _baselineTransientThemeHooksRegistered;

    private void ApplyBaselineTransientSignals(IReadOnlyList<KernelLatencyCaptureResponse> captures)
    {
        ArgumentNullException.ThrowIfNull(captures);

        _latestBaselineTransientSignals = BaselineTransientSignalAnalyzer.Analyze(
            captures.Select(static (capture, index) => new BaselineTransientWindowSignal(
                index + 1,
                capture.DpcThresholds.OverOneMillisecondCount,
                capture.DpcThresholds.OverThreeMillisecondsCount,
                capture.Dpc.MaximumMicroseconds,
                capture.IsrThresholds.OverOneMillisecondCount,
                capture.IsrThresholds.OverThreeMillisecondsCount,
                capture.Isr.MaximumMicroseconds)).ToArray());

        EnsureBaselineTransientThemeHooks();

        if (_latestBaselineTransientSignals.HasOverOneMillisecondSignal)
        {
            BaselineReasonsText.Text =
                $"{BaselineReasonsText.Text} {FormatTransientExactEvidence(_latestBaselineTransientSignals)} " +
                "This transient-tail signal is shown separately and does not change baseline-quality-v2 p99 repeatability validity.";
        }

        DecorateDashboardForTransientSignals();
    }

    private void EnsureBaselineTransientThemeHooks()
    {
        if (_baselineTransientThemeHooksRegistered)
        {
            return;
        }

        _baselineTransientThemeHooksRegistered = true;
        RootGrid.ActualThemeChanged += (_, _) =>
            DispatcherQueue.TryEnqueue(DecorateDashboardForTransientSignals);
        TryRegisterHighContrastChanged(() =>
            DispatcherQueue.TryEnqueue(DecorateDashboardForTransientSignals));
    }

    private void DecorateDashboardForTransientSignals()
    {
        var summary = _latestBaselineTransientSignals;
        if (summary is null ||
            !summary.HasOverOneMillisecondSignal ||
            !string.Equals(BaselineVerdictText.Text, "Valid", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        BaselineSummaryText.Text = "Valid · transient spike";
        BaselineSummaryIcon.Symbol = Symbol.Important;
        BaselineSummaryText.Foreground = ThemeBrush("WarningBrush");
        BaselineSummaryDetailText.Text = FormatTransientDashboardDetail(summary);
    }

    private static string FormatTransientDashboardDetail(BaselineTransientSignalSummary summary)
    {
        var countSummary = summary.HasOverThreeMillisecondSignal
            ? FormatOverThreeMillisecondCounts(summary)
            : FormatOverOneMillisecondCounts(summary);
        var maximum = summary.LargestDurationMicroseconds is null ||
                      summary.LargestWindowNumber is null ||
                      string.IsNullOrWhiteSpace(summary.LargestMetricName)
            ? string.Empty
            : string.Create(
                CultureInfo.InvariantCulture,
                $" · max {summary.LargestDurationMicroseconds.Value / 1_000d:0.###} ms {summary.LargestMetricName} in W{summary.LargestWindowNumber.Value}");

        return $"p99 repeatable · {countSummary}{maximum}";
    }

    private static string FormatTransientExactEvidence(BaselineTransientSignalSummary summary)
    {
        var oneMillisecond = FormatOverOneMillisecondCounts(summary);
        var threeMillisecond = summary.HasOverThreeMillisecondSignal
            ? $"; {FormatOverThreeMillisecondCounts(summary)}"
            : string.Empty;
        var maximum = summary.LargestDurationMicroseconds is null ||
                      summary.LargestWindowNumber is null ||
                      string.IsNullOrWhiteSpace(summary.LargestMetricName)
            ? string.Empty
            : string.Create(
                CultureInfo.InvariantCulture,
                $"; largest {summary.LargestMetricName} event {summary.LargestDurationMicroseconds.Value / 1_000d:0.###} ms in window {summary.LargestWindowNumber.Value}");

        return $"Transient tail evidence: {oneMillisecond}{threeMillisecond}{maximum}.";
    }

    private static string FormatOverOneMillisecondCounts(BaselineTransientSignalSummary summary) =>
        FormatTransientCounts(
            summary.DpcOverOneMillisecondCount,
            summary.IsrOverOneMillisecondCount,
            ">1 ms");

    private static string FormatOverThreeMillisecondCounts(BaselineTransientSignalSummary summary) =>
        FormatTransientCounts(
            summary.DpcOverThreeMillisecondCount,
            summary.IsrOverThreeMillisecondCount,
            ">3 ms");

    private static string FormatTransientCounts(int dpcCount, int isrCount, string threshold)
    {
        var parts = new List<string>(2);
        if (dpcCount > 0)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{dpcCount:N0} DPC {threshold}"));
        }

        if (isrCount > 0)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{isrCount:N0} ISR {threshold}"));
        }

        return parts.Count == 0 ? $"0 events {threshold}" : string.Join(" · ", parts);
    }
}
