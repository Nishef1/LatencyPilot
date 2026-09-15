using System.Diagnostics;
using System.Text.Json;
using LatencyPilot.Benchmarking.Baselines;
using LatencyPilot.Benchmarking.Optimization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LatencyPilot.CriticalTests;

[TestClass]
public sealed class WorkloadStabilityContractTests
{
    [TestMethod]
    public void CpuActivityDriftBlocksExperimentReadinessEvenWhenInterruptRatesAreStable()
    {
        var windows = Enumerable.Range(1, 5)
            .Select(number => new BaselineWindowEvidence(
                number,
                DateTimeOffset.UnixEpoch.AddSeconds((number - 1) * 21),
                20_000,
                20_000,
                true,
                null,
                30_000,
                100,
                12_000,
                70))
            .ToArray();
        double?[] cpuBusyPercent = [80, 80, 20, 20, 20];

        var result = WorkloadStabilityAnalyzer.Analyze(windows, cpuBusyPercent);

        Assert.AreEqual(WorkloadStabilityStatus.Changing, result.Status);
        Assert.IsFalse(result.IsEligibleForExperiment);
        Assert.IsTrue(result.Reasons.Any(static reason =>
            reason.Contains("System CPU busy", StringComparison.Ordinal)));

        var quality = BaselineQualityAnalyzer.Analyze(windows);
        var eligibility = GpuOptimizationBaselineReadiness.Evaluate(quality, result);
        Assert.IsFalse(eligibility.IsEligible);
        Assert.AreEqual("gpu-affinity-v1", eligibility.Target);
        StringAssert.Contains(eligibility.Reason, "System CPU busy");

        var stableWorkload = WorkloadStabilityAnalyzer.Analyze(windows, [50, 50, 50, 50, 50]);
        var stableEligibility = GpuOptimizationBaselineReadiness.Evaluate(quality, stableWorkload);
        Assert.IsTrue(stableEligibility.IsEligible);
        Assert.AreEqual("gpu-affinity-v1", stableEligibility.Target);

        AssertEvidenceVerifierRejectsContradictorySerializedReadiness();
        AssertGateAPlannerConsumesEvidenceV9Readiness();
    }

    private static void AssertEvidenceVerifierRejectsContradictorySerializedReadiness()
    {
        var stable = RunEvidenceVerifier([50, 50, 50, 50, 50]);
        Assert.AreEqual(
            0,
            stable.ExitCode,
            "Control evidence fixture must pass the canonical verifier before the contradiction check is meaningful.\n" +
            stable.StandardOutput + "\n" + stable.StandardError);

        var contradictory = RunEvidenceVerifier([80, 80, 20, 20, 20]);
        Assert.AreNotEqual(
            0,
            contradictory.ExitCode,
            "Verifier accepted serialized Stable workload readiness even though the source windows contain material CPU activity drift.\n" +
            contradictory.StandardOutput + "\n" + contradictory.StandardError);
    }

    private static void AssertGateAPlannerConsumesEvidenceV9Readiness()
    {
        var plannerSource = File.ReadAllText(FindRepositoryFile(
            "tools",
            "LatencyPilot.PhysicalValidation",
            "BaselineEvidenceCandidatePlan.cs"));

        StringAssert.Contains(plannerSource, "latencypilot-evidence-v9");
        StringAssert.Contains(plannerSource, "\"workloadStability\"");
        StringAssert.Contains(plannerSource, "\"optimizerEligibility\"");
        Assert.IsFalse(
            plannerSource.Contains("latencypilot-evidence-v8", StringComparison.Ordinal),
            "Gate A candidate planning must not accept the superseded evidence-v8 contract.");
    }

    private static VerifierResult RunEvidenceVerifier(double[] cpuBusy)
    {
        const string sourceRevision = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var tempPath = Path.Combine(
            Path.GetTempPath(),
            "LatencyPilot.CriticalTests",
            $"workload-readiness-{Guid.NewGuid():N}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(tempPath)!);

        try
        {
            var captures = Enumerable.Range(1, 5)
                .Select(CreateCapture)
                .ToArray();
            var windows = Enumerable.Range(1, 5)
                .Select(number => new
                {
                    windowNumber = number,
                    startedAtUtc = captures[number - 1].startedAtUtc,
                    requestedDurationMilliseconds = 20_000,
                    actualDurationMilliseconds = 20_000d,
                    dpcEventCount = 1_000,
                    isrEventCount = 1_000,
                    dpcP99Microseconds = 3d,
                    isrP99Microseconds = 3d,
                    captureIntegrityValid = true,
                })
                .ToArray();
            var runtimeWindows = Enumerable.Range(1, 5)
                .Select(number => new
                {
                    windowNumber = number,
                    context = new { systemCpuBusyPercent = cpuBusy[number - 1] },
                })
                .ToArray();
            var stableSignals = new[]
            {
                CreateStableSignal("DPC event rate", 50d),
                CreateStableSignal("ISR event rate", 50d),
                CreateStableSignal("System CPU busy", 50d),
            };

            var evidence = new
            {
                schema = "latencypilot-evidence-v9",
                purpose = "repeated-decision-baseline",
                productVersion = "0.0.2",
                sourceRevisionId = sourceRevision,
                protocolVersion = 6,
                exportedAtUtc = DateTimeOffset.UnixEpoch,
                environment = new { },
                measurementContext = new
                {
                    scenario = "RealWorld",
                    displayName = "Steady real-world workload",
                },
                baselineMethodVersion = "baseline-quality-v2",
                captures,
                windows,
                runtimeWindows,
                quality = new
                {
                    methodVersion = "baseline-quality-v2",
                    status = "Valid",
                    totalWindowCount = 5,
                    validCaptureWindowCount = 5,
                    isValidForComparison = true,
                    dpcP99 = new { isStable = true, eligibleWindowCount = 5 },
                    isrP99 = new { isStable = true, eligibleWindowCount = 5 },
                    reasons = Array.Empty<string>(),
                },
                workloadStability = new
                {
                    methodVersion = "workload-stability-v1",
                    status = "Stable",
                    isEligibleForExperiment = true,
                    signals = stableSignals,
                    reasons = Array.Empty<string>(),
                },
                optimizerEligibility = new
                {
                    target = "gpu-affinity-v1",
                    isEligible = true,
                    reason = "Latency repeatability passed baseline-quality-v2 and workload activity is Stable under workload-stability-v1.",
                },
            };

            File.WriteAllText(tempPath, JsonSerializer.Serialize(evidence));
            var verifier = FindRepositoryFile("scripts", "Verify-Evidence.ps1");
            var startInfo = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            foreach (var argument in new[]
                     {
                         "-NoProfile",
                         "-NonInteractive",
                         "-ExecutionPolicy", "Bypass",
                         "-File", verifier,
                         "-Path", tempPath,
                         "-ExpectedCommit", sourceRevision,
                     })
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Unable to launch evidence verifier.");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            Task.WaitAll(stdout, stderr);
            return new VerifierResult(process.ExitCode, stdout.Result, stderr.Result);
        }
        finally
        {
            try
            {
                File.Delete(tempPath);
            }
            catch
            {
                // Best-effort cleanup only.
            }
        }
    }

    private static object CreateStableSignal(string name, double value) => new
    {
        signalName = name,
        median = value,
        earlyMedian = value,
        lateMedian = value,
        relativeDrift = 0d,
        hasMaterialDrift = false,
        maximumRelativeDeviation = 0d,
        hasExtremeWindow = false,
    };

    private static object CreateDistribution() => new
    {
        count = 1_000,
        p50Microseconds = 1d,
        p95Microseconds = 2d,
        p99Microseconds = 3d,
        maximumMicroseconds = 4d,
    };

    private static object CreateThresholds(double threshold) => new
    {
        guidanceThresholdMicroseconds = threshold,
        guidanceExceedanceCount = 0,
        overOneMillisecondCount = 0,
        overThreeMillisecondsCount = 0,
    };

    private static dynamic CreateCapture(int number)
    {
        var dpc = CreateDistribution();
        var isr = CreateDistribution();
        var dpcThresholds = CreateThresholds(100d);
        var isrThresholds = CreateThresholds(25d);
        return new
        {
            requestId = Guid.NewGuid(),
            startedAtUtc = DateTimeOffset.UnixEpoch.AddSeconds((number - 1) * 21),
            requestedDurationMilliseconds = 20_000,
            actualDurationMilliseconds = 20_000d,
            eventsLost = 0,
            invalidEventCount = 0,
            invalidImageEventCount = 0,
            resolvedModuleEventCount = 0,
            unresolvedModuleEventCount = 0,
            eventLimitReached = false,
            dpc,
            isr,
            dpcThresholds,
            isrThresholds,
            processors = new[]
            {
                new { dpc, isr, dpcThresholds, isrThresholds },
            },
            modules = Array.Empty<object>(),
            unresolvedRoutines = Array.Empty<object>(),
        };
    }

    private static string FindRepositoryFile(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. relativeParts]);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            "Unable to locate repository file: " + Path.Combine(relativeParts));
    }

    private sealed record VerifierResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);
}
