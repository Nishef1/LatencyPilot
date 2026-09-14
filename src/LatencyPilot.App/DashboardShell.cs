using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;

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
        EvidenceWorkspace.SizeChanged += (_, _) => ApplyEvidenceLayout();

        // Use the artwork already installed with Windows; never redistribute it or
        // treat an image as machine evidence. The native PC icon is the fallback.
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
    }

    private void ApplyDashboardLayout()
    {
        var expanded = RootGrid.ActualWidth >= DesignValue<double>("NavigationExpandedThreshold");
        NavigationColumn.Width = DesignValue<GridLength>(expanded ? "NavigationRailWidth" : "NavigationCompactWidth");
        foreach (var label in new[] { NavOverviewLabel, NavMeasureLabel, NavBaselineLabel, NavDevicesLabel, NavEvidenceLabel, AppearanceLabel })
        {
            label.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        }

        // The readiness explanation remains in the refresh tooltip when the rail is collapsed.
        ServiceStatusBadgeText.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        ServiceStatusText.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        RefreshServiceButton.Content = expanded ? "Refresh" : "↻";
        RefreshServiceButton.Padding = new Thickness(expanded ? 12 : 0, 6, expanded ? 12 : 0, 6);
        RefreshServiceButton.MinWidth = expanded ? 0 : 26;
        ServiceStatusCard.Padding = new Thickness(expanded ? 10 : 3);

        var showContext = RootGrid.ActualWidth >= DesignValue<double>("ContextVisibleThreshold");
        ContextColumn.Width = showContext ? DesignValue<GridLength>("ContextPanelWidth") : new GridLength(0);
        Grid.SetColumn(LeftRail, showContext ? 0 : 1);
        Grid.SetRow(LeftRail, showContext ? 0 : 1);
        ApplyEvidenceLayout();
    }

    private void ApplyEvidenceLayout()
    {
        var width = EvidenceWorkspace.ActualWidth;
        if (width <= 0)
        {
            return;
        }

        var inlineActions = width >= DesignValue<double>("HeaderInlineThreshold");
        HeaderActionsColumn.Width = inlineActions ? GridLength.Auto : new GridLength(0);
        Grid.SetColumn(HeaderActions, inlineActions ? 1 : 0);
        Grid.SetRow(HeaderActions, inlineActions ? 0 : 1);
        HeaderActions.Orientation = width < 520 ? Orientation.Vertical : Orientation.Horizontal;
        HeaderActions.HorizontalAlignment = inlineActions ? HorizontalAlignment.Right : HorizontalAlignment.Left;

        ReflowCards(SummaryGrid, width >= DesignValue<double>("MetricsFourColumnThreshold") ? 4 : width >= 460 ? 2 : 1);
        ReflowCards(MeasureAnchor, width >= DesignValue<double>("ChartsTwoColumnThreshold") ? 2 : 1);
        ReflowCards(BaselineAnchor, width >= DesignValue<double>("PreparationTwoColumnThreshold") ? 2 : 1);
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
                item.IsChecked = string.Equals(item.Tag as string, RootGrid.RequestedTheme.ToString(), StringComparison.Ordinal);
            }
        }
    }

    private void NavigateOverview_Click(object sender, RoutedEventArgs e) => NavigateDashboard(NavOverviewButton, OverviewAnchor);

    private void NavigateMeasure_Click(object sender, RoutedEventArgs e) => NavigateDashboard(NavMeasureButton, MeasureAnchor);

    private void NavigateBaseline_Click(object sender, RoutedEventArgs e) => NavigateDashboard(NavBaselineButton, BaselineAnchor);

    private void NavigateDevices_Click(object sender, RoutedEventArgs e) => NavigateDashboard(NavDevicesButton, InspectDeviceEvidenceButton);

    private void NavigateEvidence_Click(object sender, RoutedEventArgs e)
    {
        EvidenceDetails.IsExpanded = true;
        NavigateDashboard(NavEvidenceButton, EvidenceDetails);
    }

    private void NavigateDashboard(Button selected, FrameworkElement target)
    {
        foreach (var button in new[] { NavOverviewButton, NavMeasureButton, NavBaselineButton, NavDevicesButton, NavEvidenceButton })
        {
            button.Style = DesignValue<Style>(ReferenceEquals(button, selected) ? "NavigationItemSelectedStyle" : "NavigationItemStyle");
        }

        ScrollDashboardTo(target);
    }

    private void ScrollDashboardTo(FrameworkElement target)
    {
        try
        {
            var point = target.TransformToVisual(DashboardContent).TransformPoint(new Point(0, 0));
            var inset = DesignValue<double>("Space2");
            var maximumOffset = Math.Max(0d, DashboardScrollViewer.ScrollableHeight);
            var offset = Math.Clamp(point.Y - inset, 0d, maximumOffset);

            if (!DashboardScrollViewer.ChangeView(null, offset, null, false))
            {
                target.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false, VerticalAlignmentRatio = 0 });
            }
        }
        catch (InvalidOperationException)
        {
            // Layout can still be settling during startup; retain the native fallback.
            target.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false, VerticalAlignmentRatio = 0 });
        }
    }
}
