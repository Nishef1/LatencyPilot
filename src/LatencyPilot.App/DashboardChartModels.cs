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

        return FindThemeBrush(Application.Current.Resources, themeKey, key)
            ?? throw new InvalidOperationException($"Dashboard brush '{key}' is unavailable.");
    }

    private static Brush? FindThemeBrush(ResourceDictionary resources, string themeKey, string key)
    {
        if (resources.ThemeDictionaries.TryGetValue(themeKey, out var themeObject) &&
            themeObject is ResourceDictionary themeDictionary &&
            themeDictionary.TryGetValue(key, out var value) &&
            value is Brush brush)
        {
            return brush;
        }

        // Tokens live in merged dictionaries. Resolve the owner's theme before any
        // application-level lookup, which can otherwise return the Windows theme.
        foreach (var dictionary in resources.MergedDictionaries.Reverse())
        {
            if (FindThemeBrush(dictionary, themeKey, key) is { } mergedBrush)
            {
                return mergedBrush;
            }
        }
        return null;
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
