using LatencyPilot.Benchmarking.Optimization;
using LatencyPilot.Core.Benchmarking;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace LatencyPilot.App.Controls;

public sealed class GateATrialHistoryChart : UserControl
{
    private readonly Canvas _canvas = new();
    private readonly ChartEmptyState _emptyState;
    private IReadOnlyList<GateATrialPoint> _points = Array.Empty<GateATrialPoint>();

    public GateATrialHistoryChart()
    {
        MinHeight = 250d;
        Height = 250d;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        _emptyState = new ChartEmptyState(
            "\uE823",
            "Run GPU Gate A to inspect scored control stability.");

        var root = new Grid();
        root.Children.Add(_canvas);
        root.Children.Add(_emptyState);
        Content = root;

        SizeChanged += (_, _) => Render();
        ActualThemeChanged += (_, _) => Render();
        AutomationProperties.SetName(this, "Gate A control stability history chart");
        Clear("No scored Gate A control history is available yet.");
    }

    internal void SetData(IReadOnlyList<GateATrialPoint> points, string automationSummary)
    {
        _points = points ?? Array.Empty<GateATrialPoint>();
        AutomationProperties.SetHelpText(this, automationSummary);
        Render();
    }

    internal void Clear(string message)
    {
        _points = Array.Empty<GateATrialPoint>();
        _emptyState.SetMessage(message);
        AutomationProperties.SetHelpText(this, message);
        Render();
    }

    private void Render()
    {
        _canvas.Children.Clear();
        var valid = _points
            .Where(static point => double.IsFinite(point.OnePercentLowFps) && point.OnePercentLowFps > 0d)
            .OrderBy(static point => point.RunNumber)
            .ToArray();
        if (valid.Length == 0 || ActualWidth < 240d || ActualHeight < 150d)
        {
            _emptyState.Visibility = Visibility.Visible;
            return;
        }

        _emptyState.Visibility = Visibility.Collapsed;
        const double left = 46d;
        const double right = 12d;
        const double top = 18d;
        const double bottom = 30d;
        var plotWidth = Math.Max(1d, ActualWidth - left - right);
        var plotHeight = Math.Max(1d, ActualHeight - top - bottom);
        var minimum = valid.Min(static point => point.OnePercentLowFps);
        var maximum = valid.Max(static point => point.OnePercentLowFps);
        var span = Math.Max(1d, maximum - minimum);
        minimum = Math.Max(0d, minimum - span * 0.12d);
        maximum += span * 0.12d;
        span = Math.Max(1d, maximum - minimum);
        var minimumRun = valid.Min(static point => point.RunNumber);
        var maximumRun = valid.Max(static point => point.RunNumber);
        var runSpan = Math.Max(1, maximumRun - minimumRun);

        var gridBrush = DashboardThemeResources.Brush(this, "ChartGridBrush");
        var mutedBrush = DashboardThemeResources.Brush(this, "MutedTextBrush");
        var originalBrush = DashboardThemeResources.Brush(this, "ChartAccentPrimaryBrush");
        var candidateBrush = DashboardThemeResources.Brush(this, "ChartAccentSecondaryBrush");
        var attentionBrush = DashboardThemeResources.Brush(this, "SemanticAttentionBrush");
        var ringBrush = DashboardThemeResources.Brush(this, "GlassRaisedBrush");

        for (var index = 0; index < 4; index++)
        {
            var ratio = index / 3d;
            var y = top + (plotHeight * ratio);
            _canvas.Children.Add(new Line
            {
                X1 = left,
                X2 = left + plotWidth,
                Y1 = y,
                Y2 = y,
                Stroke = gridBrush,
                StrokeThickness = 1d,
            });
            var label = new TextBlock
            {
                Text = (maximum - (span * ratio)).ToString("0", System.Globalization.CultureInfo.InvariantCulture),
                FontSize = 10d,
                Foreground = mutedBrush,
            };
            Canvas.SetLeft(label, 0d);
            Canvas.SetTop(label, Math.Max(0d, y - 8d));
            _canvas.Children.Add(label);
        }

        var firstRunLabel = new TextBlock
        {
            Text = $"Run {minimumRun}",
            FontSize = 10d,
            Foreground = mutedBrush,
        };
        Canvas.SetLeft(firstRunLabel, left);
        Canvas.SetTop(firstRunLabel, top + plotHeight + 8d);
        _canvas.Children.Add(firstRunLabel);
        if (maximumRun != minimumRun)
        {
            var lastRunLabel = new TextBlock
            {
                Text = $"Run {maximumRun}",
                FontSize = 10d,
                Foreground = mutedBrush,
            };
            Canvas.SetLeft(lastRunLabel, Math.Max(left, left + plotWidth - 42d));
            Canvas.SetTop(lastRunLabel, top + plotHeight + 8d);
            _canvas.Children.Add(lastRunLabel);
        }

        RenderOriginalSeries(
            valid.Where(static point => string.Equals(point.Series, "Original", StringComparison.OrdinalIgnoreCase)).ToArray(),
            originalBrush,
            ringBrush,
            minimumRun,
            runSpan,
            minimum,
            span,
            left,
            top,
            plotWidth,
            plotHeight);
        RenderCandidateMarkers(
            valid.Where(static point => !string.Equals(point.Series, "Original", StringComparison.OrdinalIgnoreCase)).ToArray(),
            candidateBrush,
            attentionBrush,
            ringBrush,
            minimumRun,
            runSpan,
            minimum,
            span,
            left,
            top,
            plotWidth,
            plotHeight);
    }

    private void RenderOriginalSeries(
        GateATrialPoint[] points,
        Brush brush,
        Brush ringBrush,
        int minimumRun,
        int runSpan,
        double minimum,
        double valueSpan,
        double left,
        double top,
        double plotWidth,
        double plotHeight)
    {
        if (points.Length == 0)
        {
            return;
        }

        var coordinates = points
            .OrderBy(static point => point.RunNumber)
            .Select(point => (Point: point, Position: PositionOf(
                point, minimumRun, runSpan, minimum, valueSpan, left, top, plotWidth, plotHeight)))
            .ToArray();

        if (coordinates.Length > 1)
        {
            var line = new Polyline
            {
                Stroke = brush,
                StrokeThickness = 2.2d,
                StrokeLineJoin = PenLineJoin.Round,
            };
            foreach (var coordinate in coordinates)
            {
                line.Points.Add(coordinate.Position);
            }
            _canvas.Children.Add(line);
        }

        foreach (var coordinate in coordinates)
        {
            var ready = string.Equals(
                coordinate.Point.ReadinessState,
                "Ready",
                StringComparison.OrdinalIgnoreCase);
            var dot = new Ellipse
            {
                Width = ready ? 8d : 10d,
                Height = ready ? 8d : 10d,
                Fill = ready ? brush : ringBrush,
                Stroke = brush,
                StrokeThickness = ready ? 1.5d : 2.5d,
            };
            ToolTipService.SetToolTip(
                dot,
                $"Run {coordinate.Point.RunNumber} · Original control\n" +
                $"1% low {coordinate.Point.OnePercentLowFps:0.0} FPS · {coordinate.Point.ReadinessState}\n" +
                $"{coordinate.Point.Phase}");
            Canvas.SetLeft(dot, coordinate.Position.X - dot.Width / 2d);
            Canvas.SetTop(dot, coordinate.Position.Y - dot.Height / 2d);
            _canvas.Children.Add(dot);
        }
    }

    private void RenderCandidateMarkers(
        GateATrialPoint[] points,
        Brush candidateBrush,
        Brush attentionBrush,
        Brush ringBrush,
        int minimumRun,
        int runSpan,
        double minimum,
        double valueSpan,
        double left,
        double top,
        double plotWidth,
        double plotHeight)
    {
        foreach (var point in points.OrderBy(static point => point.RunNumber))
        {
            var position = PositionOf(
                point, minimumRun, runSpan, minimum, valueSpan, left, top, plotWidth, plotHeight);
            var unstable = point.PairVerdict is GpuAutoAffinityPairVerdict.Unstable or GpuAutoAffinityPairVerdict.Inconclusive;
            var ready = string.Equals(point.ReadinessState, "Ready", StringComparison.OrdinalIgnoreCase);
            var brush = unstable ? attentionBrush : candidateBrush;
            var marker = new Ellipse
            {
                Width = unstable ? 11d : 9d,
                Height = unstable ? 11d : 9d,
                Fill = ready && !unstable ? brush : ringBrush,
                Stroke = brush,
                StrokeThickness = unstable ? 2.5d : 2d,
            };
            var pairContext = point.PairVerdict is { } verdict
                ? $" · pair {point.PairAttempt?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "—"} · {verdict}"
                : string.Empty;
            ToolTipService.SetToolTip(
                marker,
                $"Run {point.RunNumber} · {point.Series}{pairContext}\n" +
                $"1% low {point.OnePercentLowFps:0.0} FPS · {point.ReadinessState}\n" +
                $"{point.Phase}");
            Canvas.SetLeft(marker, position.X - marker.Width / 2d);
            Canvas.SetTop(marker, position.Y - marker.Height / 2d);
            _canvas.Children.Add(marker);
        }
    }

    private static Point PositionOf(
        GateATrialPoint point,
        int minimumRun,
        int runSpan,
        double minimum,
        double valueSpan,
        double left,
        double top,
        double plotWidth,
        double plotHeight)
    {
        var x = left + plotWidth * (point.RunNumber - minimumRun) / runSpan;
        var normalized = Math.Clamp((point.OnePercentLowFps - minimum) / valueSpan, 0d, 1d);
        var y = top + plotHeight - normalized * plotHeight;
        return new Point(x, y);
    }
}
