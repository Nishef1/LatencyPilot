using Microsoft.UI.Xaml;

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

        // Keep system caption buttons native while allowing Mica/content to flow behind them.
        AppWindow.TitleBar.ButtonBackgroundColor = Windows.UI.Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Windows.UI.Colors.Transparent;
    }
}
