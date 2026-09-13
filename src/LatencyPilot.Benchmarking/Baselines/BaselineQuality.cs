using LatencyPilot.Benchmarking.Statistics;

namespace LatencyPilot.Benchmarking.Baselines;

public enum BaselineQualityStatus
{
    Valid = 1,
    Inconclusive = 2,
}

public sealed record BaselineQualityPolicy(
    int RequiredWindowCount = 5,
    int MinimumEventsPerMetricWindow = 20,
    double MaximumRelativeNoiseFloor = 0.30,
    double MaximumRelativeDrift = 0.20,
    double ExtremeWindowRelativeDeviation = 0.50)
{
    public void Validate()
    {
        if (RequiredWindowCount < 3)
        {
            throw new ArgumentOutOfRangeException(nameof(RequiredWindowCount));
        }

        if (MinimumEventsPerMetricWindow < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(MinimumEventsPerMetricWindow));
        }

        ValidateFraction(MaximumRelativeNoiseFloor, nameof(MaximumRelativeNoiseFloor));
        ValidateFraction(MaximumRelativeDrift, nameof(MaximumRelativeDrift));
        ValidateFraction(ExtremeWindowRelativeDeviation, nameof(ExtremeWindowRelativeDeviation));
    }

    private static void ValidateFraction(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value <= 0d || value >= 1d)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}

public sealed record BaselineWindowEvidence(
    int WindowNumber,
    DateTimeOffset StartedAtUtc,
    bool CaptureIntegrityValid,
    string? CaptureIntegrityIssue,
    int DpcEventCount,
    double? DpcP99Microseconds,
    int IsrEventCount,
    double? IsrP99Microseconds);

public sealed record BaselineMetricQuality(
    string MetricName,
    int EligibleWindowCount,
    double? MedianMicroseconds,
    double? P10Microseconds,
    double? P90Microseconds,
    double? RelativeNoiseFloor,
    double? RelativeDrift,
    IReadOnlyList<int> ExtremeWindowNumbers,
    IReadOnlyList<string> Reasons)
{
    public bool IsStable => Reasons.Count == 0;
}

public sealed record BaselineQualityResult(
    string MethodVersion,
    BaselineQualityStatus Status,
    int TotalWindowCount,
    int ValidCaptureWindowCount,
    BaselineMetricQuality DpcP99,
    BaselineMetricQuality IsrP99,
    IReadOnlyList<string> Reasons)
{
    public bool IsValidForComparison => Status == BaselineQualityStatus.Valid;
}

public static class BaselineQualityAnalyzer
{
    public const string MethodVersion = "baseline-quality-v1";

    private static readonly BaselineQualityPolicy Version1Policy = new();

    public static BaselineQualityResult Analyze(
        IReadOnlyList<BaselineWindowEvidence> windows,
        BaselineQualityPolicy? policy = null)
    {
        ArgumentNullException.ThrowIfNull(windows);
        policy ??= Version1Policy;
        policy.Validate();
        if (policy != Version1Policy)
        {
            throw new ArgumentException(
                $"{MethodVersion} has a fixed policy identity. Changing window/sample/noise/drift thresholds requires a new baseline method version.",
                nameof(policy));
        }

        var ordered = windows.OrderBy(static window => window.WindowNumber).ToArray();
        ValidateWindowSequence(ordered, nameof(windows));

        var reasons = new List<string>();
        if (ordered.Length < policy.RequiredWindowCount)
        {
            reasons.Add($"Only {ordered.Length} of {policy.RequiredWindowCount} required windows were captured.");
        }
        else if (ordered.Length > policy.RequiredWindowCount)
        {
            reasons.Add(
                $"Captured {ordered.Length} windows, but this method requires exactly {policy.RequiredWindowCount}. " +
                "Extra windows must not silently change the versioned baseline interpretation.");
        }

        var invalidCaptureWindows = ordered
            .Where(static window => !window.CaptureIntegrityValid)
            .ToArray();
        foreach (var window in invalidCaptureWindows)
        {
            reasons.Add(
                $"Window {window.WindowNumber} failed capture integrity: " +
                (string.IsNullOrWhiteSpace(window.CaptureIntegrityIssue)
                    ? "unspecified capture-integrity failure."
                    : window.CaptureIntegrityIssue));
        }

        var dpc = AnalyzeMetric(
            "DPC p99",
            ordered,
            static window => window.DpcEventCount,
            static window => window.DpcP99Microseconds,
            policy);
        var isr = AnalyzeMetric(
            "ISR p99",
            ordered,
            static window => window.IsrEventCount,
            static window => window.IsrP99Microseconds,
            policy);

        reasons.AddRange(dpc.Reasons.Select(static reason => $"DPC p99: {reason}"));
        reasons.AddRange(isr.Reasons.Select(static reason => $"ISR p99: {reason}"));

        var validCaptureWindowCount = ordered.Length - invalidCaptureWindows.Length;
        var valid = ordered.Length == policy.RequiredWindowCount &&
            invalidCaptureWindows.Length == 0 &&
            dpc.IsStable &&
            isr.IsStable;

        return new BaselineQualityResult(
            MethodVersion,
            valid ? BaselineQualityStatus.Valid : BaselineQualityStatus.Inconclusive,
            ordered.Length,
            validCaptureWindowCount,
            dpc,
            isr,
            reasons);
    }

    private static void ValidateWindowSequence(
        BaselineWindowEvidence[] windows,
        string parameterName)
    {
        for (var index = 0; index < windows.Length; index++)
        {
            var expectedWindowNumber = index + 1;
            var window = windows[index];
            if (window.WindowNumber != expectedWindowNumber)
            {
                throw new ArgumentException(
                    $"Baseline window numbers must form a contiguous sequence starting at 1. Expected window {expectedWindowNumber}, found {window.WindowNumber}.",
                    parameterName);
            }
        }
    }

    private static BaselineMetricQuality AnalyzeMetric(
        string metricName,
        IReadOnlyList<BaselineWindowEvidence> windows,
        Func<BaselineWindowEvidence, int> eventCountSelector,
        Func<BaselineWindowEvidence, double?> valueSelector,
        BaselineQualityPolicy policy)
    {
        var reasons = new List<string>();
        var eligible = new List<(int WindowNumber, double Value)>();
        var insufficientWindowNumbers = new List<int>();

        foreach (var window in windows)
        {
            if (!window.CaptureIntegrityValid)
            {
                continue;
            }

            var eventCount = eventCountSelector(window);
            var value = valueSelector(window);
            if (eventCount < policy.MinimumEventsPerMetricWindow ||
                value is null ||
                !double.IsFinite(value.Value) ||
                value.Value <= 0d)
            {
                insufficientWindowNumbers.Add(window.WindowNumber);
                continue;
            }

            eligible.Add((window.WindowNumber, value.Value));
        }

        if (insufficientWindowNumbers.Count > 0)
        {
            reasons.Add(
                $"insufficient event evidence in window(s) {string.Join(", ", insufficientWindowNumbers)} " +
                $"(minimum {policy.MinimumEventsPerMetricWindow} events per window).");
        }

        if (eligible.Count < policy.RequiredWindowCount)
        {
            reasons.Add(
                $"only {eligible.Count} of {policy.RequiredWindowCount} required windows have analyzable metric evidence.");
        }

        if (eligible.Count == 0)
        {
            return new BaselineMetricQuality(
                metricName,
                0,
                null,
                null,
                null,
                null,
                null,
                [],
                reasons);
        }

        var sortedValues = eligible.Select(static item => item.Value).Order().ToArray();
        var median = Percentiles.CalculateSorted(sortedValues, 0.50);
        var p10 = Percentiles.CalculateSorted(sortedValues, 0.10);
        var p90 = Percentiles.CalculateSorted(sortedValues, 0.90);

        if (!double.IsFinite(median) || median <= 0d)
        {
            reasons.Add("median is non-positive, so relative stability cannot be evaluated.");
            return new BaselineMetricQuality(
                metricName,
                eligible.Count,
                median,
                p10,
                p90,
                null,
                null,
                [],
                reasons);
        }

        var relativeNoiseFloor = (p90 - p10) / Math.Abs(median);
        if (relativeNoiseFloor > policy.MaximumRelativeNoiseFloor)
        {
            reasons.Add(
                $"P10-P90 spread is {relativeNoiseFloor:P1}, above the {policy.MaximumRelativeNoiseFloor:P0} quality limit.");
        }

        var splitCount = eligible.Count / 2;
        double? relativeDrift = null;
        if (splitCount > 0)
        {
            var early = eligible
                .Take(splitCount)
                .Select(static item => item.Value)
                .Order()
                .ToArray();
            var late = eligible
                .Skip(eligible.Count - splitCount)
                .Select(static item => item.Value)
                .Order()
                .ToArray();
            var earlyMedian = Percentiles.CalculateSorted(early, 0.50);
            var lateMedian = Percentiles.CalculateSorted(late, 0.50);
            relativeDrift = Math.Abs(lateMedian - earlyMedian) / Math.Abs(median);
            if (relativeDrift > policy.MaximumRelativeDrift)
            {
                reasons.Add(
                    $"early/late median drift is {relativeDrift:P1}, above the {policy.MaximumRelativeDrift:P0} quality limit.");
            }
        }

        var extremeWindowNumbers = eligible
            .Where(item => Math.Abs(item.Value - median) / Math.Abs(median) > policy.ExtremeWindowRelativeDeviation)
            .Select(static item => item.WindowNumber)
            .ToArray();
        if (extremeWindowNumbers.Length > 0)
        {
            reasons.Add(
                $"extreme window deviation exceeded {policy.ExtremeWindowRelativeDeviation:P0} in window(s) " +
                string.Join(", ", extremeWindowNumbers) + ". No window was discarded.");
        }

        return new BaselineMetricQuality(
            metricName,
            eligible.Count,
            median,
            p10,
            p90,
            relativeNoiseFloor,
            relativeDrift,
            extremeWindowNumbers,
            reasons);
    }
}
