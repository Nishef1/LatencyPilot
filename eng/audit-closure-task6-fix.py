from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def replace_once(path: str, old: str, new: str) -> None:
    target = ROOT / path
    text = target.read_text(encoding="utf-8")
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{path}: expected one anchor, found {count}")
    target.write_text(text.replace(old, new, 1), encoding="utf-8", newline="\n")


# DeviceInterruptMutationStepResult exposes Entry; GPU rollback intentionally
# continues to expose JournalEntry, so patch only the device-interrupt block.
replace_once(
    "src/LatencyPilot.Service/GlobalRestoreBaseline.cs",
    '''        var rollback = deviceTransaction.Rollback(action.ExperimentId);\n        if (rollback.JournalEntry.State == MutationJournalState.RollbackRebootPending)\n            throw new InvalidOperationException($"Experiment {action.ExperimentId:D} restored stored policy but requires reboot verification before Restore original settings can continue.");\n        if (rollback.JournalEntry.State != MutationJournalState.Reverted || !rollback.OriginalStateRestored)\n            throw new InvalidOperationException(rollback.JournalEntry.FailureReason ?? $"Device-interrupt experiment {action.ExperimentId:D} did not reach Reverted.");\n''',
    '''        var rollback = deviceTransaction.Rollback(action.ExperimentId);\n        if (rollback.Entry.State == MutationJournalState.RollbackRebootPending)\n            throw new InvalidOperationException($"Experiment {action.ExperimentId:D} restored stored policy but requires reboot verification before Restore original settings can continue.");\n        if (rollback.Entry.State != MutationJournalState.Reverted || !rollback.OriginalStateRestored)\n            throw new InvalidOperationException(rollback.Entry.FailureReason ?? $"Device-interrupt experiment {action.ExperimentId:D} did not reach Reverted.");\n''')

# Keep analyzer-clean code: the workflow result is indexable and the runner
# method has no instance state, so use direct indexing and a static entrypoint.
replace_once(
    "src/LatencyPilot.Service/AutomaticOptimizationWorkflow.cs",
    '''    public bool RebootPending =>\n        Stages.LastOrDefault()?.Disposition == AutomaticOptimizationStageDisposition.RebootPending;\n''',
    '''    public bool RebootPending =>\n        Stages.Count > 0 &&\n        Stages[Stages.Count - 1].Disposition == AutomaticOptimizationStageDisposition.RebootPending;\n''')
replace_once(
    "src/LatencyPilot.Service/AutomaticOptimizationWorkflow.cs",
    '''    public async ValueTask<AutomaticOptimizationWorkflowResult> RunAsync(\n''',
    '''    public static async ValueTask<AutomaticOptimizationWorkflowResult> RunAsync(\n''')

# Match the static workflow entrypoint in the integration regression.
test_path = ROOT / "tests/LatencyPilot.CriticalTests/AuditClosureIntegrationTests.cs"
test_text = test_path.read_text(encoding="utf-8")
old = "new AutomaticOptimizationWorkflow().RunAsync("
count = test_text.count(old)
if count != 2:
    raise RuntimeError(f"AuditClosureIntegrationTests.cs: expected two workflow calls, found {count}")
test_path.write_text(
    test_text.replace(old, "AutomaticOptimizationWorkflow.RunAsync("),
    encoding="utf-8",
    newline="\n")
