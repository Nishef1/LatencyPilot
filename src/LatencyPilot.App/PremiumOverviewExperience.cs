using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace LatencyPilot.App;

public sealed partial class MainWindow
{
    // Dataset colors identify what is measured. Semantic colors identify evidence state.
    private const string BrandActionBrush = "BrandActionBrush";
    private const string SemanticGoodBrush = "SemanticGoodBrush";
    private const string SemanticAttentionBrush = "SemanticAttentionBrush";
    private const string SemanticFailureBrush = "SemanticFailureBrush";

    private bool _premiumOverviewInitialized;
    private Border? _premiumSystemSummaryCard;
    private Border? _premiumDpcCard;
    private Border? _premiumIsrCard;
    private Border? _premiumCpuCard;
    private Border? _premiumBaselineCard;
    private Border? _premiumDpcAccent;
    private Border? _premiumIsrAccent;
    private Border? _premiumCpuAccent;
    private Border? _premiumBaselineAccent;
    private Border? _premiumLatencyCard;
    private Border? _premiumDistributionCard;
    private Border? _premiumModuleCard;
    private Border? _premiumInterruptMapCard;
    private Border? _premiumRecentCard;
    private Border? _premiumDeviceCard;
    private Grid? _premiumEmptyStateGrid;
    private Grid? _premiumSecondaryGrid;
    private Grid? _premiumContextGrid;
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
        PrepareMetricCards();
        BuildPremiumEmptyState();
        BuildPremiumDataSections();
        ConfigurePremiumChartAccessibility();
        RestylePremiumOverview();

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
                BuildPremiumEmptyState();
                RebuildPremiumContextCards();
                RestylePremiumOverview();
                ApplyPremiumBaselineState();
                SyncPremiumRecentSnapshot();
                SyncPremiumDeviceEvidence();
                ReflowPremiumOverview();
            });
        RootGrid.SizeChanged += (_, _) => ReflowPremiumOverview();

        SyncPremiumRecentSnapshot();
        SyncPremiumDeviceEvidence();
        ApplyPremiumBaselineState();
        ApplyDashboardLayout();
        ReflowPremiumOverview();
    }

    private void ConfigurePremiumChartAccessibility()
    {
        AutomationProperties.SetName(LatencyProfileChart, "DPC and ISR latency profile");
        AutomationProperties.SetName(CpuDistributionChart, "CPU interrupt distribution");
        AutomationProperties.SetName(ModuleContributionChart, "Top kernel modules by attributed time");
        AutomationProperties.SetName(CpuInterruptMap, "CPU DPC and ISR interrupt map");
        AutomationProperties.SetHelpText(
            OverviewEmptyState,
            "Quick snapshot is a five-second read-only diagnostic capture. It does not change system settings and does not replace a repeated baseline.");
    }

    private void PromoteSystemSummary()
    {
        if (OverviewContent.Children.Count == 0 ||
            OverviewContent.Children[0] is not StackPanel rootStack ||
            rootStack.Children.Count < 4)
        {
            return;
        }

        if (rootStack.Children[rootStack.Children.Count - 1] is not Border systemSummary)
        {
            return;
        }

        rootStack.Children.Remove(systemSummary);
        rootStack.Children.Insert(1, systemSummary);
        _premiumSystemSummaryCard = systemSummary;
    }

    private void PrepareMetricCards()
    {
        if (SummaryGrid.Children.Count < 4)
        {
            return;
        }

        _premiumDpcCard = SummaryGrid.Children[0] as Border;
        _premiumIsrCard = SummaryGrid.Children[1] as Border;
        _premiumCpuCard = SummaryGrid.Children[2] as Border;
        _premiumBaselineCard = SummaryGrid.Children[3] as Border;

        _premiumDpcAccent = InsertMetricAccent(_premiumDpcCard);
        _premiumIsrAccent = InsertMetricAccent(_premiumIsrCard);
        _premiumCpuAccent = InsertMetricAccent(_premiumCpuCard);
        _premiumBaselineAccent = InsertMetricAccent(_premiumBaselineCard);
    }

    private static Border? InsertMetricAccent(Border? card)
    {
        if (card?.Child is not StackPanel stack)
        {
            return null;
        }

        var accent = new Border
        {
            Width = 28,
            Height = 3,
            HorizontalAlignment = HorizontalAlignment.Left,
            CornerRadius = new CornerRadius(2),
            Margin = new Thickness(0, 0, 0, 2),
        };
        stack.Children.Insert(0, accent);
        return accent;
    }

    private void BuildPremiumEmptyState()
    {
        _premiumEmptyStateGrid = new Grid
        {
            ColumnSpacing = 28,
            RowSpacing = 22,
        };
        _premiumEmptyStateGrid.ColumnDefinitions.Add(new ColumnDefinition());
        _premiumEmptyStateGrid.ColumnDefinitions.Add(new ColumnDefinition());
        _premiumEmptyStateGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _premiumEmptyStateGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var copy = new StackPanel
        {
            Spacing = 14,
            VerticalAlignment = VerticalAlignment.Center,
        };
        copy.Children.Add(BuildEyebrow("READ-ONLY DIAGNOSTIC"));
        copy.Children.Add(new TextBlock
        {
            Text = "See where interrupt latency is coming from.",
            FontSize = 27,
            FontWeight = FontWeights.SemiBold,
            Foreground = ThemeBrush("TextBrush"),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 680,
        });
        copy.Children.Add(new TextBlock
        {
            Text = "Quick snapshot observes five seconds of real DPC and ISR activity, then reveals the latency tail, CPU concentration, and resolved kernel contributors. Build a repeated baseline before making stability or optimization decisions.",
            FontSize = 14,
            Foreground = ThemeBrush("MutedTextBrush"),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 720,
        });

        var metadata = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
        };
        metadata.Children.Add(BuildInfoPill("5-second capture"));
        metadata.Children.Add(BuildInfoPill("DPC + ISR"));
        metadata.Children.Add(BuildInfoPill("No system changes"));
        copy.Children.Add(metadata);
        _premiumEmptyStateGrid.Children.Add(copy);

        var preview = new Border
        {
            Padding = new Thickness(18),
            CornerRadius = new CornerRadius(16),
            Background = ThemeBrush("PremiumOverviewQuietBrush"),
            BorderBrush = ThemeBrush("PremiumOverviewDividerBrush"),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        var previewStack = new StackPanel { Spacing = 13 };
        preview.Child = previewStack;

        var previewHeader = new Grid();
        previewHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        previewHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        previewHeader.Children.Add(new TextBlock
        {
            Text = "What the snapshot reveals",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = ThemeBrush("TextBrush"),
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 2,
        });
        var waiting = new Border
        {
            Padding = new Thickness(8, 4, 8, 4),
            CornerRadius = new CornerRadius(999),
            Background = ThemeBrush("BrandActionSoftBrush"),
            Child = new TextBlock
            {
                Text = "Waiting for evidence",
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = ThemeBrush(BrandActionBrush),
            },
        };
        Grid.SetColumn(waiting, 1);
        previewHeader.Children.Add(waiting);
        previewStack.Children.Add(previewHeader);
        previewStack.Children.Add(BuildEmptyFeatureRow(Symbol.Clock, "Tail shape", "Real DPC and ISR percentiles, including available p99.9 evidence."));
        previewStack.Children.Add(BuildEmptyFeatureRow(Symbol.Repair, "CPU concentration", "Which observed processor handled the largest share of interrupt work."));
        previewStack.Children.Add(BuildEmptyFeatureRow(Symbol.Document, "Kernel attribution", "Resolved modules ranked by the time they contributed during capture."));
        previewStack.Children.Add(new TextBlock
        {
            Text = "Use Quick snapshot above or press Ctrl+O.",
            FontSize = 12,
            Foreground = ThemeBrush("MutedTextBrush"),
            TextWrapping = TextWrapping.Wrap,
        });

        Grid.SetColumn(preview, 1);
        _premiumEmptyStateGrid.Children.Add(preview);
        OverviewEmptyState.Child = _premiumEmptyStateGrid;
    }

    private StackPanel BuildEyebrow(string text)
    {
        var eyebrow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 7,
        };
        eyebrow.Children.Add(new Border
        {
            Width = 7,
            Height = 7,
            CornerRadius = new CornerRadius(4),
            Background = ThemeBrush(BrandActionBrush),
            VerticalAlignment = VerticalAlignment.Center,
        });
        eyebrow.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = ThemeBrush("MutedTextBrush"),
            CharacterSpacing = 50,
        });
        return eyebrow;
    }

    private Border BuildInfoPill(string text) => new()
    {
        Padding = new Thickness(9, 5, 9, 5),
        CornerRadius = new CornerRadius(999),
        Background = ThemeBrush("PremiumOverviewQuietBrush"),
        Child = new TextBlock
        {
            Text = text,
            FontSize = 11,
            Foreground = ThemeBrush("MutedTextBrush"),
        },
    };

    private Grid BuildEmptyFeatureRow(Symbol symbol, string title, string detail)
    {
        var row = new Grid { ColumnSpacing = 11 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var iconTile = new Border
        {
            Width = 34,
            Height = 34,
            CornerRadius = new CornerRadius(10),
            Background = ThemeBrush("PremiumOverviewCardBrush"),
            Child = new SymbolIcon
            {
                Symbol = symbol,
                Foreground = ThemeBrush(BrandActionBrush),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        row.Children.Add(iconTile);

        var copy = new StackPanel
        {
            Spacing = 1,
            VerticalAlignment = VerticalAlignment.Center,
        };
        copy.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = ThemeBrush("TextBrush"),
        });
        copy.Children.Add(new TextBlock
        {
            Text = detail,
            FontSize = 11,
            Foreground = ThemeBrush("MutedTextBrush"),
            TextWrapping = TextWrapping.Wrap,
        });
        Grid.SetColumn(copy, 1);
        row.Children.Add(copy);
        return row;
    }

    private void BuildPremiumDataSections()
    {
        if (MeasureAnchor.Children.Count < 4)
        {
            return;
        }

        _premiumLatencyCard = MeasureAnchor.Children[0] as Border;
        _premiumDistributionCard = MeasureAnchor.Children[1] as Border;
        _premiumModuleCard = MeasureAnchor.Children[2] as Border;
        _premiumInterruptMapCard = MeasureAnchor.Children[3] as Border;

        if (_premiumModuleCard is null || _premiumInterruptMapCard is null)
        {
            return;
        }

        MeasureAnchor.Children.Remove(_premiumInterruptMapCard);
        MeasureAnchor.Children.Remove(_premiumModuleCard);

        _premiumSecondaryGrid = new Grid
        {
            ColumnSpacing = 14,
            RowSpacing = 14,
        };
        _premiumSecondaryGrid.ColumnDefinitions.Add(new ColumnDefinition());
        _premiumSecondaryGrid.ColumnDefinitions.Add(new ColumnDefinition());
        _premiumSecondaryGrid.Children.Add(_premiumModuleCard);
        _premiumSecondaryGrid.Children.Add(_premiumInterruptMapCard);
        OverviewDataContent.Children.Add(_premiumSecondaryGrid);

        _premiumContextGrid = new Grid
        {
            ColumnSpacing = 14,
            RowSpacing = 14,
        };
        _premiumContextGrid.ColumnDefinitions.Add(new ColumnDefinition());
        _premiumContextGrid.ColumnDefinitions.Add(new ColumnDefinition());
        RebuildPremiumContextCards();
        OverviewDataContent.Children.Add(_premiumContextGrid);
    }

    private void RebuildPremiumContextCards()
    {
        if (_premiumContextGrid is null)
        {
            return;
        }

        _premiumContextGrid.Children.Clear();
        _premiumRecentCard = BuildPremiumRecentSnapshotCard();
        _premiumDeviceCard = BuildPremiumDeviceEvidenceCard();
        _premiumContextGrid.Children.Add(_premiumRecentCard);
        _premiumContextGrid.Children.Add(_premiumDeviceCard);
    }

    private void RestylePremiumOverview()
    {
        CaptureObservationButton.Style = (Style)Application.Current.Resources["PremiumPrimaryButtonStyle"];
        CaptureObservationButton.Background = ThemeBrush(BrandActionBrush);
        OverviewAnchor.MinHeight = 68;
        OverviewAnchor.Margin = new Thickness(0, 2, 0, 2);
        OverviewDataContent.Spacing = 16;

        if (_premiumSystemSummaryCard is not null)
        {
            _premiumSystemSummaryCard.Padding = new Thickness(14, 11, 14, 11);
            _premiumSystemSummaryCard.CornerRadius = new CornerRadius(20);
            _premiumSystemSummaryCard.Background = ThemeBrush("GlassRaisedBrush");
            _premiumSystemSummaryCard.BorderBrush = ThemeBrush("PremiumOverviewDividerBrush");
            _premiumSystemSummaryCard.BorderThickness = new Thickness(1);
        }

        StyleMetricCard(_premiumDpcCard, _premiumDpcAccent, "DpcCategoryBrush");
        StyleMetricCard(_premiumIsrCard, _premiumIsrAccent, "IsrCategoryBrush");
        StyleMetricCard(_premiumCpuCard, _premiumCpuAccent, "CpuCategoryBrush");
        StyleMetricCard(_premiumBaselineCard, _premiumBaselineAccent, "MutedTextBrush");

        StyleChartCard(_premiumLatencyCard, primary: true);
        StyleChartCard(_premiumDistributionCard, primary: false);
        StyleChartCard(_premiumModuleCard, primary: false);
        StyleChartCard(_premiumInterruptMapCard, primary: false);
        StyleContextCard(_premiumRecentCard);
        StyleContextCard(_premiumDeviceCard);
        ApplyCardElevation();

        LatencyProfileChart.Height = 188;
        CpuDistributionChart.Height = 188;
        ModuleContributionChart.Height = 152;
        CpuInterruptMap.Height = 152;

        OverviewEmptyState.Padding = new Thickness(26);
        OverviewEmptyState.CornerRadius = new CornerRadius(20);
        OverviewEmptyState.Background = ThemeBrush("GlassRaisedBrush");
        OverviewEmptyState.BorderBrush = ThemeBrush("PremiumOverviewDividerBrush");
        OverviewEmptyState.BorderThickness = new Thickness(1);

        ApplyPremiumBaselineState();
    }

    private void StyleMetricCard(Border? card, Border? accent, string accentBrushKey)
    {
        if (card is null)
        {
            return;
        }

        card.Padding = new Thickness(16, 14, 16, 15);
        card.CornerRadius = new CornerRadius(20);
        card.Background = ThemeBrush("GlassCardBrush");
        card.BorderBrush = ThemeBrush("PremiumOverviewDividerBrush");
        card.BorderThickness = new Thickness(1);
        card.MinHeight = 112;
        if (accent is not null)
        {
            accent.Background = ThemeBrush(accentBrushKey);
        }
    }

    private void StyleChartCard(Border? card, bool primary)
    {
        if (card is null)
        {
            return;
        }

        card.Padding = new Thickness(18, 16, 18, 18);
        card.CornerRadius = new CornerRadius(20);
        card.Background = ThemeBrush(primary ? "GlassRaisedBrush" : "GlassCardBrush");
        card.BorderBrush = ThemeBrush("PremiumOverviewDividerBrush");
        card.BorderThickness = new Thickness(1);
        card.MinHeight = primary ? 252 : 226;
    }

    private void StyleContextCard(Border? card)
    {
        if (card is null)
        {
            return;
        }

        card.Padding = new Thickness(17);
        card.CornerRadius = new CornerRadius(20);
        card.Background = ThemeBrush("GlassCardBrush");
        card.BorderBrush = ThemeBrush("PremiumOverviewDividerBrush");
        card.BorderThickness = new Thickness(1);
    }

    private void ApplyCardElevation()
    {
        CardElevation.Apply(_premiumSystemSummaryCard);
        CardElevation.Apply(_premiumDpcCard);
        CardElevation.Apply(_premiumIsrCard);
        CardElevation.Apply(_premiumCpuCard);
        CardElevation.Apply(_premiumBaselineCard);
        CardElevation.Apply(_premiumLatencyCard);
        CardElevation.Apply(_premiumDistributionCard);
        CardElevation.Apply(_premiumModuleCard);
        CardElevation.Apply(_premiumInterruptMapCard);
        CardElevation.Apply(_premiumRecentCard);
        CardElevation.Apply(_premiumDeviceCard);
    }

    private Border BuildPremiumRecentSnapshotCard()
    {
        var card = CreatePremiumCard();
        var root = new StackPanel { Spacing = 11 };
        card.Child = root;

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        title.Children.Add(new SymbolIcon
        {
            Symbol = Symbol.Clock,
            Foreground = ThemeBrush(BrandActionBrush),
            VerticalAlignment = VerticalAlignment.Center,
        });
        title.Children.Add(new TextBlock
        {
            Text = "Recent snapshot",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = ThemeBrush("TextBrush"),
            VerticalAlignment = VerticalAlignment.Center,
        });
        header.Children.Add(title);
        var evidenceLink = BuildPremiumLink("View evidence", EvidenceNavItem);
        Grid.SetColumn(evidenceLink, 1);
        header.Children.Add(evidenceLink);
        root.Children.Add(header);

        _premiumRecentStatusText = new TextBlock
        {
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
        };
        _premiumRecentStatusPill = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(8, 4, 8, 4),
            CornerRadius = new CornerRadius(999),
            Child = _premiumRecentStatusText,
        };
        root.Children.Add(_premiumRecentStatusPill);
        _premiumRecentSummaryText = new TextBlock
        {
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = ThemeBrush("MutedTextBrush"),
            MaxLines = 3,
        };
        root.Children.Add(_premiumRecentSummaryText);
        return card;
    }

    private Border BuildPremiumDeviceEvidenceCard()
    {
        var card = CreatePremiumCard();
        var root = new StackPanel { Spacing = 9 };
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
        var row = new Grid { ColumnSpacing = 10, MinHeight = 31 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        statusDot = new Border
        {
            Width = 8,
            Height = 8,
            CornerRadius = new CornerRadius(4),
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
        Padding = new Thickness(17),
        CornerRadius = new CornerRadius(20),
        Background = ThemeBrush("GlassCardBrush"),
        BorderBrush = ThemeBrush("PremiumOverviewDividerBrush"),
        BorderThickness = new Thickness(1),
    };

    private Button BuildPremiumLink(string text, NavigationViewItem target)
    {
        var button = new Button
        {
            Content = text,
            Style = (Style)Application.Current.Resources["PremiumLinkButtonStyle"],
        };
        button.Click += (_, _) => AppNavigationView.SelectedItem = target;
        AutomationProperties.SetName(button, text);
        return button;
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
            failure ? "SemanticFailureSoftBrush" : attention ? "SemanticAttentionSoftBrush" : good ? "SemanticGoodSoftBrush" : "PremiumOverviewQuietBrush");
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
        if (_premiumBaselineCard is null)
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

        var foreground = failure ? SemanticFailureBrush : attention ? SemanticAttentionBrush : good ? SemanticGoodBrush : "MutedTextBrush";
        var soft = failure ? "SemanticFailureSoftBrush" : attention ? "SemanticAttentionSoftBrush" : good ? "SemanticGoodSoftBrush" : "PremiumOverviewQuietBrush";

        _premiumBaselineCard.Background = ThemeBrush("GlassCardBrush");
        _premiumBaselineCard.BorderBrush = ThemeBrush("PremiumOverviewDividerBrush");
        _premiumBaselineCard.BorderThickness = new Thickness(1);
        if (_premiumBaselineAccent is not null)
        {
            _premiumBaselineAccent.Background = ThemeBrush(foreground);
        }
        BaselineSummaryText.Foreground = ThemeBrush(foreground == "MutedTextBrush" ? "TextBrush" : foreground);
        if (BaselineSummaryIcon.Parent is Border iconTile)
        {
            iconTile.Background = ThemeBrush(soft);
            BaselineSummaryIcon.Foreground = ThemeBrush(foreground);
        }
    }

    private void ReflowPremiumOverview()
    {
        var width = Math.Max(0d, OverviewContent.ActualWidth);
        if (width <= 0d)
        {
            return;
        }

        ReflowCards(SummaryGrid, width >= 1080d ? 4 : width >= 620d ? 2 : 1);

        var heroColumns = width >= 880d ? 2 : 1;
        ReflowCards(MeasureAnchor, heroColumns);
        if (MeasureAnchor.ColumnDefinitions.Count >= 2)
        {
            MeasureAnchor.ColumnDefinitions[0].Width = new GridLength(1.58, GridUnitType.Star);
            MeasureAnchor.ColumnDefinitions[1].Width = heroColumns == 2
                ? new GridLength(1, GridUnitType.Star)
                : new GridLength(0);
        }

        ReflowPremiumGrid(_premiumSecondaryGrid, width >= 880d ? 2 : 1, 1.18);
        ReflowPremiumGrid(_premiumContextGrid, width >= 760d ? 2 : 1, 1.08);
        ReflowPremiumEmptyState(width >= 900d);
    }

    private static void ReflowPremiumGrid(Grid? grid, int columns, double firstColumnWeight)
    {
        if (grid is null)
        {
            return;
        }

        while (grid.ColumnDefinitions.Count < 2)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition());
        }

        grid.ColumnDefinitions[0].Width = columns == 2
            ? new GridLength(firstColumnWeight, GridUnitType.Star)
            : new GridLength(1, GridUnitType.Star);
        grid.ColumnDefinitions[1].Width = columns == 2
            ? new GridLength(1, GridUnitType.Star)
            : new GridLength(0);

        var rows = (grid.Children.Count + columns - 1) / columns;
        grid.RowDefinitions.Clear();
        for (var index = 0; index < rows; index++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        for (var index = 0; index < grid.Children.Count; index++)
        {
            if (grid.Children[index] is FrameworkElement child)
            {
                Grid.SetColumn(child, index % columns);
                Grid.SetRow(child, index / columns);
            }
        }
    }

    private void ReflowPremiumEmptyState(bool split)
    {
        if (_premiumEmptyStateGrid is null || _premiumEmptyStateGrid.Children.Count < 2)
        {
            return;
        }

        _premiumEmptyStateGrid.ColumnDefinitions[0].Width = new GridLength(split ? 1.2 : 1, GridUnitType.Star);
        _premiumEmptyStateGrid.ColumnDefinitions[1].Width = split
            ? new GridLength(0.8, GridUnitType.Star)
            : new GridLength(0);

        if (_premiumEmptyStateGrid.Children[0] is not FrameworkElement copy ||
            _premiumEmptyStateGrid.Children[1] is not FrameworkElement preview)
        {
            return;
        }

        Grid.SetColumn(copy, 0);
        Grid.SetRow(copy, 0);
        Grid.SetColumn(preview, split ? 1 : 0);
        Grid.SetRow(preview, split ? 0 : 1);
    }
}
