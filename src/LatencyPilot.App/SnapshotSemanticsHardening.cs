using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LatencyPilot.App;

public sealed partial class MainWindow
{
    private bool _normalizingSnapshotSemantics;

    private void InitializeSnapshotSemanticsHardening()
    {
        NormalizeSnapshotSemanticHeadings();

        _latencyHealthBadgeText?.RegisterPropertyChangedCallback(
            TextBlock.TextProperty,
            (_, _) => NormalizeSnapshotSemanticCopy());
        _latencyHealthTitleText?.RegisterPropertyChangedCallback(
            TextBlock.TextProperty,
            (_, _) => NormalizeSnapshotSemanticCopy());
        _latencyHealthSummaryText?.RegisterPropertyChangedCallback(
            TextBlock.TextProperty,
            (_, _) => NormalizeSnapshotSemanticCopy());

        NormalizeSnapshotSemanticCopy();
    }

    private void NormalizeSnapshotSemanticHeadings()
    {
        if (_latencyHealthCard?.Child is not StackPanel root)
        {
            return;
        }

        foreach (var textBlock in EnumerateTextBlocks(root))
        {
            if (string.Equals(textBlock.Text, "Latency health", StringComparison.Ordinal))
            {
                textBlock.Text = "Snapshot evidence";
            }
            else if (string.Equals(
                         textBlock.Text,
                         "Plain-language context for the exact DPC/ISR evidence below.",
                         StringComparison.Ordinal))
            {
                textBlock.Text =
                    "Diagnostic context for this exact DPC/ISR snapshot. Decision claims require a repeated baseline.";
            }
        }
    }

    private void NormalizeSnapshotSemanticCopy()
    {
        if (_normalizingSnapshotSemantics)
        {
            return;
        }

        _normalizingSnapshotSemantics = true;
        try
        {
            NormalizeSnapshotSemanticHeadings();

            if (_latencyHealthBadgeText is not null)
            {
                _latencyHealthBadgeText.Text = _latencyHealthBadgeText.Text switch
                {
                    "Within guidance" => "Diagnostic only",
                    "Needs context" => "Reference exceeded",
                    "≥1 ms tail observed" => "≥1 ms bucket",
                    "≥3 ms tail observed" => "≥3 ms bucket",
                    _ => _latencyHealthBadgeText.Text,
                };
            }

            if (_latencyHealthTitleText is not null)
            {
                _latencyHealthTitleText.Text = _latencyHealthTitleText.Text switch
                {
                    "Capture an observation to classify the tail." =>
                        "Capture a quick snapshot to inspect the tail.",
                    "No guidance exceedance was observed in this window." =>
                        "No driver-reference exceedance was observed in this snapshot.",
                    "Driver guidance was exceeded, without a millisecond-scale spike." =>
                        "Driver-duration reference lines were exceeded in this snapshot.",
                    "A millisecond-scale DPC/ISR event was observed." =>
                        "A ≥1 ms local diagnostic-bucket event was observed.",
                    "A long DPC/ISR tail event was observed." =>
                        "A ≥3 ms local diagnostic-bucket event was observed.",
                    _ => _latencyHealthTitleText.Text,
                };
            }

            if (_latencyHealthSummaryText is not null &&
                !string.IsNullOrWhiteSpace(_latencyHealthSummaryText.Text) &&
                !_latencyHealthSummaryText.Text.Contains(
                    "Quick snapshots are diagnostic only",
                    StringComparison.Ordinal))
            {
                _latencyHealthSummaryText.Text =
                    _latencyHealthSummaryText.Text.TrimEnd() +
                    " Quick snapshots are diagnostic only; stability and optimization decisions require the repeated decision baseline.";
            }
        }
        finally
        {
            _normalizingSnapshotSemantics = false;
        }
    }

    private static IEnumerable<TextBlock> EnumerateTextBlocks(DependencyObject root)
    {
        if (root is TextBlock textBlock)
        {
            yield return textBlock;
        }

        if (root is Panel panel)
        {
            foreach (var child in panel.Children)
            {
                foreach (var nested in EnumerateTextBlocks(child))
                {
                    yield return nested;
                }
            }
        }
        else if (root is Border border && border.Child is not null)
        {
            foreach (var nested in EnumerateTextBlocks(border.Child))
            {
                yield return nested;
            }
        }
        else if (root is ContentControl contentControl && contentControl.Content is DependencyObject content)
        {
            foreach (var nested in EnumerateTextBlocks(content))
            {
                yield return nested;
            }
        }
    }
}
