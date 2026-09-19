namespace LatencyPilot.Benchmarking.Optimization;

public enum GpuOptimizationSourceState
{
    EvidenceReady = 0,
    DevelopmentOnly = 1,
    Blocked = 2,
}

public sealed record GpuOptimizationSourceAssessment(
    GpuOptimizationSourceState State,
    string HeadRevision,
    string Branch,
    bool HasWorkingTreeChanges,
    string Reason)
{
    public bool CanRun => State is GpuOptimizationSourceState.EvidenceReady or GpuOptimizationSourceState.DevelopmentOnly;

    public bool IsClosureEligible => State == GpuOptimizationSourceState.EvidenceReady;
}

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

    public static GpuOptimizationSourceAssessment Assess(
        string? headRevision,
        string? branch,
        bool hasWorkingTreeChanges,
        string? expectedRevision)
    {
        var normalizedHead = headRevision?.Trim().ToLowerInvariant() ?? string.Empty;
        var normalizedBranch = branch?.Trim() ?? string.Empty;

        if (!IsValidFullRevision(normalizedHead))
        {
            return new GpuOptimizationSourceAssessment(
                GpuOptimizationSourceState.Blocked,
                normalizedHead,
                normalizedBranch,
                hasWorkingTreeChanges,
                "Gate A requires a valid 40-character Git HEAD revision.");
        }

        if (!IsValidFullRevision(expectedRevision) || !IsExactMatch(expectedRevision, normalizedHead))
        {
            return new GpuOptimizationSourceAssessment(
                GpuOptimizationSourceState.Blocked,
                normalizedHead,
                normalizedBranch,
                hasWorkingTreeChanges,
                "Gate A source HEAD does not match the exact expected revision.");
        }

        if (!string.Equals(normalizedBranch, "main", StringComparison.Ordinal))
        {
            return new GpuOptimizationSourceAssessment(
                GpuOptimizationSourceState.Blocked,
                normalizedHead,
                normalizedBranch,
                hasWorkingTreeChanges,
                "Gate A requires the main branch; other branches are not eligible for owner physical validation.");
        }

        if (hasWorkingTreeChanges)
        {
            return new GpuOptimizationSourceAssessment(
                GpuOptimizationSourceState.DevelopmentOnly,
                normalizedHead,
                normalizedBranch,
                true,
                "Local changes are present. The run may exercise hardware behavior and rollback, but it cannot close the physical Gate A or arm product mutation.");
        }

        return new GpuOptimizationSourceAssessment(
            GpuOptimizationSourceState.EvidenceReady,
            normalizedHead,
            normalizedBranch,
            false,
            "Clean main checkout at the exact expected revision. A successful run may contribute to physical Gate A closure.");
    }
}
