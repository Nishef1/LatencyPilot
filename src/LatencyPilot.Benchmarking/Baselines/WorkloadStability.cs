using LatencyPilot.Benchmarking.Statistics;

namespace LatencyPilot.Benchmarking.Baselines;

public enum WorkloadStabilityStatus
{
    Stable = 1,
    Changing = 2,
    Insufficient = 3,
}

public sealed record WorkloadWindowEvidence(
    int WindowNumber,
    double ActualDurationMilliseconds,
    int DpcEventCount,
    int IsrEventCount,
    double? SystemCpuBusyPercent);

public sealed record WorkloadSignalStability(
    string SignalName,
    double Median,
    double EarlyMedian,
    double LateMedian,
    double RelativeDrift,
    bool HasMaterialDrift);

public sealed record WorkloadStabilityResult(
    string MethodVersion,
    WorkloadStabilityStatus Status,
    IReadOnlyList<WorkloadSignalStability> Signals,
    IReadOnlyList<string> Reasons)
{
    public bool IsEligibleForExperiment => Status == WorkloadStabilityStatus.Stable;
}

public static class WorkloadStabilityAnalyzer
{
    public const string MethodVersion = "workload-stability-v1";
    public const int RequiredWindowCount = 5;
    public const double MaximumRelativeActivityDrift = 0.25;

    public static WorkloadStabilityResult Analyze(IReadOnlyList<WorkloadWindowEvidence> windows)
    {
        ArgumentNullException.ThrowIfNull(windows);

        var ordered = windows.OrderBy(static window => window.WindowNumber).ToArray();
        ValidateWindowSequence(ordered, nameof(windows));

        if (ordered.Length != RequiredWindowCount)
        {
            return new WorkloadStabilityResult(
                MethodVersion,
                WorkloadStabilityStatus.Insufficient,
                [],
                [$"Workload stability requires exactly {RequiredWindowCount} contiguous windows; received {ordered.Length}."]);
        }

        var invalidWindows = ordered
            .Where(static window =>
                !double.IsFinite(window.ActualDurationMilliseconds) ||
                window.ActualDurationMilliseconds <= 0d ||
                window.DpcEventCount < 0 ||
                window.IsrEventCount < 0 ||
                (window.SystemCpuBusyPercent is { } cpuBusy &&
                    (!double.IsFinite(cpuBusy) || cpuBusy is < 0d or > 100d)))
            .Select(static window => window.WindowNumber)
            .ToArray();
        if (invalidWindows.Length > 0)
        {
            return new WorkloadStabilityResult(
                MethodVersion,
                WorkloadStabilityStatus.Insufficient,
                [],
                [$"Invalid workload activity evidence in window(s) {string.Join(", ", invalidWindows)}."]);
        }

        var signals = new List<WorkloadSignalStability>(3)
        {
            AnalyzeSignal(
                "DPC event rate",
                ordered.Select(static window =>
                    window.DpcEventCount / (window.ActualDurationMilliseconds / 1_000d)).ToArray()),
            AnalyzeSignal(
                "ISR event rate",
                ordered.Select(static window =>
                    window.IsrEventCount / (window.ActualDurationMilliseconds / 1_000d)).ToArray()),
        };

        if (ordered.All(static window => window.SystemCpuBusyPercent is not null))
        {
            signals.Add(AnalyzeSignal(
                "System CPU busy",
                ordered.Select(static window => window.SystemCpuBusyPercent!.Value).ToArray()));
        }

        var changing = signals.Where(static signal => signal.HasMaterialDrift).ToArray();
        if (changing.Length == 0)
        {
            return new WorkloadStabilityResult(
                MethodVersion,
                WorkloadStabilityStatus.Stable,
                signals.AsReadOnly(),
                []);
        }

        return new WorkloadStabilityResult(
            MethodVersion,
            WorkloadStabilityStatus.Changing,
            signals.AsReadOnly(),
            [
                "Workload activity changed materially across the repeated baseline: " +
                string.Join(", ", changing.Select(static signal =>
                    $"{signal.SignalName} early/late drift {signal.RelativeDrift:P1}")) +
                $" (limit {MaximumRelativeActivityDrift:P0}).",
            ]);
    }

    private static WorkloadSignalStability AnalyzeSignal(string name, double[] values)
    {
        var median = Percentiles.Calculate(values, 0.50);
        var earlyMedian = Percentiles.Calculate(values.AsSpan(0, 2), 0.50);
        var lateMedian = Percentiles.Calculate(values.AsSpan(values.Length - 2, 2), 0.50);

        double relativeDrift;
        if (median == 0d)
        {
            relativeDrift = values.All(static value => value == 0d)
                ? 0d
                : double.PositiveInfinity;
        }
        else
        {
            relativeDrift = Math.Abs(lateMedian - earlyMedian) / Math.Abs(median);
        }

        return new WorkloadSignalStability(
            name,
            median,
            earlyMedian,
            lateMedian,
            relativeDrift,
            relativeDrift > MaximumRelativeActivityDrift);
    }

    private static void ValidateWindowSequence(WorkloadWindowEvidence[] windows, string parameterName)
    {
        for (var index = 0; index < windows.Length; index++)
        {
            var expected = index + 1;
            if (windows[index].WindowNumber != expected)
            {
                throw new ArgumentException(
                    $"Workload window numbers must form a contiguous sequence starting at 1. Expected {expected}, found {windows[index].WindowNumber}.",
                    parameterName);
            }
        }
    }
}
