using Microsoft.UI.Xaml.Controls;

namespace LatencyPilot.App;

public sealed partial class MainWindow
{
    private void ApplyBuildProvenanceUi()
    {
        var productVersion = GetProductVersion();
        var buildInfo = TryReadBuildInfo();
        if (buildInfo is null)
        {
            VersionText.Text = $"v{productVersion}";
            ToolTipService.SetToolTip(VersionText, "Build source revision is unavailable in this output.");
            return;
        }

        buildInfo.TryGetValue("commit", out var commit);
        buildInfo.TryGetValue("source_head", out var sourceHead);
        buildInfo.TryGetValue("source_state", out var sourceState);

        if (IsRevisionId(commit))
        {
            VersionText.Text = $"v{productVersion} · {commit![..7]}";
            ToolTipService.SetToolTip(
                VersionText,
                $"Exact clean source revision: {commit}");
            return;
        }

        if (string.Equals(sourceState, "dirty", StringComparison.OrdinalIgnoreCase))
        {
            VersionText.Text = $"v{productVersion} · dirty";
            ToolTipService.SetToolTip(
                VersionText,
                IsRevisionId(sourceHead)
                    ? $"Local build from dirty working tree at HEAD {sourceHead}. Exact source revision is intentionally not claimed for evidence."
                    : "Local build from a dirty working tree. Exact source revision is intentionally not claimed for evidence.");
            return;
        }

        VersionText.Text = $"v{productVersion}";
        ToolTipService.SetToolTip(VersionText, "Build metadata is present, but no exact clean source revision is available.");
    }

    private static Dictionary<string, string>? TryReadBuildInfo()
    {
        foreach (var path in new[]
                 {
                     Path.Combine(AppContext.BaseDirectory, "BUILD_INFO.txt"),
                     Path.Combine(AppContext.BaseDirectory, "..", "BUILD_INFO.txt"),
                 })
        {
            try
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var line in File.ReadLines(path))
                {
                    var separator = line.IndexOf('=');
                    if (separator <= 0)
                    {
                        continue;
                    }

                    values[line[..separator].Trim()] = line[(separator + 1)..].Trim();
                }

                return values;
            }
            catch (Exception exception) when (
                exception is IOException or
                UnauthorizedAccessException)
            {
                Logger.Warning(exception, "Build provenance metadata could not be read from {BuildInfoPath}.", path);
            }
        }

        return null;
    }

    private static bool IsRevisionId(string? value) =>
        value is { Length: >= 7 and <= 40 } && value.All(Uri.IsHexDigit);
}
