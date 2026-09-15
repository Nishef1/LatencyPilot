using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using LatencyPilot.Core.Devices;
using LatencyPilot.Persistence;
using LatencyPilot.Platform.Windows.Devices;
using LatencyPilot.Service;

internal static class ReadOnlyClosureAudit
{
    private const string ServiceName = "LatencyPilot.Observation";
    private const string KernelSessionPrefix = "LatencyPilot-Kernel-";
    private static readonly JsonSerializerOptions RecordJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    internal static int Run(string[] args)
    {
        var options = ParseOptions(args);
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The read-only closure audit is supported only on Windows.");
        }

        var expectedCommit = options["--expected-commit"];
        if (!IsFullRevision(expectedCommit))
        {
            throw new ArgumentException("--expected-commit must be the exact full 40-hex source revision.");
        }

        var repoRoot = FindRepositoryRoot(Environment.CurrentDirectory);
        var checks = new List<ReadOnlyClosureCheck>();

        RunCheck(checks, "source-boundary", () =>
        {
            if (ServiceBoundary.MutationAvailable)
            {
                throw new InvalidOperationException("Public mutation is armed; Phase 2 read-only closure cannot run.");
            }

            return "ServiceBoundary.MutationAvailable=false";
        });

        RunCheck(checks, "git-head", () =>
        {
            var branch = RequireCommand("git", ["-C", repoRoot, "branch", "--show-current"], repoRoot).Trim();
            var head = RequireCommand("git", ["-C", repoRoot, "rev-parse", "HEAD"], repoRoot).Trim();
            var dirty = RequireCommand("git", ["-C", repoRoot, "status", "--porcelain", "--untracked-files=normal"], repoRoot);

            if (!string.Equals(branch, "main", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Current branch is '{branch}', not main.");
            }
            if (!string.Equals(head, expectedCommit, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"HEAD is {head}, expected {expectedCommit}.");
            }
            if (!string.IsNullOrWhiteSpace(dirty))
            {
                throw new InvalidOperationException("The repository working tree is not clean.");
            }

            return $"main clean at {head}";
        });

        string? repository = null;
        RunCheck(checks, "remote-main", () =>
        {
            repository = RequireCommand(
                "gh",
                ["repo", "view", "--json", "nameWithOwner", "--jq", ".nameWithOwner"],
                repoRoot).Trim();
            if (string.IsNullOrWhiteSpace(repository))
            {
                throw new InvalidOperationException("GitHub repository identity could not be resolved.");
            }

            var remoteMain = RequireCommand(
                "gh",
                ["api", $"repos/{repository}/commits/main", "--jq", ".sha"],
                repoRoot).Trim();
            if (!string.Equals(remoteMain, expectedCommit, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"GitHub main is {remoteMain}, expected {expectedCommit}.");
            }

            return $"{repository} main={remoteMain}";
        });

        RunCheck(checks, "exact-green-tests", () =>
        {
            if (string.IsNullOrWhiteSpace(repository))
            {
                throw new InvalidOperationException("Remote repository identity is unavailable.");
            }

            var json = RequireCommand(
                "gh",
                [
                    "run", "list",
                    "--repo", repository,
                    "--workflow", "ci.yml",
                    "--commit", expectedCommit,
                    "--status", "completed",
                    "--limit", "10",
                    "--json", "databaseId,headSha,conclusion,url",
                ],
                repoRoot);
            using var document = JsonDocument.Parse(json);
            var match = document.RootElement.EnumerateArray().FirstOrDefault(run =>
                run.TryGetProperty("headSha", out var sha) &&
                string.Equals(sha.GetString(), expectedCommit, StringComparison.OrdinalIgnoreCase) &&
                run.TryGetProperty("conclusion", out var conclusion) &&
                string.Equals(conclusion.GetString(), "success", StringComparison.Ordinal));
            if (match.ValueKind == JsonValueKind.Undefined)
            {
                throw new InvalidOperationException("No successful Tests workflow exists for the exact expected commit.");
            }

            var runId = match.GetProperty("databaseId").GetInt64();
            return $"Tests run {runId} succeeded for exact HEAD";
        });

        RunCheck(checks, "service", () =>
        {
            var script =
                "$s=Get-CimInstance Win32_Service -Filter \"Name='" + ServiceName + "'\" -ErrorAction Stop;" +
                "[Console]::Out.Write($s.State + '|' + $s.PathName)";
            var output = RequireCommand(
                "powershell.exe",
                ["-NoProfile", "-NonInteractive", "-Command", script],
                repoRoot).Trim();
            var separator = output.IndexOf('|');
            if (separator <= 0)
            {
                throw new InvalidDataException("Installed Service state/path output was malformed.");
            }

            var state = output[..separator];
            var configuredPath = output[(separator + 1)..];
            if (!string.Equals(state, "Running", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"{ServiceName} is '{state}', not Running.");
            }

            var servicePath = ResolveServiceExecutablePath(configuredPath);
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (string.IsNullOrWhiteSpace(programFiles))
            {
                throw new InvalidOperationException("Program Files could not be resolved for Service identity verification.");
            }

            var expectedServicePath = Path.GetFullPath(Path.Combine(
                programFiles,
                "LatencyPilot",
                "Service",
                "LatencyPilot.Service.exe"));
            if (!string.Equals(servicePath, expectedServicePath, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Service executable path is '{servicePath}', expected '{expectedServicePath}'.");
            }
            if (!File.Exists(servicePath))
            {
                throw new FileNotFoundException("Installed Service executable was not found.", servicePath);
            }

            var productVersion = FileVersionInfo.GetVersionInfo(servicePath).ProductVersion;
            if (!SourceRevisionIdentity.MatchesExpectedCommit(productVersion, expectedCommit))
            {
                throw new InvalidOperationException(
                    $"Installed Service ProductVersion '{productVersion ?? "<missing>"}' does not contain exact source revision {expectedCommit}.");
            }

            return $"Running from exact protected path; binary source={expectedCommit}";
        });

        RunCheck(checks, "mutation-journal", () =>
        {
            var unresolved = MutationJournalReadOnlyInspector.GetUnresolved(MutationJournal.GetDefaultDatabasePath());
            if (unresolved.Count != 0)
            {
                throw new InvalidOperationException($"Mutation journal has {unresolved.Count} unresolved entr{(unresolved.Count == 1 ? "y" : "ies")}.");
            }

            return "unresolved=0";
        });

        RunCheck(checks, "stale-kernel-etw", () =>
        {
            var sessions = RequireCommand("logman.exe", ["query", "-ets"], repoRoot);
            var stale = sessions
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(line => line.Contains(KernelSessionPrefix, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (stale.Length != 0)
            {
                throw new InvalidOperationException("Stale LatencyPilot kernel ETW session(s): " + string.Join(", ", stale));
            }

            return "no active LatencyPilot-Kernel-* session";
        });

        DeviceInventorySnapshot? inventory = null;
        RunCheck(checks, "representative-devices", () =>
        {
            inventory = DeviceInventoryReader.CapturePresentDevices();
            var selected = RepresentativeDeviceEvidenceSelector.Select(inventory);
            var display = selected.Count(static item => item.Kind == RepresentativeDeviceKind.DisplayAdapter);
            var network = selected.Count(static item => item.Kind == RepresentativeDeviceKind.NetworkAdapter);
            var xhci = selected.Count(static item => item.Kind == RepresentativeDeviceKind.XhciController);
            if (display == 0 || network == 0 || xhci == 0)
            {
                throw new InvalidOperationException(
                    $"Representative inventory incomplete: display={display}, network={network}, xHCI={xhci}.");
            }

            return $"display={display}, network={network}, xHCI={xhci}";
        });

        RunCheck(checks, "usb-topology", () =>
        {
            if (inventory is null)
            {
                throw new InvalidOperationException("Present-device inventory is unavailable.");
            }

            var topology = UsbTopologyReader.Capture(inventory);
            var connected = topology.Ports.Count(static port => port.ConnectionStatus == UsbPortConnectionStatus.Connected);
            var controllerLinked = topology.Ports.Count(static port => !string.IsNullOrWhiteSpace(port.HostControllerInstanceId));
            if (topology.Ports.Count == 0 || connected == 0 || controllerLinked == 0)
            {
                throw new InvalidOperationException(
                    $"USB topology is not closure-ready: ports={topology.Ports.Count}, connected={connected}, xHCI-linked={controllerLinked}, errors={topology.Errors.Count}.");
            }

            return $"ports={topology.Ports.Count}, connected={connected}, xHCI-linked={controllerLinked}, read-errors={topology.Errors.Count}";
        });

        RunCheck(checks, "network-rss", () =>
        {
            var coverage = NetworkRssInspectionCoverage.Evaluate(NetworkRssReader.Capture());
            if (!coverage.IsUsable)
            {
                throw new InvalidOperationException(
                    $"RSS inspection is not closure-ready: {coverage.Reason} " +
                    $"rows={coverage.ProviderRowCount}, PnP-correlated={coverage.PnpCorrelatedRowCount}.");
            }

            return $"provider=Available, rows={coverage.ProviderRowCount}, PnP-correlated={coverage.PnpCorrelatedRowCount}";
        });

        RunCheck(checks, "realworld-baseline", () => VerifyBaseline(
            repoRoot,
            options["--realworld-baseline"],
            expectedCommit,
            "RealWorld"));
        RunCheck(checks, "controlled-idle-baseline", () => VerifyBaseline(
            repoRoot,
            options["--controlled-idle-baseline"],
            expectedCommit,
            "IdleBaseline"));

        var passed = checks.All(static check => check.Passed);
        var record = new ReadOnlyClosureRecord(
            "latencypilot-readonly-closure-audit-v1",
            DateTimeOffset.UtcNow,
            expectedCommit,
            passed,
            checks,
            [
                "Independent DPC/ISR attribution plausibility comparison where practical.",
                "App-close/Service-restart/partial-baseline interruption and active-console-session rejection exercise.",
                "Light/Dark/High Contrast, narrow/text scaling, keyboard-only and screen-reader/UIA pass.",
                "Explicit before/after machine-state review confirming read-only validation caused no unrelated system mutation.",
            ]);

        var outputPath = Path.GetFullPath(options["--output"]);
        var outputDirectory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }
        File.WriteAllText(
            outputPath,
            JsonSerializer.Serialize(record, RecordJsonOptions));

        foreach (var check in checks)
        {
            Console.WriteLine($"{(check.Passed ? "PASS" : "FAIL")} {check.Name}: {check.Summary}");
        }
        Console.WriteLine($"audit-record={outputPath}");
        Console.WriteLine($"automated-readonly-preflight={(passed ? "passed" : "failed")}");
        Console.WriteLine("phase2-closure=manual-evidence-still-required");
        return passed ? 0 : 3;
    }

    private static string VerifyBaseline(
        string repoRoot,
        string evidencePath,
        string expectedCommit,
        string expectedScenario)
    {
        var fullPath = Path.GetFullPath(evidencePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Baseline evidence file was not found.", fullPath);
        }

        using (var document = JsonDocument.Parse(File.ReadAllBytes(fullPath)))
        {
            var root = document.RootElement;
            var sourceRevision = root.GetProperty("sourceRevisionId").GetString();
            var scenario = root.GetProperty("measurementContext").GetProperty("scenario").GetString();
            if (!string.Equals(sourceRevision, expectedCommit, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Baseline source revision '{sourceRevision}' does not exactly match '{expectedCommit}'.");
            }
            if (!string.Equals(scenario, expectedScenario, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Baseline scenario is '{scenario}', expected '{expectedScenario}'.");
            }
        }

        var digest = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(fullPath))).ToLowerInvariant();
        var verifier = Path.Combine(repoRoot, "scripts", "Verify-Evidence.ps1");
        var result = RunCommand(
            "powershell.exe",
            [
                "-NoProfile",
                "-NonInteractive",
                "-ExecutionPolicy", "Bypass",
                "-File", verifier,
                "-Path", fullPath,
                "-ExpectedCommit", expectedCommit,
                "-ExpectedSha256", digest,
                "-RequireCleanCapture",
                "-RequireValidBaseline",
            ],
            repoRoot);
        if (result.ExitCode != 0)
        {
            throw new InvalidDataException(
                $"Canonical evidence verification failed (exit {result.ExitCode}): {SummarizeFailure(result)}");
        }

        return $"scenario={expectedScenario}, SHA-256={digest}, canonical verification passed";
    }

    private static string ResolveServiceExecutablePath(string configuredPath)
    {
        var value = configuredPath.Trim();
        if (value.Length == 0)
        {
            throw new InvalidDataException("Installed Service executable path is empty.");
        }

        string executablePath;
        if (value[0] == '"')
        {
            var closingQuote = value.IndexOf('"', 1);
            if (closingQuote <= 1)
            {
                throw new InvalidDataException($"Installed Service executable path is malformed: {configuredPath}");
            }

            executablePath = value[1..closingQuote];
        }
        else
        {
            var executableEnd = value.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (executableEnd < 0)
            {
                throw new InvalidDataException($"Installed Service executable path is malformed: {configuredPath}");
            }

            executablePath = value[..(executableEnd + 4)];
        }

        return Path.GetFullPath(executablePath);
    }

    private static void RunCheck(
        List<ReadOnlyClosureCheck> checks,
        string name,
        Func<string> action)
    {
        try
        {
            checks.Add(new ReadOnlyClosureCheck(name, true, action()));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            checks.Add(new ReadOnlyClosureCheck(name, false, exception.Message));
        }
    }

    private static string RequireCommand(string fileName, string[] arguments, string workingDirectory)
    {
        var result = RunCommand(fileName, arguments, workingDirectory);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"{fileName} exited with code {result.ExitCode}: {SummarizeFailure(result)}");
        }

        return result.StandardOutput;
    }

    private static CommandResult RunCommand(string fileName, string[] arguments, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Unable to start '{fileName}'.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        Task.WaitAll(stdout, stderr);
        return new CommandResult(process.ExitCode, stdout.Result, stderr.Result);
    }

    private static string SummarizeFailure(CommandResult result)
    {
        var message = string.IsNullOrWhiteSpace(result.StandardError)
            ? result.StandardOutput
            : result.StandardError;
        message = message.Trim();
        return message.Length <= 800 ? message : message[^800..];
    }

    private static string FindRepositoryRoot(string startDirectory)
    {
        var directory = new DirectoryInfo(Path.GetFullPath(startDirectory));
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LatencyPilot.slnx")) &&
                Directory.Exists(Path.Combine(directory.FullName, ".git")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Run audit-readonly from the LatencyPilot repository or one of its subdirectories.");
    }

    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        var required = new HashSet<string>(StringComparer.Ordinal)
        {
            "--expected-commit",
            "--realworld-baseline",
            "--controlled-idle-baseline",
            "--output",
        };
        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        for (var index = 1; index < args.Length; index++)
        {
            var option = args[index];
            if (!required.Contains(option))
            {
                throw new ArgumentException($"Option '{option}' is not allowed for audit-readonly.");
            }
            if (values.ContainsKey(option))
            {
                throw new ArgumentException($"Option '{option}' was specified more than once.");
            }
            if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Option '{option}' requires a value.");
            }

            values.Add(option, args[++index]);
        }

        var missing = required.Where(option => !values.ContainsKey(option)).ToArray();
        if (missing.Length != 0)
        {
            throw new ArgumentException("audit-readonly is missing required option(s): " + string.Join(", ", missing));
        }

        return values;
    }

    private static bool IsFullRevision(string value) =>
        value.Length == 40 && value.All(Uri.IsHexDigit);

    private sealed record CommandResult(int ExitCode, string StandardOutput, string StandardError);

    private sealed record ReadOnlyClosureCheck(string Name, bool Passed, string Summary);

    private sealed record ReadOnlyClosureRecord(
        string Schema,
        DateTimeOffset CapturedAtUtc,
        string SourceRevision,
        bool AutomatedChecksPassed,
        IReadOnlyList<ReadOnlyClosureCheck> Checks,
        IReadOnlyList<string> ManualEvidenceRemaining);
}
