using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace LatencyPilot.App.Controls;

public sealed class LatencyProfileChart : UserControl
{
    private readonly Canvas _canvas = new();
    private readonly ChartEmptyState _emptyState;
    private IReadOnlyList<ChartPoint> _primary = Array.Empty<ChartPoint>();
    private IReadOnlyList<ChartPoint> _secondary = Array.Empty<ChartPoint>();
    private IReadOnlyList<string> _labels = Array.Empty<string>();

    public LatencyProfileChart()
    {
        MinHeight = (double)Application.Current.Resources["ChartPlotMinHeight"];
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;

        _emptyState = new ChartEmptyState(
            "\uE9D2",
            "Capture a quick snapshot to reveal the DPC and ISR tail profile.");

        var root = new Grid();
        root.Children.Add(_canvas);
        root.Children.Add(_emptyState);
        Content = root;

        SizeChanged += (_, _) => Render();
        ActualThemeChanged += (_, _) => Render();
        AutomationProperties.SetName(this, "Latency distribution profile chart");
    }

    internal void SetSeries(
        IReadOnlyList<ChartPoint> primary,
        IReadOnlyList<ChartPoint> secondary,
        IReadOnlyList<string> labels,
        string automationSummary)
    {
        _primary = primary;
        _secondary = secondary;
        _labels = labels;
        AutomationProperties.SetHelpText(this, automationSummary);
        Render();
    }

    internal void Clear(string message)
    {
        _primary = Array.Empty<ChartPoint>();
        _secondary = Array.Empty<ChartPoint>();
        _labels = Array.Empty<string>();
        _emptyState.SetMessage(message);
        AutomationProperties.SetHelpText(this, message);
        Render();
    }

    private void Render()
    {
        _canvas.Children.Clear();
        var values = _primary.Concat(_secondary)
            .Where(point => point.Value is not null && double.IsFinite(point.Value.Value))
            .Select(point => point.Value!.Value)
            .ToArray();

        if (values.Length == 0 || ActualWidth < 140 || ActualHeight < 110)
        {
            _emptyState.Visibility = Visibility.Visible;
            return;
        }

        _emptyState.Visibility = Visibility.Collapsed;
        var width = ActualWidth;
        var height = ActualHeight;
        const double left = 38;
        const double right = 10;
        const double top = 10;
        const double bottom = 28;
        var plotWidth = Math.Max(1, width - left - right);
        var plotHeight = Math.Max(1, height - top - bottom);
        var maximum = Math.Max(1d, values.Max() * 1.12d);

        var gridBrush = DashboardThemeResources.Brush(this, "ChartGridBrush");
        var mutedBrush = DashboardThemeResources.Brush(this, "MutedTextBrush");

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
                StrokeThickness = 1,
            });

            var label = new TextBlock
            {
                Text = FormatAxis(maximum * (1d - ratio)),
                FontSize = 10,
                Foreground = mutedBrush,
            };
            Canvas.SetLeft(label, 0);
            Canvas.SetTop(label, Math.Max(0, y - 8));
            _canvas.Children.Add(label);
        }

        if (_labels.Count > 0)
        {
            for (var index = 0; index < _labels.Count; index++)
            {
                var x = _labels.Count == 1
                    ? left + plotWidth / 2d
                    : left + plotWidth * index / (_labels.Count - 1d);
                _canvas.Children.Add(new Line
                {
                    X1 = x,
                    X2 = x,
                    Y1 = top,
                    Y2 = top + plotHeight,
                    Stroke = gridBrush,
                    StrokeThickness = 1,
                });

                var label = new TextBlock
                {
                    Text = _labels[index],
                    FontSize = 10,
                    Foreground = mutedBrush,
                };
                Canvas.SetLeft(label, Math.Clamp(x - 18, left - 4, left + plotWidth - 36));
                Canvas.SetTop(label, top + plotHeight + 7);
                _canvas.Children.Add(label);
            }
        }

        RenderSeries(
            _primary,
            DashboardThemeResources.Brush(this, "ChartAccentPrimaryBrush"),
            maximum, left, top, plotWidth, plotHeight);
        RenderSeries(
            _secondary,
            DashboardThemeResources.Brush(this, "ChartAccentSecondaryBrush"),
            maximum, left, top, plotWidth, plotHeight);
    }

    private void RenderSeries(
        IReadOnlyList<ChartPoint> series,
        Brush brush,
        double maximum,
        double left,
        double top,
        double plotWidth,
        double plotHeight)
    {
        if (series.Count == 0)
        {
            return;
        }

        var coordinates = new List<(Point Position, string Label, double Value)>();
        for (var index = 0; index < series.Count; index++)
        {
            var value = series[index].Value;
            if (value is null || !double.IsFinite(value.Value))
            {
                continue;
            }

            var x = series.Count == 1
                ? left + plotWidth / 2d
                : left + plotWidth * index / (series.Count - 1d);
            var y = top + plotHeight - Math.Clamp(value.Value / maximum, 0d, 1d) * plotHeight;
            coordinates.Add((new Point(x, y), series[index].Label, value.Value));
        }

        if (coordinates.Count == 0)
        {
            return;
        }

        // Soft area wash under the line. Skipped in High Contrast to keep the
        // plot strictly foreground-on-background.
        if (!DashboardThemeResources.IsHighContrastMode() && coordinates.Count > 1)
        {
            var area = new Polygon
            {
                Fill = brush,
                Opacity = 0.10,
            };
            foreach (var coordinate in coordinates)
            {
                area.Points.Add(coordinate.Position);
            }

            area.Points.Add(new Point(coordinates[^1].Position.X, top + plotHeight));
            area.Points.Add(new Point(coordinates[0].Position.X, top + plotHeight));
            _canvas.Children.Add(area);
        }

        var ring = DashboardThemeResources.Brush(this, "GlassRaisedBrush");
        var line = new Polyline
        {
            Stroke = brush,
            StrokeThickness = 2.5,
            StrokeLineJoin = PenLineJoin.Round,
        };

        foreach (var coordinate in coordinates)
        {
            line.Points.Add(coordinate.Position);
        }

        if (line.Points.Count > 1)
        {
            _canvas.Children.Add(line);
        }

        foreach (var coordinate in coordinates)
        {
            var dot = new Ellipse
            {
                Width = 8,
                Height = 8,
                Fill = brush,
                Stroke = ring,
                StrokeThickness = 1.5,
            };
            ToolTipService.SetToolTip(dot, $"{coordinate.Label}: {coordinate.Value:0.0} µs");
            Canvas.SetLeft(dot, coordinate.Position.X - 4);
            Canvas.SetTop(dot, coordinate.Position.Y - 4);
            _canvas.Children.Add(dot);
        }
    }

    private static string FormatAxis(double value) =>
        value >= 1000d ? $"{value / 1000d:0.#} ms" : $"{value:0} µs";
}
