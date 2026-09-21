using LatencyPilot.Benchmarking.Optimization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace LatencyPilot.App.Controls;

public sealed class GpuCandidateComparisonChart : UserControl
{
    private const double RowHeight = 31d;
    private const double MinimumChartHeight = 190d;
    private readonly Canvas _canvas = new();
    private readonly ChartEmptyState _emptyState;
    private IReadOnlyList<GateACandidateBar> _candidates = Array.Empty<GateACandidateBar>();
    private double? _originalOnePercentLowFps;

    public GpuCandidateComparisonChart()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        _emptyState = new ChartEmptyState(
            "\uE9D2",
            "Run GPU Gate A to compare measured logical-processor candidates.");

        var root = new Grid();
        root.Children.Add(_canvas);
        root.Children.Add(_emptyState);
        Content = root;

        SizeChanged += (_, _) => Render();
        ActualThemeChanged += (_, _) => Render();
        AutomationProperties.SetName(this, "GPU candidate comparison");
        AutomationProperties.SetAutomationId(this, "GpuCandidateComparisonChart");
        AutomationProperties.SetAutomationControlType(this, AutomationControlType.Group);
        Clear("No Gate A candidate measurement has been completed yet.");
    }

    protected override AutomationPeer OnCreateAutomationPeer() =>
        new FrameworkElementAutomationPeer(this);

    internal void SetData(
        IReadOnlyList<GateACandidateBar> candidates,
        double? originalOnePercentLowFps,
        string automationSummary,
        string? emptyMessage = null)
    {
        _candidates = candidates ?? Array.Empty<GateACandidateBar>();
        _originalOnePercentLowFps = IsFinitePositive(originalOnePercentLowFps)
            ? originalOnePercentLowFps
            : null;
        var desiredHeight = Math.Max(MinimumChartHeight, 58d + (_candidates.Count * RowHeight));
        MinHeight = desiredHeight;
        Height = desiredHeight;
        _emptyState.SetMessage(string.IsNullOrWhiteSpace(emptyMessage)
            ? "No decision-grade candidate could be charted. Measured local pairs remain available below with their control-movement and retry evidence."
            : emptyMessage);
        AutomationProperties.SetHelpText(this, automationSummary);
        AutomationProperties.SetItemStatus(this, BuildAutomationStatus(_candidates));
        AutomationProperties.SetFullDescription(this, BuildAutomationDescription(_candidates));
        Render();
    }

    internal void Clear(string message)
    {
        _candidates = Array.Empty<GateACandidateBar>();
        _originalOnePercentLowFps = null;
        MinHeight = MinimumChartHeight;
        Height = MinimumChartHeight;
        _emptyState.SetMessage(message);
        AutomationProperties.SetHelpText(this, message);
        AutomationProperties.SetItemStatus(this, "No decision-grade GPU candidate data is available.");
        AutomationProperties.SetFullDescription(this, message);
        Render();
    }

    private void Render()
    {
        _canvas.Children.Clear();
        var valid = _candidates
            .Where(static candidate =>
                candidate.OnePercentLowEffect is { } effect && double.IsFinite(effect))
            .OrderBy(static candidate => candidate.DecisionRank ?? int.MaxValue)
            .ThenBy(static candidate => candidate.Processor.Group)
            .ThenBy(static candidate => candidate.Processor.Number)
            .ToArray();
        if (valid.Length == 0 || ActualWidth < 300d || ActualHeight < 120d)
        {
            _emptyState.Visibility = Visibility.Visible;
            return;
        }

        _emptyState.Visibility = Visibility.Collapsed;
        const double labelWidth = 86d;
        const double stateWidth = 82d;
        const double top = 32d;
        const double right = 10d;
        var plotLeft = labelWidth;
        var plotWidth = Math.Max(100d, ActualWidth - plotLeft - stateWidth - right);
        var zeroX = plotLeft + (plotWidth / 2d);
        var halfPlotWidth = plotWidth / 2d;
        var maximumMagnitude = valid.Max(static candidate => Math.Abs(candidate.OnePercentLowEffect!.Value));
        maximumMagnitude = Math.Max(0.01d, maximumMagnitude * 1.12d);

        var gridBrush = DashboardThemeResources.Brush(this, "ChartGridBrush");
        var textBrush = DashboardThemeResources.Brush(this, "TextBrush");
        var mutedBrush = DashboardThemeResources.Brush(this, "MutedTextBrush");
        var keptBrush = DashboardThemeResources.Brush(this, "SemanticGoodBrush");
        var comparedBrush = DashboardThemeResources.Brush(this, "ChartAccentPrimaryBrush");
        var ordinaryBrush = DashboardThemeResources.Brush(this, "ChartAccentTertiaryBrush");

        _canvas.Children.Add(new Line
        {
            X1 = plotLeft,
            X2 = plotLeft + plotWidth,
            Y1 = top - 9d,
            Y2 = top - 9d,
            Stroke = gridBrush,
            StrokeThickness = 1d,
        });
        _canvas.Children.Add(new Line
        {
            X1 = zeroX,
            X2 = zeroX,
            Y1 = top - 14d,
            Y2 = top + (valid.Length * RowHeight) - 3d,
            Stroke = textBrush,
            StrokeThickness = 1.5d,
            Opacity = 0.72d,
        });

        var referenceText = _originalOnePercentLowFps is { } originalValue
            ? $"Original reference · 0% paired effect · {originalValue:0.0} FPS baseline"
            : "Original reference · 0% paired effect";
        var referenceLabel = new TextBlock
        {
            Text = referenceText,
            FontSize = 10d,
            Foreground = mutedBrush,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = Math.Max(120d, plotWidth - 8d),
        };
        Canvas.SetLeft(referenceLabel, plotLeft + 4d);
        Canvas.SetTop(referenceLabel, 0d);
        _canvas.Children.Add(referenceLabel);

        var leftScale = new TextBlock
        {
            Text = $"{-maximumMagnitude:P0}",
            FontSize = 9d,
            Foreground = mutedBrush,
        };
        Canvas.SetLeft(leftScale, plotLeft);
        Canvas.SetTop(leftScale, top - 24d);
        _canvas.Children.Add(leftScale);

        var rightScale = new TextBlock
        {
            Text = $"+{maximumMagnitude:P0}",
            FontSize = 9d,
            Foreground = mutedBrush,
        };
        Canvas.SetLeft(rightScale, Math.Max(plotLeft, plotLeft + plotWidth - 31d));
        Canvas.SetTop(rightScale, top - 24d);
        _canvas.Children.Add(rightScale);

        for (var index = 0; index < valid.Length; index++)
        {
            var candidate = valid[index];
            var effect = candidate.OnePercentLowEffect!.Value;
            var y = top + (index * RowHeight);
            var effectWidth = Math.Max(
                2d,
                halfPlotWidth * Math.Clamp(Math.Abs(effect) / maximumMagnitude, 0d, 1d));
            var brush = candidate.IsKept
                ? keptBrush
                : candidate.IsCompared
                    ? comparedBrush
                    : string.Equals(candidate.Verdict, "Inconclusive", StringComparison.OrdinalIgnoreCase)
                        ? mutedBrush
                        : ordinaryBrush;
            var rankPrefix = candidate.DecisionRank is > 0 and var rank ? $"#{rank}  " : string.Empty;

            var cpu = new TextBlock
            {
                Text = $"{rankPrefix}CPU {candidate.Processor.Number}",
                FontSize = 11d,
                FontWeight = candidate.IsKept || candidate.IsCompared
                    ? Microsoft.UI.Text.FontWeights.SemiBold
                    : Microsoft.UI.Text.FontWeights.Normal,
                Foreground = textBrush,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Canvas.SetLeft(cpu, 2d);
            Canvas.SetTop(cpu, y + 5d);
            _canvas.Children.Add(cpu);

            var bar = new Rectangle
            {
                Width = effectWidth,
                Height = 18d,
                RadiusX = 5d,
                RadiusY = 5d,
                Fill = brush,
                Opacity = candidate.IsKept ? 0.92d : candidate.IsCompared ? 0.82d : 0.34d,
                Stroke = candidate.IsCompared && !candidate.IsKept ? comparedBrush : null,
                StrokeThickness = candidate.IsCompared && !candidate.IsKept ? 1.5d : 0d,
            };
            ToolTipService.SetToolTip(bar, BuildToolTip(candidate));
            Canvas.SetLeft(bar, effect >= 0d ? zeroX : zeroX - effectWidth);
            Canvas.SetTop(bar, y + 3d);
            _canvas.Children.Add(bar);

            var value = new TextBlock
            {
                Text = effect.ToString("+0.0%;-0.0%;0.0%", System.Globalization.CultureInfo.InvariantCulture),
                FontSize = 10d,
                Foreground = textBrush,
            };
            var valueLeft = effect >= 0d
                ? Math.Min(zeroX + effectWidth + 5d, plotLeft + plotWidth - 43d)
                : Math.Max(plotLeft, zeroX - effectWidth - 43d);
            Canvas.SetLeft(value, valueLeft);
            Canvas.SetTop(value, y + 5d);
            _canvas.Children.Add(value);

            var state = new TextBlock
            {
                Text = candidate.StateLabel,
                FontSize = 10d,
                Foreground = candidate.IsKept ? keptBrush : candidate.IsCompared ? comparedBrush : mutedBrush,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = stateWidth - 6d,
            };
            Canvas.SetLeft(state, plotLeft + plotWidth + 8d);
            Canvas.SetTop(state, y + 5d);
            _canvas.Children.Add(state);
        }
    }

    private static string BuildAutomationStatus(IReadOnlyList<GateACandidateBar> candidates)
    {
        var ranked = OrderDecisionCandidates(candidates);
        if (ranked.Length == 0)
        {
            return "No decision-grade GPU candidate bars are available.";
        }

        var kept = ranked.FirstOrDefault(static candidate => candidate.IsKept);
        if (kept is not null)
        {
            return $"{ranked.Length} decision-grade candidate(s). CPU {kept.Processor.Number} is kept.";
        }

        var compared = ranked.FirstOrDefault(static candidate => candidate.IsCompared);
        return compared is not null
            ? $"{ranked.Length} decision-grade candidate(s). CPU {compared.Processor.Number} is the best measured comparison candidate and was not kept."
            : $"{ranked.Length} decision-grade candidate(s). No candidate is marked as kept or compared.";
    }

    private static string BuildAutomationDescription(IReadOnlyList<GateACandidateBar> candidates)
    {
        var ranked = OrderDecisionCandidates(candidates);
        if (ranked.Length == 0)
        {
            return "No decision-grade candidate bar is available. Measured local-pair evidence, when present, is listed below the chart with control movement and retry outcomes.";
        }

        return string.Join(
            " ",
            ranked.Select(static candidate =>
            {
                var rank = candidate.DecisionRank is > 0 ? $"Rank {candidate.DecisionRank}" : "Unranked";
                var effect = candidate.OnePercentLowEffect!.Value.ToString(
                    "+0.0%;-0.0%;0.0%",
                    System.Globalization.CultureInfo.InvariantCulture);
                var uncertainty = candidate.LocalControlUncertainty is { } local && double.IsFinite(local) && local >= 0d
                    ? local.ToString("P1", System.Globalization.CultureInfo.InvariantCulture)
                    : "unavailable";
                return $"{rank}, CPU {candidate.Processor.Number}: paired 1 percent low effect {effect}; state {candidate.StateLabel}; local uncertainty {uncertainty}.";
            }));
    }

    private static GateACandidateBar[] OrderDecisionCandidates(IReadOnlyList<GateACandidateBar> candidates) =>
        candidates
            .Where(static candidate => candidate.OnePercentLowEffect is { } effect && double.IsFinite(effect))
            .OrderBy(static candidate => candidate.DecisionRank ?? int.MaxValue)
            .ThenBy(static candidate => candidate.Processor.Group)
            .ThenBy(static candidate => candidate.Processor.Number)
            .ToArray();

    private static string BuildToolTip(GateACandidateBar candidate)
    {
        static string Metric(double? value, string format, string unit) =>
            value is { } number && double.IsFinite(number)
                ? $"{number.ToString(format, System.Globalization.CultureInfo.InvariantCulture)} {unit}"
                : "—";
        static string Effect(double? value) =>
            value is { } number && double.IsFinite(number)
                ? number.ToString("+0.0%;-0.0%;0.0%", System.Globalization.CultureInfo.InvariantCulture)
                : "—";

        var uncertainty = candidate.LocalControlUncertainty is { } value && double.IsFinite(value)
            ? value.ToString("P1", System.Globalization.CultureInfo.InvariantCulture)
            : "—";
        var rank = candidate.DecisionRank is > 0
            ? $"Rank #{candidate.DecisionRank} · "
            : string.Empty;
        return $"{rank}CPU {candidate.Processor.Number} · physical core {candidate.PhysicalCoreIndex} · {candidate.StateLabel}\n" +
               $"Paired 1% low effect {Effect(candidate.OnePercentLowEffect)} · local uncertainty {uncertainty}\n" +
               $"Raw candidate: 1% low {candidate.OnePercentLowFps:0.0} FPS · AVG {Metric(candidate.AvgFps, "0.0", "FPS")}\n" +
               $"p99 {Metric(candidate.FrameP99Milliseconds, "0.00", "ms")} · 0.1% low {Metric(candidate.Low01PctFps, "0.0", "FPS")}\n" +
               $"Trials {candidate.TrialCount} · verdict {candidate.Verdict}";
    }

    private static bool IsFinitePositive(double? value) =>
        value is > 0d && double.IsFinite(value.Value);
}
