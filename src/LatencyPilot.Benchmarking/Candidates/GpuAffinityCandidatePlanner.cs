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

    public static IReadOnlyList<GpuAffinityCandidate> Create(
        ProcessorTopologySnapshot topology,
        IEnumerable<ProcessorPressureEvidence> pressureEvidence,
        int maximumCandidates = DefaultMaximumCandidates)
    {
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(pressureEvidence);

        if (maximumCandidates is < 1 or > 16)
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

        var candidates = new List<GpuAffinityCandidate>(topology.PhysicalCoreCount);
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
                    Pressure = pressureByProcessor.TryGetValue(processor, out var score)
                        ? score
                        : double.PositiveInfinity,
                })
                .OrderBy(static item => item.Pressure)
                .ThenBy(static item => item.Processor.Group)
                .ThenBy(static item => item.Processor.Number)
                .First();

            candidates.Add(new GpuAffinityCandidate(
                core.Index,
                bestLogical.Processor,
                core.EfficiencyClass,
                core.IsSmt,
                bestLogical.Pressure));
        }

        var ordered = candidates
            .OrderBy(static candidate => candidate.ObservedPressureScore)
            .ThenByDescending(static candidate => candidate.EfficiencyClass)
            .ThenBy(static candidate => candidate.PhysicalCoreIndex)
            .ToArray();

        if (!topology.HasHeterogeneousCores)
        {
            return ordered.Take(maximumCandidates).ToArray();
        }

        // On hybrid CPUs, do not silently assume either the fastest class or the
        // most-efficient class is always best for interrupt work. Ensure that the
        // bounded screening set represents distinct efficiency classes first, then
        // fill the remaining slots from the globally lowest-pressure physical cores.
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
}
