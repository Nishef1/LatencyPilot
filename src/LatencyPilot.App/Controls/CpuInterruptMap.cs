using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace LatencyPilot.App.Controls;

public sealed class CpuInterruptMap : UserControl
{
    private readonly StackPanel _rows = new() { Spacing = 2 };
    private readonly ChartEmptyState _emptyState;

    public CpuInterruptMap()
    {
        MinHeight = (double)Application.Current.Resources["ChartPlotMinHeight"];
        _emptyState = new ChartEmptyState(
            "\uE81E",
            "Capture a quick snapshot to compare DPC and ISR intensity by processor.");

        var root = new Grid();
        root.Children.Add(_rows);
        root.Children.Add(_emptyState);
        Content = root;
        AutomationProperties.SetName(this, "CPU interrupt intensity map");
    }

    internal void SetRows(IReadOnlyList<InterruptMapRow> rows, string automationSummary)
    {
        _rows.Children.Clear();
        AutomationProperties.SetHelpText(this, automationSummary);
        var visible = rows.Take(8).ToArray();
        if (visible.Length == 0)
        {
            _emptyState.Visibility = Visibility.Visible;
            return;
        }

        _emptyState.Visibility = Visibility.Collapsed;
        var header = new Grid { ColumnSpacing = 8 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(62) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.Children.Add(new TextBlock
        {
            Text = "CPU",
            FontSize = 10,
            Foreground = DashboardThemeResources.Brush(this, "MutedTextBrush"),
        });
        var dpcHeader = new TextBlock
        {
            Text = "DPC share",
            FontSize = 10,
            Foreground = DashboardThemeResources.Brush(this, "MutedTextBrush"),
        };
        Grid.SetColumn(dpcHeader, 1);
        header.Children.Add(dpcHeader);
        var isrHeader = new TextBlock
        {
            Text = "ISR share",
            FontSize = 10,
            Foreground = DashboardThemeResources.Brush(this, "MutedTextBrush"),
        };
        Grid.SetColumn(isrHeader, 2);
        header.Children.Add(isrHeader);
        _rows.Children.Add(header);

        var dpcMax = Math.Max(0.0001d, visible.Max(row => row.DpcShare));
        var isrMax = Math.Max(0.0001d, visible.Max(row => row.IsrShare));
        var text = DashboardThemeResources.Brush(this, "TextBrush");
        var primary = DashboardThemeResources.Brush(this, "ChartAccentPrimaryBrush");
        var secondary = DashboardThemeResources.Brush(this, "ChartAccentSecondaryBrush");

        foreach (var item in visible)
        {
            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(62) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(new TextBlock
            {
                Text = item.Label,
                FontSize = 11,
                Foreground = text,
                VerticalAlignment = VerticalAlignment.Center,
            });

            var dpc = CreateCell(primary, item.DpcShare / dpcMax, $"{item.DpcShare:0.0}%");
            Grid.SetColumn(dpc, 1);
            row.Children.Add(dpc);
            var isr = CreateCell(secondary, item.IsrShare / isrMax, $"{item.IsrShare:0.0}%");
            Grid.SetColumn(isr, 2);
            row.Children.Add(isr);
            _rows.Children.Add(row);
        }
    }

    internal void Clear(string message)
    {
        _rows.Children.Clear();
        _emptyState.SetMessage(message);
        _emptyState.Visibility = Visibility.Visible;
        AutomationProperties.SetHelpText(this, message);
    }

    private Grid CreateCell(Brush brush, double normalized, string text)
    {
        var opacity = 0.12d + Math.Clamp(normalized, 0d, 1d) * 0.78d;
        var cell = new Grid { MinHeight = 14 };
        cell.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(3),
            Background = brush,
            Opacity = opacity,
        });
        cell.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 10,
            Foreground = DashboardThemeResources.Brush(this, normalized >= 0.65d ? "OnAccentBrush" : "TextBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        });
        return cell;
    }
}
