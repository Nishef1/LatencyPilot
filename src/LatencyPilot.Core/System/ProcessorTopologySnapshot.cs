namespace LatencyPilot.Core.System;

public readonly record struct LogicalProcessorId(ushort Group, byte Number)
{
    public override string ToString() => $"{Group}:{Number}";
}

public sealed record ProcessorCoreSnapshot(
    int Index,
    byte EfficiencyClass,
    IReadOnlyList<LogicalProcessorId> LogicalProcessors)
{
    public bool IsSmt => LogicalProcessors.Count > 1;
}

public sealed record ProcessorPackageSnapshot(
    int Index,
    IReadOnlyList<LogicalProcessorId> LogicalProcessors);

public sealed record ProcessorTopologySnapshot(
    IReadOnlyList<ProcessorPackageSnapshot> Packages,
    IReadOnlyList<ProcessorCoreSnapshot> Cores,
    DateTimeOffset CapturedAtUtc)
{
    public int PhysicalCoreCount => Cores.Count;

    public int LogicalProcessorCount => Cores
        .SelectMany(static core => core.LogicalProcessors)
        .Distinct()
        .Count();

    public int ProcessorGroupCount => Cores
        .SelectMany(static core => core.LogicalProcessors)
        .Select(static processor => processor.Group)
        .Distinct()
        .Count();

    public int SmtCoreCount => Cores.Count(static core => core.IsSmt);
}
