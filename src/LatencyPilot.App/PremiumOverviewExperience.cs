using System.Globalization;
using LatencyPilot.App.Controls;
using LatencyPilot.Protocol;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace LatencyPilot.App;

public sealed partial class MainWindow
{
    // Category colors identify the measured signal. Traffic-light colors identify state.
    private const string BrandActionBrush = "BrandActionBrush";
    private const string SemanticGoodBrush = "SemanticGoodBrush";
    private const string SemanticAttentionBrush = "SemanticAttentionBrush";
    private const string SemanticFailureBrush = "SemanticFailureBrush";

    private Grid? _premiumOverviewRoot;
    private Grid? _premiumOverviewHeader;
    private Grid? _premiumSystemSummaryGrid;
    private Grid? _premiumMetricGrid;
    private Grid? _premiumChartGrid;
    private Grid? _premiumBottomGrid;
    private Button? _premiumQuickSnapshotButton;

    private TextBlock? _premiumOsText;
    private TextBlock? _premiumGpuText;
    private TextBlock? _premiumGpuDriverText;
    private TextBlock? _premiumCpuTopologyText;

    private TextBlock? _premiumDpcValueText;
    private TextBlock? _premiumDpcDetailText;
    private TextBlock? _premiumIsrValueText;
    private TextBlock? _premiumIsrDetailText;
    private TextBlock? _premiumCpuValueText;
    private TextBlock? _premiumCpuDetailText;
    private Border? _premiumBaselineCard;
    private Border? _premiumBaselineIconHost;
    private TextBlock? _premiumBaselineValueText;
    private TextBlock? _premiumBaselineDetailText;
    private FontIcon? _premiumBaselineIcon;

    private LatencyProfileChart? _premiumLatencyProfileChart;
    private CpuDistributionChart? _premiumCpuDistributionChart;
    private ModuleContributionChart? _premiumModuleContributionChart;
    private CpuInterruptMap? _premiumCpuInterruptMap;

    private Border? _premiumSnapshotBadge;
    private TextBlock? _premiumSnapshotBadgeText;
    private TextBlock? _premiumSnapshotSummaryText;
    private TextBlock? _premiumSnapshotMetricsText;

    private TextBlock? _premiumGraphicsEvidenceText;
    private TextBlock? _premiumNetworkEvidenceText;
    private TextBlock? _premiumUsbEvidenceText;
    private Border? _premiumGraphicsStatusDot;
    private Border? _premiumNetworkStatusDot;
    private Border? _premiumUsbStatusDot;

    private readonly List<FrameworkElement> _premiumMetricCards = [];
    private readonly List<FrameworkElement> _premiumChartCards = [];
    private readonly List<FrameworkElement> _premiumBottomCards = [];

    internal void InitializePremiumOverviewExperience()
    {
        _premiumOverviewRoot = BuildPremiumOverview();
        OverviewContent.Children.Clear();
        OverviewContent.Children.Add(_premiumOverviewRoot);

        _premiumOverviewRoot.SizeChanged += (_, _) => ApplyPremiumOverviewLayout();
        CaptureObservationButton.RegisterPropertyChangedCallback(
            Control.IsEnabledProperty,
            (_, _) => SyncPremiumQuickSnapshotState());

        OperatingSystemText.RegisterPropertyChangedCallback(
            TextBlock.TextProperty,
            (_, _) => SyncPremiumSystemContext());
        PrimaryGpuText.RegisterPropertyChangedCallback(
            TextBlock.TextProperty,
            (_, _) => SyncPremiumSystemContext());
        PrimaryGpuDriverText.RegisterPropertyChangedCallback(
            TextBlock.TextProperty,
            (_, _) => SyncPremiumSystemContext());
        HardwareLogicalProcessorCountText.RegisterPropertyChangedCallback(
            TextBlock.TextProperty,
            (_, _) => SyncPremiumSystemContext());
        PhysicalCoreCountText.RegisterPropertyChangedCallback(
            TextBlock.TextProperty,
            (_, _) => SyncPremiumSystemContext());
        SmtCoreCountText.RegisterPropertyChangedCallback(
            TextBlock.TextProperty,
            (_, _) => SyncPremiumSystemContext());

        BaselineVerdictText.RegisterPropertyChangedCallback(
            TextBlock.TextProperty,
            (_, _) => RefreshPremiumBaselineState());
        BaselineSummaryText.RegisterPropertyChangedCallback(
            TextBlock.TextProperty,
            (_, _) => RefreshPremiumBaselineState());
        BaselineSummaryDetailText.RegisterPropertyChangedCallback(
            TextBlock.TextProperty,
            (_, _) => RefreshPremiumBaselineState());

        DisplayEvidenceText.RegisterPropertyChangedCallback(
            TextBlock.TextProperty,
            (_, _) => SyncPremiumDeviceEvidence());
        NetworkEvidenceText.RegisterPropertyChangedCallback(
            TextBlock.TextProperty,
            (_, _) => SyncPremiumDeviceEvidence());
        UsbEvidenceText.RegisterPropertyChangedCallback(
            TextBlock.TextProperty,
            (_, _) => SyncPremiumDeviceEvidence());

        if (_snapshotEvidenceBadgeText is not null)
        {
            _snapshotEvidenceBadgeText.RegisterPropertyChangedCallback(
                TextBlock.TextProperty,
                (_, _) => RefreshPremiumSnapshotFromEvidence());
        }

        RootGrid.ActualThemeChanged += (_, _) =>
            DispatcherQueue.TryEnqueue(() =>
            {
                ApplyPremiumOverviewTheme();
                RefreshPremiumBaselineState();
                SyncPremiumDeviceEvidence();
                RefreshPremiumSnapshotFromEvidence();
            });

        SyncPremiumQuickSnapshotState();
        SyncPremiumSystemContext();
        SyncPremiumDeviceEvidence();
        RefreshPremiumBaselineState();
        RefreshPremiumSnapshotFromEvidence();
        ApplyPremiumOverviewTheme();
        ApplyPremiumOverviewLayout();
    }

    private Grid BuildPremiumOverview()
    {
        _premiumMetricCards.Clear();
        _premiumChartCards.Clear();
        _premiumBottomCards.Clear();

        var root = new Grid();
        var stack = new StackPanel { Spacing = 14 };
        root.Children.Add(stack);

        _premiumOverviewHeader = BuildPremiumHeader();
        stack.Children.Add(_premiumOverviewHeader);

        stack.Children.Add(BuildSystemSummaryCard());

        _premiumMetricGrid = new Grid { ColumnSpacing = 12, RowSpacing = 12 };
        stack.Children.Add(_premiumMetricGrid);

        _premiumMetricCards.Add(BuildSignalMetricCard(
            "DPC p99",
            "\uE9D2",
            "DpcCategoryBrush",
            "DpcCategorySoftBrush",
            out _premiumDpcValueText,
            out _premiumDpcDetailText));
        _premiumMetricCards.Add(BuildSignalMetricCard(
            "ISR p99",
            "\uEA86",
            "IsrCategoryBrush",
            "IsrCategorySoftBrush",
            out _premiumIsrValueText,
            out _premiumIsrDetailText));
        _premiumMetricCards.Add(BuildSignalMetricCard(
            "CPU concentration",
            "\uE950",
            "CpuCategoryBrush",
            "CpuCategorySoftBrush",
            out _premiumCpuValueText,
            out _premiumCpuDetailText));
        _premiumMetricCards.Add(BuildBaselineMetricCard());
        foreach (var card in _premiumMetricCards)
        {
            _premiumMetricGrid.Children.Add(card);
        }

        _premiumLatencyProfileChart = new LatencyProfileChart { Height = 224 };
        _premiumCpuDistributionChart = new CpuDistributionChart { Height = 224 };
        _premiumCpuInterruptMap = new CpuInterruptMap { Height = 224 };
        _premiumChartGrid = new Grid { ColumnSpacing = 12, RowSpacing = 12 };
        _premiumChartCards.Add(BuildChartCard(
            "Interrupt latency profile",
            "DPC and ISR percentile shape from measured evidence",
            "\uE9D2",
            "BrandActionBrush",
            _premiumLatencyProfileChart,
            BuildLatencyLegend()));
        _premiumChartCards.Add(BuildChartCard(
            "CPU interrupt distribution",
            "Share of observed DPC + ISR events",
            "\uE950",
            "CpuCategoryBrush",
            _premiumCpuDistributionChart));
        _premiumChartCards.Add(BuildChartCard(
            "CPU interrupt map",
            "DPC and ISR intensity across the busiest processors",
            "\uE81E",
            "CpuCategoryBrush",
            _premiumCpuInterruptMap));
        foreach (var card in _premiumChartCards)
        {
            _premiumChartGrid.Children.Add(card);
        }
        stack.Children.Add(_premiumChartGrid);

        _premiumModuleContributionChart = new ModuleContributionChart { Height = 176 };
        _premiumBottomGrid = new Grid { ColumnSpacing = 12, RowSpacing = 12 };
        _premiumBottomCards.Add(BuildChartCard(
            "Top kernel modules by time",
            "Share of attributed DPC / ISR duration",
            "\uE8A5",
            "CpuCategoryBrush",
            _premiumModuleContributionChart,
            BuildNavigationLink("View evidence", EvidenceNavItem)));
        _premiumBottomCards.Add(BuildRecentSnapshotCard());
        _premiumBottomCards.Add(BuildDeviceEvidenceCard());
        foreach (var card in _premiumBottomCards)
        {
            _premiumBottomGrid.Children.Add(card);
        }
        stack.Children.Add(_premiumBottomGrid);

        var footer = new Grid { Margin = new Thickness(2, 0, 2, 4) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var readyDot = new Border
        {
            Width = 9,
            Height = 9,
            CornerRadius = new CornerRadius(5),
            Background = Brush(SemanticGoodBrush),
            VerticalAlignment = VerticalAlignment.Center,
        };
        footer.Children.Add(readyDot);
        var footerText = new TextBlock
        {
            Text = "Read-only measurement · no active mutation surface",
            FontSize = 11,
            Margin = new Thickness(9, 0, 0, 0),
            Foreground = Brush("MutedTextBrush"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(footerText, 1);
        footer.Children.Add(footerText);
        stack.Children.Add(footer);

        return root;
    }

    private Grid BuildPremiumHeader()
    {
        var header = new Grid { ColumnSpacing = 18, RowSpacing = 8, MinHeight = 62 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        header.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var copy = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        copy.Children.Add(new TextBlock
        {
            Text = "Overview",
            FontSize = 30,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("TextBrush"),
        });
        copy.Children.Add(new TextBlock
        {
            Text = "Interrupt latency, CPU concentration, and baseline quality.",
            FontSize = 13,
            Foreground = Brush("MutedTextBrush"),
        });
        header.Children.Add(copy);

        var action = new StackPanel
        {
            Grid.Column = 1,
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _premiumQuickSnapshotButton = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    new SymbolIcon(Symbol.Play),
                    new TextBlock { Text = "Quick snapshot", FontWeight = FontWeights.SemiBold },
                },
            },
            Style = (Style)Application.Current.Resources["PremiumPrimaryButtonStyle"],
        };
        _premiumQuickSnapshotButton.Click += async (_, _) => await CaptureObservationAsync();
        AutomationProperties.SetName(_premiumQuickSnapshotButton, "Quick snapshot, five seconds");
        action.Children.Add(_premiumQuickSnapshotButton);
        action.Children.Add(new TextBlock
        {
            Text = "5 s diagnostic capture",
            FontSize = 11,
            Foreground = Brush("MutedTextBrush"),
            VerticalAlignment = VerticalAlignment.Center,
        });
        Grid.SetColumn(action, 1);
        header.Children.Add(action);
        return header;
    }

    private Border BuildSystemSummaryCard()
    {
        var card = PremiumCard(18);
        _premiumSystemSummaryGrid = new Grid { ColumnSpacing = 20, RowSpacing = 12 };
        card.Child = _premiumSystemSummaryGrid;

        var osPanel = new Grid { ColumnSpacing = 14 };
        osPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        osPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        osPanel.Children.Add(IconTile("\uE7F8", "BrandActionBrush", "BrandActionSoftBrush", 48));
        var osCopy = new StackPanel { Grid.Column = 1, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        _premiumOsText = new TextBlock { FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Brush("TextBrush"), TextWrapping = TextWrapping.Wrap };
        _premiumCpuTopologyText = new TextBlock { FontSize = 12, Foreground = Brush("MutedTextBrush"), TextWrapping = TextWrapping.Wrap };
        osCopy.Children.Add(_premiumOsText);
        osCopy.Children.Add(_premiumCpuTopologyText);
        Grid.SetColumn(osCopy, 1);
        osPanel.Children.Add(osCopy);

        var gpuPanel = new Grid { ColumnSpacing = 14 };
        gpuPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        gpuPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        gpuPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        gpuPanel.Children.Add(IconTile("\uE950", "CpuCategoryBrush", "CpuCategorySoftBrush", 48));
        var gpuCopy = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        _premiumGpuText = new TextBlock { FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Brush("TextBrush"), TextWrapping = TextWrapping.Wrap };
        _premiumGpuDriverText = new TextBlock { FontSize = 12, Foreground = Brush("MutedTextBrush"), TextWrapping = TextWrapping.Wrap };
        gpuCopy.Children.Add(_premiumGpuText);
        gpuCopy.Children.Add(_premiumGpuDriverText);
        Grid.SetColumn(gpuCopy, 1);
        gpuPanel.Children.Add(gpuCopy);
        var devicesButton = new Button
        {
            Content = "Devices",
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = Brush(BrandActionBrush),
            VerticalAlignment = VerticalAlignment.Center,
        };
        devicesButton.Click += (_, _) => AppNavigationView.SelectedItem = DevicesNavItem;
        Grid.SetColumn(devicesButton, 2);
        gpuPanel.Children.Add(devicesButton);

        _premiumSystemSummaryGrid.Children.Add(osPanel);
        _premiumSystemSummaryGrid.Children.Add(gpuPanel);
        return card;
    }

    private Border BuildSignalMetricCard(
        string title,
        string glyph,
        string accentBrush,
        string softBrush,
        out TextBlock valueText,
        out TextBlock detailText)
    {
        var card = PremiumCard(0);
        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(new Border
        {
            Background = Brush(accentBrush),
            CornerRadius = new CornerRadius(10, 0, 0, 10),
        });

        var content = new Grid { Margin = new Thickness(14, 12, 14, 12), ColumnSpacing = 12 };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.Children.Add(IconTile(glyph, accentBrush, softBrush, 42));
        var copy = new StackPanel { Grid.Column = 1, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        copy.Children.Add(new TextBlock { Text = title, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Brush("TextBrush") });
        valueText = new TextBlock { Text = "—", FontSize = 27, FontWeight = FontWeights.SemiBold, Foreground = Brush("TextBrush") };
        detailText = new TextBlock { Text = "No capture yet", FontSize = 11, Foreground = Brush("MutedTextBrush"), TextWrapping = TextWrapping.Wrap };
        copy.Children.Add(valueText);
        copy.Children.Add(detailText);
        Grid.SetColumn(copy, 1);
        content.Children.Add(copy);
        Grid.SetColumn(content, 1);
        root.Children.Add(content);
        card.Child = root;
        return card;
    }

    private Border BuildBaselineMetricCard()
    {
        _premiumBaselineCard = PremiumCard(0);
        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(new Border
        {
            Background = Brush("MutedTextBrush"),
            Opacity = 0.42,
            CornerRadius = new CornerRadius(10, 0, 0, 10),
        });
        var content = new Grid { Margin = new Thickness(14, 12, 14, 12), ColumnSpacing = 12 };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _premiumBaselineIcon = new FontIcon
        {
            FontFamily = (FontFamily)Application.Current.Resources["SymbolThemeFontFamily"],
            Glyph = "\uE823",
            FontSize = 18,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _premiumBaselineIconHost = new Border
        {
            Width = 42,
            Height = 42,
            CornerRadius = new CornerRadius(10),
            Child = _premiumBaselineIcon,
        };
        content.Children.Add(_premiumBaselineIconHost);
        var copy = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        copy.Children.Add(new TextBlock { Text = "Baseline status", FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Brush("TextBrush") });
        _premiumBaselineValueText = new TextBlock { Text = "Not captured", FontSize = 17, FontWeight = FontWeights.SemiBold, Foreground = Brush("TextBrush"), TextWrapping = TextWrapping.Wrap };
        _premiumBaselineDetailText = new TextBlock { Text = "Build a repeated baseline for comparison", FontSize = 11, Foreground = Brush("MutedTextBrush"), TextWrapping = TextWrapping.Wrap };
        copy.Children.Add(_premiumBaselineValueText);
        copy.Children.Add(_premiumBaselineDetailText);
        Grid.SetColumn(copy, 1);
        content.Children.Add(copy);
        Grid.SetColumn(content, 1);
        root.Children.Add(content);
        _premiumBaselineCard.Child = root;
        return _premiumBaselineCard;
    }

    private Border BuildChartCard(
        string title,
        string subtitle,
        string glyph,
        string iconBrush,
        UIElement content,
        UIElement? headerTrailing = null)
    {
        var card = PremiumCard(14);
        var stack = new StackPanel { Spacing = 10 };
        card.Child = stack;
        var header = new Grid { ColumnSpacing = 10 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = new FontIcon
        {
            FontFamily = (FontFamily)Application.Current.Resources["SymbolThemeFontFamily"],
            Glyph = glyph,
            FontSize = 17,
            Foreground = Brush(iconBrush),
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(1, 2, 0, 0),
        };
        header.Children.Add(icon);
        var copy = new StackPanel { Spacing = 1 };
        copy.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Brush("TextBrush") });
        copy.Children.Add(new TextBlock { Text = subtitle, FontSize = 11, Foreground = Brush("MutedTextBrush"), TextWrapping = TextWrapping.Wrap });
        Grid.SetColumn(copy, 1);
        header.Children.Add(copy);
        if (headerTrailing is not null)
        {
            Grid.SetColumn(headerTrailing, 2);
            header.Children.Add(headerTrailing);
        }
        stack.Children.Add(header);
        stack.Children.Add(content);
        return card;
    }

    private UIElement BuildLatencyLegend()
    {
        var legend = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Top };
        legend.Children.Add(LegendItem("DPC", "DpcCategoryBrush"));
        legend.Children.Add(LegendItem("ISR", "IsrCategoryBrush"));
        return legend;
    }

    private UIElement LegendItem(string text, string brush)
    {
        var item = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        item.Children.Add(new Border { Width = 7, Height = 7, CornerRadius = new CornerRadius(4), Background = Brush(brush), VerticalAlignment = VerticalAlignment.Center });
        item.Children.Add(new TextBlock { Text = text, FontSize = 10, Foreground = Brush("MutedTextBrush") });
        return item;
    }

    private Border BuildRecentSnapshotCard()
    {
        var card = PremiumCard(14);
        var stack = new StackPanel { Spacing = 10 };
        card.Child = stack;
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        heading.Children.Add(new FontIcon
        {
            FontFamily = (FontFamily)Application.Current.Resources["SymbolThemeFontFamily"],
            Glyph = "\uE823",
            FontSize = 16,
            Foreground = Brush("BrandActionBrush"),
        });
        heading.Children.Add(new TextBlock { Text = "Recent snapshot", FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Brush("TextBrush") });
        header.Children.Add(heading);
        var evidenceLink = BuildNavigationLink("View evidence", EvidenceNavItem);
        Grid.SetColumn(evidenceLink, 1);
        header.Children.Add(evidenceLink);
        stack.Children.Add(header);

        _premiumSnapshotBadgeText = new TextBlock { Text = "Not captured", FontSize = 11, FontWeight = FontWeights.SemiBold };
        _premiumSnapshotBadge = new Border
        {
            Padding = new Thickness(8, 3, 8, 3),
            CornerRadius = new CornerRadius(999),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = _premiumSnapshotBadgeText,
        };
        stack.Children.Add(_premiumSnapshotBadge);
        _premiumSnapshotSummaryText = new TextBlock { Text = "Run a five-second snapshot to populate this dashboard.", FontSize = 12, Foreground = Brush("TextBrush"), TextWrapping = TextWrapping.Wrap };
        _premiumSnapshotMetricsText = new TextBlock { Text = "DPC — · ISR — · CPU —", FontSize = 11, Foreground = Brush("MutedTextBrush"), TextWrapping = TextWrapping.Wrap };
        stack.Children.Add(_premiumSnapshotSummaryText);
        stack.Children.Add(_premiumSnapshotMetricsText);
        return card;
    }

    private Border BuildDeviceEvidenceCard()
    {
        var card = PremiumCard(14);
        var stack = new StackPanel { Spacing = 8 };
        card.Child = stack;
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock { Text = "Device evidence", FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Brush("TextBrush") });
        var devicesLink = BuildNavigationLink("View devices", DevicesNavItem);
        Grid.SetColumn(devicesLink, 1);
        header.Children.Add(devicesLink);
        stack.Children.Add(header);

        stack.Children.Add(BuildDeviceEvidenceRow("Graphics", out _premiumGraphicsStatusDot, out _premiumGraphicsEvidenceText));
        stack.Children.Add(BuildDeviceEvidenceRow("Network", out _premiumNetworkStatusDot, out _premiumNetworkEvidenceText));
        stack.Children.Add(BuildDeviceEvidenceRow("USB / xHCI", out _premiumUsbStatusDot, out _premiumUsbEvidenceText));
        return card;
    }

    private Grid BuildDeviceEvidenceRow(string label, out Border statusDot, out TextBlock valueText)
    {
        var row = new Grid { ColumnSpacing = 9, MinHeight = 30 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        statusDot = new Border { Width = 9, Height = 9, CornerRadius = new CornerRadius(5), VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(statusDot);
        var copy = new StackPanel { Spacing = 0, VerticalAlignment = VerticalAlignment.Center };
        copy.Children.Add(new TextBlock { Text = label, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Brush("TextBrush") });
        valueText = new TextBlock { Text = "Checking…", FontSize = 10, Foreground = Brush("MutedTextBrush"), TextTrimming = TextTrimming.CharacterEllipsis };
        copy.Children.Add(valueText);
        Grid.SetColumn(copy, 1);
        row.Children.Add(copy);
        return row;
    }

    private Button BuildNavigationLink(string text, NavigationViewItem target)
    {
        var button = new Button
        {
            Content = text,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(6, 2, 6, 2),
            Foreground = Brush(BrandActionBrush),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Top,
        };
        button.Click += (_, _) => AppNavigationView.SelectedItem = target;
        return button;
    }

    private Border PremiumCard(double padding) => new()
    {
        Padding = new Thickness(padding),
        CornerRadius = new CornerRadius(11),
        Background = Brush("PremiumOverviewCardBrush"),
        BorderBrush = Brush("BorderBrush"),
        BorderThickness = new Thickness(1),
    };

    private Border IconTile(string glyph, string foregroundBrush, string backgroundBrush, double size)
    {
        return new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(Math.Min(12, size / 4)),
            Background = Brush(backgroundBrush),
            Child = new FontIcon
            {
                FontFamily = (FontFamily)Application.Current.Resources["SymbolThemeFontFamily"],
                Glyph = glyph,
                FontSize = Math.Clamp(size * 0.42, 15, 22),
                Foreground = Brush(foregroundBrush),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
    }

    private void SyncPremiumQuickSnapshotState()
    {
        if (_premiumQuickSnapshotButton is not null)
        {
            _premiumQuickSnapshotButton.IsEnabled = CaptureObservationButton.IsEnabled;
        }
    }

    private void SyncPremiumSystemContext()
    {
        if (_premiumOsText is null || _premiumGpuText is null || _premiumGpuDriverText is null || _premiumCpuTopologyText is null)
        {
            return;
        }

        _premiumOsText.Text = string.IsNullOrWhiteSpace(OperatingSystemText.Text)
            ? "Detecting Windows…"
            : OperatingSystemText.Text;
        _premiumGpuText.Text = string.IsNullOrWhiteSpace(PrimaryGpuText.Text)
            ? "Detecting graphics adapter…"
            : PrimaryGpuText.Text;
        _premiumGpuDriverText.Text = string.IsNullOrWhiteSpace(PrimaryGpuDriverText.Text)
            ? "Driver metadata pending"
            : PrimaryGpuDriverText.Text;
        _premiumCpuTopologyText.Text =
            $"Logical {HardwareLogicalProcessorCountText.Text} · Cores {PhysicalCoreCountText.Text} · SMT {SmtCoreCountText.Text}";
    }

    private void SyncPremiumDeviceEvidence()
    {
        if (_premiumGraphicsEvidenceText is null || _premiumNetworkEvidenceText is null || _premiumUsbEvidenceText is null)
        {
            return;
        }

        _premiumGraphicsEvidenceText.Text = PrimaryGpuText.Text is { Length: > 0 }
            ? $"{PrimaryGpuText.Text} · {DisplayEvidenceText.Text}"
            : DisplayEvidenceText.Text;
        _premiumNetworkEvidenceText.Text = NetworkEvidenceText.Text;
        _premiumUsbEvidenceText.Text = UsbEvidenceText.Text;
        ApplyDetectionState(_premiumGraphicsStatusDot, DisplayEvidenceText.Text);
        ApplyDetectionState(_premiumNetworkStatusDot, NetworkEvidenceText.Text);
        ApplyDetectionState(_premiumUsbStatusDot, UsbEvidenceText.Text);
    }

    private void ApplyDetectionState(Border? dot, string status)
    {
        if (dot is null)
        {
            return;
        }

        dot.Background = Brush(status.Equals("Detected", StringComparison.OrdinalIgnoreCase)
            ? SemanticGoodBrush
            : status.Contains("not", StringComparison.OrdinalIgnoreCase)
                ? SemanticAttentionBrush
                : "MutedTextBrush");
    }

    private void RefreshPremiumBaselineState()
    {
        if (_premiumBaselineValueText is null || _premiumBaselineDetailText is null ||
            _premiumBaselineCard is null || _premiumBaselineIconHost is null || _premiumBaselineIcon is null)
        {
            return;
        }

        var verdict = BaselineVerdictText.Text ?? "Not captured";
        var summary = BaselineSummaryText.Text;
        var detail = BaselineSummaryDetailText.Text;
        var hasAttention = summary.Contains("transient", StringComparison.OrdinalIgnoreCase) ||
                           summary.Contains("not ready", StringComparison.OrdinalIgnoreCase) ||
                           detail.Contains("changing", StringComparison.OrdinalIgnoreCase) ||
                           detail.Contains("repeat", StringComparison.OrdinalIgnoreCase);

        string stateBrush;
        string stateSoftBrush;
        string glyph;
        if (verdict.Equals("Valid", StringComparison.OrdinalIgnoreCase) && !hasAttention)
        {
            stateBrush = SemanticGoodBrush;
            stateSoftBrush = "SemanticGoodSoftBrush";
            glyph = "\uE73E";
        }
        else if (verdict.Equals("Inconclusive", StringComparison.OrdinalIgnoreCase) || hasAttention)
        {
            stateBrush = SemanticAttentionBrush;
            stateSoftBrush = "SemanticAttentionSoftBrush";
            glyph = "\uE7BA";
        }
        else if (verdict.Contains("fail", StringComparison.OrdinalIgnoreCase))
        {
            stateBrush = SemanticFailureBrush;
            stateSoftBrush = "SemanticFailureSoftBrush";
            glyph = "\uEA39";
        }
        else
        {
            stateBrush = "MutedTextBrush";
            stateSoftBrush = "SurfaceAltBrush";
            glyph = "\uE823";
        }

        _premiumBaselineValueText.Text = string.IsNullOrWhiteSpace(summary) ? verdict : summary;
        _premiumBaselineDetailText.Text = string.IsNullOrWhiteSpace(detail)
            ? verdict.Equals("Not captured", StringComparison.OrdinalIgnoreCase)
                ? "Build a repeated baseline for comparison"
                : BaselineStatusText.Text
            : detail;
        _premiumBaselineValueText.Foreground = Brush(stateBrush);
        _premiumBaselineIcon.Foreground = Brush(stateBrush);
        _premiumBaselineIcon.Glyph = glyph;
        _premiumBaselineIconHost.Background = Brush(stateSoftBrush);
        _premiumBaselineCard.BorderBrush = Brush(stateSoftBrush);
    }

    private void RefreshPremiumSnapshotFromEvidence()
    {
        if (_lastPremiumCapture is null)
        {
            ClearPremiumOverviewCapture();
            return;
        }

        RenderPremiumOverviewCapture(_lastPremiumCapture);
    }

    private void RenderPremiumOverviewCapture(KernelLatencyCaptureResponse capture)
    {
        if (_premiumDpcValueText is null || _premiumIsrValueText is null || _premiumCpuValueText is null ||
            _premiumLatencyProfileChart is null || _premiumCpuDistributionChart is null ||
            _premiumModuleContributionChart is null || _premiumCpuInterruptMap is null ||
            _premiumSnapshotBadge is null || _premiumSnapshotBadgeText is null ||
            _premiumSnapshotSummaryText is null || _premiumSnapshotMetricsText is null)
        {
            return;
        }

        _premiumDpcValueText.Text = FormatMicroseconds(capture.Dpc.P99Microseconds);
        _premiumIsrValueText.Text = FormatMicroseconds(capture.Isr.P99Microseconds);
        _premiumDpcDetailText!.Text = capture.Dpc.P999Microseconds is null
            ? $"max {FormatMicroseconds(capture.Dpc.MaximumMicroseconds)}"
            : $"p99.9 {FormatMicroseconds(capture.Dpc.P999Microseconds)}";
        _premiumIsrDetailText!.Text = capture.Isr.P999Microseconds is null
            ? $"max {FormatMicroseconds(capture.Isr.MaximumMicroseconds)}"
            : $"p99.9 {FormatMicroseconds(capture.Isr.P999Microseconds)}";

        var totalEvents = capture.Processors.Sum(static processor => processor.Dpc.Count + processor.Isr.Count);
        var busiest = capture.Processors
            .OrderByDescending(static processor => processor.Dpc.Count + processor.Isr.Count)
            .FirstOrDefault();
        var busiestEvents = busiest is null ? 0 : busiest.Dpc.Count + busiest.Isr.Count;
        var concentration = totalEvents <= 0 ? 0d : busiestEvents * 100d / totalEvents;
        _premiumCpuValueText.Text = totalEvents <= 0 ? "—" : $"{concentration:0.#}%";
        _premiumCpuDetailText!.Text = busiest is null ? "No CPU evidence" : $"CPU {busiest.ProcessorNumber} · {busiestEvents:N0} events";

        var labels = new[] { "p50", "p95", "p99", "p99.9", "max" };
        _premiumLatencyProfileChart.SetSeries(
            [
                new ChartPoint("DPC p50", capture.Dpc.P50Microseconds),
                new ChartPoint("DPC p95", capture.Dpc.P95Microseconds),
                new ChartPoint("DPC p99", capture.Dpc.P99Microseconds),
                new ChartPoint("DPC p99.9", capture.Dpc.P999Microseconds),
                new ChartPoint("DPC max", capture.Dpc.MaximumMicroseconds),
            ],
            [
                new ChartPoint("ISR p50", capture.Isr.P50Microseconds),
                new ChartPoint("ISR p95", capture.Isr.P95Microseconds),
                new ChartPoint("ISR p99", capture.Isr.P99Microseconds),
                new ChartPoint("ISR p99.9", capture.Isr.P999Microseconds),
                new ChartPoint("ISR max", capture.Isr.MaximumMicroseconds),
            ],
            labels,
            $"DPC p99 {FormatMicroseconds(capture.Dpc.P99Microseconds)}; ISR p99 {FormatMicroseconds(capture.Isr.P99Microseconds)}.");

        var cpuBars = capture.Processors
            .OrderBy(static processor => processor.ProcessorNumber)
            .Select(processor =>
            {
                var events = processor.Dpc.Count + processor.Isr.Count;
                return new ChartBar(
                    $"CPU {processor.ProcessorNumber}",
                    totalEvents <= 0 ? 0d : events * 100d / totalEvents,
                    $"{events:N0} events");
            })
            .ToArray();
        _premiumCpuDistributionChart.SetBars(
            cpuBars,
            busiest is null
                ? "No processor distribution was captured."
                : $"CPU {busiest.ProcessorNumber} handled {concentration:0.#}% of observed DPC and ISR events.");

        var moduleDuration = capture.Modules.Sum(static module => module.TotalDurationMicroseconds);
        var moduleBars = capture.Modules
            .OrderByDescending(static module => module.TotalDurationMicroseconds)
            .Take(6)
            .Select(module => new ChartBar(
                module.ModuleName,
                moduleDuration <= 0d ? 0d : module.TotalDurationMicroseconds * 100d / moduleDuration,
                $"{module.TotalDurationMicroseconds / 1000d:0.###} ms observed"))
            .ToArray();
        _premiumModuleContributionChart.SetBars(
            moduleBars,
            moduleBars.Length == 0
                ? "No module attribution was available."
                : $"{moduleBars[0].Label} contributed the largest share of attributed kernel time.");

        var totalDpc = Math.Max(1, capture.Processors.Sum(static processor => processor.Dpc.Count));
        var totalIsr = Math.Max(1, capture.Processors.Sum(static processor => processor.Isr.Count));
        _premiumCpuInterruptMap.SetRows(
            capture.Processors
                .OrderByDescending(static processor => processor.Dpc.Count + processor.Isr.Count)
                .Take(8)
                .Select(processor => new InterruptMapRow(
                    $"CPU {processor.ProcessorNumber}",
                    processor.Dpc.Count * 100d / totalDpc,
                    processor.Isr.Count * 100d / totalIsr))
                .ToArray(),
            "Cells show each listed processor's observed DPC share and ISR share.");

        var integrityIssue = GetCaptureIntegrityIssue(capture);
        var hasThreeMs = capture.DpcThresholds.OverThreeMillisecondsCount > 0 || capture.IsrThresholds.OverThreeMillisecondsCount > 0;
        var hasOneMs = capture.DpcThresholds.OverOneMillisecondCount > 0 || capture.IsrThresholds.OverOneMillisecondCount > 0;
        var hasReference = capture.DpcThresholds.GuidanceExceedanceCount > 0 || capture.IsrThresholds.GuidanceExceedanceCount > 0;
        var dominantModule = moduleBars.FirstOrDefault()?.Label;

        if (integrityIssue is not null)
        {
            ApplySnapshotStatus("Invalid capture", SemanticFailureBrush, "SemanticFailureSoftBrush");
            _premiumSnapshotSummaryText.Text = integrityIssue;
        }
        else if (hasThreeMs || hasOneMs || hasReference)
        {
            ApplySnapshotStatus(hasThreeMs ? "Review tail · >3 ms" : hasOneMs ? "Review tail · >1 ms" : "Review reference", SemanticAttentionBrush, "SemanticAttentionSoftBrush");
            _premiumSnapshotSummaryText.Text = dominantModule is null
                ? "The snapshot contains tail/reference evidence worth reviewing."
                : $"{dominantModule} led attributed kernel time; inspect exact evidence before drawing a conclusion.";
        }
        else
        {
            ApplySnapshotStatus("Clean snapshot", SemanticGoodBrush, "SemanticGoodSoftBrush");
            _premiumSnapshotSummaryText.Text = dominantModule is null
                ? "Capture integrity is clean."
                : $"Capture integrity is clean · {dominantModule} led attributed kernel time.";
        }

        _premiumSnapshotMetricsText.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"DPC {FormatMicroseconds(capture.Dpc.P99Microseconds)} · ISR {FormatMicroseconds(capture.Isr.P99Microseconds)} · CPU {concentration:0.#}% · {capture.ActualDurationMilliseconds / 1000d:0.#} s");
    }

    private void ApplySnapshotStatus(string text, string foregroundBrush, string backgroundBrush)
    {
        if (_premiumSnapshotBadge is null || _premiumSnapshotBadgeText is null)
        {
            return;
        }

        _premiumSnapshotBadgeText.Text = text;
        _premiumSnapshotBadgeText.Foreground = Brush(foregroundBrush);
        _premiumSnapshotBadge.Background = Brush(backgroundBrush);
    }

    private void ClearPremiumOverviewCapture()
    {
        if (_premiumDpcValueText is null || _premiumIsrValueText is null || _premiumCpuValueText is null)
        {
            return;
        }

        _premiumDpcValueText.Text = "—";
        _premiumIsrValueText.Text = "—";
        _premiumCpuValueText.Text = "—";
        _premiumDpcDetailText!.Text = "No capture yet";
        _premiumIsrDetailText!.Text = "No capture yet";
        _premiumCpuDetailText!.Text = "No capture yet";
        _premiumLatencyProfileChart?.Clear("Run a quick snapshot to reveal DPC and ISR percentiles.");
        _premiumCpuDistributionChart?.Clear("Run a quick snapshot to reveal processor concentration.");
        _premiumModuleContributionChart?.Clear("Run a quick snapshot to rank attributed kernel modules.");
        _premiumCpuInterruptMap?.Clear("Run a quick snapshot to compare DPC and ISR intensity by processor.");
        ApplySnapshotStatus("Not captured", "MutedTextBrush", "SurfaceAltBrush");
        if (_premiumSnapshotSummaryText is not null)
        {
            _premiumSnapshotSummaryText.Text = "Run a five-second snapshot to populate this dashboard.";
        }
        if (_premiumSnapshotMetricsText is not null)
        {
            _premiumSnapshotMetricsText.Text = "DPC — · ISR — · CPU —";
        }
    }

    private void ApplyPremiumOverviewTheme()
    {
        if (_premiumOverviewRoot is null)
        {
            return;
        }

        // Re-resolve semantic status resources after a Light/Dark/High Contrast change.
        SyncPremiumQuickSnapshotState();
        if (_premiumQuickSnapshotButton is not null)
        {
            _premiumQuickSnapshotButton.Background = Brush(BrandActionBrush);
        }
    }

    private void ApplyPremiumOverviewLayout()
    {
        if (_premiumOverviewRoot is null || _premiumOverviewHeader is null ||
            _premiumSystemSummaryGrid is null || _premiumMetricGrid is null ||
            _premiumChartGrid is null || _premiumBottomGrid is null)
        {
            return;
        }

        var width = Math.Max(0d, _premiumOverviewRoot.ActualWidth);
        if (width <= 0d)
        {
            width = OverviewContent.ActualWidth;
        }

        var wide = width >= 1180d;
        var medium = width >= 760d;

        ConfigureSimpleGrid(_premiumMetricGrid, _premiumMetricCards, wide ? 4 : medium ? 2 : 1);
        ConfigureSystemSummaryGrid(wide || medium ? 2 : 1);
        ConfigureChartGrid(wide ? 3 : medium ? 2 : 1);
        ConfigureBottomGrid(wide ? 3 : medium ? 2 : 1);

        if (_premiumOverviewHeader.Children.Count > 1 && _premiumOverviewHeader.Children[1] is FrameworkElement action)
        {
            Grid.SetColumn(action, wide || medium ? 1 : 0);
            Grid.SetRow(action, wide || medium ? 0 : 1);
            action.HorizontalAlignment = wide || medium ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        }
    }

    private void ConfigureSystemSummaryGrid(int columns)
    {
        if (_premiumSystemSummaryGrid is null)
        {
            return;
        }

        _premiumSystemSummaryGrid.ColumnDefinitions.Clear();
        _premiumSystemSummaryGrid.RowDefinitions.Clear();
        for (var index = 0; index < columns; index++)
        {
            _premiumSystemSummaryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }
        var rows = (2 + columns - 1) / columns;
        for (var index = 0; index < rows; index++)
        {
            _premiumSystemSummaryGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }
        for (var index = 0; index < _premiumSystemSummaryGrid.Children.Count; index++)
        {
            Grid.SetColumn(_premiumSystemSummaryGrid.Children[index], index % columns);
            Grid.SetRow(_premiumSystemSummaryGrid.Children[index], index / columns);
        }
    }

    private void ConfigureChartGrid(int columns)
    {
        if (_premiumChartGrid is null)
        {
            return;
        }

        _premiumChartGrid.ColumnDefinitions.Clear();
        _premiumChartGrid.RowDefinitions.Clear();
        if (columns == 3)
        {
            _premiumChartGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.35, GridUnitType.Star) });
            _premiumChartGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _premiumChartGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _premiumChartGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (var index = 0; index < _premiumChartCards.Count; index++)
            {
                Grid.SetRow(_premiumChartCards[index], 0);
                Grid.SetColumn(_premiumChartCards[index], index);
                Grid.SetColumnSpan(_premiumChartCards[index], 1);
            }
            return;
        }

        if (columns == 2)
        {
            _premiumChartGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _premiumChartGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _premiumChartGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _premiumChartGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(_premiumChartCards[0], 0);
            Grid.SetColumn(_premiumChartCards[0], 0);
            Grid.SetColumnSpan(_premiumChartCards[0], 2);
            for (var index = 1; index < _premiumChartCards.Count; index++)
            {
                Grid.SetRow(_premiumChartCards[index], 1);
                Grid.SetColumn(_premiumChartCards[index], index - 1);
                Grid.SetColumnSpan(_premiumChartCards[index], 1);
            }
            return;
        }

        ConfigureSimpleGrid(_premiumChartGrid, _premiumChartCards, 1);
    }

    private void ConfigureBottomGrid(int columns)
    {
        if (_premiumBottomGrid is null)
        {
            return;
        }

        if (columns == 3)
        {
            _premiumBottomGrid.ColumnDefinitions.Clear();
            _premiumBottomGrid.RowDefinitions.Clear();
            _premiumBottomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.25, GridUnitType.Star) });
            _premiumBottomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _premiumBottomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _premiumBottomGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (var index = 0; index < _premiumBottomCards.Count; index++)
            {
                Grid.SetRow(_premiumBottomCards[index], 0);
                Grid.SetColumn(_premiumBottomCards[index], index);
                Grid.SetColumnSpan(_premiumBottomCards[index], 1);
            }
            return;
        }

        ConfigureSimpleGrid(_premiumBottomGrid, _premiumBottomCards, columns);
    }

    private static void ConfigureSimpleGrid(Grid grid, IReadOnlyList<FrameworkElement> items, int columns)
    {
        grid.ColumnDefinitions.Clear();
        grid.RowDefinitions.Clear();
        for (var index = 0; index < columns; index++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }
        var rows = (items.Count + columns - 1) / columns;
        for (var index = 0; index < rows; index++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }
        for (var index = 0; index < items.Count; index++)
        {
            Grid.SetColumn(items[index], index % columns);
            Grid.SetRow(items[index], index / columns);
            Grid.SetColumnSpan(items[index], 1);
        }
    }

    private Brush Brush(string key) => DashboardThemeResources.Brush(RootGrid, key);
}
