using LatencyPilot.Benchmarking.Optimization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
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
        AutomationProperties.SetName(this, "GPU candidate comparison chart");
        Clear("No Gate A candidate evidence is available yet.");
    }

    internal void SetData(
        IReadOnlyList<GateACandidateBar> candidates,
        double? originalOnePercentLowFps,
        string automationSummary)
    {
        _candidates = candidates ?? Array.Empty<GateACandidateBar>();
        _originalOnePercentLowFps = IsFinitePositive(originalOnePercentLowFps)
            ? originalOnePercentLowFps
            : null;
        var desiredHeight = Math.Max(MinimumChartHeight, 54d + (_candidates.Count * RowHeight));
        MinHeight = desiredHeight;
        Height = desiredHeight;
        AutomationProperties.SetHelpText(this, automationSummary);
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
        Render();
    }

    private void Render()
    {
        _canvas.Children.Clear();
        var valid = _candidates
            .Where(static candidate => double.IsFinite(candidate.OnePercentLowFps) && candidate.OnePercentLowFps > 0d)
            .ToArray();
        if (valid.Length == 0 || ActualWidth < 260d || ActualHeight < 120d)
        {
            _emptyState.Visibility = Visibility.Visible;
            return;
        }

        _emptyState.Visibility = Visibility.Collapsed;
        const double labelWidth = 70d;
        const double stateWidth = 92d;
        const double top = 28d;
        const double right = 10d;
        var plotLeft = labelWidth;
        var plotWidth = Math.Max(60d, ActualWidth - plotLeft - stateWidth - right);
        var maximum = valid.Max(static candidate => candidate.OnePercentLowFps);
        if (_originalOnePercentLowFps is { } original)
        {
            maximum = Math.Max(maximum, original);
        }
        maximum = Math.Max(1d, maximum * 1.05d);

        var gridBrush = DashboardThemeResources.Brush(this, "ChartGridBrush");
        var textBrush = DashboardThemeResources.Brush(this, "TextBrush");
        var mutedBrush = DashboardThemeResources.Brush(this, "MutedTextBrush");
        var keptBrush = DashboardThemeResources.Brush(this, "ChartAccentPrimaryBrush");
        var comparedBrush = DashboardThemeResources.Brush(this, "ChartAccentSecondaryBrush");
        var ordinaryBrush = DashboardThemeResources.Brush(this, "ChartWarmBrush");

        var axis = new Line
        {
            X1 = plotLeft,
            X2 = plotLeft + plotWidth,
            Y1 = top - 8d,
            Y2 = top - 8d,
            Stroke = gridBrush,
            StrokeThickness = 1d,
        };
        _canvas.Children.Add(axis);

        if (_originalOnePercentLowFps is { } originalValue)
        {
            var originalX = plotLeft + (plotWidth * Math.Clamp(originalValue / maximum, 0d, 1d));
            _canvas.Children.Add(new Line
            {
                X1 = originalX,
                X2 = originalX,
                Y1 = top - 12d,
                Y2 = top + (valid.Length * RowHeight) - 3d,
                Stroke = textBrush,
                StrokeThickness = 1.5d,
                Opacity = 0.72d,
            });
            var originalLabel = new TextBlock
            {
                Text = $"Original {originalValue:0.0}",
                FontSize = 10d,
                Foreground = mutedBrush,
            };
            Canvas.SetLeft(originalLabel, Math.Clamp(originalX - 30d, plotLeft, plotLeft + plotWidth - 65d));
            Canvas.SetTop(originalLabel, 0d);
            _canvas.Children.Add(originalLabel);
        }

        for (var index = 0; index < valid.Length; index++)
        {
            var candidate = valid[index];
            var y = top + (index * RowHeight);
            var barWidth = Math.Max(2d, plotWidth * Math.Clamp(candidate.OnePercentLowFps / maximum, 0d, 1d));
            var brush = candidate.IsKept
                ? keptBrush
                : candidate.IsCompared
                    ? comparedBrush
                    : string.Equals(candidate.Verdict, "Inconclusive", StringComparison.OrdinalIgnoreCase)
                        ? mutedBrush
                        : ordinaryBrush;

            var cpu = new TextBlock
            {
                Text = $"CPU {candidate.Processor.Number}",
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
                Width = barWidth,
                Height = 18d,
                RadiusX = 5d,
                RadiusY = 5d,
                Fill = brush,
                Opacity = candidate.IsKept ? 0.92d : candidate.IsCompared ? 0.68d : 0.42d,
                Stroke = candidate.IsCompared && !candidate.IsKept ? comparedBrush : null,
                StrokeThickness = candidate.IsCompared && !candidate.IsKept ? 2d : 0d,
            };
            ToolTipService.SetToolTip(bar, BuildToolTip(candidate));
            Canvas.SetLeft(bar, plotLeft);
            Canvas.SetTop(bar, y + 3d);
            _canvas.Children.Add(bar);

            var value = new TextBlock
            {
                Text = candidate.OnePercentLowFps.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture),
                FontSize = 10d,
                Foreground = textBrush,
            };
            Canvas.SetLeft(value, Math.Min(plotLeft + barWidth + 5d, plotLeft + plotWidth - 34d));
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

    private static string BuildToolTip(GateACandidateBar candidate)
    {
        static string Metric(double? value, string format, string unit) =>
            value is { } number && double.IsFinite(number)
                ? $"{number.ToString(format, System.Globalization.CultureInfo.InvariantCulture)} {unit}"
                : "—";

        var uncertainty = candidate.LocalControlUncertainty is { } value && double.IsFinite(value)
            ? value.ToString("P1", System.Globalization.CultureInfo.InvariantCulture)
            : "—";
        return $"CPU {candidate.Processor.Number} · {candidate.StateLabel}\n" +
               $"1% low {candidate.OnePercentLowFps:0.0} FPS · AVG {Metric(candidate.AvgFps, "0.0", "FPS")}\n" +
               $"p99 {Metric(candidate.FrameP99Milliseconds, "0.00", "ms")} · 0.1% low {Metric(candidate.Low01PctFps, "0.0", "FPS")}\n" +
               $"Trials {candidate.TrialCount} · verdict {candidate.Verdict} · local uncertainty {uncertainty}";
    }

    private static bool IsFinitePositive(double? value) =>
        value is > 0d && double.IsFinite(value.Value);
}
