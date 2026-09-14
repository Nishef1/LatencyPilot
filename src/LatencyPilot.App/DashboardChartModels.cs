using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI.ViewManagement;

namespace LatencyPilot.App;

internal sealed record ChartPoint(string Label, double? Value);

internal sealed record ChartBar(string Label, double Value, string? Detail = null);

internal sealed record InterruptMapRow(string Label, double DpcShare, double IsrShare);

internal static class DashboardThemeResources
{
    private static readonly AccessibilitySettings AccessibilitySettings = new();

    public static Brush Brush(FrameworkElement owner, string key)
    {
        var themeKey = IsHighContrast()
            ? "HighContrast"
            : owner.ActualTheme == ElementTheme.Dark
                ? "Dark"
                : "Light";

        if (Application.Current.Resources.ThemeDictionaries.TryGetValue(themeKey, out var themeObject) &&
            themeObject is ResourceDictionary themeDictionary &&
            themeDictionary.TryGetValue(key, out var value) &&
            value is Brush brush)
        {
            return brush;
        }

        if (Application.Current.Resources.TryGetValue(key, out var fallback) && fallback is Brush fallbackBrush)
        {
            return fallbackBrush;
        }

        throw new InvalidOperationException($"Dashboard brush '{key}' is unavailable.");
    }

    private static bool IsHighContrast()
    {
        try
        {
            return AccessibilitySettings.HighContrast;
        }
        catch
        {
            return false;
        }
    }
}
