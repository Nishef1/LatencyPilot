using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace LatencyPilot.App.Controls;

public sealed class ModuleContributionChart : UserControl
{
    private readonly StackPanel _rows = new() { Spacing = 9 };
    private readonly ChartEmptyState _emptyState;
    private IReadOnlyList<ChartBar> _bars = Array.Empty<ChartBar>();

    public ModuleContributionChart()
    {
        MinHeight = (double)Application.Current.Resources["ChartPlotMinHeight"];
        _emptyState = new ChartEmptyState(
            "\uE8A5",
            "Capture a quick snapshot to rank observed kernel modules by inclusive time.");

        var root = new Grid();
        root.Children.Add(_rows);
        root.Children.Add(_emptyState);
        Content = root;
        ActualThemeChanged += (_, _) => Render();
        AutomationProperties.SetName(this, "Top modules by observed kernel time chart");
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
        _emptyState.SetMessage(message);
        AutomationProperties.SetHelpText(this, message);
        Render();
    }

    private void Render()
    {
        _rows.Children.Clear();
        var visible = _bars.Take(6).ToArray();
        if (visible.Length == 0)
        {
            _emptyState.Visibility = Visibility.Visible;
            return;
        }

        _emptyState.Visibility = Visibility.Collapsed;
        var maximum = Math.Max(1d, visible.Max(item => item.Value));
        var primary = DashboardThemeResources.Brush(this, "AccentGradientBrush");
        var secondary = DashboardThemeResources.Brush(this, "ChartAccentSecondaryBrush");
        var track = DashboardThemeResources.Brush(this, "ChartTrackBrush");
        var text = DashboardThemeResources.Brush(this, "TextBrush");
        var muted = DashboardThemeResources.Brush(this, "MutedTextBrush");

        for (var index = 0; index < visible.Length; index++)
        {
            var item = visible[index];
            var row = new Grid { ColumnSpacing = 10 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(118) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });

            var label = new TextBlock
            {
                Text = item.Label,
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = text,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            row.Children.Add(label);

            var fill = Math.Max(0d, item.Value);
            var remainder = Math.Max(0d, maximum - fill);
            var barHost = new Grid
            {
                Height = 14,
                VerticalAlignment = VerticalAlignment.Center,
            };
            barHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(fill, GridUnitType.Star) });
            barHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(remainder, GridUnitType.Star) });
            barHost.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(7),
                Background = index == 0 ? primary : secondary,
                Opacity = index == 0 ? 1d : Math.Max(0.38d, 0.84d - index * 0.08d),
            });
            var trackBorder = new Border
            {
                CornerRadius = new CornerRadius(7),
                Background = track,
                Child = barHost,
            };
            Grid.SetColumn(trackBorder, 1);
            row.Children.Add(trackBorder);

            var value = new TextBlock
            {
                Text = $"{item.Value:0.#}%",
                FontSize = 11,
                Foreground = muted,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
            };
            ToolTipService.SetToolTip(value, item.Detail);
            Grid.SetColumn(value, 2);
            row.Children.Add(value);
            _rows.Children.Add(row);
        }
    }
}
