using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace LatencyPilot.App;

public sealed partial class MainWindow
{
    private readonly string _appearancePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LatencyPilot", "appearance.txt");

    private void InitializeDashboardShell()
    {
        try
        {
            if (File.Exists(_appearancePath) &&
                Enum.TryParse<ElementTheme>(File.ReadAllText(_appearancePath), out var theme) &&
                Enum.IsDefined(theme))
            {
                RootGrid.RequestedTheme = theme;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Logger.Warning(exception, "Appearance preference could not be loaded.");
        }

        UpdateAppearanceMenu();
        RootGrid.SizeChanged += (_, _) => ApplyDashboardLayout();
        RootGrid.Loaded += (_, _) => ApplyDesktopAcrylicBackdrop();
        AppNavigationView.SelectedItem = OverviewNavItem;
        ShowDashboardView("overview");

        // Use artwork already installed with Windows; never redistribute it or
        // treat an image as machine evidence. The native PC glyph is the fallback.
        var artworkPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "Web", "Wallpaper", "Windows", "img0.jpg");
        if (File.Exists(artworkPath))
        {
            var artwork = new BitmapImage();
            artwork.ImageOpened += (_, _) => SystemArtworkFallback.Visibility = Visibility.Collapsed;
            artwork.UriSource = new Uri(artworkPath);
            SystemArtwork.Source = artwork;
        }

        ApplyDashboardLayout();
    }

    private void ApplyDesktopAcrylicBackdrop()
    {
        try
        {
            if (SystemBackdrop is not DesktopAcrylicBackdrop)
            {
                SystemBackdrop = new DesktopAcrylicBackdrop();
            }
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Desktop Acrylic backdrop could not be enabled; continuing with the semantic surface fallback.");
        }
    }

    private void AppNavigationView_SelectionChanged(
        NavigationView sender,
        NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer?.Tag is string tag)
        {
            ShowDashboardView(tag);
        }
    }

    private void ShowDashboardView(string tag)
    {
        var normalized = tag.ToLowerInvariant();
        OverviewView.Visibility = normalized == "overview" ? Visibility.Visible : Visibility.Collapsed;
        MeasureView.Visibility = normalized == "measure" ? Visibility.Visible : Visibility.Collapsed;
        DevicesView.Visibility = normalized == "devices" ? Visibility.Visible : Visibility.Collapsed;
        EvidenceView.Visibility = normalized == "evidence" ? Visibility.Visible : Visibility.Collapsed;

        if (normalized is not ("overview" or "measure" or "devices" or "evidence"))
        {
            Logger.Warning("Ignoring unknown dashboard navigation tag {NavigationTag}.", tag);
            OverviewView.Visibility = Visibility.Visible;
            AppNavigationView.SelectedItem = OverviewNavItem;
        }

        ApplyDashboardLayout();
    }

    private void ApplyDashboardLayout()
    {
        var contentWidth = Math.Max(0d, RootGrid.ActualWidth - AppNavigationView.CompactPaneLength - 64d);
        if (contentWidth <= 0d)
        {
            return;
        }

        var inlineActions = contentWidth >= DesignValue<double>("HeaderInlineThreshold");
        HeaderActionsColumn.Width = inlineActions ? GridLength.Auto : new GridLength(0);
        Grid.SetColumn(HeaderActions, inlineActions ? 1 : 0);
        Grid.SetRow(HeaderActions, inlineActions ? 0 : 1);
        HeaderActions.Orientation = contentWidth < 620d ? Orientation.Vertical : Orientation.Horizontal;
        HeaderActions.HorizontalAlignment = inlineActions ? HorizontalAlignment.Right : HorizontalAlignment.Left;

        ReflowCards(
            SummaryGrid,
            contentWidth >= DesignValue<double>("MetricsFourColumnThreshold") ? 4 : contentWidth >= 620d ? 2 : 1);
        ReflowCards(
            MeasureAnchor,
            contentWidth >= DesignValue<double>("ChartsTwoColumnThreshold") ? 2 : 1);
        ReflowCards(
            BaselineAnchor,
            contentWidth >= DesignValue<double>("PreparationTwoColumnThreshold") ? 2 : 1);

        ServiceStatusText.Visibility = contentWidth >= 620d ? Visibility.Visible : Visibility.Collapsed;
        ServiceStatusBadgeText.Visibility = contentWidth >= 420d ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void ReflowCards(Grid grid, int columns)
    {
        for (var index = 0; index < grid.ColumnDefinitions.Count; index++)
        {
            grid.ColumnDefinitions[index].Width = index < columns
                ? new GridLength(1, GridUnitType.Star)
                : new GridLength(0);
        }

        var rows = (grid.Children.Count + columns - 1) / columns;
        while (grid.RowDefinitions.Count < rows)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }
        while (grid.RowDefinitions.Count > rows)
        {
            grid.RowDefinitions.RemoveAt(grid.RowDefinitions.Count - 1);
        }

        for (var index = 0; index < grid.Children.Count; index++)
        {
            if (grid.Children[index] is FrameworkElement card)
            {
                Grid.SetColumn(card, index % columns);
                Grid.SetRow(card, index / columns);
            }
        }
    }

    private static T DesignValue<T>(string key) => (T)Application.Current.Resources[key];

    private void Appearance_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioMenuFlyoutItem { Tag: string value } ||
            !Enum.TryParse<ElementTheme>(value, out var theme))
        {
            return;
        }

        RootGrid.RequestedTheme = theme;
        UpdateAppearanceMenu();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_appearancePath)!);
            File.WriteAllText(_appearancePath, value);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Logger.Warning(exception, "Appearance changed for this session but could not be saved.");
        }
    }

    private void UpdateAppearanceMenu()
    {
        if (AppearanceButton.Flyout is MenuFlyout menu)
        {
            foreach (var item in menu.Items.OfType<RadioMenuFlyoutItem>())
            {
                item.IsChecked = string.Equals(
                    item.Tag as string,
                    RootGrid.RequestedTheme.ToString(),
                    StringComparison.Ordinal);
            }
        }
    }
}