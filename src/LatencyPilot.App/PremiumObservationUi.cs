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
    private Border? _latencyHealthCard;
    private Border? _latencyHealthBadge;
    private TextBlock? _latencyHealthBadgeText;
    private TextBlock? _latencyHealthTitleText;
    private TextBlock? _latencyHealthSummaryText;
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

    private enum CaptureSeverity
    {
        CaptureWarning,
        WithinGuidance,
        GuidanceExceeded,
        PotentialImpact,
        Severe,
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
        try
        {
            SystemBackdrop = new MicaBackdrop();
            RootGrid.Background = null;
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Mica backdrop could not be enabled; continuing with the solid theme fallback.");
        }

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

        if (_latencyHealthCard is not null)
        {
            observationStack.Children.Remove(_latencyHealthCard);
        }

        _latencyHealthCard = BuildLatencyHealthCard();
        observationStack.Children.Insert(Math.Min(3, observationStack.Children.Count), _latencyHealthCard);

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

    private Border BuildLatencyHealthCard()
    {
        var card = new Border
        {
            Padding = new Thickness(18),
            CornerRadius = new CornerRadius(17),
            Background = ThemeBrush("PremiumSurfaceBrush"),
            BorderBrush = ThemeBrush("BorderBrush"),
            BorderThickness = new Thickness(1),
        };

        var root = new StackPanel { Spacing = 15 };
        card.Child = root;

        var header = new Grid { ColumnSpacing = 12 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var heading = new StackPanel { Spacing = 3 };
        heading.Children.Add(new TextBlock
        {
            Text = "Latency health",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = ThemeBrush("TextBrush"),
        });
        heading.Children.Add(new TextBlock
        {
            Text = "Plain-language context for the exact DPC/ISR evidence below.",
            FontSize = 12,
            Foreground = ThemeBrush("MutedTextBrush"),
            TextWrapping = TextWrapping.Wrap,
        });
        header.Children.Add(heading);

        _latencyHealthBadgeText = new TextBlock
        {
            Text = "Not captured",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
        };
        _latencyHealthBadge = new Border
        {
            Padding = new Thickness(10, 5, 10, 5),
            CornerRadius = new CornerRadius(11),
            VerticalAlignment = VerticalAlignment.Center,
            Child = _latencyHealthBadgeText,
        };
        Grid.SetColumn(_latencyHealthBadge, 1);
        header.Children.Add(_latencyHealthBadge);
        root.Children.Add(header);

        _latencyHealthTitleText = new TextBlock
        {
            Text = "Capture an observation to classify the tail.",
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Foreground = ThemeBrush("TextBrush"),
            TextWrapping = TextWrapping.Wrap,
        };
        root.Children.Add(_latencyHealthTitleText);

        _latencyHealthSummaryText = new TextBlock
        {
            Text = "Numbers remain authoritative; color and context labels are additional cues only.",
            FontSize = 14,
            Foreground = ThemeBrush("MutedTextBrush"),
            TextWrapping = TextWrapping.Wrap,
        };
        root.Children.Add(_latencyHealthSummaryText);

        var chartCard = new Border
        {
            Padding = new Thickness(15),
            CornerRadius = new CornerRadius(13),
            Background = ThemeBrush("SurfaceAltBrush"),
            BorderBrush = ThemeBrush("BorderBrush"),
            BorderThickness = new Thickness(1),
        };
        var chartStack = new StackPanel { Spacing = 12 };
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
            "Driver-guidance exceedance rate within DPC events.",
            "WarningBrush",
            out _dpcGuidanceBar,
            out _dpcGuidanceValueText));
        chartStack.Children.Add(CreateTailBarRow(
            "ISR > 25 µs",
            "Driver-guidance exceedance rate within ISR events.",
            "WarningBrush",
            out _isrGuidanceBar,
            out _isrGuidanceValueText));
        chartStack.Children.Add(CreateTailBarRow(
            "DPC/ISR > 1 ms",
            "Local diagnostic bucket: millisecond-scale events as a share of all observed DPC and ISR events. This is not a Windows pass/fail threshold.",
            "ImpactBrush",
            out _oneMillisecondBar,
            out _oneMillisecondValueText));
        chartStack.Children.Add(CreateTailBarRow(
            "DPC/ISR > 3 ms",
            "Local diagnostic bucket: events above 3 ms as a share of all observed DPC and ISR events. This is not a Windows pass/fail threshold.",
            "DangerBrush",
            out _threeMillisecondBar,
            out _threeMillisecondValueText));

        chartStack.Children.Add(new TextBlock
        {
            Text = "Bars are auto-scaled visual aids only. Exact count, denominator and percentage stay visible; 100 µs DPC / 25 µs ISR are Microsoft driver guidance, while 1 ms / 3 ms are LatencyPilot diagnostic buckets rather than Windows pass/fail thresholds.",
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
        if (_latencyHealthBadge is null ||
            _latencyHealthBadgeText is null ||
            _latencyHealthTitleText is null ||
            _latencyHealthSummaryText is null)
        {
            return;
        }

        var integrityIssue = GetCaptureIntegrityIssue(capture);
        var severity = integrityIssue is null
            ? GetCaptureSeverity(capture)
            : CaptureSeverity.CaptureWarning;
        ApplySeverityAppearance(severity);

        switch (severity)
        {
            case CaptureSeverity.CaptureWarning:
                _latencyHealthBadgeText.Text = "Capture warning";
                _latencyHealthTitleText.Text = "This window is incomplete evidence.";
                _latencyHealthSummaryText.Text =
                    $"{integrityIssue} Exact values remain visible for diagnosis, but do not interpret a green-looking metric or low tail count as a clean result until capture integrity is restored.";
                break;
            case CaptureSeverity.WithinGuidance:
                _latencyHealthBadgeText.Text = "Within guidance";
                _latencyHealthTitleText.Text = "No guidance exceedance was observed in this window.";
                _latencyHealthSummaryText.Text =
                    "No DPC exceeded 100 µs and no ISR exceeded 25 µs. That is encouraging for this five-second capture, but a repeated baseline is still required before treating the system as consistently clean.";
                break;
            case CaptureSeverity.GuidanceExceeded:
                var dpcGuidanceRate = Rate(capture.DpcThresholds.GuidanceExceedanceCount, capture.Dpc.Count);
                var isrGuidanceRate = Rate(capture.IsrThresholds.GuidanceExceedanceCount, capture.Isr.Count);
                _latencyHealthBadgeText.Text = "Needs context";
                _latencyHealthTitleText.Text = "Driver guidance was exceeded, without a millisecond-scale spike.";
                _latencyHealthSummaryText.Text = string.Create(
                    CultureInfo.InvariantCulture,
                    $"Guidance exceedance rates in this window were DPC {dpcGuidanceRate:0.###}% and ISR {isrGuidanceRate:0.###}%. These are driver-duration guidance values, not user-impact pass/fail thresholds. Use module attribution and a repeated baseline to determine whether the same pattern persists under the workload you care about.");
                break;
            case CaptureSeverity.PotentialImpact:
                _latencyHealthBadgeText.Text = "≥1 ms tail observed";
                _latencyHealthTitleText.Text = "A millisecond-scale DPC/ISR event was observed.";
                _latencyHealthSummaryText.Text =
                    "At least one event exceeded 1 ms. LatencyPilot treats this as a local diagnostic bucket, not a Windows pass/fail threshold. Repeat the same workload and inspect the responsible modules before drawing a conclusion.";
                break;
            case CaptureSeverity.Severe:
                _latencyHealthBadgeText.Text = "≥3 ms tail observed";
                _latencyHealthTitleText.Text = "A long DPC/ISR tail event was observed.";
                _latencyHealthSummaryText.Text =
                    "At least one DPC/ISR exceeded 3 ms. This is a local diagnostic bucket rather than an official Windows severity boundary; repeat the workload, build a baseline and compare module-level max/tail evidence before attributing impact.";
                break;
            default:
                ClearPremiumCapture();
                return;
        }

        if (severity == CaptureSeverity.CaptureWarning)
        {
            DpcP999Text.Foreground = ThemeBrush("WarningBrush");
            IsrP999Text.Foreground = ThemeBrush("WarningBrush");
        }
        else
        {
            DpcP999Text.Foreground = capture.Dpc.P999Microseconds is null
                ? ThemeBrush("MutedTextBrush")
                : SeverityBrush(GetDistributionSeverity(capture.DpcThresholds));
            IsrP999Text.Foreground = capture.Isr.P999Microseconds is null
                ? ThemeBrush("MutedTextBrush")
                : SeverityBrush(GetDistributionSeverity(capture.IsrThresholds));
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

    private static CaptureSeverity GetCaptureSeverity(KernelLatencyCaptureResponse capture)
    {
        if (capture.DpcThresholds.OverThreeMillisecondsCount > 0 ||
            capture.IsrThresholds.OverThreeMillisecondsCount > 0)
        {
            return CaptureSeverity.Severe;
        }

        if (capture.DpcThresholds.OverOneMillisecondCount > 0 ||
            capture.IsrThresholds.OverOneMillisecondCount > 0)
        {
            return CaptureSeverity.PotentialImpact;
        }

        if (capture.DpcThresholds.GuidanceExceedanceCount > 0 ||
            capture.IsrThresholds.GuidanceExceedanceCount > 0)
        {
            return CaptureSeverity.GuidanceExceeded;
        }

        return CaptureSeverity.WithinGuidance;
    }

    private static CaptureSeverity GetDistributionSeverity(LatencyThresholdSummary thresholds)
    {
        if (thresholds.OverThreeMillisecondsCount > 0)
        {
            return CaptureSeverity.Severe;
        }

        if (thresholds.OverOneMillisecondCount > 0)
        {
            return CaptureSeverity.PotentialImpact;
        }

        return thresholds.GuidanceExceedanceCount > 0
            ? CaptureSeverity.GuidanceExceeded
            : CaptureSeverity.WithinGuidance;
    }

    private void ApplySeverityAppearance(CaptureSeverity severity)
    {
        if (_latencyHealthBadge is null || _latencyHealthBadgeText is null)
        {
            return;
        }

        var foregroundKey = severity switch
        {
            CaptureSeverity.WithinGuidance => "SuccessBrush",
            CaptureSeverity.CaptureWarning => "WarningBrush",
            CaptureSeverity.GuidanceExceeded => "WarningBrush",
            CaptureSeverity.PotentialImpact => "ImpactBrush",
            CaptureSeverity.Severe => "DangerBrush",
            _ => "MutedTextBrush",
        };
        var backgroundKey = severity switch
        {
            CaptureSeverity.WithinGuidance => "SuccessSoftBrush",
            CaptureSeverity.CaptureWarning => "WarningSoftBrush",
            CaptureSeverity.GuidanceExceeded => "WarningSoftBrush",
            CaptureSeverity.PotentialImpact => "ImpactSoftBrush",
            CaptureSeverity.Severe => "DangerSoftBrush",
            _ => "SurfaceStrongBrush",
        };

        _latencyHealthBadge.Background = ThemeBrush(backgroundKey);
        _latencyHealthBadgeText.Foreground = ThemeBrush(foregroundKey);
    }

    private Brush SeverityBrush(CaptureSeverity severity) =>
        ThemeBrush(severity switch
        {
            CaptureSeverity.WithinGuidance => "SuccessBrush",
            CaptureSeverity.CaptureWarning => "WarningBrush",
            CaptureSeverity.GuidanceExceeded => "WarningBrush",
            CaptureSeverity.PotentialImpact => "ImpactBrush",
            CaptureSeverity.Severe => "DangerBrush",
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
        if (_latencyHealthBadge is null ||
            _latencyHealthBadgeText is null ||
            _latencyHealthTitleText is null ||
            _latencyHealthSummaryText is null)
        {
            return;
        }

        _latencyHealthBadge.Background = ThemeBrush("SurfaceStrongBrush");
        _latencyHealthBadgeText.Foreground = ThemeBrush("MutedTextBrush");
        _latencyHealthBadgeText.Text = "Not captured";
        _latencyHealthTitleText.Text = "Capture an observation to classify the tail.";
        _latencyHealthSummaryText.Text =
            "The app keeps exact p99/max values visible, shows p99.9 only when at least 1,000 samples support it, and adds bounded context labels plus a compact tail-rate view.";

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

    private Brush ThemeBrush(string key)
    {
        var themeKey = _accessibilitySettings.HighContrast
            ? "HighContrast"
            : RootGrid.ActualTheme == ElementTheme.Dark
                ? "Dark"
                : "Light";

        if (Application.Current.Resources.ThemeDictionaries.TryGetValue(themeKey, out var themeObject) &&
            themeObject is ResourceDictionary themeDictionary &&
            themeDictionary.TryGetValue(key, out var value) &&
            value is Brush brush)
        {
            return brush;
        }

        if (Application.Current.Resources.TryGetValue(key, out var fallback) && fallback is Brush fallbackBrush)
        {
            return fallbackBrush;
        }

        throw new InvalidOperationException($"Theme brush '{key}' is unavailable.");
    }
}
