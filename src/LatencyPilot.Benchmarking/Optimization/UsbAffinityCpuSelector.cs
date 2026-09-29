using LatencyPilot.Core.Observation;
using LatencyPilot.Core.System;

namespace LatencyPilot.Benchmarking.Optimization;

public sealed record UsbAffinityCpuCandidate(
    int PhysicalCoreIndex,
    LogicalProcessorId Processor,
    double DpcTotalDurationMicroseconds,
    double IsrTotalDurationMicroseconds,
    double InterruptTailP99Microseconds,
    int DpcCount,
    int IsrCount)
{
    public double TotalInterruptDurationMicroseconds =>
        DpcTotalDurationMicroseconds + IsrTotalDurationMicroseconds;

    public int InterruptCount => DpcCount + IsrCount;
}

public sealed record UsbAffinityStableSelection(
    bool IsStable,
    UsbAffinityCpuCandidate? Candidate,
    int? PhysicalCoreIndex,
    int WinningCoreVotes,
    int WindowCount,
    string Reason);


public static class UsbAffinityCpuSelector
{
    public static IReadOnlyList<UsbAffinityCpuCandidate> Rank(
        ProcessorTopologySnapshot topology,
        KernelLatencyCaptureResult capture,
        LogicalProcessorId gpuWinner) =>
        Rank(topology, capture, (LogicalProcessorId?)gpuWinner);

    public static IReadOnlyList<UsbAffinityCpuCandidate> Rank(
        ProcessorTopologySnapshot topology,
        KernelLatencyCaptureResult capture,
        LogicalProcessorId? reservedGpuProcessor) =>
        Rank(
            topology,
            capture,
            reservedGpuProcessor is { } processor
                ? [processor]
                : Array.Empty<LogicalProcessorId>());

    public static IReadOnlyList<UsbAffinityCpuCandidate> Rank(
        ProcessorTopologySnapshot topology,
        KernelLatencyCaptureResult capture,
        IReadOnlyCollection<LogicalProcessorId> reservedProcessors)
    {
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(reservedProcessors);

        if (topology.ProcessorGroupCount != 1 ||
            reservedProcessors.Any(static processor => processor.Group != 0))
        {
            throw new NotSupportedException(
                "Automatic USB/xHCI affinity v1 currently requires one processor group.");
        }

        var reservedCoreIndexes = topology.Cores
            .Where(core => core.LogicalProcessors.Any(reservedProcessors.Contains))
            .Select(static core => core.Index)
            .ToHashSet();

        var ranked = new List<UsbAffinityCpuCandidate>();
        foreach (var core in topology.Cores)
        {
            // Existing explicit interrupt-affinity policies are treated as CPU
            // reservations. Exclude the whole physical core so an SMT sibling is
            // not benchmarked as if it were independent capacity.
            if (reservedCoreIndexes.Contains(core.Index))
            {
                continue;
            }

            foreach (var processor in core.LogicalProcessors)
            {
                if (processor.Group != 0)
                {
                    continue;
                }

                var events = capture.Events
                    .Where(item => item.ProcessorNumber == processor.Number)
                    .Where(static item => item.Kind is KernelLatencyEventKind.Dpc or KernelLatencyEventKind.Isr)
                    .Where(static item => double.IsFinite(item.DurationMicroseconds) && item.DurationMicroseconds >= 0d)
                    .ToArray();

                var dpc = events
                    .Where(static item => item.Kind == KernelLatencyEventKind.Dpc)
                    .Select(static item => item.DurationMicroseconds)
                    .ToArray();
                var isr = events
                    .Where(static item => item.Kind == KernelLatencyEventKind.Isr)
                    .Select(static item => item.DurationMicroseconds)
                    .ToArray();
                var allDurations = events
                    .Select(static item => item.DurationMicroseconds)
                    .Order()
                    .ToArray();

                ranked.Add(new UsbAffinityCpuCandidate(
                    core.Index,
                    processor,
                    dpc.Sum(),
                    isr.Sum(),
                    Percentile99(allDurations),
                    dpc.Length,
                    isr.Length));
            }
        }

        return ranked
            .OrderBy(static item => item.TotalInterruptDurationMicroseconds)
            .ThenBy(static item => item.InterruptTailP99Microseconds)
            .ThenBy(static item => item.InterruptCount)
            .ThenBy(static item => item.Processor.Number)
            .ToArray();
    }

    public static UsbAffinityStableSelection SelectStable(
        ProcessorTopologySnapshot topology,
        IReadOnlyList<KernelLatencyCaptureResult> captures,
        IReadOnlyCollection<LogicalProcessorId> reservedProcessors)
    {
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(captures);
        ArgumentNullException.ThrowIfNull(reservedProcessors);
        if (captures.Count == 0)
        {
            throw new ArgumentException(
                "At least one USB/xHCI headroom capture is required.",
                nameof(captures));
        }

        var rankings = captures
            .Select(capture => Rank(topology, capture, reservedProcessors).ToArray())
            .ToArray();
        if (rankings.Any(static ranking => ranking.Length == 0))
        {
            return new(
                false,
                null,
                null,
                0,
                captures.Count,
                "At least one USB/xHCI headroom window had no eligible logical processor.");
        }

        var windowCoreWinners = rankings
            .Select(static ranking => ranking
                .GroupBy(static candidate => candidate.PhysicalCoreIndex)
                .Select(static group => new
                {
                    CoreIndex = group.Key,
                    TotalDuration = group.Sum(static candidate => candidate.TotalInterruptDurationMicroseconds),
                    TailP99 = group.Max(static candidate => candidate.InterruptTailP99Microseconds),
                    InterruptCount = group.Sum(static candidate => candidate.InterruptCount),
                })
                .OrderBy(static core => core.TotalDuration)
                .ThenBy(static core => core.TailP99)
                .ThenBy(static core => core.InterruptCount)
                .ThenBy(static core => core.CoreIndex)
                .First().CoreIndex)
            .ToArray();

        var vote = windowCoreWinners
            .GroupBy(static coreIndex => coreIndex)
            .Select(static group => new
            {
                CoreIndex = group.Key,
                Votes = group.Count(),
            })
            .OrderByDescending(static item => item.Votes)
            .ThenBy(static item => item.CoreIndex)
            .First();
        var requiredVotes = captures.Count == 1
            ? 1
            : (captures.Count / 2) + 1;
        if (vote.Votes < requiredVotes)
        {
            return new(
                false,
                null,
                vote.CoreIndex,
                vote.Votes,
                captures.Count,
                $"USB/xHCI headroom was not stable enough: no physical core won a majority of the {captures.Count} capture windows.");
        }

        var processorsOnWinningCore = rankings
            .SelectMany(static ranking => ranking)
            .Where(candidate => candidate.PhysicalCoreIndex == vote.CoreIndex)
            .Select(static candidate => candidate.Processor)
            .Distinct()
            .ToArray();

        var aggregated = processorsOnWinningCore
            .Select(processor =>
            {
                var observations = rankings
                    .SelectMany(static ranking => ranking)
                    .Where(candidate =>
                        candidate.PhysicalCoreIndex == vote.CoreIndex &&
                        candidate.Processor == processor)
                    .ToArray();
                return new UsbAffinityCpuCandidate(
                    vote.CoreIndex,
                    processor,
                    Median(observations.Select(static item => item.DpcTotalDurationMicroseconds)),
                    Median(observations.Select(static item => item.IsrTotalDurationMicroseconds)),
                    Median(observations.Select(static item => item.InterruptTailP99Microseconds)),
                    (int)Math.Round(Median(observations.Select(static item => (double)item.DpcCount))),
                    (int)Math.Round(Median(observations.Select(static item => (double)item.IsrCount))));
            })
            .OrderBy(static candidate => candidate.TotalInterruptDurationMicroseconds)
            .ThenBy(static candidate => candidate.InterruptTailP99Microseconds)
            .ThenBy(static candidate => candidate.InterruptCount)
            .ThenBy(static candidate => candidate.Processor.Number)
            .ToArray();

        var selected = aggregated[0];
        return new(
            true,
            selected,
            vote.CoreIndex,
            vote.Votes,
            captures.Count,
            $"Physical core {vote.CoreIndex} won {vote.Votes}/{captures.Count} independent headroom windows; CPU {selected.Processor.Number} had the best median sibling evidence on that core.");
    }

    public static UsbAffinityCpuCandidate Select(
        ProcessorTopologySnapshot topology,
        KernelLatencyCaptureResult capture,
        LogicalProcessorId gpuWinner)
    {
        var ranked = Rank(topology, capture, gpuWinner);
        return ranked.Count > 0
            ? ranked[0]
            : throw new InvalidOperationException(
                "No logical processor remains for USB/xHCI affinity after excluding reserved physical cores.");
    }

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        if (sorted.Length == 0)
        {
            return 0d;
        }

        var middle = sorted.Length / 2;
        return (sorted.Length & 1) == 1
            ? sorted[middle]
            : (sorted[middle - 1] + sorted[middle]) / 2d;
    }

    private static double Percentile99(double[] sortedAscending)
    {
        if (sortedAscending.Length == 0)
        {
            return 0d;
        }

        var position = (sortedAscending.Length - 1) * 0.99d;
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        return lower == upper
            ? sortedAscending[lower]
            : sortedAscending[lower] +
              ((sortedAscending[upper] - sortedAscending[lower]) * (position - lower));
    }
}
