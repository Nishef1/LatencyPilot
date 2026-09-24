using System.ComponentModel;
using System.Diagnostics;
using LatencyPilot.App.Controls;
using LatencyPilot.Benchmarking.Optimization;
using LatencyPilot.Core.Benchmarking;
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
        root.Children.Add(BuildGateAPairEvidence(result));
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

    private static Border BuildGateAResultHero(GateAResultViewModel result)
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

        var badges = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8d,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        badges.Children.Add(BuildStatusBadge(
            card,
            result.EligibilityLabel,
            result.GateAClosureEligible ? "ChartAccentPrimaryBrush" : "SemanticAttentionBrush",
            result.GateAClosureEligible ? "BrandActionSoftBrush" : "SemanticAttentionSoftBrush"));
        if (!string.Equals(result.SelectionConfidence, "Unavailable", StringComparison.OrdinalIgnoreCase))
        {
            var confidenceIsHigh = string.Equals(result.SelectionConfidence, "High", StringComparison.OrdinalIgnoreCase);
            badges.Children.Add(BuildStatusBadge(
                card,
                $"Confidence: {result.SelectionConfidence}",
                confidenceIsHigh ? "SemanticGoodBrush" : "SemanticAttentionBrush",
                confidenceIsHigh ? "SemanticGoodSoftBrush" : "SemanticAttentionSoftBrush"));
        }
        badges.Children.Add(BuildStatusBadge(
            card,
            result.TerminalStateLabel,
            result.TerminalStateVerified ? "SemanticGoodBrush" : "SemanticFailureBrush",
            result.TerminalStateVerified ? "SemanticGoodSoftBrush" : "SemanticFailureSoftBrush"));
        Grid.SetColumn(badges, 1);
        eyebrowRow.Children.Add(badges);

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

    private static Border BuildStatusBadge(
        FrameworkElement owner,
        string text,
        string foregroundBrush,
        string backgroundBrush) =>
        new()
        {
            Padding = new Thickness(10d, 5d, 10d, 5d),
            CornerRadius = new CornerRadius(999d),
            BorderThickness = new Thickness(1d),
            BorderBrush = DashboardThemeResources.Brush(owner, foregroundBrush),
            Background = DashboardThemeResources.Brush(owner, backgroundBrush),
            Child = new TextBlock
            {
                Text = text,
                FontSize = 11d,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = DashboardThemeResources.Brush(owner, foregroundBrush),
            },
        };

    private static Grid BuildGateAMetricGrid(GateAResultViewModel result)
    {
        var grid = new Grid { ColumnSpacing = 12d, RowSpacing = 12d };
        if (result.OriginalOnlyResult)
        {
            foreach (var metric in result.OriginalMetricSummaries)
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
                    Text = $"{metric.Median:0.0} {metric.Unit}",
                    Style = AppStyle("MetricValueTextStyle"),
                    TextWrapping = TextWrapping.Wrap,
                });
                content.Children.Add(new TextBlock
                {
                    Text = result.BaselineQualificationFailed
                        ? "Unstable baseline"
                        : result.OriginalEvidenceRepeatable
                            ? "Repeatable diagnostic"
                            : "Unstable diagnostic",
                    FontSize = 11d,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = DashboardThemeResources.Brush(
                        card,
                        result.OriginalEvidenceRepeatable && !result.BaselineQualificationFailed
                            ? "SemanticGoodBrush"
                            : "SemanticAttentionBrush"),
                    TextWrapping = TextWrapping.Wrap,
                });
                content.Children.Add(new TextBlock
                {
                    Text = $"Scored Original range {metric.Minimum:0.0}–{metric.Maximum:0.0} {metric.Unit}. Raw observations only; not a candidate comparison.",
                    Style = AppStyle("CaptionTextStyle"),
                    TextWrapping = TextWrapping.Wrap,
                });
                card.Child = content;
                grid.Children.Add(card);
                CardElevation.Apply(card);
            }

            return grid;
        }

        foreach (var metric in result.Metrics)
        {
            var card = StyledBorder("DashboardMetricStyle");
            var content = new StackPanel { Spacing = 7d };
            content.Children.Add(new TextBlock { Text = metric.Label, Style = AppStyle("MetricLabelTextStyle") });
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
                Text = metric.ImprovementFraction is not null
                    ? $"Median paired comparison · noise / uncertainty guide {metric.UncertaintyFraction:P1} · confidence {result.SelectionConfidence}"
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

    private static Grid BuildGateAChartsGrid(GateAResultViewModel result)
    {
        var grid = new Grid { ColumnSpacing = 12d, RowSpacing = 12d };
        if (!result.OriginalOnlyResult)
        {
            var measuredProcessors = result.Pairs.Select(static pair => pair.Processor).Distinct().Count();
            var confirmed = result.Candidates.Where(static candidate => candidate.IsFinalistEvidence).ToArray();
            var screeningOnly = result.Candidates.Where(static candidate => !candidate.IsFinalistEvidence).ToArray();
            var candidateSections = new StackPanel { Spacing = 14d };

            if (confirmed.Length > 0)
            {
                candidateSections.Children.Add(new TextBlock
                {
                    Text = "Final confirmed",
                    Style = AppStyle("CaptionTextStyle"),
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                });
                var confirmedChart = new GpuCandidateComparisonChart();
                confirmedChart.SetData(
                    confirmed,
                    null,
                    $"{confirmed.Length} finalist CPU(s) with repeated 15-second confirmation. {result.ComparedCandidateLabel}.",
                    null);
                candidateSections.Children.Add(confirmedChart);
            }

            if (screeningOnly.Length > 0)
            {
                candidateSections.Children.Add(new TextBlock
                {
                    Text = "Screening only · not directly comparable to finalist medians",
                    Style = AppStyle("CaptionTextStyle"),
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                });
                var screeningChart = new GpuCandidateComparisonChart();
                screeningChart.SetData(
                    screeningOnly,
                    null,
                    $"{screeningOnly.Length} CPU(s) show short-screen evidence only. These values are not finalist-confirmed and must not be compared as if they had the same authority.",
                    null);
                candidateSections.Children.Add(screeningChart);
            }

            if (confirmed.Length == 0 && screeningOnly.Length == 0)
            {
                var emptyChart = new GpuCandidateComparisonChart();
                emptyChart.SetData(
                    [],
                    null,
                    $"{measuredProcessors} measured CPU candidate(s), but no decision-grade aggregate is available.",
                    result.Pairs.Count > 0
                        ? "No decision-grade candidate could be charted. Measured local pairs remain available below with their control movement and retry outcomes."
                        : null);
                candidateSections.Children.Add(emptyChart);
            }

            grid.Children.Add(BuildChartCard(
                "Candidate evidence",
                "Finalist medians and short-screen signals are separated so measurements with different authority are never presented as one ranking.",
                candidateSections));
        }

        var trialChart = new GateATrialHistoryChart();
        var originalTrials = result.Trials.Count(static point =>
            string.Equals(point.Series, "Original", StringComparison.OrdinalIgnoreCase));
        var candidateTrials = result.Trials.Count - originalTrials;
        trialChart.SetData(
            result.Trials,
            result.OriginalOnlyResult
                ? result.OriginalEvidenceDetail
                : $"Scored 1% low history contains {originalTrials} Original control point(s) and {candidateTrials} candidate observation(s). Original controls are connected; candidate observations are discrete markers so different CPUs are never presented as one synthetic series.");

        var stabilityTitle = result.OriginalOnlyResult ? "Original stability" : "Control stability";
        var stabilitySubtitle = result.OriginalOnlyResult
            ? result.OriginalEvidenceDetail
            : "Raw scored 1% low FPS in run order. Original controls are connected; candidate measurements are discrete markers, and unstable or inconclusive pairs use the attention state.";
        grid.Children.Add(BuildChartCard(stabilityTitle, stabilitySubtitle, trialChart));
        return grid;
    }

    private static Border BuildChartCard(string title, string subtitle, FrameworkElement chart)
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

    private static Border BuildGateAPairEvidence(GateAResultViewModel result)
    {
        var card = StyledBorder("ChartCardStyle");
        var stack = new StackPanel { Spacing = 10d };

        if (result.OriginalOnlyResult && result.Pairs.Count == 0)
        {
            stack.Children.Add(new TextBlock
            {
                Text = "Candidate comparison unavailable",
                Style = AppStyle("SubsectionTitleTextStyle"),
            });
            stack.Children.Add(new TextBlock
            {
                Text = result.BaselineQualificationFailed
                    ? "Candidate testing was not started because the scored Original evidence was structurally unusable. Ordinary benchmark variability alone does not block v3 ranking. No candidate was mutated, ranked, or retained."
                    : "This Original-only diagnostic intentionally performs no affinity mutation or device restart, so there is no candidate comparison for this run.",
                Style = AppStyle("MutedBodyTextStyle"),
                TextWrapping = TextWrapping.Wrap,
            });
            card.Child = stack;
            CardElevation.Apply(card);
            return card;
        }

        stack.Children.Add(new TextBlock { Text = "Local pair evidence", Style = AppStyle("SubsectionTitleTextStyle") });
        stack.Children.Add(new TextBlock
        {
            Text = "Direct Original → Candidate → Original measurements. The compact header keeps effect, control movement and verdict visible; expand a row for raw FPS and the full reason.",
            Style = AppStyle("CaptionTextStyle"),
            TextWrapping = TextWrapping.Wrap,
        });

        if (result.Finalists.Count > 0)
        {
            stack.Children.Add(new TextBlock
            {
                Text = "Finalist authority",
                FontSize = 12d,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = DashboardThemeResources.Brush(card, "TextBrush"),
                Margin = new Thickness(0d, 4d, 0d, 0d),
            });
            foreach (var finalist in result.Finalists.OrderBy(static item =>
                         item.PairNumbers.Count == 0 ? int.MaxValue : item.PairNumbers.Min()))
            {
                var median = finalist.MedianOnePercentLowEffect is { } effect && double.IsFinite(effect)
                    ? effect.ToString("+0.0%;-0.0%;0.0%", System.Globalization.CultureInfo.InvariantCulture)
                    : "—";
                var pairNumbers = finalist.PairNumbers.Count == 0 ? "none" : string.Join(", ", finalist.PairNumbers);
                stack.Children.Add(new TextBlock
                {
                    Text = $"CPU {finalist.Processor.Number} · median 1% low {median} · effect MAD {(finalist.OnePercentLowEffectMedianAbsoluteDeviation is { } mad ? mad.ToString("P1", System.Globalization.CultureInfo.InvariantCulture) : "—")} · noise guide {(finalist.NoiseFraction is { } noise ? noise.ToString("P1", System.Globalization.CultureInfo.InvariantCulture) : "—")} · {finalist.Verdict} · pairs {pairNumbers}",
                    Style = AppStyle("CaptionTextStyle"),
                    TextWrapping = TextWrapping.Wrap,
                });
            }
        }

        if (result.Pairs.Count == 0)
        {
            stack.Children.Add(new TextBlock
            {
                Text = "No candidate pair was completed in this run.",
                Style = AppStyle("MutedBodyTextStyle"),
                TextWrapping = TextWrapping.Wrap,
            });
        }
        else
        {
            foreach (var pair in result.Pairs.OrderBy(static item => item.PairNumber).ThenBy(static item => item.Attempt))
            {
                var header = new StackPanel { Spacing = 3d };
                header.Children.Add(new TextBlock
                {
                    Text = $"Pair {pair.PairNumber} · CPU {pair.Processor.Number} · core {pair.PhysicalCoreIndex} · {FormatPairStage(pair.Stage)} · attempt {pair.Attempt}",
                    FontSize = 12d,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = DashboardThemeResources.Brush(card, "TextBrush"),
                    TextWrapping = TextWrapping.Wrap,
                });
                header.Children.Add(new TextBlock
                {
                    Text = $"1% low effect {pair.OnePercentLowEffect:+0.0%;-0.0%;0.0%} · Control movement {pair.ControlMovement:P1} / {pair.DriftBudget:P1} noise guide · {pair.Verdict}",
                    FontSize = 11d,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = PairVerdictBrush(card, pair.Verdict),
                    TextWrapping = TextWrapping.Wrap,
                });

                var details = new StackPanel { Spacing = 6d, Padding = new Thickness(0d, 4d, 0d, 4d) };
                details.Children.Add(new TextBlock
                {
                    Text = $"Original before {pair.OriginalBeforeOnePercentLowFps:0.0} FPS · Candidate {pair.CandidateOnePercentLowFps:0.0} FPS · Original after {pair.OriginalAfterOnePercentLowFps:0.0} FPS",
                    Style = AppStyle("CaptionTextStyle"),
                    TextWrapping = TextWrapping.Wrap,
                });
                details.Children.Add(new TextBlock
                {
                    Text = pair.Reason,
                    Style = AppStyle("CaptionTextStyle"),
                    TextWrapping = TextWrapping.Wrap,
                });

                var expander = new Expander
                {
                    Header = header,
                    Content = details,
                    IsExpanded = false,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                };
                AutomationProperties.SetName(
                    expander,
                    $"Pair {pair.PairNumber}, CPU {pair.Processor.Number}, {pair.Verdict}, 1 percent low effect {pair.OnePercentLowEffect:+0.0%;-0.0%;0.0%}, control movement {pair.ControlMovement:P1}");
                stack.Children.Add(expander);
            }
        }

        card.Child = stack;
        CardElevation.Apply(card);
        return card;
    }

    private static Border BuildGateADecisionEvidence(GateAResultViewModel result)
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

    private static Border BuildGateAEvidenceActions(GateAResultViewModel result)
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

    private static Button ActionButton(string text, bool primary = false)
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
            if (grid.Children[index] is not FrameworkElement child) continue;
            Grid.SetColumn(child, index % columnCount);
            Grid.SetRow(child, index / columnCount);
        }
    }

    private static void UpdateGateAChartsGridLayout(Grid grid, double width)
    {
        var sideBySide = grid.Children.Count > 1 && width >= 900d;
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
            for (var row = 0; row < Math.Max(1, grid.Children.Count); row++)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }
        }
        for (var index = 0; index < grid.Children.Count; index++)
        {
            if (grid.Children[index] is not FrameworkElement child) continue;
            Grid.SetColumn(child, sideBySide ? index : 0);
            Grid.SetRow(child, sideBySide ? 0 : index);
        }
    }

    private static string FormatMetricComparison(GateAMetricComparison metric)
    {
        if (metric.OriginalValue is { } original &&
            metric.CandidateValue is { } candidate &&
            double.IsFinite(original) &&
            double.IsFinite(candidate))
        {
            var absoluteImprovement = metric.LowerIsBetter
                ? original - candidate
                : candidate - original;
            var observedPercent = metric.ObservedChangeFraction is { } observed && double.IsFinite(observed)
                ? $" ({observed:+0.0%;-0.0%;0.0%})"
                : string.Empty;
            var pairedEffect = metric.ImprovementFraction is { } effect && double.IsFinite(effect)
                ? $" · drift-adjusted paired {effect:+0.0%;-0.0%;0.0%}"
                : string.Empty;
            var direction = absoluteImprovement >= 0d ? "improvement" : "regression";
            return $"{original:0.##} → {candidate:0.##} {metric.Unit} · {direction} {Math.Abs(absoluteImprovement):0.##} {metric.Unit}{observedPercent}{pairedEffect}";
        }

        return metric.ImprovementFraction is { } fallback && double.IsFinite(fallback)
            ? $"{fallback:+0.0%;-0.0%;0.0%} paired effect"
            : "—";
    }

    private static string FormatPairStage(string stage) => stage switch
    {
        "screening-representative" => "core representative",
        "screening-sibling" => "SMT sibling refinement",
        "screening-finalists" => "finalist confirmation",
        _ => stage,
    };

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

    private static Brush PairVerdictBrush(FrameworkElement owner, GpuAutoAffinityPairVerdict verdict) =>
        DashboardThemeResources.Brush(owner, verdict == GpuAutoAffinityPairVerdict.Valid
            ? "ChartAccentPrimaryBrush"
            : "SemanticAttentionBrush");

    private static Brush DecisionStateBrush(FrameworkElement owner, string state) =>
        DashboardThemeResources.Brush(owner,
            string.Equals(state, "Passed", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(state, "High", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(state, "Best observed", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(state, "Restored", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(state, "Verified", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(state, "Repeatable", StringComparison.OrdinalIgnoreCase)
                ? "SemanticGoodBrush"
                : string.Equals(state, "Needs attention", StringComparison.OrdinalIgnoreCase) ||
                  string.Equals(state, "Blocked", StringComparison.OrdinalIgnoreCase)
                    ? "SemanticFailureBrush"
                    : string.Equals(state, "Diagnostic only", StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(state, "Medium", StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(state, "Low", StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(state, "Ranked", StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(state, "Not kept", StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(state, "Unavailable", StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(state, "Unstable", StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(state, "Not started", StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(state, "Not run", StringComparison.OrdinalIgnoreCase)
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

    private static Border StyledBorder(string styleKey) => new() { Style = AppStyle(styleKey) };

    private static Style AppStyle(string key)
    {
        if (Application.Current.Resources.TryGetValue(key, out var value) && value is Style style) return style;
        throw new InvalidOperationException($"Application style '{key}' is unavailable.");
    }

    private static double ResourceDouble(string key, double fallback) =>
        Application.Current.Resources.TryGetValue(key, out var value) && value is double number ? number : fallback;

    private static string ShortRevision(string revision) => revision.Length >= 12 ? revision[..12] : revision;

    private static string FormatDuration(TimeSpan duration) =>
        duration.TotalHours >= 1d
            ? $"{(int)duration.TotalHours}:{duration.Minutes:00}:{duration.Seconds:00}"
            : $"{duration.Minutes}:{duration.Seconds:00}";
}
