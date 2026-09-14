namespace LatencyPilot.Benchmarking.Baselines;

public sealed record BaselineTransientWindowSignal(
    int WindowNumber,
    int DpcOverOneMillisecondCount,
    int DpcOverThreeMillisecondCount,
    double? DpcMaximumMicroseconds,
    int IsrOverOneMillisecondCount,
    int IsrOverThreeMillisecondCount,
    double? IsrMaximumMicroseconds);

public sealed record BaselineTransientSignalSummary(
    int DpcOverOneMillisecondCount,
    int DpcOverThreeMillisecondCount,
    int IsrOverOneMillisecondCount,
    int IsrOverThreeMillisecondCount,
    int? LargestWindowNumber,
    string? LargestMetricName,
    double? LargestDurationMicroseconds)
{
    public bool HasOverOneMillisecondSignal =>
        DpcOverOneMillisecondCount > 0 || IsrOverOneMillisecondCount > 0;

    public bool HasOverThreeMillisecondSignal =>
        DpcOverThreeMillisecondCount > 0 || IsrOverThreeMillisecondCount > 0;
}

public static class BaselineTransientSignalAnalyzer
{
    public static BaselineTransientSignalSummary Analyze(
        IReadOnlyList<BaselineTransientWindowSignal> windows)
    {
        ArgumentNullException.ThrowIfNull(windows);

        var seenWindows = new HashSet<int>();
        var dpcOverOne = 0;
        var dpcOverThree = 0;
        var isrOverOne = 0;
        var isrOverThree = 0;
        int? largestWindow = null;
        string? largestMetric = null;
        double? largestDuration = null;

        foreach (var window in windows)
        {
            ArgumentNullException.ThrowIfNull(window);
            if (window.WindowNumber <= 0 || !seenWindows.Add(window.WindowNumber))
            {
                throw new ArgumentException(
                    "Transient-signal windows must have unique positive window numbers.",
                    nameof(windows));
            }

            ValidateCounts(
                window.DpcOverOneMillisecondCount,
                window.DpcOverThreeMillisecondCount,
                $"DPC window {window.WindowNumber}");
            ValidateCounts(
                window.IsrOverOneMillisecondCount,
                window.IsrOverThreeMillisecondCount,
                $"ISR window {window.WindowNumber}");
            ValidateMaximum(window.DpcMaximumMicroseconds, $"DPC window {window.WindowNumber}");
            ValidateMaximum(window.IsrMaximumMicroseconds, $"ISR window {window.WindowNumber}");

            dpcOverOne = checked(dpcOverOne + window.DpcOverOneMillisecondCount);
            dpcOverThree = checked(dpcOverThree + window.DpcOverThreeMillisecondCount);
            isrOverOne = checked(isrOverOne + window.IsrOverOneMillisecondCount);
            isrOverThree = checked(isrOverThree + window.IsrOverThreeMillisecondCount);

            ConsiderMaximum(window.WindowNumber, "DPC", window.DpcMaximumMicroseconds,
                ref largestWindow, ref largestMetric, ref largestDuration);
            ConsiderMaximum(window.WindowNumber, "ISR", window.IsrMaximumMicroseconds,
                ref largestWindow, ref largestMetric, ref largestDuration);
        }

        return new BaselineTransientSignalSummary(
            dpcOverOne,
            dpcOverThree,
            isrOverOne,
            isrOverThree,
            largestWindow,
            largestMetric,
            largestDuration);
    }

    private static void ValidateCounts(int overOne, int overThree, string label)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(overOne);
        ArgumentOutOfRangeException.ThrowIfNegative(overThree);
        if (overThree > overOne)
        {
            throw new ArgumentException(
                $"{label} cannot contain more >3 ms events than >1 ms events.");
        }
    }

    private static void ValidateMaximum(double? maximum, string label)
    {
        if (maximum is not null && (!double.IsFinite(maximum.Value) || maximum.Value < 0d))
        {
            throw new ArgumentOutOfRangeException(nameof(maximum), $"{label} maximum must be finite and non-negative.");
        }
    }

    private static void ConsiderMaximum(
        int windowNumber,
        string metricName,
        double? candidate,
        ref int? largestWindow,
        ref string? largestMetric,
        ref double? largestDuration)
    {
        if (candidate is null || (largestDuration is not null && candidate.Value <= largestDuration.Value))
        {
            return;
        }

        largestWindow = windowNumber;
        largestMetric = metricName;
        largestDuration = candidate;
    }
}
