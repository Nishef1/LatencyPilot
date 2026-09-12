namespace LatencyPilot.Core.System;

public sealed record SystemInventorySnapshot(
    string OperatingSystem,
    string OsArchitecture,
    string ProcessArchitecture,
    int ProcessAvailableProcessorCount,
    DateTimeOffset CapturedAtUtc);
