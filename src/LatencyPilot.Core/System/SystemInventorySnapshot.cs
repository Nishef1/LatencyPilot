namespace LatencyPilot.Core.System;

public sealed record SystemInventorySnapshot(
    string OperatingSystem,
    string OsArchitecture,
    string ProcessArchitecture,
    int LogicalProcessorCount,
    DateTimeOffset CapturedAtUtc);
