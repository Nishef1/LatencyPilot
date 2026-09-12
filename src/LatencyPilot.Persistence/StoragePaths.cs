namespace LatencyPilot.Persistence;

public static class StoragePaths
{
    public static string GetApplicationDataRoot() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LatencyPilot");
}
