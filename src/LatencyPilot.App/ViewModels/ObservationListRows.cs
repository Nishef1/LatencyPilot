namespace LatencyPilot.App.ViewModels;

internal sealed record ModuleObservationRow(
    string Name,
    string EventSummary,
    string DurationSummary);

internal sealed record ProcessorObservationRow(
    string Name,
    string EventSummary,
    string TailSummary);
