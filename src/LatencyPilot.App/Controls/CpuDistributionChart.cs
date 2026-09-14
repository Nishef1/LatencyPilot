using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace LatencyPilot.App.Controls;

public sealed class CpuDistributionChart : UserControl
{
    private readonly Canvas _canvas = new();
    private readonly TextBlock _emptyState;
    private IReadOnlyList<ChartBar> _bars = Array.Empty<ChartBar>();

    public CpuDistributionChart()
    {
        MinHeight = 180;
        _emptyState = new TextBlock
        {
            Text = "Capture evidence to see where interrupt work concentrates.",
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
        AutomationProperties.SetName(this, "CPU interrupt distribution chart");
    }

    internal void SetBars(IReadOnlyList<ChartBar> bars, string automationSummary)
    {
        _bars = bars;
        AutomationProperties.SetHelpText(this, automationSummary);
        Render();
    }

    internal void Clear(string message)
    {
        _bars = Array.Empty<ChartBar>();
        _emptyState.Text = message;
        AutomationProperties.SetHelpText(this, message);
        Render();
    }

    private void Render()
    {
        _canvas.Children.Clear();
        if (_bars.Count == 0 || ActualWidth < 150 || ActualHeight < 110)
        {
            _emptyState.Visibility = Visibility.Visible;
            return;
        }

        _emptyState.Visibility = Visibility.Collapsed;
        var bars = _bars.Take(24).ToArray();
        var width = ActualWidth;
        var height = ActualHeight;
        const double left = 34;
        const double right = 8;
        const double top = 12;
        const double bottom = 30;
        var plotWidth = Math.Max(1d, width - left - right);
        var plotHeight = Math.Max(1d, height - top - bottom);
        var maximum = Math.Max(1d, bars.Max(item => item.Value) * 1.12d);
        var gridBrush = DashboardThemeResources.Brush(this, "ChartGridBrush");
        var mutedBrush = DashboardThemeResources.Brush(this, "MutedTextBrush");
        var primaryBrush = DashboardThemeResources.Brush(this, "ChartAccentSecondaryBrush");
        var tertiaryBrush = DashboardThemeResources.Brush(this, "ChartAccentTertiaryBrush");

        for (var index = 0; index < 4; index++)
        {
            var ratio = index / 3d;
            var y = top + plotHeight * ratio;
            _canvas.Children.Add(new Line
            {
                X1 = left,
                X2 = left + plotWidth,
                Y1 = y,
                Y2 = y,
                Stroke = gridBrush,
                StrokeThickness = 1,
            });

            var tick = new TextBlock
            {
                Text = $"{maximum * (1d - ratio):0}%",
                FontSize = 10,
                Foreground = mutedBrush,
            };
            Canvas.SetLeft(tick, 0);
            Canvas.SetTop(tick, Math.Max(0d, y - 8d));
            _canvas.Children.Add(tick);
        }

        var slot = plotWidth / bars.Length;
        var barWidth = Math.Clamp(slot * 0.56d, 5d, 24d);
        var peak = bars.Max(item => item.Value);

        for (var index = 0; index < bars.Length; index++)
        {
            var item = bars[index];
            var barHeight = Math.Clamp(item.Value / maximum, 0d, 1d) * plotHeight;
            var x = left + slot * index + (slot - barWidth) / 2d;
            var y = top + plotHeight - barHeight;
            var rect = new Rectangle
            {
                Width = barWidth,
                Height = Math.Max(2d, barHeight),
                RadiusX = 3,
                RadiusY = 3,
                Fill = Math.Abs(item.Value - peak) < 0.0001d ? primaryBrush : tertiaryBrush,
                Opacity = Math.Abs(item.Value - peak) < 0.0001d ? 1d : 0.72d,
            };
            ToolTipService.SetToolTip(rect, $"{item.Label}: {item.Value:0.0}%{(string.IsNullOrWhiteSpace(item.Detail) ? string.Empty : $" · {item.Detail}")}");
            Canvas.SetLeft(rect, x);
            Canvas.SetTop(rect, y);
            _canvas.Children.Add(rect);

            var label = new TextBlock
            {
                Text = item.Label.Replace("CPU ", string.Empty, StringComparison.Ordinal),
                FontSize = 9,
                Foreground = mutedBrush,
            };
            Canvas.SetLeft(label, x + barWidth / 2d - 5d);
            Canvas.SetTop(label, top + plotHeight + 7d);
            _canvas.Children.Add(label);
        }
    }
}
