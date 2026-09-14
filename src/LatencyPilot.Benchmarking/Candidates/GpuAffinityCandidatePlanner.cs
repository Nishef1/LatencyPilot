using LatencyPilot.Core.System;

namespace LatencyPilot.Benchmarking.Candidates;

public sealed record ProcessorPressureEvidence(
    LogicalProcessorId Processor,
    double PressureScore)
{
    public ProcessorPressureEvidence(LogicalProcessorId processor, double pressureScore)
        : this()
    {
        if (!double.IsFinite(pressureScore) || pressureScore < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pressureScore));
        }

        Processor = processor;
        PressureScore = pressureScore;
    }
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

        // Missing pressure evidence is allowed for diagnostics, but it must never
        // outrank measured candidates. CPU 0 is deliberately not hard-excluded.
        return candidates
            .OrderBy(static candidate => candidate.ObservedPressureScore)
            .ThenByDescending(static candidate => candidate.EfficiencyClass)
            .ThenBy(static candidate => candidate.PhysicalCoreIndex)
            .Take(maximumCandidates)
            .ToArray();
    }
}
