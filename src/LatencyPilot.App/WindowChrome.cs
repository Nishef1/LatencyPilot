using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace LatencyPilot.App;

public sealed partial class MainWindow
{
    private bool _windowChromeInitialized;

    internal void InitializeWindowChrome()
    {
        if (_windowChromeInitialized)
        {
            return;
        }

        _windowChromeInitialized = true;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBarDragRegion);
        ApplyCaptionTheme();
        RootGrid.ActualThemeChanged += (_, _) => ApplyCaptionTheme();
    }

    private void ApplyCaptionTheme()
    {
        AppWindow.TitleBar.ButtonForegroundColor = ((SolidColorBrush)ThemeBrush("TextBrush")).Color;
        AppWindow.TitleBar.ButtonHoverForegroundColor = ((SolidColorBrush)ThemeBrush("TextBrush")).Color;
        AppWindow.TitleBar.ButtonHoverBackgroundColor = ((SolidColorBrush)ThemeBrush("SurfaceHoverBrush")).Color;
        AppWindow.TitleBar.ButtonPressedForegroundColor = ((SolidColorBrush)ThemeBrush("TextBrush")).Color;
        AppWindow.TitleBar.ButtonPressedBackgroundColor = ((SolidColorBrush)ThemeBrush("SurfaceStrongBrush")).Color;
    }

    internal void SetInitialWindowSize()
    {
        var workArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var width = Math.Min((int)DesignValue<double>("InitialWindowWidth"), workArea.Width);
        var height = Math.Min((int)DesignValue<double>("InitialWindowHeight"), workArea.Height);
        AppWindow.MoveAndResize(new RectInt32(
            workArea.X + (workArea.Width - width) / 2,
            workArea.Y + (workArea.Height - height) / 2,
            width, height));
    }
}
