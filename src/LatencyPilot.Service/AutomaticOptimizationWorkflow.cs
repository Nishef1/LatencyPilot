namespace LatencyPilot.Service;

public enum AutomaticOptimizationStage
{
    OriginalMeasurement = 0,
    GpuAffinity = 1,
    PrimaryInputUsbXhci = 2,
    FinalVerification = 3,
    Report = 4,
}

public enum AutomaticOptimizationStageDisposition
{
    Completed = 0,
    NotReady = 1,
    RebootPending = 2,
    Failed = 3,
}

public sealed record AutomaticOptimizationStageResult(
    AutomaticOptimizationStage Stage,
    AutomaticOptimizationStageDisposition Disposition,
    string Detail,
    string? BeforeEvidenceId,
    string? AfterEvidenceId);

public sealed record AutomaticOptimizationWorkflowResult(
    bool Completed,
    AutomaticOptimizationStage TerminalStage,
    IReadOnlyList<AutomaticOptimizationStageResult> Stages)
{
    public bool RebootPending =>
        Stages.Count > 0 &&
        Stages[Stages.Count - 1].Disposition == AutomaticOptimizationStageDisposition.RebootPending;
}

public interface IAutomaticOptimizationStageRunner
{
    ValueTask<AutomaticOptimizationStageResult> RunStageAsync(
        AutomaticOptimizationStage stage,
        CancellationToken cancellationToken);
}

/// <summary>
/// Optional composition contract for a future "Optimize all" experience.
/// Individual subsystem benchmarks remain independently runnable and must
/// discover CPU reservations from current machine policy state rather than
/// consuming hidden winner/provenance output from an earlier stage. This
/// orchestrator sequences actions only; it does not own their ranking,
/// verification, rollback, or reservation semantics.
/// </summary>
public sealed class AutomaticOptimizationWorkflow
{
    private static readonly AutomaticOptimizationStage[] Stages =
    [
        AutomaticOptimizationStage.OriginalMeasurement,
        AutomaticOptimizationStage.GpuAffinity,
        AutomaticOptimizationStage.PrimaryInputUsbXhci,
        AutomaticOptimizationStage.FinalVerification,
        AutomaticOptimizationStage.Report,
    ];

    public static IReadOnlyList<AutomaticOptimizationStage> OrderedStages => Stages;

    public static async ValueTask<AutomaticOptimizationWorkflowResult> RunAsync(
        IAutomaticOptimizationStageRunner runner,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runner);
        var results = new List<AutomaticOptimizationStageResult>(Stages.Length);
        foreach (var stage in Stages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await runner.RunStageAsync(stage, cancellationToken).ConfigureAwait(false);
            if (result.Stage != stage)
            {
                throw new InvalidOperationException(
                    $"Optimize stage runner returned {result.Stage} while {stage} was requested.");
            }
            if (string.IsNullOrWhiteSpace(result.Detail))
            {
                throw new InvalidDataException($"Optimize stage {stage} returned no diagnostic detail.");
            }

            results.Add(result);
            if (result.Disposition != AutomaticOptimizationStageDisposition.Completed)
            {
                return new AutomaticOptimizationWorkflowResult(false, stage, results.AsReadOnly());
            }
        }

        return new AutomaticOptimizationWorkflowResult(
            true,
            AutomaticOptimizationStage.Report,
            results.AsReadOnly());
    }
}
