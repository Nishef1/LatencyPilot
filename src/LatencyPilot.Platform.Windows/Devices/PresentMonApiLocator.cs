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

        var candidates = new List<string>
        {
            Path.Combine(AppContext.BaseDirectory, "PresentMon", "PresentMonAPI2.dll"),
        };

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            candidates.Add(Path.Combine(programFiles, "Intel", "PresentMon", "PresentMonAPI2.dll"));
            candidates.Add(Path.Combine(programFiles, "Intel", "PresentMon", "SDK", "PresentMonAPI2.dll"));
        }

        return candidates.FirstOrDefault(File.Exists);
    }
}
