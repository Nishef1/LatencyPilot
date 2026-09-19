namespace LatencyPilot.Service;

public enum AutomaticOptimizationStage
{
    OriginalMeasurement = 0,
    GpuAffinity = 1,
    Msi = 2,
    PrimaryInputUsbXhci = 3,
    FinalVerification = 4,
    Report = 5,
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
/// Internal sequencing contract for one-at-a-time optimization. It deliberately
/// does not arm public mutation; ServiceBoundary.MutationAvailable remains the
/// exact-revision physical-validation gate.
/// </summary>
public sealed class AutomaticOptimizationWorkflow
{
    private static readonly AutomaticOptimizationStage[] Stages =
    [
        AutomaticOptimizationStage.OriginalMeasurement,
        AutomaticOptimizationStage.GpuAffinity,
        AutomaticOptimizationStage.Msi,
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
