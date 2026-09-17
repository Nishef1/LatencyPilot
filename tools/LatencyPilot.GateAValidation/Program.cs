using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text.Json;
using System.Text.RegularExpressions;
using LatencyPilot.GateAValidation;
using LatencyPilot.Service;

return await GateAOneClickProgram.RunAsync(args);

internal static partial class GateAOneClickProgram
{
    private const string MutationConfirmationFlag = "--confirm-physical-mutation";
    private static readonly TimeSpan WorkloadReturnDelay = TimeSpan.FromSeconds(8);
    private static readonly JsonSerializerOptions ReportJsonOptions = new() { WriteIndented = true };

    internal static async Task<int> RunAsync(string[] args)
    {
        if (args.Contains(GpuAutoAffinityGateARunner.ModeFlag, StringComparer.Ordinal))
        {
            return await GpuAutoAffinityGateARunner.RunAsync(args);
        }

        GateAOptions? options = null;
        var steps = new List<GateAStepReport>();
        var startedAtUtc = DateTimeOffset.UtcNow;
        string? deviceInstanceId = null;
        byte? candidateProcessor = null;
        Guid? primaryExperiment = null;
        Guid? recoveryExperiment = null;
        var exactRevision = false;
        var baselineEligible = false;
        var initialJournalClean = false;
        var candidatePrepared = false;
        var preparedStateSurvivedRestart = false;
        var applySucceeded = false;
        var runtimePlacementVerified = false;
        var rollbackVerified = false;
        var recoveryExerciseVerified = false;
        var finalUnresolvedCount = -1;
        string? failure = null;

        try
        {
            options = GateAOptions.Parse(args);
            EnsureAdministrator();
            Directory.CreateDirectory(Path.GetDirectoryName(options.OutputPath)
                ?? throw new InvalidOperationException("The Gate A report path has no parent directory."));

            var head = await RunStepAsync(steps, "source-head", "git", ["rev-parse", "HEAD"], options.RepositoryRoot);
            var branch = await RunStepAsync(steps, "source-branch", "git", ["branch", "--show-current"], options.RepositoryRoot);
            var status = await RunStepAsync(steps, "source-cleanliness", "git", ["status", "--porcelain"], options.RepositoryRoot);
            exactRevision = head.ExitCode == 0 &&
                branch.ExitCode == 0 &&
                status.ExitCode == 0 &&
                string.Equals(head.StandardOutput.Trim(), options.ExpectedCommit, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(branch.StandardOutput.Trim(), "main", StringComparison.Ordinal) &&
                string.IsNullOrWhiteSpace(status.StandardOutput);
            if (!exactRevision)
            {
                throw new InvalidOperationException(
                    "Gate A requires a clean main checkout whose full HEAD exactly matches the baseline source revision.");
            }

            var physicalProject = Path.Combine(
                options.RepositoryRoot,
                "tools",
                "LatencyPilot.PhysicalValidation",
                "LatencyPilot.PhysicalValidation.csproj");
            var build = await RunStepAsync(
                steps,
                "build-physical-validation",
                "dotnet",
                ["build", physicalProject, "--configuration", "Release", "--nologo"],
                options.RepositoryRoot);
            RequireSuccess(build, "The physical-validation harness could not be built.");

            var harnessDll = GetHarnessDll(options.RepositoryRoot);
            if (!File.Exists(harnessDll))
            {
                throw new FileNotFoundException("The built physical-validation harness was not found.", harnessDll);
            }

            var inspect = await RunHarnessAsync(steps, "initial-journal-inspect", harnessDll, options.RepositoryRoot, "inspect");
            initialJournalClean = inspect.ExitCode == 0 && ParseUnresolvedCount(inspect.StandardOutput) == 0;
            if (!initialJournalClean)
            {
                throw new InvalidOperationException("Gate A requires zero unresolved mutation journal entries before starting.");
            }

            var gpuList = await RunHarnessAsync(steps, "discover-gpu", harnessDll, options.RepositoryRoot, "list-gpus");
            RequireSuccess(gpuList, "GPU discovery failed.");
            var devices = Regex.Matches(gpuList.StandardOutput, "(?m)^device=(?<id>.+?)\\r?$")
                .Select(static match => match.Groups["id"].Value.Trim())
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (devices.Length != 1)
            {
                throw new InvalidOperationException(
                    $"One-click Gate A requires one unambiguous present display adapter; discovered {devices.Length.ToString(CultureInfo.InvariantCulture)}.");
            }
            deviceInstanceId = devices[0];

            var plan = await RunHarnessAsync(
                steps,
                "plan-candidate",
                harnessDll,
                options.RepositoryRoot,
                "plan-gpu-affinity",
                "--evidence", options.EvidencePath,
                "--expected-commit", options.ExpectedCommit);
            baselineEligible = plan.ExitCode == 0;
            RequireSuccess(plan, "The baseline was rejected for Gate A candidate planning.");
            var candidateMatch = Regex.Match(plan.StandardOutput, "(?m)^rank=1\\b.*?\\bcpu=(?<cpu>\\d+)\\b");
            if (!candidateMatch.Success ||
                !byte.TryParse(candidateMatch.Groups["cpu"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedCpu))
            {
                throw new InvalidOperationException("No bounded rank-1 GPU affinity candidate was produced.");
            }
            candidateProcessor = parsedCpu;

            var prepare = await RunHarnessAsync(
                steps,
                "prepare-candidate",
                harnessDll,
                options.RepositoryRoot,
                "prepare-gpu-affinity",
                "--device", deviceInstanceId,
                "--processor", parsedCpu.ToString(CultureInfo.InvariantCulture),
                MutationConfirmationFlag);
            candidatePrepared = prepare.ExitCode == 0;
            RequireSuccess(prepare, "The GPU candidate could not be prepared.");
            primaryExperiment = ParseExperimentId(prepare.StandardOutput);

            await RestartServiceAsync(steps, "restart-service-after-prepare");
            var preparedInspect = await RunHarnessAsync(steps, "inspect-prepared-after-restart", harnessDll, options.RepositoryRoot, "inspect");
            preparedStateSurvivedRestart = preparedInspect.ExitCode == 0 &&
                preparedInspect.StandardOutput.Contains($"experiment={primaryExperiment:D}", StringComparison.OrdinalIgnoreCase) &&
                !preparedInspect.StandardOutput.Contains("relation=Unknown", StringComparison.OrdinalIgnoreCase) &&
                !preparedInspect.StandardOutput.Contains("relation=Diverged", StringComparison.OrdinalIgnoreCase);
            if (!preparedStateSurvivedRestart)
            {
                throw new InvalidOperationException("Prepared Gate A state did not survive Service restart with a trusted classification.");
            }

            var apply = await RunHarnessAsync(
                steps,
                "apply-candidate",
                harnessDll,
                options.RepositoryRoot,
                "apply",
                "--experiment", primaryExperiment.Value.ToString("D"),
                MutationConfirmationFlag);
            applySucceeded = apply.ExitCode == 0;
            RequireSuccess(apply, "The bounded GPU affinity candidate did not reach the applied state.");

            var workloadReturnStartedAt = DateTimeOffset.UtcNow;
            steps.Add(new GateAStepReport(
                "return-to-workload",
                workloadReturnStartedAt,
                workloadReturnStartedAt + WorkloadReturnDelay,
                0,
                "Return to the same warmed workload now; runtime placement capture starts automatically after the settle delay.",
                string.Empty));
            await Task.Delay(WorkloadReturnDelay);

            var placement = await RunHarnessAsync(
                steps,
                "verify-runtime-placement",
                harnessDll,
                options.RepositoryRoot,
                "verify-gpu-placement",
                "--experiment", primaryExperiment.Value.ToString("D"));
            runtimePlacementVerified = placement.ExitCode == 0 &&
                placement.StandardOutput.Contains("placement-proof=confirmed", StringComparison.OrdinalIgnoreCase);
            if (!runtimePlacementVerified)
            {
                throw new InvalidOperationException(
                    "Resolved single-adapter GPU ISR placement was not proven on the requested processor. The original state will be restored.");
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            failure = $"{exception.GetType().Name}: {exception.Message}";
        }
        finally
        {
            if (options is not null && primaryExperiment is Guid primary)
            {
                try
                {
                    var harnessDll = GetHarnessDll(options.RepositoryRoot);
                    if (applySucceeded)
                    {
                        var rollback = await RunHarnessAsync(
                            steps,
                            "rollback-primary",
                            harnessDll,
                            options.RepositoryRoot,
                            "rollback",
                            "--experiment", primary.ToString("D"),
                            MutationConfirmationFlag);
                        rollbackVerified = rollback.ExitCode == 0 &&
                            rollback.StandardOutput.Contains("state=Reverted", StringComparison.OrdinalIgnoreCase) &&
                            rollback.StandardOutput.Contains("original-state-restored=True", StringComparison.OrdinalIgnoreCase);
                    }

                    if (!rollbackVerified)
                    {
                        var recovery = await RunHarnessAsync(
                            steps,
                            "recover-primary",
                            harnessDll,
                            options.RepositoryRoot,
                            "recover",
                            "--experiment", primary.ToString("D"),
                            MutationConfirmationFlag);
                        rollbackVerified = recovery.ExitCode == 0 &&
                            recovery.StandardOutput.Contains("original-state-restored=True", StringComparison.OrdinalIgnoreCase);
                    }
                }
                catch (Exception cleanupException)
                {
                    failure = AppendFailure(failure, $"Primary cleanup failed: {cleanupException.Message}");
                }
            }
        }

        if (options is not null &&
            failure is null &&
            rollbackVerified &&
            deviceInstanceId is not null &&
            candidateProcessor is byte recoveryCpu)
        {
            try
            {
                var harnessDll = GetHarnessDll(options.RepositoryRoot);
                var prepareRecovery = await RunHarnessAsync(
                    steps,
                    "prepare-recovery-exercise",
                    harnessDll,
                    options.RepositoryRoot,
                    "prepare-gpu-affinity",
                    "--device", deviceInstanceId,
                    "--processor", recoveryCpu.ToString(CultureInfo.InvariantCulture),
                    MutationConfirmationFlag);
                RequireSuccess(prepareRecovery, "The prepared-state recovery exercise could not be created.");
                recoveryExperiment = ParseExperimentId(prepareRecovery.StandardOutput);

                await RestartServiceAsync(steps, "restart-service-for-recovery");
                var recoverPrepared = await RunHarnessAsync(
                    steps,
                    "recover-prepared-state",
                    harnessDll,
                    options.RepositoryRoot,
                    "recover",
                    "--experiment", recoveryExperiment.Value.ToString("D"),
                    MutationConfirmationFlag);
                recoveryExerciseVerified = recoverPrepared.ExitCode == 0 &&
                    recoverPrepared.StandardOutput.Contains("recovery-plan=AbortPreparedWithoutApply", StringComparison.OrdinalIgnoreCase) &&
                    recoverPrepared.StandardOutput.Contains("state=AbortedBeforeApply", StringComparison.OrdinalIgnoreCase);
                if (!recoveryExerciseVerified)
                {
                    throw new InvalidOperationException("The supported prepared-state recovery exercise did not terminalize safely.");
                }
            }
            catch (Exception exception)
            {
                failure = AppendFailure(failure, $"Recovery exercise failed: {exception.Message}");
            }
        }

        if (options is not null)
        {
            try
            {
                var harnessDll = GetHarnessDll(options.RepositoryRoot);
                var finalInspect = await RunHarnessAsync(steps, "final-journal-inspect", harnessDll, options.RepositoryRoot, "inspect");
                finalUnresolvedCount = finalInspect.ExitCode == 0
                    ? ParseUnresolvedCount(finalInspect.StandardOutput)
                    : -1;
            }
            catch (Exception exception)
            {
                failure = AppendFailure(failure, $"Final journal inspection failed: {exception.Message}");
            }
        }

        var facts = new GateAValidationFacts(
            exactRevision,
            baselineEligible,
            initialJournalClean,
            candidatePrepared,
            preparedStateSurvivedRestart,
            applySucceeded,
            runtimePlacementVerified,
            rollbackVerified,
            recoveryExerciseVerified,
            finalUnresolvedCount);
        var completion = GateAValidationCompletion.Evaluate(facts);
        if (!completion.Passed)
        {
            failure = AppendFailure(failure, completion.Reason);
        }

        if (options is null)
        {
            Console.Error.WriteLine(failure ?? "Gate A options could not be parsed.");
            return 2;
        }

        var evidenceSha = File.Exists(options.EvidencePath)
            ? Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(options.EvidencePath))).ToLowerInvariant()
            : null;
        var report = new GateAValidationReport(
            "latencypilot-gate-a-report-v1",
            startedAtUtc,
            DateTimeOffset.UtcNow,
            completion.Passed ? "Passed" : finalUnresolvedCount == 0 ? "FailedRecovered" : "ManualRecoveryRequired",
            completion.Passed,
            completion.Reason,
            failure,
            options.ExpectedCommit,
            options.EvidencePath,
            evidenceSha,
            deviceInstanceId,
            candidateProcessor,
            primaryExperiment,
            recoveryExperiment,
            facts,
            steps.AsReadOnly());

        await File.WriteAllTextAsync(
            options.OutputPath,
            JsonSerializer.Serialize(report, ReportJsonOptions));

        Console.WriteLine($"gate-a-status={report.Status}");
        Console.WriteLine($"gate-a-report={options.OutputPath}");
        if (!string.IsNullOrWhiteSpace(failure))
        {
            Console.Error.WriteLine(failure);
        }

        return completion.Passed ? 0 : finalUnresolvedCount == 0 ? 3 : 4;
    }

    private static string GetHarnessDll(string repositoryRoot) => Path.Combine(
        repositoryRoot,
        "tools",
        "LatencyPilot.PhysicalValidation",
        "bin",
        "Release",
        "net10.0-windows10.0.26100.0",
        "win-x64",
        "LatencyPilot.PhysicalValidation.dll");

    private static async Task<GateAStepReport> RunHarnessAsync(
        ICollection<GateAStepReport> steps,
        string name,
        string harnessDll,
        string workingDirectory,
        params string[] arguments)
    {
        var allArguments = new List<string>(arguments.Length + 1) { harnessDll };
        allArguments.AddRange(arguments);
        return await RunStepAsync(steps, name, "dotnet", allArguments, workingDirectory);
    }

    private static async Task RestartServiceAsync(
        List<GateAStepReport> steps,
        string stepName)
    {
        var startedAtUtc = DateTimeOffset.UtcNow;
        var standardOutput = string.Empty;
        var standardError = string.Empty;
        var exitCode = 0;
        try
        {
            using var service = new ServiceController(ServiceBoundary.ServiceName);
            service.Refresh();
            switch (service.Status)
            {
                case ServiceControllerStatus.StartPending:
                case ServiceControllerStatus.ContinuePending:
                    service.WaitForStatus(
                        ServiceControllerStatus.Running,
                        TimeSpan.FromSeconds(30));
                    break;
                case ServiceControllerStatus.StopPending:
                    service.WaitForStatus(
                        ServiceControllerStatus.Stopped,
                        TimeSpan.FromSeconds(30));
                    break;
                case ServiceControllerStatus.PausePending:
                    service.WaitForStatus(
                        ServiceControllerStatus.Paused,
                        TimeSpan.FromSeconds(30));
                    break;
            }

            service.Refresh();
            if (service.Status != ServiceControllerStatus.Stopped)
            {
                service.Stop();
                service.WaitForStatus(
                    ServiceControllerStatus.Stopped,
                    TimeSpan.FromSeconds(30));
            }

            service.Start();
            service.WaitForStatus(
                ServiceControllerStatus.Running,
                TimeSpan.FromSeconds(30));
            service.Refresh();
            standardOutput = service.Status.ToString();
            if (service.Status != ServiceControllerStatus.Running)
            {
                throw new InvalidOperationException(
                    "The observation Service did not return to Running after restart.");
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or
            System.ComponentModel.Win32Exception or
            System.ServiceProcess.TimeoutException)
        {
            exitCode = 1;
            standardError = $"{exception.GetType().Name}: {exception.Message}";
        }

        var step = new GateAStepReport(
            stepName,
            startedAtUtc,
            DateTimeOffset.UtcNow,
            exitCode,
            standardOutput,
            standardError);
        steps.Add(step);
        RequireSuccess(step, "The protected observation Service could not be restarted.");
        await Task.CompletedTask.ConfigureAwait(false);
    }

    private static async Task<GateAStepReport> RunStepAsync(
        ICollection<GateAStepReport> steps,
        string name,
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory)
    {
        var startedAtUtc = DateTimeOffset.UtcNow;
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
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
            ?? throw new InvalidOperationException($"Could not start '{fileName}'.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var step = new GateAStepReport(
            name,
            startedAtUtc,
            DateTimeOffset.UtcNow,
            process.ExitCode,
            await stdoutTask,
            await stderrTask);
        steps.Add(step);
        return step;
    }

    private static void RequireSuccess(GateAStepReport step, string message)
    {
        if (step.ExitCode != 0)
        {
            var details = string.IsNullOrWhiteSpace(step.StandardError)
                ? step.StandardOutput.Trim()
                : step.StandardError.Trim();
            throw new InvalidOperationException($"{message} {details}".Trim());
        }
    }

    private static Guid ParseExperimentId(string output)
    {
        var match = Regex.Match(output, "(?m)^experiment=(?<id>[0-9A-Fa-f-]{36})\\r?$");
        return match.Success && Guid.TryParseExact(match.Groups["id"].Value, "D", out var experiment) && experiment != Guid.Empty
            ? experiment
            : throw new InvalidOperationException("The physical-validation harness did not return a valid experiment ID.");
    }

    private static int ParseUnresolvedCount(string output)
    {
        var match = Regex.Match(output, "unresolved=(?<count>\\d+)");
        return match.Success && int.TryParse(match.Groups["count"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var count)
            ? count
            : -1;
    }

    private static string AppendFailure(string? current, string next) =>
        string.IsNullOrWhiteSpace(current) ? next : $"{current} | {next}";

    private static void EnsureAdministrator()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Gate A one-click validation is supported only on Windows.");
        }

        using var identity = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
        {
            throw new UnauthorizedAccessException("Gate A one-click validation must run in the elevated helper process.");
        }
    }

    private sealed record GateAOptions(
        string RepositoryRoot,
        string EvidencePath,
        string ExpectedCommit,
        string OutputPath)
    {
        internal static GateAOptions Parse(string[] args)
        {
            ArgumentNullException.ThrowIfNull(args);
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            var confirmation = false;
            for (var index = 0; index < args.Length; index++)
            {
                var token = args[index];
                if (string.Equals(token, MutationConfirmationFlag, StringComparison.Ordinal))
                {
                    confirmation = true;
                    continue;
                }

                if (token is not ("--repo-root" or "--evidence" or "--expected-commit" or "--output"))
                {
                    throw new ArgumentException($"Unknown Gate A option '{token}'.");
                }
                if (index + 1 >= args.Length)
                {
                    throw new ArgumentException($"Gate A option '{token}' requires a value.");
                }
                values[token] = args[++index];
            }

            if (!confirmation)
            {
                throw new InvalidOperationException(
                    $"Gate A one-click validation requires the explicit {MutationConfirmationFlag} acknowledgement.");
            }

            string Required(string key) => values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value
                : throw new ArgumentException($"Required Gate A option '{key}' is missing.");

            var expectedCommit = Required("--expected-commit").Trim();
            if (expectedCommit.Length != 40 || !expectedCommit.All(Uri.IsHexDigit))
            {
                throw new ArgumentException("--expected-commit must be an exact 40-character hexadecimal Git revision.");
            }

            var repoRoot = Path.GetFullPath(Required("--repo-root"));
            var evidence = Path.GetFullPath(Required("--evidence"));
            var output = Path.GetFullPath(Required("--output"));
            if (!Directory.Exists(repoRoot) || !File.Exists(Path.Combine(repoRoot, "LatencyPilot.slnx")))
            {
                throw new DirectoryNotFoundException("--repo-root is not a LatencyPilot source checkout.");
            }
            if (!File.Exists(evidence))
            {
                throw new FileNotFoundException("The Gate A baseline evidence file was not found.", evidence);
            }

            return new GateAOptions(repoRoot, evidence, expectedCommit.ToLowerInvariant(), output);
        }
    }
}

internal sealed record GateAStepReport(
    string Name,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    int ExitCode,
    string StandardOutput,
    string StandardError);

internal sealed record GateAValidationReport(
    string Schema,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    string Status,
    bool Passed,
    string CompletionReason,
    string? Failure,
    string SourceRevisionId,
    string EvidencePath,
    string? EvidenceSha256,
    string? DeviceInstanceId,
    byte? CandidateProcessor,
    Guid? PrimaryExperimentId,
    Guid? RecoveryExperimentId,
    GateAValidationFacts Facts,
    IReadOnlyList<GateAStepReport> Steps);
