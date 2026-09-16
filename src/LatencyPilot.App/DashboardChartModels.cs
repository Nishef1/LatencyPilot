using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
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

    internal static bool AnimationsEnabled()
    {
        try
        {
            return new UISettings().AnimationsEnabled;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// Shared soft elevation for glass cards. Applied in code because
/// <c>UIElement.Translation</c> cannot be set from a XAML <c>Style</c>.
/// High Contrast stays flat; hover lift additionally requires animation effects.
/// </summary>
internal static class CardElevation
{
    private static readonly Vector3 RestTranslation = new(0f, 0f, 8f);
    private static readonly Vector3 HoverTranslation = new(0f, -2f, 20f);

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
        if (!DashboardThemeResources.AnimationsEnabled())
        {
            return;
        }

        card.TranslationTransition = new Vector3Transition
        {
            Duration = TimeSpan.FromMilliseconds(150),
        };
        card.PointerEntered -= OnPointerEntered;
        card.PointerExited -= OnPointerExited;
        card.PointerEntered += OnPointerEntered;
        card.PointerExited += OnPointerExited;
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

    private static void OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border card)
        {
            card.Translation = HoverTranslation;
        }
    }

    private static void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border card)
        {
            card.Translation = RestTranslation;
        }
    }
}
