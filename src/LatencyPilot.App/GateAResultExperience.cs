using System.ComponentModel;
using System.Diagnostics;
using LatencyPilot.App.Controls;
using LatencyPilot.Benchmarking.Optimization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;

namespace LatencyPilot.App;

public sealed partial class MainWindow
{
    private FrameworkElement? _gateAResultRoot;

    internal void RenderGateAResult(GateAResultViewModel result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var overviewStack = OverviewContent.Children
            .OfType<StackPanel>()
            .FirstOrDefault()
            ?? throw new InvalidOperationException("Overview result host is unavailable.");
        if (_gateAResultRoot is not null)
        {
            overviewStack.Children.Remove(_gateAResultRoot);
        }

        var root = new StackPanel { Spacing = ResourceDouble("DashboardGap", 16d) };
        AutomationProperties.SetName(root, "GPU optimization result");
        AutomationProperties.SetHelpText(root, result.Summary);

        root.Children.Add(BuildGateAResultHero(result));
        var metricGrid = BuildGateAMetricGrid(result);
        root.Children.Add(metricGrid);
        var chartsGrid = BuildGateAChartsGrid(result);
        root.Children.Add(chartsGrid);
        root.Children.Add(BuildGateADecisionEvidence(result));
        root.Children.Add(BuildGateAEvidenceActions(result));
        root.SizeChanged += (_, _) =>
        {
            UpdateGateAMetricGridLayout(metricGrid, root.ActualWidth);
            UpdateGateAChartsGridLayout(chartsGrid, root.ActualWidth);
        };

        overviewStack.Children.Insert(Math.Min(1, overviewStack.Children.Count), root);
        _gateAResultRoot = root;
        UpdateGateAMetricGridLayout(metricGrid, OverviewContent.ActualWidth);
        UpdateGateAChartsGridLayout(chartsGrid, OverviewContent.ActualWidth);

        AppNavigationView.SelectedItem = OverviewNavItem;
        OverviewNavItem.IsSelected = true;
        OverviewView.ChangeView(null, 0d, null, disableAnimation: true);
    }

    private Border BuildGateAResultHero(GateAResultViewModel result)
    {
        var card = StyledBorder("ChartCardStyle");
        var stack = new StackPanel { Spacing = 10d };
        var eyebrowRow = new Grid { ColumnSpacing = 10d };
        eyebrowRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1d, GridUnitType.Star) });
        eyebrowRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        eyebrowRow.Children.Add(new TextBlock
        {
            Text = "GPU OPTIMIZATION RESULT",
            Style = AppStyle("HeroEyebrowTextStyle"),
            VerticalAlignment = VerticalAlignment.Center,
        });

        var eligibilityBrush = result.GateAClosureEligible ? "SemanticGoodBrush" : "SemanticAttentionBrush";
        var eligibilitySoftBrush = result.GateAClosureEligible ? "SemanticGoodSoftBrush" : "SemanticAttentionSoftBrush";
        var eligibility = new Border
        {
            Padding = new Thickness(10d, 5d, 10d, 5d),
            CornerRadius = new CornerRadius(999d),
            BorderThickness = new Thickness(1d),
            BorderBrush = DashboardThemeResources.Brush(card, eligibilityBrush),
            Background = DashboardThemeResources.Brush(card, eligibilitySoftBrush),
            Child = new TextBlock
            {
                Text = result.EligibilityLabel,
                FontSize = 11d,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = DashboardThemeResources.Brush(card, eligibilityBrush),
            },
        };
        Grid.SetColumn(eligibility, 1);
        eyebrowRow.Children.Add(eligibility);
        stack.Children.Add(eyebrowRow);
        stack.Children.Add(new TextBlock
        {
            Text = result.Title,
            Style = AppStyle("HeroTitleTextStyle"),
            TextWrapping = TextWrapping.Wrap,
        });
        stack.Children.Add(new TextBlock
        {
            Text = result.Summary,
            Style = AppStyle("MutedBodyTextStyle"),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 920d,
        });
        stack.Children.Add(new TextBlock
        {
            Text = $"{ShortRevision(result.SourceRevision)} · {FormatDuration(result.Duration)} · {result.ComparedCandidateLabel}",
            Style = AppStyle("CaptionTextStyle"),
            TextWrapping = TextWrapping.Wrap,
        });

        if (!result.BundleAvailable)
        {
            stack.Children.Add(new Border
            {
                Padding = new Thickness(10d, 8d, 10d, 8d),
                CornerRadius = new CornerRadius(10d),
                Background = DashboardThemeResources.Brush(card, "SemanticAttentionSoftBrush"),
                Child = new TextBlock
                {
                    Text = result.BundleStatus,
                    FontSize = 11d,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = DashboardThemeResources.Brush(card, "SemanticAttentionBrush"),
                },
            });
        }

        card.Child = stack;
        CardElevation.Apply(card);
        return card;
    }

    private Grid BuildGateAMetricGrid(GateAResultViewModel result)
    {
        var grid = new Grid { ColumnSpacing = 12d, RowSpacing = 12d };
        foreach (var metric in result.Metrics)
        {
            var card = StyledBorder("DashboardMetricStyle");
            var content = new StackPanel { Spacing = 7d };
            content.Children.Add(new TextBlock
            {
                Text = metric.Label,
                Style = AppStyle("MetricLabelTextStyle"),
            });
            content.Children.Add(new TextBlock
            {
                Text = FormatMetricComparison(metric),
                Style = AppStyle("MetricValueTextStyle"),
                TextWrapping = TextWrapping.Wrap,
            });
            content.Children.Add(new TextBlock
            {
                Text = MetricStateLabel(metric.State),
                FontSize = 11d,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = MetricStateBrush(card, metric.State),
                TextWrapping = TextWrapping.Wrap,
            });
            content.Children.Add(new TextBlock
            {
                Text = metric.ImprovementFraction is { } delta
                    ? $"{delta:+0.0%;-0.0%;0.0%} improvement-direction delta · local uncertainty {metric.UncertaintyFraction:P1}"
                    : "No comparable decision aggregate is available.",
                Style = AppStyle("CaptionTextStyle"),
                TextWrapping = TextWrapping.Wrap,
            });
            card.Child = content;
            grid.Children.Add(card);
            CardElevation.Apply(card);
        }
        return grid;
    }

    private Grid BuildGateAChartsGrid(GateAResultViewModel result)
    {
        var candidateChart = new GpuCandidateComparisonChart();
        var originalLow1 = result.Metrics.FirstOrDefault(static metric => metric.Key == "low1")?.OriginalValue;
        candidateChart.SetData(
            result.Candidates,
            originalLow1,
            $"{result.Candidates.Count} GPU affinity candidates. {result.ComparedCandidateLabel}. Original reference {FormatNumber(originalLow1, "0.0")} FPS 1% low.");

        var trialChart = new GateATrialHistoryChart();
        var originalTrials = result.Trials.Count(static point =>
            string.Equals(point.Series, "Original", StringComparison.OrdinalIgnoreCase));
        trialChart.SetData(
            result.Trials,
            $"Scored 1% low history contains {originalTrials} Original point(s) and {result.Trials.Count - originalTrials} comparison-candidate point(s). Warm-ups are excluded.");

        var grid = new Grid { ColumnSpacing = 12d, RowSpacing = 12d };
        grid.Children.Add(BuildChartCard(
            "Candidate comparison",
            "Decision aggregates from the optimizer; Original is a reference, not a synthetic CPU candidate.",
            candidateChart));
        grid.Children.Add(BuildChartCard(
            "Repeatability",
            "Scored 1% low FPS in run order. Non-ready observations stay visible rather than being silently erased.",
            trialChart));
        return grid;
    }

    private Border BuildChartCard(string title, string subtitle, FrameworkElement chart)
    {
        var card = StyledBorder("ChartCardStyle");
        var stack = new StackPanel { Spacing = 10d };
        stack.Children.Add(new TextBlock { Text = title, Style = AppStyle("SubsectionTitleTextStyle") });
        stack.Children.Add(new TextBlock
        {
            Text = subtitle,
            Style = AppStyle("CaptionTextStyle"),
            TextWrapping = TextWrapping.Wrap,
        });
        stack.Children.Add(chart);
        card.Child = stack;
        CardElevation.Apply(card);
        return card;
    }

    private Border BuildGateADecisionEvidence(GateAResultViewModel result)
    {
        var card = StyledBorder("ChartCardStyle");
        var stack = new StackPanel { Spacing = 12d };
        stack.Children.Add(new TextBlock { Text = "Why this decision", Style = AppStyle("SubsectionTitleTextStyle") });
        stack.Children.Add(new TextBlock
        {
            Text = "These rows render report decision facts; the UI does not calculate another winner.",
            Style = AppStyle("CaptionTextStyle"),
            TextWrapping = TextWrapping.Wrap,
        });

        foreach (var row in result.DecisionEvidence)
        {
            var rowBorder = new Border
            {
                Padding = new Thickness(12d, 10d, 12d, 10d),
                CornerRadius = new CornerRadius(10d),
                Background = DashboardThemeResources.Brush(card, "SurfaceAltBrush"),
            };
            var rowGrid = new Grid { ColumnSpacing = 12d };
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160d) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110d) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1d, GridUnitType.Star) });
            rowGrid.Children.Add(new TextBlock
            {
                Text = row.Label,
                FontSize = 12d,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = DashboardThemeResources.Brush(card, "TextBrush"),
                TextWrapping = TextWrapping.Wrap,
            });
            var state = new TextBlock
            {
                Text = row.State,
                FontSize = 11d,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = DecisionStateBrush(card, row.State),
                TextWrapping = TextWrapping.Wrap,
            };
            Grid.SetColumn(state, 1);
            rowGrid.Children.Add(state);
            var detail = new TextBlock
            {
                Text = row.Detail,
                FontSize = 11d,
                Foreground = DashboardThemeResources.Brush(card, "MutedTextBrush"),
                TextWrapping = TextWrapping.Wrap,
            };
            Grid.SetColumn(detail, 2);
            rowGrid.Children.Add(detail);
            rowBorder.Child = rowGrid;
            stack.Children.Add(rowBorder);
        }

        card.Child = stack;
        CardElevation.Apply(card);
        return card;
    }

    private Border BuildGateAEvidenceActions(GateAResultViewModel result)
    {
        var card = StyledBorder("ChartCardStyle");
        var stack = new StackPanel { Spacing = 10d };
        stack.Children.Add(new TextBlock { Text = "Evidence", Style = AppStyle("SubsectionTitleTextStyle") });
        stack.Children.Add(new TextBlock
        {
            Text = result.BundleStatus,
            Style = AppStyle("CaptionTextStyle"),
            TextWrapping = TextWrapping.Wrap,
        });

        var actions = new Grid { ColumnSpacing = 8d, RowSpacing = 8d };
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1d, GridUnitType.Star) });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1d, GridUnitType.Star) });
        actions.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        actions.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var status = new TextBlock { Style = AppStyle("CaptionTextStyle"), TextWrapping = TextWrapping.Wrap };

        var openZip = ActionButton("Open ZIP", primary: true);
        openZip.IsEnabled = result.BundleAvailable;
        openZip.Click += (_, _) => OpenGateAEvidencePath(result.ZipPath, status, "ZIP");
        actions.Children.Add(openZip);

        var copyZip = ActionButton("Copy ZIP path");
        copyZip.IsEnabled = result.BundleAvailable;
        copyZip.Click += (_, _) => CopyGateAEvidencePath(result.ZipPath, status);
        Grid.SetColumn(copyZip, 1);
        actions.Children.Add(copyZip);

        var openFolder = ActionButton("Open session folder");
        openFolder.Click += (_, _) => OpenGateAEvidencePath(result.SessionDirectory, status, "session folder");
        Grid.SetRow(openFolder, 1);
        actions.Children.Add(openFolder);

        var openReport = ActionButton("Open raw report");
        openReport.Click += (_, _) => OpenGateAEvidencePath(result.ReportPath, status, "raw report");
        Grid.SetRow(openReport, 1);
        Grid.SetColumn(openReport, 1);
        actions.Children.Add(openReport);

        stack.Children.Add(actions);
        stack.Children.Add(status);
        card.Child = stack;
        CardElevation.Apply(card);
        return card;
    }

    private Button ActionButton(string text, bool primary = false)
    {
        var button = new Button
        {
            Content = text,
            MinHeight = 34d,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Style = AppStyle(primary ? "PrimaryButtonStyle" : "SecondaryButtonStyle"),
        };
        AutomationProperties.SetName(button, text);
        return button;
    }

    private static void UpdateGateAMetricGridLayout(Grid grid, double width)
    {
        var columnCount = width >= 1050d ? 4 : width >= 620d ? 2 : 1;
        grid.ColumnDefinitions.Clear();
        grid.RowDefinitions.Clear();
        for (var column = 0; column < columnCount; column++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1d, GridUnitType.Star) });
        }
        var rowCount = Math.Max(1, (grid.Children.Count + columnCount - 1) / columnCount);
        for (var row = 0; row < rowCount; row++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }
        for (var index = 0; index < grid.Children.Count; index++)
        {
            if (grid.Children[index] is not FrameworkElement child)
            {
                continue;
            }
            Grid.SetColumn(child, index % columnCount);
            Grid.SetRow(child, index / columnCount);
        }
    }

    private static void UpdateGateAChartsGridLayout(Grid grid, double width)
    {
        var sideBySide = width >= 900d;
        grid.ColumnDefinitions.Clear();
        grid.RowDefinitions.Clear();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1d, GridUnitType.Star) });
        if (sideBySide)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1d, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }
        else
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }
        for (var index = 0; index < grid.Children.Count; index++)
        {
            if (grid.Children[index] is not FrameworkElement child)
            {
                continue;
            }
            Grid.SetColumn(child, sideBySide ? index : 0);
            Grid.SetRow(child, sideBySide ? 0 : index);
        }
    }

    private static string FormatMetricComparison(GateAMetricComparison metric)
    {
        var format = metric.Unit == "ms" ? "0.00" : "0.0";
        return $"{FormatNumber(metric.OriginalValue, format)} → {FormatNumber(metric.CandidateValue, format)} {metric.Unit}";
    }

    private static string MetricStateLabel(GateAMetricState state) => state switch
    {
        GateAMetricState.Improved => "Improved",
        GateAMetricState.DecisionGuardrailSatisfied => "Inside decision guardrail",
        GateAMetricState.DiagnosticOnly => "Diagnostic only",
        _ => "Not enough comparable evidence",
    };

    private static Brush MetricStateBrush(FrameworkElement owner, GateAMetricState state) =>
        DashboardThemeResources.Brush(owner, state switch
        {
            GateAMetricState.Improved or GateAMetricState.DecisionGuardrailSatisfied => "SemanticGoodBrush",
            GateAMetricState.DiagnosticOnly => "SemanticAttentionBrush",
            _ => "MutedTextBrush",
        });

    private static Brush DecisionStateBrush(FrameworkElement owner, string state) =>
        DashboardThemeResources.Brush(owner,
            string.Equals(state, "Passed", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(state, "Decision-grade", StringComparison.OrdinalIgnoreCase)
                ? "SemanticGoodBrush"
                : string.Equals(state, "Needs attention", StringComparison.OrdinalIgnoreCase) ||
                  string.Equals(state, "Blocked", StringComparison.OrdinalIgnoreCase)
                    ? "SemanticFailureBrush"
                    : string.Equals(state, "Diagnostic only", StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(state, "Unavailable", StringComparison.OrdinalIgnoreCase)
                        ? "SemanticAttentionBrush"
                        : "MutedTextBrush");

    private static void OpenGateAEvidencePath(string? path, TextBlock status, string label)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            status.Text = $"{label} is unavailable for this result.";
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            status.Text = $"Opened {label}.";
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            Logger.Warning(exception, "Gate A evidence path could not be opened: {EvidencePath}", path);
            status.Text = $"Could not open {label}: {exception.Message}";
        }
    }

    private static void CopyGateAEvidencePath(string? path, TextBlock status)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            status.Text = "ZIP path is unavailable for this result.";
            return;
        }
        try
        {
            var package = new DataPackage();
            package.SetText(path);
            Clipboard.SetContent(package);
            Clipboard.Flush();
            status.Text = "ZIP path copied.";
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Gate A ZIP path could not be copied to the clipboard.");
            status.Text = $"Could not copy ZIP path: {exception.Message}";
        }
    }

    private Border StyledBorder(string styleKey) => new() { Style = AppStyle(styleKey) };

    private static Style AppStyle(string key)
    {
        if (Application.Current.Resources.TryGetValue(key, out var value) && value is Style style)
        {
            return style;
        }
        throw new InvalidOperationException($"Application style '{key}' is unavailable.");
    }

    private static double ResourceDouble(string key, double fallback) =>
        Application.Current.Resources.TryGetValue(key, out var value) && value is double number
            ? number
            : fallback;

    private static string FormatNumber(double? value, string format) =>
        value is { } number && double.IsFinite(number)
            ? number.ToString(format, System.Globalization.CultureInfo.InvariantCulture)
            : "—";

    private static string ShortRevision(string revision) => revision.Length >= 12 ? revision[..12] : revision;

    private static string FormatDuration(TimeSpan duration) =>
        duration.TotalHours >= 1d
            ? $"{(int)duration.TotalHours}:{duration.Minutes:00}:{duration.Seconds:00}"
            : $"{duration.Minutes}:{duration.Seconds:00}";
}