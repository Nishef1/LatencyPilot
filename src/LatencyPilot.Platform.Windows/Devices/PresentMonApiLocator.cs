namespace LatencyPilot.Platform.Windows.Devices;

internal static class PresentMonApiLocator
{
    internal static string? Resolve(string? explicitPath = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            var fullPath = Path.GetFullPath(explicitPath);
            return File.Exists(fullPath) ? fullPath : null;
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (string.IsNullOrWhiteSpace(programFiles))
        {
            return null;
        }

        string[] candidates =
        [
            Path.Combine(programFiles, "Intel", "PresentMon", "PresentMonAPI2.dll"),
            Path.Combine(programFiles, "Intel", "PresentMon", "SDK", "PresentMonAPI2.dll"),
            Path.Combine(programFiles, "Intel", "PresentMonSharedService", "PresentMonAPI2.dll"),
        ];

        return candidates.FirstOrDefault(File.Exists);
    }
}
