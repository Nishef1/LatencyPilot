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
    private readonly TextBlock _emptyState;
    private IReadOnlyList<ChartPoint> _primary = Array.Empty<ChartPoint>();
    private IReadOnlyList<ChartPoint> _secondary = Array.Empty<ChartPoint>();
    private IReadOnlyList<string> _labels = Array.Empty<string>();

    public LatencyProfileChart()
    {
        MinHeight = (double)Application.Current.Resources["ChartPlotMinHeight"];
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;

        _emptyState = new TextBlock
        {
            Text = "Capture evidence to reveal the latency profile.",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
        };

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
        _emptyState.Text = message;
        AutomationProperties.SetHelpText(this, message);
        Render();
    }

    private void Render()
    {
        _canvas.Children.Clear();
        _emptyState.Foreground = DashboardThemeResources.Brush(this, "MutedTextBrush");
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

        RenderSeries(_primary, DashboardThemeResources.Brush(this, "ChartAccentPrimaryBrush"), maximum, left, top, plotWidth, plotHeight);
        RenderSeries(_secondary, DashboardThemeResources.Brush(this, "ChartAccentSecondaryBrush"), maximum, left, top, plotWidth, plotHeight);
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

        var line = new Polyline
        {
            Stroke = brush,
            StrokeThickness = 2.25,
            StrokeLineJoin = PenLineJoin.Round,
        };

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
            line.Points.Add(new Point(x, y));

            var dot = new Ellipse
            {
                Width = 7,
                Height = 7,
                Fill = brush,
            };
            ToolTipService.SetToolTip(dot, $"{series[index].Label}: {value.Value:0.0} µs");
            Canvas.SetLeft(dot, x - 3.5);
            Canvas.SetTop(dot, y - 3.5);
            _canvas.Children.Add(dot);
        }

        if (line.Points.Count > 1)
        {
            _canvas.Children.Insert(0, line);
        }
    }

    private static string FormatAxis(double value) =>
        value >= 1000d ? $"{value / 1000d:0.#} ms" : $"{value:0} µs";
}
