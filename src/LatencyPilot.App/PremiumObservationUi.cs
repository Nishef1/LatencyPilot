using System.Globalization;
using System.Runtime.InteropServices;
using LatencyPilot.Protocol;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI.ViewManagement;

namespace LatencyPilot.App;

public sealed partial class MainWindow
{
    private readonly AccessibilitySettings _accessibilitySettings = new();
    private Border? _snapshotEvidenceCard;
    private Border? _snapshotEvidenceBadge;
    private TextBlock? _snapshotEvidenceBadgeText;
    private TextBlock? _snapshotEvidenceTitleText;
    private TextBlock? _snapshotEvidenceSummaryText;
    private TextBlock? _tailChartScaleText;
    private TailDataBar? _dpcGuidanceBar;
    private TailDataBar? _isrGuidanceBar;
    private TailDataBar? _oneMillisecondBar;
    private TailDataBar? _threeMillisecondBar;
    private TextBlock? _dpcGuidanceValueText;
    private TextBlock? _isrGuidanceValueText;
    private TextBlock? _oneMillisecondValueText;
    private TextBlock? _threeMillisecondValueText;
    private KernelLatencyCaptureResponse? _lastPremiumCapture;

    private enum SnapshotSignal
    {
        CaptureWarning,
        NoReferenceExceedance,
        ReferenceExceeded,
        OneMillisecondBucket,
        ThreeMillisecondBucket,
    }

    private sealed class TailDataBar
    {
        private readonly ColumnDefinition _fillColumn;
        private readonly ColumnDefinition _remainderColumn;

        public TailDataBar(ColumnDefinition fillColumn, ColumnDefinition remainderColumn)
        {
            _fillColumn = fillColumn;
            _remainderColumn = remainderColumn;
        }

        public void Set(double value, double maximum)
        {
            var safeMaximum = double.IsFinite(maximum) && maximum > 0d ? maximum : 1d;
            var safeValue = double.IsFinite(value) ? Math.Clamp(value, 0d, safeMaximum) : 0d;
            _fillColumn.Width = new GridLength(safeValue, GridUnitType.Star);
            _remainderColumn.Width = new GridLength(Math.Max(0d, safeMaximum - safeValue), GridUnitType.Star);
        }

        public void Reset() => Set(0d, 1d);
    }

    private void InitializePremiumObservationUi()
    {
        RebuildPremiumObservationCard();
        RootGrid.ActualThemeChanged += (_, _) => RebuildPremiumObservationCard();
        TryRegisterHighContrastChanged(RebuildPremiumObservationCard);

        ServiceStatusBadgeText.RegisterPropertyChangedCallback(
            TextBlock.TextProperty,
            (_, _) => ApplyServiceStatusAppearance());
        BaselineVerdictText.RegisterPropertyChangedCallback(
            TextBlock.TextProperty,
            (_, _) => ApplyBaselineVerdictAppearance());

        ApplyServiceStatusAppearance();
        ApplyBaselineVerdictAppearance();
    }

    private void TryRegisterHighContrastChanged(Action callback)
    {
        try
        {
            _accessibilitySettings.HighContrastChanged += (_, _) =>
                DispatcherQueue.TryEnqueue(() => callback());
        }
        catch (COMException exception)
        {
            Logger.Warning(exception, "High contrast change notifications are unavailable; continuing with the current theme.");
        }
    }

    private void RebuildPremiumObservationCard()
    {
        if (ObservationCard.Child is not StackPanel observationStack)
        {
            return;
        }

        if (_snapshotEvidenceCard is not null)
        {
            observationStack.Children.Remove(_snapshotEvidenceCard);
        }

        _snapshotEvidenceCard = BuildSnapshotEvidenceCard();
        observationStack.Children.Insert(Math.Min(3, observationStack.Children.Count), _snapshotEvidenceCard);

        if (_lastPremiumCapture is null)
        {
            ClearPremiumCapture();
        }
        else
        {
            RenderPremiumCapture(_lastPremiumCapture);
        }

        ApplyServiceStatusAppearance();
        ApplyBaselineVerdictAppearance();
    }

    private Border BuildSnapshotEvidenceCard()
    {
        var card = new Border
        {
            Padding = new Thickness(16),
            CornerRadius = new CornerRadius(16),
            Background = ThemeBrush("PremiumSurfaceBrush"),
            BorderBrush = ThemeBrush("BorderBrush"),
            BorderThickness = new Thickness(1),
        };

        var root = new StackPanel { Spacing = 13 };
        card.Child = root;

        var header = new Grid { ColumnSpacing = 12 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var heading = new StackPanel { Spacing = 2 };
        heading.Children.Add(new TextBlock
        {
            Text = "Snapshot evidence",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = ThemeBrush("TextBrush"),
        });
        heading.Children.Add(new TextBlock
        {
            Text = "Diagnostic context only. Stability decisions require a baseline.",
            FontSize = 12,
            Foreground = ThemeBrush("MutedTextBrush"),
            TextWrapping = TextWrapping.Wrap,
        });
        header.Children.Add(heading);

        _snapshotEvidenceBadgeText = new TextBlock
        {
            Text = "Not captured",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
        };
        _snapshotEvidenceBadge = new Border
        {
            Padding = new Thickness(8, 4, 8, 4),
            CornerRadius = new CornerRadius(999),
            VerticalAlignment = VerticalAlignment.Center,
            Child = _snapshotEvidenceBadgeText,
        };
        Grid.SetColumn(_snapshotEvidenceBadge, 1);
        header.Children.Add(_snapshotEvidenceBadge);
        root.Children.Add(header);

        _snapshotEvidenceTitleText = new TextBlock
        {
            Text = "No snapshot yet.",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = ThemeBrush("TextBrush"),
            TextWrapping = TextWrapping.Wrap,
        };
        root.Children.Add(_snapshotEvidenceTitleText);

        _snapshotEvidenceSummaryText = new TextBlock
        {
            Text = "Quick snapshots are diagnostic only. Build a baseline for stability or optimization decisions.",
            FontSize = 13,
            Foreground = ThemeBrush("MutedTextBrush"),
            TextWrapping = TextWrapping.Wrap,
        };
        root.Children.Add(_snapshotEvidenceSummaryText);

        var chartCard = new Border
        {
            Padding = new Thickness(14),
            CornerRadius = new CornerRadius(12),
            Background = ThemeBrush("SurfaceAltBrush"),
            BorderBrush = ThemeBrush("BorderBrush"),
            BorderThickness = new Thickness(1),
        };
        var chartStack = new StackPanel { Spacing = 11 };
        chartCard.Child = chartStack;

        var chartHeader = new Grid();
        chartHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        chartHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        chartHeader.Children.Add(new TextBlock
        {
            Text = "Tail profile",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = ThemeBrush("TextBrush"),
        });
        _tailChartScaleText = new TextBlock
        {
            Text = "Auto-scaled event rate",
            FontSize = 12,
            Foreground = ThemeBrush("MutedTextBrush"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(_tailChartScaleText, 1);
        chartHeader.Children.Add(_tailChartScaleText);
        chartStack.Children.Add(chartHeader);

        chartStack.Children.Add(CreateTailBarRow(
            "DPC > 100 µs",
            "Microsoft driver-duration guidance reference within DPC events. It is not a LatencyPilot system-health pass/fail threshold.",
            "WarningBrush",
            out _dpcGuidanceBar,
            out _dpcGuidanceValueText));
        chartStack.Children.Add(CreateTailBarRow(
            "ISR > 25 µs",
            "Microsoft driver-duration guidance reference within ISR events. It is not a LatencyPilot system-health pass/fail threshold.",
            "WarningBrush",
            out _isrGuidanceBar,
            out _isrGuidanceValueText));
        chartStack.Children.Add(CreateTailBarRow(
            "DPC/ISR > 1 ms",
            "LatencyPilot local diagnostic bucket: millisecond-scale events as a share of all observed DPC and ISR events. This is not a Windows pass/fail threshold.",
            "ImpactBrush",
            out _oneMillisecondBar,
            out _oneMillisecondValueText));
        chartStack.Children.Add(CreateTailBarRow(
            "DPC/ISR > 3 ms",
            "LatencyPilot local diagnostic bucket: events above 3 ms as a share of all observed DPC and ISR events. This is not a Windows pass/fail threshold.",
            "DangerBrush",
            out _threeMillisecondBar,
            out _threeMillisecondValueText));

        chartStack.Children.Add(new TextBlock
        {
            Text = "Bars are auto-scaled. Exact counts and percentages remain visible.",
            FontSize = 12,
            Foreground = ThemeBrush("MutedTextBrush"),
            TextWrapping = TextWrapping.Wrap,
        });
        root.Children.Add(chartCard);

        return card;
    }

    private Grid CreateTailBarRow(
        string label,
        string tooltip,
        string foregroundBrushKey,
        out TailDataBar bar,
        out TextBlock valueText)
    {
        var row = new Grid { RowSpacing = 6 };
        row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var labelText = new TextBlock
        {
            Text = label,
            FontSize = 12,
            Foreground = ThemeBrush("TextBrush"),
        };
        ToolTipService.SetToolTip(labelText, tooltip);
        header.Children.Add(labelText);

        valueText = new TextBlock
        {
            Text = "—",
            FontSize = 12,
            Foreground = ThemeBrush("MutedTextBrush"),
        };
        ToolTipService.SetToolTip(valueText, tooltip);
        Grid.SetColumn(valueText, 1);
        header.Children.Add(valueText);
        row.Children.Add(header);

        var fillColumn = new ColumnDefinition { Width = new GridLength(0, GridUnitType.Star) };
        var remainderColumn = new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) };
        var barGrid = new Grid();
        barGrid.ColumnDefinitions.Add(fillColumn);
        barGrid.ColumnDefinitions.Add(remainderColumn);
        barGrid.Children.Add(new Border
        {
            Background = ThemeBrush(foregroundBrushKey),
            CornerRadius = new CornerRadius(3.5),
        });

        var track = new Border
        {
            Height = 7,
            CornerRadius = new CornerRadius(3.5),
            Background = ThemeBrush("ChartTrackBrush"),
            Child = barGrid,
        };
        Grid.SetRow(track, 1);
        row.Children.Add(track);

        bar = new TailDataBar(fillColumn, remainderColumn);
        return row;
    }

    private void RenderPremiumCapture(KernelLatencyCaptureResponse capture)
    {
        _lastPremiumCapture = capture;
        if (_snapshotEvidenceBadge is null ||
            _snapshotEvidenceBadgeText is null ||
            _snapshotEvidenceTitleText is null ||
            _snapshotEvidenceSummaryText is null)
        {
            return;
        }

        RenderDashboardCapture(capture);

        var integrityIssue = GetCaptureIntegrityIssue(capture);
        var signal = integrityIssue is null
            ? GetSnapshotSignal(capture)
            : SnapshotSignal.CaptureWarning;
        ApplySnapshotSignalAppearance(signal);

        switch (signal)
        {
            case SnapshotSignal.CaptureWarning:
                _snapshotEvidenceBadgeText.Text = "Capture warning";
                _snapshotEvidenceTitleText.Text = "Snapshot incomplete.";
                _snapshotEvidenceSummaryText.Text =
                    $"{integrityIssue} Use these values for diagnosis only.";
                break;
            case SnapshotSignal.NoReferenceExceedance:
                _snapshotEvidenceBadgeText.Text = "Diagnostic only";
                _snapshotEvidenceTitleText.Text = "No reference exceedance in this snapshot.";
                _snapshotEvidenceSummaryText.Text =
                    "No DPC exceeded 100 µs and no ISR exceeded 25 µs in this five-second window. Build a baseline before making stability claims.";
                break;
            case SnapshotSignal.ReferenceExceeded:
                var dpcGuidanceRate = Rate(capture.DpcThresholds.GuidanceExceedanceCount, capture.Dpc.Count);
                var isrGuidanceRate = Rate(capture.IsrThresholds.GuidanceExceedanceCount, capture.Isr.Count);
                _snapshotEvidenceBadgeText.Text = "Reference exceeded";
                _snapshotEvidenceTitleText.Text = "Driver-duration references exceeded.";
                _snapshotEvidenceSummaryText.Text = string.Create(
                    CultureInfo.InvariantCulture,
                    $"DPC {dpcGuidanceRate:0.###}% · ISR {isrGuidanceRate:0.###}%. These are driver guidance references, not a system health score.");
                break;
            case SnapshotSignal.OneMillisecondBucket:
                _snapshotEvidenceBadgeText.Text = "≥1 ms bucket";
                _snapshotEvidenceTitleText.Text = "An event above 1 ms was observed.";
                _snapshotEvidenceSummaryText.Text =
                    "This is a local tail bucket, not an official Windows severity threshold. Repeat the workload and inspect attribution before drawing conclusions.";
                break;
            case SnapshotSignal.ThreeMillisecondBucket:
                _snapshotEvidenceBadgeText.Text = "≥3 ms bucket";
                _snapshotEvidenceTitleText.Text = "An event above 3 ms was observed.";
                _snapshotEvidenceSummaryText.Text =
                    "This is a local tail bucket, not an official Windows severity threshold. Repeat the workload and inspect attribution before drawing conclusions.";
                break;
            default:
                ClearPremiumCapture();
                return;
        }

        if (signal == SnapshotSignal.CaptureWarning)
        {
            DpcP999Text.Foreground = ThemeBrush("WarningBrush");
            IsrP999Text.Foreground = ThemeBrush("WarningBrush");
        }
        else
        {
            DpcP999Text.Foreground = capture.Dpc.P999Microseconds is null
                ? ThemeBrush("MutedTextBrush")
                : SnapshotSignalBrush(GetDistributionSignal(capture.DpcThresholds));
            IsrP999Text.Foreground = capture.Isr.P999Microseconds is null
                ? ThemeBrush("MutedTextBrush")
                : SnapshotSignalBrush(GetDistributionSignal(capture.IsrThresholds));
        }

        UpdateTailChart(capture);
    }

    private void UpdateTailChart(KernelLatencyCaptureResponse capture)
    {
        if (_dpcGuidanceBar is null ||
            _isrGuidanceBar is null ||
            _oneMillisecondBar is null ||
            _threeMillisecondBar is null ||
            _dpcGuidanceValueText is null ||
            _isrGuidanceValueText is null ||
            _oneMillisecondValueText is null ||
            _threeMillisecondValueText is null ||
            _tailChartScaleText is null)
        {
            return;
        }

        var totalEvents = capture.Dpc.Count + capture.Isr.Count;
        var dpcGuidanceRate = Rate(capture.DpcThresholds.GuidanceExceedanceCount, capture.Dpc.Count);
        var isrGuidanceRate = Rate(capture.IsrThresholds.GuidanceExceedanceCount, capture.Isr.Count);
        var overOneCount = capture.DpcThresholds.OverOneMillisecondCount + capture.IsrThresholds.OverOneMillisecondCount;
        var overThreeCount = capture.DpcThresholds.OverThreeMillisecondsCount + capture.IsrThresholds.OverThreeMillisecondsCount;
        var overOneRate = Rate(overOneCount, totalEvents);
        var overThreeRate = Rate(overThreeCount, totalEvents);

        var largestRate = Math.Max(
            Math.Max(dpcGuidanceRate, isrGuidanceRate),
            Math.Max(overOneRate, overThreeRate));
        var chartMaximum = Math.Min(100d, Math.Max(1d, Math.Ceiling(largestRate)));

        SetTailBar(_dpcGuidanceBar, _dpcGuidanceValueText, chartMaximum, dpcGuidanceRate, capture.DpcThresholds.GuidanceExceedanceCount, capture.Dpc.Count);
        SetTailBar(_isrGuidanceBar, _isrGuidanceValueText, chartMaximum, isrGuidanceRate, capture.IsrThresholds.GuidanceExceedanceCount, capture.Isr.Count);
        SetTailBar(_oneMillisecondBar, _oneMillisecondValueText, chartMaximum, overOneRate, overOneCount, totalEvents);
        SetTailBar(_threeMillisecondBar, _threeMillisecondValueText, chartMaximum, overThreeRate, overThreeCount, totalEvents);

        _tailChartScaleText.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"Auto-scale 0–{chartMaximum:0.#}%");
    }

    private static void SetTailBar(
        TailDataBar bar,
        TextBlock valueText,
        double maximum,
        double rate,
        int count,
        int total)
    {
        bar.Set(rate, maximum);
        valueText.Text = total == 0
            ? "—"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{count:N0} / {total:N0} · {rate:0.###}%");
    }

    private static double Rate(int count, int total) =>
        total <= 0 ? 0d : count * 100d / total;

    private static SnapshotSignal GetSnapshotSignal(KernelLatencyCaptureResponse capture)
    {
        if (capture.DpcThresholds.OverThreeMillisecondsCount > 0 ||
            capture.IsrThresholds.OverThreeMillisecondsCount > 0)
        {
            return SnapshotSignal.ThreeMillisecondBucket;
        }

        if (capture.DpcThresholds.OverOneMillisecondCount > 0 ||
            capture.IsrThresholds.OverOneMillisecondCount > 0)
        {
            return SnapshotSignal.OneMillisecondBucket;
        }

        if (capture.DpcThresholds.GuidanceExceedanceCount > 0 ||
            capture.IsrThresholds.GuidanceExceedanceCount > 0)
        {
            return SnapshotSignal.ReferenceExceeded;
        }

        return SnapshotSignal.NoReferenceExceedance;
    }

    private static SnapshotSignal GetDistributionSignal(LatencyThresholdSummary thresholds)
    {
        if (thresholds.OverThreeMillisecondsCount > 0)
        {
            return SnapshotSignal.ThreeMillisecondBucket;
        }

        if (thresholds.OverOneMillisecondCount > 0)
        {
            return SnapshotSignal.OneMillisecondBucket;
        }

        return thresholds.GuidanceExceedanceCount > 0
            ? SnapshotSignal.ReferenceExceeded
            : SnapshotSignal.NoReferenceExceedance;
    }

    private void ApplySnapshotSignalAppearance(SnapshotSignal signal)
    {
        if (_snapshotEvidenceBadge is null || _snapshotEvidenceBadgeText is null)
        {
            return;
        }

        var foregroundKey = signal switch
        {
            SnapshotSignal.NoReferenceExceedance => "SuccessBrush",
            SnapshotSignal.CaptureWarning => "WarningBrush",
            SnapshotSignal.ReferenceExceeded => "WarningBrush",
            SnapshotSignal.OneMillisecondBucket => "ImpactBrush",
            SnapshotSignal.ThreeMillisecondBucket => "DangerBrush",
            _ => "MutedTextBrush",
        };
        var backgroundKey = signal switch
        {
            SnapshotSignal.NoReferenceExceedance => "SuccessSoftBrush",
            SnapshotSignal.CaptureWarning => "WarningSoftBrush",
            SnapshotSignal.ReferenceExceeded => "WarningSoftBrush",
            SnapshotSignal.OneMillisecondBucket => "ImpactSoftBrush",
            SnapshotSignal.ThreeMillisecondBucket => "DangerSoftBrush",
            _ => "SurfaceStrongBrush",
        };

        _snapshotEvidenceBadge.Background = ThemeBrush(backgroundKey);
        _snapshotEvidenceBadgeText.Foreground = ThemeBrush(foregroundKey);
    }

    private Brush SnapshotSignalBrush(SnapshotSignal signal) =>
        ThemeBrush(signal switch
        {
            SnapshotSignal.NoReferenceExceedance => "SuccessBrush",
            SnapshotSignal.CaptureWarning => "WarningBrush",
            SnapshotSignal.ReferenceExceeded => "WarningBrush",
            SnapshotSignal.OneMillisecondBucket => "ImpactBrush",
            SnapshotSignal.ThreeMillisecondBucket => "DangerBrush",
            _ => "TextBrush",
        });

    private void ApplyServiceStatusAppearance()
    {
        var text = ServiceStatusBadgeText.Text ?? string.Empty;
        var brushKey = text.Contains("connected", StringComparison.OrdinalIgnoreCase)
            ? "SuccessBrush"
            : text.Contains("checking", StringComparison.OrdinalIgnoreCase)
                ? "AccentBrush"
                : text.Contains("mismatch", StringComparison.OrdinalIgnoreCase) ||
                  text.Contains("not ready", StringComparison.OrdinalIgnoreCase)
                    ? "WarningBrush"
                    : "DangerBrush";
        ServiceStatusDot.Background = ThemeBrush(brushKey);
    }

    private void ApplyBaselineVerdictAppearance()
    {
        if (BaselineVerdictText.Parent is not Border badge)
        {
            return;
        }

        var verdict = BaselineVerdictText.Text ?? string.Empty;
        var foregroundKey = verdict.Equals("Valid", StringComparison.OrdinalIgnoreCase)
            ? "SuccessBrush"
            : verdict.Equals("Capturing", StringComparison.OrdinalIgnoreCase)
                ? "AccentBrush"
                : verdict.Equals("Inconclusive", StringComparison.OrdinalIgnoreCase)
                    ? "WarningBrush"
                    : "MutedTextBrush";
        var backgroundKey = verdict.Equals("Valid", StringComparison.OrdinalIgnoreCase)
            ? "SuccessSoftBrush"
            : verdict.Equals("Capturing", StringComparison.OrdinalIgnoreCase)
                ? "AccentSoftBrush"
                : verdict.Equals("Inconclusive", StringComparison.OrdinalIgnoreCase)
                    ? "WarningSoftBrush"
                    : "SurfaceBrush";

        badge.Background = ThemeBrush(backgroundKey);
        BaselineVerdictText.Foreground = ThemeBrush(foregroundKey);
    }

    private void ClearPremiumCapture()
    {
        _lastPremiumCapture = null;
        ClearDashboardCaptureVisuals();
        if (_snapshotEvidenceBadge is null ||
            _snapshotEvidenceBadgeText is null ||
            _snapshotEvidenceTitleText is null ||
            _snapshotEvidenceSummaryText is null)
        {
            return;
        }

        _snapshotEvidenceBadge.Background = ThemeBrush("SurfaceStrongBrush");
        _snapshotEvidenceBadgeText.Foreground = ThemeBrush("MutedTextBrush");
        _snapshotEvidenceBadgeText.Text = "Not captured";
        _snapshotEvidenceTitleText.Text = "No snapshot yet.";
        _snapshotEvidenceSummaryText.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"p99.9 appears only with at least {ObservationProtocol.MinimumSamplesForP999:N0} samples. Quick snapshots are diagnostic; baseline evidence decides stability.");

        DpcP999Text.Foreground = ThemeBrush("TextBrush");
        IsrP999Text.Foreground = ThemeBrush("TextBrush");
        ResetTailBar(_dpcGuidanceBar, _dpcGuidanceValueText);
        ResetTailBar(_isrGuidanceBar, _isrGuidanceValueText);
        ResetTailBar(_oneMillisecondBar, _oneMillisecondValueText);
        ResetTailBar(_threeMillisecondBar, _threeMillisecondValueText);
        if (_tailChartScaleText is not null)
        {
            _tailChartScaleText.Text = "Auto-scaled event rate";
        }
    }

    private static void ResetTailBar(TailDataBar? bar, TextBlock? valueText)
    {
        bar?.Reset();
        if (valueText is not null)
        {
            valueText.Text = "—";
        }
    }

    private Brush ThemeBrush(string key) => DashboardThemeResources.Brush(RootGrid, key);
}
