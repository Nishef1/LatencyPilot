using LatencyPilot.Core.Benchmarking;

namespace LatencyPilot.Benchmarking.Optimization;

public enum GpuBenchmarkReadinessState
{
    Ready,
    RetryableContamination,
    Inconclusive,
}

public sealed record GpuBenchmarkContaminationContext(
    bool SystemCpuBusyDrifted,
    bool ControlTrialDrifted,
    bool SleepOrResumeDetected,
    bool DeviceResetDetected,
    int RetryAttempt,
    IReadOnlyList<string>? SoftNotes = null)
{
    public static GpuBenchmarkContaminationContext Clean { get; } = new(
        false,
        false,
        false,
        false,
        0);
}

public sealed record GpuBenchmarkReadinessResult(
    string MethodId,
    GpuBenchmarkReadinessState State,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<string> Context)
{
    public bool IsReady => State == GpuBenchmarkReadinessState.Ready;
}

public static class GpuBenchmarkReadiness
{
    public static GpuBenchmarkReadinessResult Evaluate(
        GpuBenchmarkEvidence reference,
        GpuBenchmarkEvidence trial,
        GpuBenchmarkContaminationContext contamination)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(trial);
        ArgumentNullException.ThrowIfNull(contamination);
        if (contamination.RetryAttempt is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(contamination),
                "GPU benchmark contamination supports exactly one bounded retry.");
        }

        var reasons = new List<string>();
        var context = new List<string>();
        if (contamination.SoftNotes is not null)
        {
            context.AddRange(contamination.SoftNotes.Where(static note => !string.IsNullOrWhiteSpace(note)));
        }

        AddInterpreterReasons("Reference", reference, reasons);
        AddInterpreterReasons("Trial", trial, reasons);
        RequireEqual(reference.Schema, trial.Schema, "Evidence schema changed across the benchmark session.", reasons);
        RequireEqual(reference.MethodId, trial.MethodId, "Benchmark method identity changed across the session.", reasons);
        RequireEqual(reference.SourceRevisionId, trial.SourceRevisionId, "Source revision changed across the benchmark session.", reasons);
        RequireEqual(reference.WindowsIdentity, trial.WindowsIdentity, "Windows identity changed across the benchmark session.", reasons);
        RequireEqual(reference.GpuIdentity, trial.GpuIdentity, "GPU identity changed across the benchmark session.", reasons);
        RequireEqual(reference.DriverIdentity, trial.DriverIdentity, "GPU driver identity changed across the benchmark session.", reasons);
        RequireEqual(reference.TopologyIdentity, trial.TopologyIdentity, "Processor topology changed across the benchmark session.", reasons);
        if (reference.BenchmarkProcessId != trial.BenchmarkProcessId)
        {
            reasons.Add("Benchmark process identity changed across the session.");
        }
        RequireEqual(
            reference.FrozenWorkloadIdentity,
            trial.FrozenWorkloadIdentity,
            "Frozen benchmark workload identity changed across the session.",
            reasons);
        if (reference.Seed != trial.Seed)
        {
            reasons.Add("Benchmark deterministic seed changed across the session.");
        }
        if (!reference.WorkerMap.SequenceEqual(trial.WorkerMap))
        {
            reasons.Add("Benchmark worker placement changed across the session.");
        }
        if (reference.D3D12TimestampFrequency != trial.D3D12TimestampFrequency)
        {
            context.Add(
                "D3D12 timestamp frequency changed across benchmark trials. Each GPU-work interval remains comparable because it is converted from ticks with the frequency captured for its measurement window; frequency is provenance, not a session identity invariant.");
        }

        if (contamination.SleepOrResumeDetected)
        {
            reasons.Add("A system sleep, suspend, or resume transition occurred during the benchmark block.");
        }
        if (contamination.DeviceResetDetected)
        {
            reasons.Add("A GPU device reset or removal occurred during the benchmark block.");
        }

        if (contamination.SystemCpuBusyDrifted)
        {
            context.Add(
                "System CPU busy drifted during the benchmark block; this remains context unless GPU/control comparability is also broken.");
        }

        if (reasons.Count > 0)
        {
            return new GpuBenchmarkReadinessResult(
                GpuBenchmarkEvidence.MethodIdValue,
                GpuBenchmarkReadinessState.Inconclusive,
                reasons.AsReadOnly(),
                context.AsReadOnly());
        }

        if (contamination.ControlTrialDrifted)
        {
            reasons.Add(
                contamination.RetryAttempt == 0
                    ? "Original/control benchmark evidence drifted materially; repeat this contaminated block once."
                    : "Original/control benchmark evidence remained non-comparable after the single bounded retry.");
            return new GpuBenchmarkReadinessResult(
                GpuBenchmarkEvidence.MethodIdValue,
                contamination.RetryAttempt == 0
                    ? GpuBenchmarkReadinessState.RetryableContamination
                    : GpuBenchmarkReadinessState.Inconclusive,
                reasons.AsReadOnly(),
                context.AsReadOnly());
        }

        return new GpuBenchmarkReadinessResult(
            GpuBenchmarkEvidence.MethodIdValue,
            GpuBenchmarkReadinessState.Ready,
            [],
            context.AsReadOnly());
    }

    private static void AddInterpreterReasons(
        string label,
        GpuBenchmarkEvidence evidence,
        List<string> reasons)
    {
        var interpretation = GpuBenchmarkEvidenceInterpreter.Interpret(evidence);
        if (interpretation.IsValid)
        {
            return;
        }

        foreach (var reason in interpretation.ValidityReasons)
        {
            reasons.Add($"{label} benchmark evidence is invalid: {reason}");
        }
    }

    private static void RequireEqual(
        string left,
        string right,
        string reason,
        List<string> reasons)
    {
        if (!string.Equals(left, right, StringComparison.Ordinal))
        {
            reasons.Add(reason);
        }
    }
}
