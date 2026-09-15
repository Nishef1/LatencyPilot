using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using LatencyPilot.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace LatencyPilot.App;

public sealed partial class MainWindow
{
    private Button? _gateAValidationButton;
    private string? _gateARepositoryRoot;

    internal void InitializeGateAValidationExperience()
    {
        _gateARepositoryRoot = TryFindRepositoryRoot();
        if (_gateARepositoryRoot is null)
        {
            return;
        }

        _gateAValidationButton = new Button
        {
            Content = "Validate GPU · one click",
            MinHeight = 40,
            Padding = new Thickness(16, 8, 16, 8),
            Style = (Style)Application.Current.Resources["SecondaryButtonStyle"],
        };
        AutomationProperties.SetName(_gateAValidationButton, "Validate GPU with one click");
        AutomationProperties.SetHelpText(
            _gateAValidationButton,
            "Captures a fresh steady real-world baseline, requests administrator consent once, tests one bounded reversible GPU interrupt-affinity candidate, verifies runtime placement, restores the original state, exercises recovery, and saves a JSON report. Keep the warmed workload running while validation executes.");
        ToolTipService.SetToolTip(
            _gateAValidationButton,
            "One-click owner validation: fresh baseline → candidate → apply → runtime proof → automatic restore/recovery → JSON report. Keep the same warmed game/workload active; UAC appears once.");
        _gateAValidationButton.Click += GateAValidationButton_Click;
        HeaderActions.Children.Add(_gateAValidationButton);
    }

    private async void GateAValidationButton_Click(object sender, RoutedEventArgs e)
    {
        if (_gateAValidationButton is null || _gateARepositoryRoot is null || _measurementBusy)
        {
            return;
        }

        var windowHiddenForWorkload = false;
        _gateAValidationButton.IsEnabled = false;
        try
        {
            if (_measurementScenarioComboBox is not null)
            {
                _measurementScenarioComboBox.SelectedIndex = 0;
            }

            var dotnetExecutable = ResolveDotnetExecutable();
            EvidenceExportStatusText.Text =
                "One-click GPU validation started. LatencyPilot will hide itself so the same warmed game/scene stays foreground, capture the baseline, request UAC once, test one candidate, restore the original state, and write a report.";

            AppWindow.Hide();
            windowHiddenForWorkload = true;
            await Task.Delay(TimeSpan.FromSeconds(2));

            await CaptureBaselineAsync();
            if (string.IsNullOrWhiteSpace(_latestEvidenceJson))
            {
                throw new InvalidOperationException("A completed baseline evidence document was not produced.");
            }

            var sourceRevision = ReadExactSourceRevision(_latestEvidenceJson);
            var validationDirectory = GetValidationDirectory();
            Directory.CreateDirectory(validationDirectory);
            var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfffZ", System.Globalization.CultureInfo.InvariantCulture);
            var baselinePath = Path.Combine(validationDirectory, $"gate-a-baseline-{stamp}.json");
            var reportPath = Path.Combine(validationDirectory, $"gate-a-report-{stamp}.json");
            await File.WriteAllTextAsync(baselinePath, _latestEvidenceJson);

            var helperProject = Path.Combine(
                _gateARepositoryRoot,
                "tools",
                "LatencyPilot.GateAValidation",
                "LatencyPilot.GateAValidation.csproj");
            if (!File.Exists(helperProject))
            {
                throw new FileNotFoundException("The one-click Gate A validation helper project was not found.", helperProject);
            }

            var dotnetDirectory = Path.GetDirectoryName(dotnetExecutable)
                ?? throw new InvalidOperationException("The resolved dotnet executable has no parent directory.");
            var startInfo = new ProcessStartInfo
            {
                FileName = dotnetExecutable,
                WorkingDirectory = dotnetDirectory,
                UseShellExecute = true,
                Verb = "runas",
            };
            foreach (var argument in new[]
                     {
                         "run",
                         "--project", helperProject,
                         "--configuration", "Release",
                         "--",
                         "--repo-root", _gateARepositoryRoot,
                         "--evidence", baselinePath,
                         "--expected-commit", sourceRevision,
                         "--output", reportPath,
                         "--confirm-physical-mutation",
                     })
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var helper = Process.Start(startInfo)
                ?? throw new InvalidOperationException("The elevated Gate A validation helper could not be started.");
            await helper.WaitForExitAsync();

            if (!File.Exists(reportPath))
            {
                throw new InvalidOperationException(
                    $"Gate A helper exited with code {helper.ExitCode}, but did not produce its JSON report.");
            }

            using var reportDocument = JsonDocument.Parse(await File.ReadAllTextAsync(reportPath));
            var reportRoot = reportDocument.RootElement;
            var status = reportRoot.TryGetProperty("status", out var statusElement)
                ? statusElement.GetString() ?? "Unknown"
                : "Unknown";
            var passed = reportRoot.TryGetProperty("passed", out var passedElement) && passedElement.GetBoolean();
            EvidenceExportStatusText.Text = passed
                ? $"GPU validation passed and the original state was restored. Report: {reportPath}"
                : $"GPU validation finished safely with status {status}. The report contains the exact blocker and recovery evidence: {reportPath}";

            TryRevealReport(reportPath);
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            EvidenceExportStatusText.Text = "GPU validation was cancelled at the UAC prompt. No Gate A mutation was started.";
        }
        catch (Exception exception) when (exception is
            IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            InvalidOperationException or
            JsonException or
            Win32Exception)
        {
            Logger.Error(exception, "One-click Gate A validation failed before a complete report could be presented.");
            EvidenceExportStatusText.Text = $"GPU validation could not complete: {exception.Message}";
        }
        finally
        {
            if (windowHiddenForWorkload && !AppWindow.IsVisible)
            {
                AppWindow.Show(true);
            }

            _gateAValidationButton.IsEnabled = true;
        }
    }

    private static string ReadExactSourceRevision(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("sourceRevisionId", out var revisionElement))
        {
            throw new InvalidDataException("The baseline does not contain sourceRevisionId provenance.");
        }

        var revision = revisionElement.GetString()?.Trim();
        if (revision is not { Length: 40 } || !revision.All(Uri.IsHexDigit))
        {
            throw new InvalidDataException(
                "One-click Gate A requires a fresh baseline from an exact clean 40-character source revision. Pull/build the current main and capture again.");
        }

        return revision.ToLowerInvariant();
    }

    private static string GetValidationDirectory()
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrWhiteSpace(documents))
        {
            documents = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Documents");
        }

        return Path.Combine(documents, "LatencyPilot", "validation");
    }

    private static string? TryFindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LatencyPilot.slnx")) &&
                Directory.Exists(Path.Combine(directory.FullName, ".git")))
            {
                return directory.FullName;
            }
        }

        return null;
    }

    private static string ResolveDotnetExecutable()
    {
        var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrWhiteSpace(dotnetRoot))
        {
            var rootedCandidate = Path.Combine(dotnetRoot.Trim(), "dotnet.exe");
            if (File.Exists(rootedCandidate))
            {
                return Path.GetFullPath(rootedCandidate);
            }
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            var defaultCandidate = Path.Combine(programFiles, "dotnet", "dotnet.exe");
            if (File.Exists(defaultCandidate))
            {
                return Path.GetFullPath(defaultCandidate);
            }
        }

        var path = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(path))
        {
            foreach (var segment in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var normalizedSegment = segment.Trim('"');
                if (string.IsNullOrWhiteSpace(normalizedSegment))
                {
                    continue;
                }

                var pathCandidate = Path.Combine(normalizedSegment, "dotnet.exe");
                if (File.Exists(pathCandidate))
                {
                    return Path.GetFullPath(pathCandidate);
                }
            }
        }

        throw new FileNotFoundException(
            "dotnet.exe could not be resolved for the elevated Gate A helper. Install the .NET 10 SDK or ensure DOTNET_ROOT/PATH points to it.");
    }

    private static void TryRevealReport(string reportPath)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{reportPath}\"",
                UseShellExecute = true,
            });
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            Logger.Warning(exception, "Gate A report was written, but File Explorer could not reveal it.");
        }
    }
}
