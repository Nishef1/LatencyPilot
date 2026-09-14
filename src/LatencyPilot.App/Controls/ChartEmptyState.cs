using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace LatencyPilot.App.Controls;

internal sealed class ChartEmptyState : Grid
{
    private readonly Border _iconTile;
    private readonly FontIcon _icon;
    private readonly TextBlock _message;

    public ChartEmptyState(string glyph, string message)
    {
        IsHitTestVisible = false;
        Padding = new Thickness(18, 12, 18, 12);

        _icon = new FontIcon
        {
            Glyph = glyph,
            FontSize = 18,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        _iconTile = new Border
        {
            Width = 38,
            Height = 38,
            CornerRadius = new CornerRadius(12),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = _icon,
        };

        _message = new TextBlock
        {
            MaxWidth = 320,
            FontSize = 12,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        var content = new StackPanel
        {
            Spacing = 9,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        content.Children.Add(_iconTile);
        content.Children.Add(_message);
        Children.Add(content);

        ActualThemeChanged += (_, _) => ApplyTheme();
        ApplyTheme();
        SetMessage(message);
    }

    public void SetMessage(string message)
    {
        _message.Text = message;
        AutomationProperties.SetHelpText(this, message);
    }

    private void ApplyTheme()
    {
        _iconTile.Background = DashboardThemeResources.Brush(this, "AccentSoftBrush");
        _icon.Foreground = DashboardThemeResources.Brush(this, "AccentBrush");
        _message.Foreground = DashboardThemeResources.Brush(this, "MutedTextBrush");
    }
}
