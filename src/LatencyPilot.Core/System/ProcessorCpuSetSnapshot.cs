namespace LatencyPilot.Core.System;

public sealed record ProcessorCpuSetEntry(
    uint Id,
    LogicalProcessorId Processor,
    byte CoreIndex,
    byte LastLevelCacheIndex,
    byte NumaNodeIndex,
    byte EfficiencyClass,
    byte SchedulingClass,
    bool Parked,
    bool Allocated,
    bool AllocatedToCurrentProcess,
    bool RealTime,
    ulong AllocationTag);

public sealed record ProcessorCpuSetSnapshot(
    IReadOnlyList<ProcessorCpuSetEntry> CpuSets,
    DateTimeOffset CapturedAtUtc)
{
    private Dictionary<LogicalProcessorId, ProcessorCpuSetEntry>? byProcessor;

    public int Count => CpuSets.Count;

    public int ParkedCount => CpuSets.Count(static cpuSet => cpuSet.Parked);

    public int AllocatedCount => CpuSets.Count(static cpuSet => cpuSet.Allocated);

    public bool HasHeterogeneousEfficiencyClasses => CpuSets
        .Select(static cpuSet => cpuSet.EfficiencyClass)
        .Distinct()
        .Skip(1)
        .Any();

    public ProcessorCpuSetEntry? TryGet(LogicalProcessorId processor)
    {
        byProcessor ??= CpuSets.ToDictionary(static cpuSet => cpuSet.Processor);
        return byProcessor.TryGetValue(processor, out var cpuSet) ? cpuSet : null;
    }
}
