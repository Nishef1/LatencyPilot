using LatencyPilot.Benchmarking.Candidates;
using LatencyPilot.Core.System;

namespace LatencyPilot.Benchmarking.Optimization;

public sealed class GpuAutoAffinityProgressPlan
{
    private const int ControlWarmupCount = 1;
    private const int ScreeningUnitsPerCandidate = 2; // transition warm-up + one scored run
    private const int FinalistUnitsPerCandidate = 3; // transition warm-up + two scored re-tests
    private const int MaximumFinalistCandidates = 3;
    private const int FinalVerificationCount = 1;
    private readonly HashSet<int> physicalCoreIndexes;

    private GpuAutoAffinityProgressPlan(IReadOnlyList<GpuAffinityCandidate> physicalCandidates)
    {
        PhysicalCandidateCount = physicalCandidates.Count;
        physicalCoreIndexes = physicalCandidates.Select(static candidate => candidate.PhysicalCoreIndex).ToHashSet();
    }

    public int PhysicalCandidateCount { get; }

    // Kept for compatibility with the development progress surface. The v1
    // search no longer performs a separate SMT sibling-refinement phase.
    public int MaximumRefinementCandidateCount => 0;

    public int FinalistCandidateCount => Math.Min(MaximumFinalistCandidates, PhysicalCandidateCount);

    public int InitialTotalUnits => GetBaseTotalUnits();

    public int GetRefinementCandidateCount(int physicalCoreIndex)
    {
        ValidatePhysicalCore(physicalCoreIndex);
        return 0;
    }

    public int GetTotalUnitsForFinalist(int physicalCoreIndex)
    {
        ValidatePhysicalCore(physicalCoreIndex);
        return GetBaseTotalUnits();
    }

    public static GpuAutoAffinityProgressPlan Create(
        ProcessorTopologySnapshot topology,
        IEnumerable<ProcessorPressureEvidence> pressureEvidence,
        ProcessorCpuSetSnapshot? cpuSets)
    {
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(pressureEvidence);

        var pressure = pressureEvidence.ToArray();
        var physicalCandidates = GpuAffinityCandidatePlanner.Create(topology, pressure, cpuSets);
        return new GpuAutoAffinityProgressPlan(physicalCandidates);
    }

    private int GetBaseTotalUnits() =>
        ControlWarmupCount +
        (PhysicalCandidateCount * ScreeningUnitsPerCandidate) +
        (FinalistCandidateCount * FinalistUnitsPerCandidate) +
        FinalVerificationCount;

    private void ValidatePhysicalCore(int physicalCoreIndex)
    {
        if (!physicalCoreIndexes.Contains(physicalCoreIndex))
        {
            throw new ArgumentOutOfRangeException(
                nameof(physicalCoreIndex),
                "Physical core is not part of the planned GPU affinity screen.");
        }
    }
}
