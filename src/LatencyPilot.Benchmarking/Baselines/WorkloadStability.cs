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
    bool HasMaterialDrift,
    double MaximumRelativeDeviation,
    bool HasExtremeWindow);

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
    public const double MaximumExtremeWindowRelativeDeviation = 0.50;

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

        var cpuEvidenceWindowCount = ordered.Count(static window => window.SystemCpuBusyPercent is not null);
        if (cpuEvidenceWindowCount is > 0 and < RequiredWindowCount)
        {
            return new WorkloadStabilityResult(
                MethodVersion,
                WorkloadStabilityStatus.Insufficient,
                [],
                [$"System CPU busy evidence must be present for all {RequiredWindowCount} workload windows or absent from all of them; received {cpuEvidenceWindowCount}."]);
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

        if (cpuEvidenceWindowCount == RequiredWindowCount)
        {
            signals.Add(AnalyzeSignal(
                "System CPU busy",
                ordered.Select(static window => window.SystemCpuBusyPercent!.Value).ToArray()));
        }

        var changing = signals
            .Where(static signal => signal.HasMaterialDrift || signal.HasExtremeWindow)
            .ToArray();
        if (changing.Length == 0)
        {
            return new WorkloadStabilityResult(
                MethodVersion,
                WorkloadStabilityStatus.Stable,
                signals.AsReadOnly(),
                []);
        }

        var reasonParts = changing.Select(static signal =>
        {
            var parts = new List<string>(2);
            if (signal.HasMaterialDrift)
            {
                parts.Add($"early/late drift {signal.RelativeDrift:P1}");
            }
            if (signal.HasExtremeWindow)
            {
                parts.Add($"maximum window deviation {signal.MaximumRelativeDeviation:P1}");
            }
            return $"{signal.SignalName} {string.Join(" and ", parts)}";
        });

        return new WorkloadStabilityResult(
            MethodVersion,
            WorkloadStabilityStatus.Changing,
            signals.AsReadOnly(),
            [
                "Workload activity changed materially across the repeated baseline: " +
                string.Join(", ", reasonParts) +
                $" (drift limit {MaximumRelativeActivityDrift:P0}; extreme-window limit {MaximumExtremeWindowRelativeDeviation:P0}).",
            ]);
    }

    private static WorkloadSignalStability AnalyzeSignal(string name, double[] values)
    {
        var median = Percentiles.Calculate(values, 0.50);
        var earlyMedian = Percentiles.Calculate(values.Take(2).ToArray(), 0.50);
        var lateMedian = Percentiles.Calculate(values.Skip(values.Length - 2).ToArray(), 0.50);

        double relativeDrift;
        double maximumRelativeDeviation;
        if (median == 0d)
        {
            var allZero = values.All(static value => value == 0d);
            relativeDrift = allZero ? 0d : double.PositiveInfinity;
            maximumRelativeDeviation = allZero ? 0d : double.PositiveInfinity;
        }
        else
        {
            var denominator = Math.Abs(median);
            relativeDrift = Math.Abs(lateMedian - earlyMedian) / denominator;
            maximumRelativeDeviation = values.Max(value => Math.Abs(value - median) / denominator);
        }

        return new WorkloadSignalStability(
            name,
            median,
            earlyMedian,
            lateMedian,
            relativeDrift,
            relativeDrift > MaximumRelativeActivityDrift,
            maximumRelativeDeviation,
            maximumRelativeDeviation > MaximumExtremeWindowRelativeDeviation);
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
