using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace LatencyPilot.App.Controls;

public sealed class ModuleContributionChart : UserControl
{
    private readonly StackPanel _rows = new() { Spacing = 9 };
    private readonly TextBlock _emptyState;

    public ModuleContributionChart()
    {
        MinHeight = (double)Application.Current.Resources["ChartPlotMinHeight"];
        _emptyState = new TextBlock
        {
            Text = "Capture evidence to rank kernel modules by observed time.",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
        };

        var root = new Grid();
        root.Children.Add(_rows);
        root.Children.Add(_emptyState);
        Content = root;
        ActualThemeChanged += (_, _) => _emptyState.Foreground = DashboardThemeResources.Brush(this, "MutedTextBrush");
        AutomationProperties.SetName(this, "Top modules by observed kernel time chart");
    }

    internal void SetBars(IReadOnlyList<ChartBar> bars, string automationSummary)
    {
        _rows.Children.Clear();
        AutomationProperties.SetHelpText(this, automationSummary);
        var visible = bars.Take(6).ToArray();
        if (visible.Length == 0)
        {
            _emptyState.Visibility = Visibility.Visible;
            return;
        }

        _emptyState.Visibility = Visibility.Collapsed;
        var maximum = Math.Max(1d, visible.Max(item => item.Value));
        var primary = DashboardThemeResources.Brush(this, "ChartAccentPrimaryBrush");
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

            var barHost = new Grid
            {
                Height = 12,
                VerticalAlignment = VerticalAlignment.Center,
            };
            barHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.001d, item.Value), GridUnitType.Star) });
            barHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.001d, maximum - item.Value), GridUnitType.Star) });
            barHost.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(6),
                Background = index == 0 ? primary : secondary,
                Opacity = index == 0 ? 1d : Math.Max(0.38d, 0.84d - index * 0.08d),
            });
            var trackBorder = new Border
            {
                CornerRadius = new CornerRadius(6),
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

    internal void Clear(string message)
    {
        _rows.Children.Clear();
        _emptyState.Foreground = DashboardThemeResources.Brush(this, "MutedTextBrush");
        _emptyState.Text = message;
        _emptyState.Visibility = Visibility.Visible;
        AutomationProperties.SetHelpText(this, message);
    }
}
