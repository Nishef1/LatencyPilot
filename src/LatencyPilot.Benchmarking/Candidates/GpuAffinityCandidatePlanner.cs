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
    public const int DefaultMaximumCandidates = 4;
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

        var pressureByProcessor = pressureEvidence
            .GroupBy(static item => item.Processor)
            .ToDictionary(
                static group => group.Key,
                static group => group.Average(static item => item.PressureScore));

        var rankedCandidates = new List<RankedGpuAffinityCandidate>(topology.PhysicalCoreCount);
        foreach (var core in topology.Cores)
        {
            if (core.LogicalProcessors.Count == 0)
            {
                continue;
            }

            var bestLogical = core.LogicalProcessors
                .Select(processor => new
                {
                    Processor = processor,
                    AvailabilityRank = GetAvailabilityRank(cpuSets, processor),
                    Pressure = pressureByProcessor.TryGetValue(processor, out var score)
                        ? score
                        : double.PositiveInfinity,
                })
                .OrderBy(static item => item.AvailabilityRank)
                .ThenBy(static item => item.Pressure)
                .ThenBy(static item => item.Processor.Group)
                .ThenBy(static item => item.Processor.Number)
                .First();

            rankedCandidates.Add(new RankedGpuAffinityCandidate(
                new GpuAffinityCandidate(
                    core.Index,
                    bestLogical.Processor,
                    core.EfficiencyClass,
                    core.IsSmt,
                    bestLogical.Pressure),
                bestLogical.AvailabilityRank));
        }

        var ordered = rankedCandidates
            .OrderBy(static ranked => ranked.AvailabilityRank)
            .ThenBy(static ranked => ranked.Candidate.ObservedPressureScore)
            .ThenByDescending(static ranked => ranked.Candidate.EfficiencyClass)
            .ThenBy(static ranked => ranked.Candidate.PhysicalCoreIndex)
            .Select(static ranked => ranked.Candidate)
            .ToArray();

        if (!topology.HasHeterogeneousCores)
        {
            return ordered.Take(maximumCandidates).ToArray();
        }

        // On hybrid CPUs, do not silently assume either the fastest class or the
        // most-efficient class is always best for interrupt work. Ensure that the
        // bounded screening set represents distinct efficiency classes first, then
        // fill the remaining slots from the globally safest/lowest-pressure cores.
        var selected = new List<GpuAffinityCandidate>(maximumCandidates);
        foreach (var efficiencyClass in topology.EfficiencyClasses.OrderDescending())
        {
            var representative = ordered.FirstOrDefault(candidate =>
                candidate.EfficiencyClass == efficiencyClass);
            if (representative is not null)
            {
                selected.Add(representative);
                if (selected.Count == maximumCandidates)
                {
                    return selected.ToArray();
                }
            }
        }

        foreach (var candidate in ordered)
        {
            if (selected.Any(existing => existing.PhysicalCoreIndex == candidate.PhysicalCoreIndex))
            {
                continue;
            }

            selected.Add(candidate);
            if (selected.Count == maximumCandidates)
            {
                break;
            }
        }

        return selected.ToArray();
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
        if (cpuSet is null)
        {
            return 4;
        }

        // Allocated/real-time CPUs may belong to a workload with stronger ownership
        // semantics than our experiment. A parked CPU is not forbidden, but an
        // already-active, unallocated sibling is a safer first candidate.
        if (cpuSet.RealTime)
        {
            return 3;
        }

        if (cpuSet.Allocated && !cpuSet.AllocatedToCurrentProcess)
        {
            return 3;
        }

        if (cpuSet.Parked)
        {
            return 2;
        }

        return cpuSet.Allocated ? 1 : 0;
    }

    private readonly record struct RankedGpuAffinityCandidate(
        GpuAffinityCandidate Candidate,
        int AvailabilityRank);
}
