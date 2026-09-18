using LatencyPilot.Core.System;

namespace LatencyPilot.Benchmarking.Candidates;

public sealed record ProcessorPressureEvidence
{
    public ProcessorPressureEvidence(LogicalProcessorId processor, double pressureScore)
    {
        if (!double.IsFinite(pressureScore) || pressureScore < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pressureScore));
        }

        Processor = processor;
        PressureScore = pressureScore;
    }

    public LogicalProcessorId Processor { get; }

    public double PressureScore { get; }
}

public sealed record GpuAffinityCandidate(
    int PhysicalCoreIndex,
    LogicalProcessorId Processor,
    byte EfficiencyClass,
    bool CoreUsesSmt,
    double ObservedPressureScore);

public static class GpuAffinityCandidatePlanner
{
    public const int DefaultMaximumCandidates = 16;
    public const int MaximumCandidates = 16;

    public static IReadOnlyList<GpuAffinityCandidate> Create(
        ProcessorTopologySnapshot topology,
        IEnumerable<ProcessorPressureEvidence> pressureEvidence,
        int maximumCandidates = DefaultMaximumCandidates) =>
        Create(topology, pressureEvidence, cpuSets: null, maximumCandidates);

    public static IReadOnlyList<GpuAffinityCandidate> Create(
        ProcessorTopologySnapshot topology,
        IEnumerable<ProcessorPressureEvidence> pressureEvidence,
        ProcessorCpuSetSnapshot? cpuSets,
        int maximumCandidates = DefaultMaximumCandidates)
    {
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(pressureEvidence);

        if (maximumCandidates is < 1 or > MaximumCandidates)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumCandidates));
        }

        // Phase 3 v1 writes a KAFFINITY mask. Do not pretend that a group-local
        // mask is sufficient for an arbitrary multi-group machine.
        if (topology.ProcessorGroupCount != 1)
        {
            throw new NotSupportedException(
                "Automatic GPU interrupt-affinity candidates currently require exactly one processor group.");
        }

        var pressureByProcessor = BuildPressureMap(pressureEvidence);
        var rankedCandidates = CreateRankedPhysicalCoreCandidates(topology, pressureByProcessor, cpuSets);
        var ordered = rankedCandidates
            .OrderBy(static ranked => ranked.AvailabilityRank)
            .ThenBy(static ranked => ranked.Candidate.ObservedPressureScore)
            .ThenByDescending(static ranked => ranked.Candidate.EfficiencyClass)
            .ThenBy(static ranked => ranked.Candidate.PhysicalCoreIndex)
            .ToArray();

        if (ordered.Length <= maximumCandidates)
        {
            return ordered.Select(static ranked => ranked.Candidate).ToArray();
        }

        return SelectStratified(ordered, cpuSets, maximumCandidates);
    }

    private static Dictionary<LogicalProcessorId, double> BuildPressureMap(
        IEnumerable<ProcessorPressureEvidence> pressureEvidence) =>
        pressureEvidence
            .GroupBy(static item => item.Processor)
            .ToDictionary(
                static group => group.Key,
                static group => group.Average(static item => item.PressureScore));

    private static List<RankedGpuAffinityCandidate> CreateRankedPhysicalCoreCandidates(
        ProcessorTopologySnapshot topology,
        Dictionary<LogicalProcessorId, double> pressureByProcessor,
        ProcessorCpuSetSnapshot? cpuSets)
    {
        var rankedCandidates = new List<RankedGpuAffinityCandidate>(topology.PhysicalCoreCount);
        foreach (var core in topology.Cores)
        {
            var eligible = core.LogicalProcessors
                .Where(processor => IsEligible(cpuSets, processor))
                .Select(processor => new RankedLogicalProcessor(
                    processor,
                    GetAvailabilityRank(cpuSets, processor),
                    pressureByProcessor.TryGetValue(processor, out var score)
                        ? score
                        : double.PositiveInfinity))
                .OrderBy(static item => item.AvailabilityRank)
                .ThenBy(static item => item.Pressure)
                .ThenBy(static item => item.Processor.Group)
                .ThenBy(static item => item.Processor.Number)
                .ToArray();
            if (eligible.Length == 0)
            {
                continue;
            }

            var bestLogical = eligible[0];
            rankedCandidates.Add(new RankedGpuAffinityCandidate(
                new GpuAffinityCandidate(
                    core.Index,
                    bestLogical.Processor,
                    core.EfficiencyClass,
                    core.IsSmt,
                    bestLogical.Pressure),
                bestLogical.AvailabilityRank));
        }

        return rankedCandidates;
    }

    private static GpuAffinityCandidate[] SelectStratified(
        IReadOnlyList<RankedGpuAffinityCandidate> ordered,
        ProcessorCpuSetSnapshot? cpuSets,
        int maximumCandidates)
    {
        var selected = new List<GpuAffinityCandidate>(maximumCandidates);
        var strata = ordered
            .GroupBy(ranked => CreateStratum(ranked.Candidate, cpuSets))
            .OrderByDescending(static group => group.Key.EfficiencyClass)
            .ThenBy(static group => group.Key.NumaNodeIndex)
            .ThenBy(static group => group.Key.LastLevelCacheIndex);

        foreach (var stratum in strata)
        {
            selected.Add(stratum.First().Candidate);
            if (selected.Count == maximumCandidates)
            {
                return selected.ToArray();
            }
        }

        foreach (var ranked in ordered)
        {
            if (selected.Any(existing =>
                    existing.PhysicalCoreIndex == ranked.Candidate.PhysicalCoreIndex))
            {
                continue;
            }

            selected.Add(ranked.Candidate);
            if (selected.Count == maximumCandidates)
            {
                break;
            }
        }

        return selected.ToArray();
    }

    private static CandidateStratum CreateStratum(
        GpuAffinityCandidate candidate,
        ProcessorCpuSetSnapshot? cpuSets)
    {
        var cpuSet = cpuSets?.TryGet(candidate.Processor);
        return new CandidateStratum(
            candidate.EfficiencyClass,
            cpuSet?.NumaNodeIndex ?? byte.MaxValue,
            cpuSet?.LastLevelCacheIndex ?? byte.MaxValue);
    }

    private static bool IsEligible(
        ProcessorCpuSetSnapshot? cpuSets,
        LogicalProcessorId processor)
    {
        if (cpuSets is null)
        {
            return true;
        }

        var cpuSet = cpuSets.TryGet(processor);
        if (cpuSet is null || cpuSet.RealTime)
        {
            return false;
        }

        return !cpuSet.Allocated || cpuSet.AllocatedToCurrentProcess;
    }

    private static int GetAvailabilityRank(
        ProcessorCpuSetSnapshot? cpuSets,
        LogicalProcessorId processor)
    {
        if (cpuSets is null)
        {
            return 0;
        }

        var cpuSet = cpuSets.TryGet(processor);
        if (cpuSet is null || cpuSet.RealTime ||
            (cpuSet.Allocated && !cpuSet.AllocatedToCurrentProcess))
        {
            return int.MaxValue;
        }

        // Parked CPU sets remain legal Windows CPU-set targets; prefer an active
        // sibling when available, but do not silently ban parked cores from the
        // bounded active screen. A CPU set already allocated to this process is
        // likewise usable but ranked behind an unallocated active processor.
        if (cpuSet.Parked)
        {
            return 2;
        }

        return cpuSet.Allocated ? 1 : 0;
    }

    private readonly record struct RankedLogicalProcessor(
        LogicalProcessorId Processor,
        int AvailabilityRank,
        double Pressure);

    private readonly record struct RankedGpuAffinityCandidate(
        GpuAffinityCandidate Candidate,
        int AvailabilityRank);

    private readonly record struct CandidateStratum(
        byte EfficiencyClass,
        byte NumaNodeIndex,
        byte LastLevelCacheIndex);
}
