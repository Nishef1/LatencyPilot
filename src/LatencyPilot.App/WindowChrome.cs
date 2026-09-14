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
    }
}
