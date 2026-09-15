namespace LatencyPilot.Benchmarking.Optimization;

public static class GpuOptimizationSourceRevisionPolicy
{
    public const int FullRevisionLength = 40;

    public static bool IsValidFullRevision(string? revision) =>
        revision is { Length: FullRevisionLength } &&
        revision.All(Uri.IsHexDigit);

    public static bool IsExactMatch(string? expectedRevision, string? evidenceRevision) =>
        IsValidFullRevision(expectedRevision) &&
        IsValidFullRevision(evidenceRevision) &&
        string.Equals(expectedRevision, evidenceRevision, StringComparison.OrdinalIgnoreCase);
}
