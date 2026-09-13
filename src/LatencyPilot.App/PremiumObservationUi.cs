using System.Globalization;
using LatencyPilot.Protocol;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
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
    private ProgressBar? _dpcGuidanceBar;
    private ProgressBar? _isrGuidanceBar;
    private ProgressBar? _oneMillisecondBar;
    private ProgressBar? _threeMillisecondBar;
    private TextBlock? _dpcGuidanceValueText;
    private TextBlock? _isrGuidanceValueText;
    private TextBlock? _oneMillisecondValueText;
    private TextBlock? _threeMillisecondValueText;
    private KernelLatencyCaptureResponse? _lastPremiumCapture;
    private bool _premiumUiInitialized;

    private enum CaptureSeverity
    {
        NoData,
        CaptureWarning,
        WithinGuidance,
        GuidanceExceeded,
        PotentialImpact,
        Severe,
    }

    private void InitializePremiumObservationUi()
    {
        if (_premiumUiInitialized)
        {
            return;
        }

        _premiumUiInitialized = true;

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
        _accessibilitySettings.HighContrastChanged += (_, _) =>
            DispatcherQueue.TryEnqueue(RebuildPremiumObservationCard);

        ServiceStatusBadgeText.RegisterPropertyChangedCallback(
            TextBlock.TextProperty,
            (_, _) => ApplyServiceStatusAppearance());
        BaselineVerdictText.RegisterPropertyChangedCallback(
            TextBlock.TextProperty,
            (_, _) => ApplyBaselineVerdictAppearance());

        ApplyServiceStatusAppearance();
        ApplyBaselineVerdictAppearance();
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
            Text = "Numbers remain authoritative; color and severity labels are additional cues only.",
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
            "Millisecond-scale events as a share of all observed DPC and ISR events.",
            "ImpactBrush",
            out _oneMillisecondBar,
            out _oneMillisecondValueText));
        chartStack.Children.Add(CreateTailBarRow(
            "DPC/ISR > 3 ms",
            "Severe tail events as a share of all observed DPC and ISR events.",
            "DangerBrush",
            out _threeMillisecondBar,
            out _threeMillisecondValueText));

        chartStack.Children.Add(new TextBlock
        {
            Text = "The bars are an auto-scaled visual aid. Exact count, denominator and percentage stay visible; guidance rows use their DPC/ISR family as the denominator and millisecond rows use all DPC + ISR events.",
            FontSize = 12,
            Foreground = ThemeBrush("MutedTextBrush"),
            TextWrapping = TextWrapping.Wrap,
        });
        root.Children.Add(chartCard);

        root.Children.Add(BuildMeasurementContextCard());
        return card;
    }

    private Grid CreateTailBarRow(
        string label,
        string tooltip,
        string foregroundBrushKey,
        out ProgressBar bar,
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
        Grid.SetColumn(valueText, 1);
        header.Children.Add(valueText);
        row.Children.Add(header);

        bar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 1,
            Value = 0,
            Height = 7,
            Foreground = ThemeBrush(foregroundBrushKey),
            Background = ThemeBrush("ChartTrackBrush"),
            IsTabStop = false,
        };
        AutomationProperties.SetName(bar, label + " event rate");
        AutomationProperties.SetHelpText(bar, tooltip);
        Grid.SetRow(bar, 1);
        row.Children.Add(bar);
        return row;
    }

    private Border BuildMeasurementContextCard()
    {
        var card = new Border
        {
            Padding = new Thickness(14),
            CornerRadius = new CornerRadius(13),
            Background = ThemeBrush("AccentSoftBrush"),
            BorderBrush = ThemeBrush("BorderBrush"),
            BorderThickness = new Thickness(1),
        };
        var stack = new StackPanel { Spacing = 9 };
        card.Child = stack;

        stack.Children.Add(new TextBlock
        {
            Text = "Measure the scenario you actually care about",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = ThemeBrush("TextBrush"),
        });

        AddMeasurementMode(
            stack,
            "Real-world",
            "Keep the apps or game that reproduce the issue open. Their activity is part of the evidence.");
        AddMeasurementMode(
            stack,
            "Idle baseline",
            "Close unnecessary apps when you want a controlled idle baseline with less background noise.");
        AddMeasurementMode(
            stack,
            "Before / after",
            "Use the same apps, workload and power state on both sides. Consistency matters more than closing everything.");

        return card;
    }

    private void AddMeasurementMode(StackPanel host, string title, string body)
    {
        var mode = new Border
        {
            Padding = new Thickness(11),
            CornerRadius = new CornerRadius(11),
            Background = ThemeBrush("SurfaceBrush"),
            BorderBrush = ThemeBrush("BorderBrush"),
            BorderThickness = new Thickness(1),
        };
        var content = new Grid { ColumnSpacing = 12 };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(116) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        content.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = ThemeBrush("AccentBrush"),
            VerticalAlignment = VerticalAlignment.Top,
        });

        var bodyText = new TextBlock
        {
            Text = body,
            FontSize = 12,
            Foreground = ThemeBrush("MutedTextBrush"),
            TextWrapping = TextWrapping.Wrap,
        };
        Grid.SetColumn(bodyText, 1);
        content.Children.Add(bodyText);

        mode.Child = content;
        host.Children.Add(mode);
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
                _latencyHealthBadgeText.Text = "Needs context";
                _latencyHealthTitleText.Text = "Driver guidance was exceeded, without a millisecond-scale spike.";
                _latencyHealthSummaryText.Text =
                    "Short exceedances can occur without a user-visible problem. Use the module list, maximum durations and repeated baseline to see whether the same tail repeats under the workload you care about.";
                break;
            case CaptureSeverity.PotentialImpact:
                _latencyHealthBadgeText.Text = "Potential impact";
                _latencyHealthTitleText.Text = "A millisecond-scale DPC/ISR event was observed.";
                _latencyHealthSummaryText.Text =
                    "At least one event exceeded 1 ms. Reproduce the same workload and inspect the responsible modules; repeated events in this range can matter to audio, video and other real-time paths.";
                break;
            case CaptureSeverity.Severe:
                _latencyHealthBadgeText.Text = "Severe tail";
                _latencyHealthTitleText.Text = "A severe tail spike was observed in this capture.";
                _latencyHealthSummaryText.Text =
                    "At least one DPC/ISR exceeded 3 ms. This deserves investigation, but do not blame a driver from one sample alone—repeat the workload, build a baseline and compare the module-level max/tail evidence.";
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
            DpcP999Text.Foreground = SeverityBrush(GetDistributionSeverity(capture.DpcThresholds));
            IsrP999Text.Foreground = SeverityBrush(GetDistributionSeverity(capture.IsrThresholds));
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
        ProgressBar bar,
        TextBlock valueText,
        double maximum,
        double rate,
        int count,
        int total)
    {
        bar.Maximum = maximum;
        bar.Value = Math.Clamp(rate, 0d, maximum);
        valueText.Text = total == 0
            ? "—"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{count:N0} / {total:N0} · {rate:0.###}%");
        AutomationProperties.SetHelpText(
            bar,
            total == 0
                ? "No eligible events were observed."
                : string.Create(CultureInfo.InvariantCulture, $"{count:N0} of {total:N0} events, {rate:0.###} percent."));
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
                : text.Contains("mismatch", StringComparison.OrdinalIgnoreCase)
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
            "The app keeps the exact p99/p99.9/max values visible and adds semantic severity plus a compact tail-rate view.";

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

    private static void ResetTailBar(ProgressBar? bar, TextBlock? valueText)
    {
        if (bar is not null)
        {
            bar.Maximum = 1;
            bar.Value = 0;
        }

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
