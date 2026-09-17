using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

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
        AppNavigationView.PaneOpened += (_, _) => ApplyDashboardLayout();
        AppNavigationView.PaneClosed += (_, _) => ApplyDashboardLayout();
        ServiceStatusBadgeText.RegisterPropertyChangedCallback(
            TextBlock.TextProperty,
            (_, _) => UpdateServiceStatusVisibility());
        AppNavigationView.SelectedItem = OverviewNavItem;
        ShowDashboardView("overview");
        UpdateServiceStatusVisibility();
        ApplyDashboardLayout();
        ApplyStaticCardElevation();
    }

    /// <summary>
    /// Soft elevation for XAML-declared glass cards. Overview cards are owned by
    /// the premium overview experience; everything else is elevated here once.
    /// High Contrast stays flat inside <see cref="CardElevation"/>.
    /// </summary>
    private void ApplyStaticCardElevation()
    {
        CardElevation.Apply(ServiceStatusCard);
        CardElevation.Apply(ObservationCard);
        CardElevation.Apply(DeveloperValidationCard);
        CardElevation.Apply(SystemInventoryCard);
        CardElevation.ApplyToChildren(BaselineAnchor);
        CardElevation.ApplyToChildren(DevicesSummaryGrid);
        CardElevation.ApplyToChildren(EvidenceMetricGrid);
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

    private void UpdateServiceStatusVisibility()
    {
        var status = ServiceStatusBadgeText.Text ?? string.Empty;
        ServiceStatusCard.Visibility = status.Equals("Service connected", StringComparison.OrdinalIgnoreCase)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void ApplyDashboardLayout()
    {
        var paneWidth = AppNavigationView.IsPaneOpen
            ? AppNavigationView.OpenPaneLength
            : AppNavigationView.CompactPaneLength;
        var contentWidth = Math.Max(0d, RootGrid.ActualWidth - paneWidth - 64d);
        if (contentWidth <= 0d)
        {
            return;
        }

        var pageWidth = Math.Min(
            contentWidth + 64d,
            DesignValue<double>("ContentMaxWidth"));
        OverviewContent.Width = pageWidth;
        MeasureContent.Width = pageWidth;
        DevicesContent.Width = pageWidth;
        EvidenceContent.Width = pageWidth;

        var inlineActions = contentWidth >= DesignValue<double>("HeaderInlineThreshold");
        PlaceHeaderAction(HeaderActions, HeaderActionsColumn, inlineActions);
        HeaderActions.Orientation = contentWidth < 620d ? Orientation.Vertical : Orientation.Horizontal;
        HeaderActions.HorizontalAlignment = inlineActions ? HorizontalAlignment.Right : HorizontalAlignment.Left;

        PlaceHeaderAction(MeasureHeaderActions, MeasureHeaderActionsColumn, inlineActions);
        MeasureHeaderActions.Orientation = contentWidth < 620d ? Orientation.Vertical : Orientation.Horizontal;
        MeasureHeaderActions.HorizontalAlignment = inlineActions ? HorizontalAlignment.Right : HorizontalAlignment.Left;

        PlaceHeaderAction(ExportEvidenceButton, EvidenceHeaderActionsColumn, inlineActions);
        ExportEvidenceButton.HorizontalAlignment = inlineActions ? HorizontalAlignment.Right : HorizontalAlignment.Left;

        ReflowCards(
            SummaryGrid,
            contentWidth >= DesignValue<double>("MetricsFourColumnThreshold") ? 4 : contentWidth >= 620d ? 2 : 1);
        ReflowCards(
            MeasureAnchor,
            contentWidth >= DesignValue<double>("ChartsTwoColumnThreshold") ? 2 : 1);
        ReflowCards(
            BaselineAnchor,
            contentWidth >= DesignValue<double>("PreparationTwoColumnThreshold") ? 2 : 1);
        ReflowCards(
            DevicesSummaryGrid,
            contentWidth >= 980d ? 3 : contentWidth >= 620d ? 2 : 1);
        ReflowCards(
            SystemInventoryGrid,
            contentWidth >= 760d ? 3 : 1);
        ReflowCards(
            EvidenceMetricGrid,
            contentWidth >= 900d ? 4 : contentWidth >= 620d ? 2 : 1);
        ReflowCards(
            EvidenceAttributionGrid,
            contentWidth >= 760d ? 2 : 1);

        var inventoryActionInline = contentWidth >= 760d;
        Grid.SetColumn(InspectDeviceEvidenceButton, inventoryActionInline ? 1 : 0);
        Grid.SetRow(InspectDeviceEvidenceButton, inventoryActionInline ? 0 : 1);
        InspectDeviceEvidenceButton.HorizontalAlignment = inventoryActionInline
            ? HorizontalAlignment.Right
            : HorizontalAlignment.Left;
        InspectDeviceEvidenceButton.Margin = inventoryActionInline
            ? new Thickness(0)
            : new Thickness(0, 4, 0, 0);

        ServiceStatusText.Visibility = contentWidth >= 620d ? Visibility.Visible : Visibility.Collapsed;
        ServiceStatusBadgeText.Visibility = contentWidth >= 420d ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void PlaceHeaderAction(
        FrameworkElement action,
        ColumnDefinition actionColumn,
        bool inline)
    {
        actionColumn.Width = GridLength.Auto;
        Grid.SetColumn(action, inline ? 1 : 0);
        Grid.SetRow(action, inline ? 0 : 1);
        action.Margin = inline ? new Thickness(0) : new Thickness(0, 8, 0, 0);
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
