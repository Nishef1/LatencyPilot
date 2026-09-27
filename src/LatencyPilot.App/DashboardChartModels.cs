using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
        return IsHighContrastMode();
    }

    internal static bool IsHighContrastMode()
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

/// <summary>
/// Shared static depth for glass cards. Cards never translate on pointer hover:
/// interaction feedback belongs on the actual control, while content surfaces stay
/// spatially stable. High Contrast remains flat.
/// </summary>
internal static class CardElevation
{
    private static readonly Vector3 RestTranslation = new(0f, 0f, 8f);

    internal static void Apply(Border? card)
    {
        if (card is null || DashboardThemeResources.IsHighContrastMode())
        {
            return;
        }

        if (Application.Current.Resources.TryGetValue("CardShadow", out var shadow) &&
            shadow is ThemeShadow themeShadow)
        {
            card.Shadow = themeShadow;
        }

        card.Translation = RestTranslation;
    }

    internal static void ApplyToChildren(Panel? parent)
    {
        if (parent is null)
        {
            return;
        }

        foreach (var child in parent.Children)
        {
            Apply(child as Border);
        }
    }
}
