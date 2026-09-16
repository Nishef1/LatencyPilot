using LatencyPilot.Benchmarking.Candidates;
using LatencyPilot.Core.System;

namespace LatencyPilot.Benchmarking.Optimization;

public sealed class GpuAutoAffinityProgressPlan
{
    private const int ControlWarmupCount = 1;
    private const int DecisionControlTrialCount = 2;
    private const int TrialsPerCandidate = 2;
    private const int ConfirmationTrialCount = 8;
    private readonly IReadOnlyDictionary<int, int> refinementCandidateCounts;

    private GpuAutoAffinityProgressPlan(
        int physicalCandidateCount,
        IReadOnlyDictionary<int, int> refinementCandidateCounts)
    {
        PhysicalCandidateCount = physicalCandidateCount;
        this.refinementCandidateCounts = refinementCandidateCounts;
        MaximumRefinementCandidateCount = refinementCandidateCounts.Count == 0
            ? 0
            : refinementCandidateCounts.Values.Max();
    }

    public int PhysicalCandidateCount { get; }

    public int MaximumRefinementCandidateCount { get; }

    public int InitialTotalUnits =>
        GetBaseTotalUnits() + (MaximumRefinementCandidateCount * TrialsPerCandidate);

    public int GetRefinementCandidateCount(int physicalCoreIndex)
    {
        if (!refinementCandidateCounts.TryGetValue(physicalCoreIndex, out var count))
        {
            throw new ArgumentOutOfRangeException(
                nameof(physicalCoreIndex),
                "Physical core is not part of the planned GPU affinity screen.");
        }

        return count;
    }

    public int GetTotalUnitsForFinalist(int physicalCoreIndex) =>
        GetBaseTotalUnits() +
        (GetRefinementCandidateCount(physicalCoreIndex) * TrialsPerCandidate);

    public static GpuAutoAffinityProgressPlan Create(
        ProcessorTopologySnapshot topology,
        IEnumerable<ProcessorPressureEvidence> pressureEvidence,
        ProcessorCpuSetSnapshot? cpuSets)
    {
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(pressureEvidence);

        var pressure = pressureEvidence.ToArray();
        var physicalCandidates = GpuAffinityCandidatePlanner.Create(topology, pressure, cpuSets);
        var refinementCounts = physicalCandidates.ToDictionary(
            static candidate => candidate.PhysicalCoreIndex,
            candidate => GpuAffinityCandidatePlanner.CreateSiblingRefinement(
                topology,
                pressure,
                candidate,
                cpuSets).Count);

        return new GpuAutoAffinityProgressPlan(
            physicalCandidates.Count,
            refinementCounts);
    }

    private int GetBaseTotalUnits() =>
        ControlWarmupCount +
        DecisionControlTrialCount +
        (PhysicalCandidateCount * TrialsPerCandidate) +
        ConfirmationTrialCount;
}
