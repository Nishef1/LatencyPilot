using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace LatencyPilot.App;

public sealed partial class MainWindow
{
    // Data colors identify what is measured. Traffic-light colors identify evidence state.
    private const string BrandActionBrush = "BrandActionBrush";
    private const string SemanticGoodBrush = "SemanticGoodBrush";
    private const string SemanticAttentionBrush = "SemanticAttentionBrush";
    private const string SemanticFailureBrush = "SemanticFailureBrush";

    private bool _premiumOverviewInitialized;
    private bool _forcingPremiumOverviewVisibility;
    private Grid? _premiumBottomGrid;
    private TextBlock? _premiumRecentStatusText;
    private TextBlock? _premiumRecentSummaryText;
    private Border? _premiumRecentStatusPill;
    private TextBlock? _premiumGraphicsEvidenceText;
    private TextBlock? _premiumNetworkEvidenceText;
    private TextBlock? _premiumUsbEvidenceText;
    private Border? _premiumGraphicsEvidenceDot;
    private Border? _premiumNetworkEvidenceDot;
    private Border? _premiumUsbEvidenceDot;

    internal void InitializePremiumOverviewExperience()
    {
        if (_premiumOverviewInitialized)
        {
            return;
        }

        _premiumOverviewInitialized = true;
        PromoteSystemSummary();
        RestylePremiumOverview();
        BuildPremiumBottomRow();
        ConfigurePremiumChartAccessibility();
        ForcePremiumOverviewContentVisible();

        OverviewEmptyState.RegisterPropertyChangedCallback(
            UIElement.VisibilityProperty,
            (_, _) => ForcePremiumOverviewContentVisible());
        OverviewDataContent.RegisterPropertyChangedCallback(
            UIElement.VisibilityProperty,
            (_, _) => ForcePremiumOverviewContentVisible());
        RecentSnapshotStatusText.RegisterPropertyChangedCallback(
            TextBlock.TextProperty,
            (_, _) => SyncPremiumRecentSnapshot());
        RecentSnapshotSummaryText.RegisterPropertyChangedCallback(
            TextBlock.TextProperty,
            (_, _) => SyncPremiumRecentSnapshot());
        DisplayEvidenceText.RegisterPropertyChangedCallback(
            TextBlock.TextProperty,
            (_, _) => SyncPremiumDeviceEvidence());
        NetworkEvidenceText.RegisterPropertyChangedCallback(
            TextBlock.TextProperty,
            (_, _) => SyncPremiumDeviceEvidence());
        UsbEvidenceText.RegisterPropertyChangedCallback(
            TextBlock.TextProperty,
            (_, _) => SyncPremiumDeviceEvidence());
        PrimaryGpuText.RegisterPropertyChangedCallback(
            TextBlock.TextProperty,
            (_, _) => SyncPremiumDeviceEvidence());
        BaselineSummaryText.RegisterPropertyChangedCallback(
            TextBlock.TextProperty,
            (_, _) => ApplyPremiumBaselineState());
        BaselineSummaryDetailText.RegisterPropertyChangedCallback(
            TextBlock.TextProperty,
            (_, _) => ApplyPremiumBaselineState());
        BaselineVerdictText.RegisterPropertyChangedCallback(
            TextBlock.TextProperty,
            (_, _) => ApplyPremiumBaselineState());

        RootGrid.ActualThemeChanged += (_, _) =>
            DispatcherQueue.TryEnqueue(() =>
            {
                RestylePremiumOverview();
                ApplyPremiumBaselineState();
                SyncPremiumRecentSnapshot();
                SyncPremiumDeviceEvidence();
            });
        RootGrid.SizeChanged += (_, _) => ReflowPremiumBottomRow();

        SyncPremiumRecentSnapshot();
        SyncPremiumDeviceEvidence();
        ApplyPremiumBaselineState();
        ApplyDashboardLayout();
        ReflowPremiumBottomRow();
    }

    private void ConfigurePremiumChartAccessibility()
    {
        AutomationProperties.SetName(LatencyProfileChart, "DPC and ISR latency profile");
        AutomationProperties.SetName(CpuDistributionChart, "CPU interrupt distribution");
        AutomationProperties.SetName(ModuleContributionChart, "Top kernel modules by attributed time");
        AutomationProperties.SetName(CpuInterruptMap, "CPU DPC and ISR interrupt map");
    }

    private void PromoteSystemSummary()
    {
        if (OverviewContent.Children.Count == 0 ||
            OverviewContent.Children[0] is not StackPanel rootStack ||
            rootStack.Children.Count < 4)
        {
            return;
        }

        var systemSummary = rootStack.Children[rootStack.Children.Count - 1];
        rootStack.Children.Remove(systemSummary);
        rootStack.Children.Insert(1, systemSummary);
    }

    private void RestylePremiumOverview()
    {
        CaptureObservationButton.Style = (Style)Application.Current.Resources["PremiumPrimaryButtonStyle"];
        CaptureObservationButton.Background = ThemeBrush(BrandActionBrush);

        if (SummaryGrid.Children.Count >= 4)
        {
            ApplyCategoryCard(SummaryGrid.Children[0] as Border, "DpcCategorySoftBrush", "DpcCategoryBrush");
            ApplyCategoryCard(SummaryGrid.Children[1] as Border, "IsrCategorySoftBrush", "IsrCategoryBrush");
            ApplyCategoryCard(SummaryGrid.Children[2] as Border, "CpuCategorySoftBrush", "CpuCategoryBrush");
        }

        foreach (var item in MeasureAnchor.Children)
        {
            if (item is Border card)
            {
                card.Background = ThemeBrush("PremiumOverviewCardBrush");
                card.BorderBrush = ThemeBrush("BorderBrush");
                card.CornerRadius = new CornerRadius(11);
            }
        }

        ApplyPremiumBaselineState();
    }

    private void ApplyCategoryCard(Border? card, string softBrush, string accentBrush)
    {
        if (card is null)
        {
            return;
        }

        card.Background = ThemeBrush(softBrush);
        card.BorderBrush = ThemeBrush(accentBrush);
        card.BorderThickness = new Thickness(1, 1, 1, 2);
        card.CornerRadius = new CornerRadius(11);
    }

    private void BuildPremiumBottomRow()
    {
        if (_premiumBottomGrid is not null || MeasureAnchor.Children.Count < 4)
        {
            return;
        }

        // Keep one real module chart and one real data path; only its visual placement changes.
        var moduleCard = MeasureAnchor.Children[2];
        MeasureAnchor.Children.Remove(moduleCard);

        _premiumBottomGrid = new Grid { ColumnSpacing = 12, RowSpacing = 12 };
        _premiumBottomGrid.Children.Add(moduleCard);
        _premiumBottomGrid.Children.Add(BuildPremiumRecentSnapshotCard());
        _premiumBottomGrid.Children.Add(BuildPremiumDeviceEvidenceCard());
        OverviewDataContent.Children.Add(_premiumBottomGrid);
    }

    private Border BuildPremiumRecentSnapshotCard()
    {
        var card = CreatePremiumCard();
        var root = new StackPanel { Spacing = 10 };
        card.Child = root;

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        title.Children.Add(new FontIcon
        {
            FontFamily = (FontFamily)Application.Current.Resources["SymbolThemeFontFamily"],
            Glyph = "\uE823",
            FontSize = 16,
            Foreground = ThemeBrush(BrandActionBrush),
        });
        title.Children.Add(new TextBlock
        {
            Text = "Recent snapshot",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = ThemeBrush("TextBrush"),
        });
        header.Children.Add(title);
        var evidenceLink = BuildPremiumLink("View evidence", EvidenceNavItem);
        Grid.SetColumn(evidenceLink, 1);
        header.Children.Add(evidenceLink);
        root.Children.Add(header);

        _premiumRecentStatusText = new TextBlock { FontSize = 11, FontWeight = FontWeights.SemiBold };
        _premiumRecentStatusPill = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(8, 3, 8, 3),
            CornerRadius = new CornerRadius(999),
            Child = _premiumRecentStatusText,
        };
        root.Children.Add(_premiumRecentStatusPill);
        _premiumRecentSummaryText = new TextBlock
        {
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = ThemeBrush("MutedTextBrush"),
        };
        root.Children.Add(_premiumRecentSummaryText);
        return card;
    }

    private Border BuildPremiumDeviceEvidenceCard()
    {
        var card = CreatePremiumCard();
        var root = new StackPanel { Spacing = 8 };
        card.Child = root;

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock
        {
            Text = "Device evidence",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = ThemeBrush("TextBrush"),
        });
        var devicesLink = BuildPremiumLink("View devices", DevicesNavItem);
        Grid.SetColumn(devicesLink, 1);
        header.Children.Add(devicesLink);
        root.Children.Add(header);

        root.Children.Add(BuildEvidenceRow("Graphics", out _premiumGraphicsEvidenceDot, out _premiumGraphicsEvidenceText));
        root.Children.Add(BuildEvidenceRow("Network", out _premiumNetworkEvidenceDot, out _premiumNetworkEvidenceText));
        root.Children.Add(BuildEvidenceRow("USB / xHCI", out _premiumUsbEvidenceDot, out _premiumUsbEvidenceText));
        return card;
    }

    private Grid BuildEvidenceRow(string label, out Border statusDot, out TextBlock valueText)
    {
        var row = new Grid { ColumnSpacing = 9, MinHeight = 30 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        statusDot = new Border
        {
            Width = 9,
            Height = 9,
            CornerRadius = new CornerRadius(5),
            VerticalAlignment = VerticalAlignment.Center,
        };
        row.Children.Add(statusDot);
        var copy = new StackPanel { Spacing = 0, VerticalAlignment = VerticalAlignment.Center };
        copy.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = ThemeBrush("TextBrush"),
        });
        valueText = new TextBlock
        {
            Text = "Checking…",
            FontSize = 10,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = ThemeBrush("MutedTextBrush"),
        };
        copy.Children.Add(valueText);
        Grid.SetColumn(copy, 1);
        row.Children.Add(copy);
        return row;
    }

    private Border CreatePremiumCard() => new()
    {
        Padding = new Thickness(14),
        CornerRadius = new CornerRadius(11),
        Background = ThemeBrush("PremiumOverviewCardBrush"),
        BorderBrush = ThemeBrush("BorderBrush"),
        BorderThickness = new Thickness(1),
    };

    private Button BuildPremiumLink(string text, NavigationViewItem target)
    {
        var button = new Button
        {
            Content = text,
            Padding = new Thickness(6, 2, 6, 2),
            BorderThickness = new Thickness(0),
            Foreground = ThemeBrush(BrandActionBrush),
            FontSize = 11,
        };
        button.Click += (_, _) => AppNavigationView.SelectedItem = target;
        AutomationProperties.SetName(button, text);
        return button;
    }

    private void ForcePremiumOverviewContentVisible()
    {
        if (_forcingPremiumOverviewVisibility)
        {
            return;
        }

        _forcingPremiumOverviewVisibility = true;
        try
        {
            OverviewEmptyState.Visibility = Visibility.Collapsed;
            OverviewDataContent.Visibility = Visibility.Visible;
        }
        finally
        {
            _forcingPremiumOverviewVisibility = false;
        }
    }

    private void SyncPremiumRecentSnapshot()
    {
        if (_premiumRecentStatusText is null || _premiumRecentSummaryText is null || _premiumRecentStatusPill is null)
        {
            return;
        }

        var status = RecentSnapshotStatusText.Text ?? "No snapshot yet";
        _premiumRecentStatusText.Text = status;
        _premiumRecentSummaryText.Text = RecentSnapshotSummaryText.Text;
        var failure = status.Contains("invalid", StringComparison.OrdinalIgnoreCase) ||
                      status.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
                      status.Contains("warning", StringComparison.OrdinalIgnoreCase);
        var attention = !failure && _snapshotEvidenceBadgeText is not null &&
                        (_snapshotEvidenceBadgeText.Text.Contains("exceeded", StringComparison.OrdinalIgnoreCase) ||
                         _snapshotEvidenceBadgeText.Text.Contains("ms bucket", StringComparison.OrdinalIgnoreCase));
        var good = !failure && !attention && status.Contains("ready", StringComparison.OrdinalIgnoreCase);
        _premiumRecentStatusText.Foreground = ThemeBrush(
            failure ? SemanticFailureBrush : attention ? SemanticAttentionBrush : good ? SemanticGoodBrush : "MutedTextBrush");
        _premiumRecentStatusPill.Background = ThemeBrush(
            failure ? "SemanticFailureSoftBrush" : attention ? "SemanticAttentionSoftBrush" : good ? "SemanticGoodSoftBrush" : "SurfaceAltBrush");
    }

    private void SyncPremiumDeviceEvidence()
    {
        if (_premiumGraphicsEvidenceText is null || _premiumNetworkEvidenceText is null || _premiumUsbEvidenceText is null)
        {
            return;
        }

        _premiumGraphicsEvidenceText.Text = string.IsNullOrWhiteSpace(PrimaryGpuText.Text)
            ? DisplayEvidenceText.Text
            : $"{PrimaryGpuText.Text} · {DisplayEvidenceText.Text}";
        _premiumNetworkEvidenceText.Text = NetworkEvidenceText.Text;
        _premiumUsbEvidenceText.Text = UsbEvidenceText.Text;
        ApplyDetectionDot(_premiumGraphicsEvidenceDot, DisplayEvidenceText.Text);
        ApplyDetectionDot(_premiumNetworkEvidenceDot, NetworkEvidenceText.Text);
        ApplyDetectionDot(_premiumUsbEvidenceDot, UsbEvidenceText.Text);
    }

    private void ApplyDetectionDot(Border? dot, string state)
    {
        if (dot is null)
        {
            return;
        }

        dot.Background = ThemeBrush(state.Equals("Detected", StringComparison.OrdinalIgnoreCase)
            ? SemanticGoodBrush
            : state.Contains("not", StringComparison.OrdinalIgnoreCase)
                ? SemanticAttentionBrush
                : "MutedTextBrush");
    }

    private void ApplyPremiumBaselineState()
    {
        if (SummaryGrid.Children.Count < 4 || SummaryGrid.Children[3] is not Border baselineCard)
        {
            return;
        }

        var verdict = BaselineVerdictText.Text ?? "Not captured";
        var summary = BaselineSummaryText.Text ?? verdict;
        var detail = BaselineSummaryDetailText.Text ?? string.Empty;
        var attention = summary.Contains("transient", StringComparison.OrdinalIgnoreCase) ||
                        summary.Contains("not ready", StringComparison.OrdinalIgnoreCase) ||
                        detail.Contains("changing", StringComparison.OrdinalIgnoreCase) ||
                        verdict.Equals("Inconclusive", StringComparison.OrdinalIgnoreCase);
        var failure = verdict.Contains("fail", StringComparison.OrdinalIgnoreCase) ||
                      verdict.Contains("invalid", StringComparison.OrdinalIgnoreCase);
        var good = verdict.Equals("Valid", StringComparison.OrdinalIgnoreCase) && !attention;

        var foreground = failure ? SemanticFailureBrush : attention ? SemanticAttentionBrush : good ? SemanticGoodBrush : "TextBrush";
        var background = failure ? "SemanticFailureSoftBrush" : attention ? "SemanticAttentionSoftBrush" : good ? "SemanticGoodSoftBrush" : "PremiumOverviewCardBrush";
        baselineCard.Background = ThemeBrush(background);
        baselineCard.BorderBrush = ThemeBrush(foreground == "TextBrush" ? "BorderBrush" : foreground);
        baselineCard.BorderThickness = new Thickness(1, 1, 1, 2);
        baselineCard.CornerRadius = new CornerRadius(11);
        BaselineSummaryText.Foreground = ThemeBrush(foreground);
    }

    private void ReflowPremiumBottomRow()
    {
        if (_premiumBottomGrid is null)
        {
            return;
        }

        var width = Math.Max(0d, OverviewContent.ActualWidth);
        var columns = width >= 1050d ? 3 : width >= 640d ? 2 : 1;
        _premiumBottomGrid.ColumnDefinitions.Clear();
        _premiumBottomGrid.RowDefinitions.Clear();
        for (var index = 0; index < columns; index++)
        {
            _premiumBottomGrid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(index == 0 && columns == 3 ? 1.25 : 1, GridUnitType.Star),
            });
        }
        var rows = (_premiumBottomGrid.Children.Count + columns - 1) / columns;
        for (var index = 0; index < rows; index++)
        {
            _premiumBottomGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }
        for (var index = 0; index < _premiumBottomGrid.Children.Count; index++)
        {
            if (_premiumBottomGrid.Children[index] is FrameworkElement child)
            {
                Grid.SetColumn(child, index % columns);
                Grid.SetRow(child, index / columns);
            }
        }
    }
}
