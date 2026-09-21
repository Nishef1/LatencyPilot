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

        var root = new StackPanel
        {
            Spacing = ResourceDouble("DashboardGap", 16d),
        };
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
        var eyebrow = new TextBlock
        {
            Text = "GPU OPTIMIZATION RESULT",
            Style = AppStyle("HeroEyebrowTextStyle"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        eyebrowRow.Children.Add(eyebrow);
        var eligibility = new Border
        {
            Grid = { },
            Padding = new Thickness(10d, 5d, 10d, 5d),
            CornerRadius = new CornerRadius(999d),
            BorderThickness = new Thickness(1d),
            BorderBrush = DashboardThemeResources.Brush(card, result.GateAClosureEligible ? "SemanticGoodBrush" : "SemanticAttentionBrush"),
            Background = DashboardThemeResources.Brush(card, result.GateAClosureEligible ? "SemanticGoodSoftBrush" : "SemanticAttentionSoftBrush"),
            Child = new TextBlock
            {
                Text = result.EligibilityLabel,
                FontSize = 11d,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = DashboardThemeResources.Brush(card, result.GateAClosureEligible ? "SemanticGoodBrush" : "SemanticAttentionBrush"),
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
            var bundleWarning = new Border
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
            };
            stack.Children.Add(bundleWarning);
        }

        card.Child = stack;
        CardElevation.Apply(card);
        return card;
    }

    private Grid BuildGateAMetricGrid(GateAResultViewModel result)
    {
        var grid = new Grid
        {
            ColumnSpacing = 12d,
            RowSpacing = 12d,
            Tag = result.Metrics.Count,
        };
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
        var candidateTrials = result.Trials.Count - originalTrials;
        trialChart.SetData(
            result.Trials,
            $"Scored 1% low history contains {originalTrials} Original point(s) and {candidateTrials} comparison-candidate point(s). Warm-ups are excluded.");

        var grid = new Grid { ColumnSpacing = 12d, RowSpacing = 12d };
        var candidateCard = BuildChartCard(
            "Candidate comparison",
            "Decision aggregates from the optimizer; Original is shown as a reference, not a fake CPU candidate.",
            candidateChart);
        var repeatabilityCard = BuildChartCard(
            "Repeatability",
            "Scored 1% low FPS in run order. Non-ready observations remain visible instead of being silently discarded.",
            trialChart);
        grid.Children.Add(candidateCard);
        grid.Children.Add(repeatabilityCard);
        return grid;
    }

    private Border BuildChartCard(string title, string subtitle, FrameworkElement chart)
    {
        var card = StyledBorder("ChartCardStyle");
        var stack = new StackPanel { Spacing = 10d };
        stack.Children.Add(new TextBlock
        {
            Text = title,
            Style = AppStyle("SubsectionTitleTextStyle"),
        });
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
        stack.Children.Add(new TextBlock
        {
            Text = "Why this decision",
            Style = AppStyle("SubsectionTitleTextStyle"),
        });
        stack.Children.Add(new TextBlock
        {
            Text = "These rows render the report's decision facts; the UI does not calculate another winner.",
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
            var label = new TextBlock
            {
                Text = row.Label,
                FontSize = 12d,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = DashboardThemeResources.Brush(card, "TextBrush"),
                TextWrapping = TextWrapping.Wrap,
            };
            rowGrid.Children.Add(label);
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
        stack.Children.Add(new TextBlock
        {
            Text = "Evidence",
            Style = AppStyle("SubsectionTitleTextStyle"),
        });
        stack.Children.Add(new TextBlock
        {
            Text = result.BundleStatus,
            Style = AppStyle("CaptionTextStyle"),
            TextWrapping = TextWrapping.Wrap,
        });

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8d,
        };
        var status = new TextBlock
        {
            Style = AppStyle("CaptionTextStyle"),
            TextWrapping = TextWrapping.Wrap,
        };

        var openZip = ActionButton("Open ZIP", primary: true);
        openZip.IsEnabled = result.BundleAvailable;
        openZip.Click += (_, _) => OpenGateAEvidencePath(result.ZipPath, status, "ZIP");
        actions.Children.Add(openZip);

        var copyZip = ActionButton("Copy ZIP path");
        copyZip.IsEnabled = result.BundleAvailable;
        copyZip.Click += (_, _) => CopyGateAEvidencePath(result.ZipPath, status);
        actions.Children.Add(copyZip);

        var openFolder = ActionButton("Open session folder");
        openFolder.Click += (_, _) => OpenGateAEvidencePath(result.SessionDirectory, status, "session folder");
        actions.Children.Add(openFolder);

        var openReport = ActionButton("Open raw report");
        openReport.Click += (_, _) => OpenGateAEvidencePath(result.ReportPath, status, "raw report");
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
            Grid.SetColumn(grid.Children[index], index % columnCount);
            Grid.SetRow(grid.Children[index], index / columnCount);
        }
    }

    private static void UpdateGateAChartsGridLayout(Grid grid, double width)
    {
        var sideBySide = width >= 900d;
        grid.ColumnDefinitions.Clear();
        grid.RowDefinitions.Clear();
        if (sideBySide)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1d, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1d, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            if (grid.Children.Count > 0)
            {
                Grid.SetColumn(grid.Children[0], 0);
                Grid.SetRow(grid.Children[0], 0);
            }
            if (grid.Children.Count > 1)
            {
                Grid.SetColumn(grid.Children[1], 1);
                Grid.SetRow(grid.Children[1], 0);
            }
            return;
        }

        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1d, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var index = 0; index < grid.Children.Count; index++)
        {
            Grid.SetColumn(grid.Children[index], 0);
            Grid.SetRow(grid.Children[index], index);
        }
    }

    private static string FormatMetricComparison(GateAMetricComparison metric)
    {
        var original = FormatNumber(metric.OriginalValue, metric.Unit == "ms" ? "0.00" : "0.0");
        var candidate = FormatNumber(metric.CandidateValue, metric.Unit == "ms" ? "0.00" : "0.0");
        return $"{original} → {candidate} {metric.Unit}";
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
            GateAMetricState.Improved => "SemanticGoodBrush",
            GateAMetricState.DecisionGuardrailSatisfied => "SemanticGoodBrush",
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
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            });
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

    private Border StyledBorder(string styleKey) =>
        new() { Style = AppStyle(styleKey) };

    private static Style AppStyle(string key) =>
        (Style)(Application.Current.Resources[key]
            ?? throw new InvalidOperationException($"Application style '{key}' is unavailable."));

    private static double ResourceDouble(string key, double fallback) =>
        Application.Current.Resources.TryGetValue(key, out var value) && value is double number
            ? number
            : fallback;

    private static string FormatNumber(double? value, string format) =>
        value is { } number && double.IsFinite(number)
            ? number.ToString(format, System.Globalization.CultureInfo.InvariantCulture)
            : "—";

    private static string ShortRevision(string revision) =>
        revision.Length >= 12 ? revision[..12] : revision;

    private static string FormatDuration(TimeSpan duration) =>
        duration.TotalHours >= 1d
            ? $"{(int)duration.TotalHours}:{duration.Minutes:00}:{duration.Seconds:00}"
            : $"{duration.Minutes}:{duration.Seconds:00}";
}
