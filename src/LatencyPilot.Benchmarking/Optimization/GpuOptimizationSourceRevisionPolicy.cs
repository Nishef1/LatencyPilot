namespace LatencyPilot.Benchmarking.Optimization;

public static class GpuOptimizationSourceRevisionPolicy
{
    public const int FullRevisionLength = 40;

    public static bool IsExactMatch(string? expectedRevision, string? evidenceRevision) =>
        IsFullRevision(expectedRevision) &&
        IsFullRevision(evidenceRevision) &&
        string.Equals(expectedRevision, evidenceRevision, StringComparison.OrdinalIgnoreCase);

    private static bool IsFullRevision(string? revision) =>
        revision is { Length: FullRevisionLength } &&
        revision.All(Uri.IsHexDigit);
}
