#pragma warning disable CA1822 // AuditCase methods are reflection-invoked by ConsolidatedCriticalTests.
using System.Reflection;
using LatencyPilot.Benchmarking.Optimization;
using LatencyPilot.Core.Devices;
using LatencyPilot.Platform.Windows.Devices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LatencyPilot.CriticalTests;

[TestClass]
public sealed class GpuMeasurementBootstrapContractTests
{
    [AuditCase]
    public void OriginalVariabilityIsMeasuredWithMedianAndMadInsteadOfUsedAsANoiseOnlyStop()
    {
        var sessionSource = File.ReadAllText(FindRepositoryFile(
            "src",
            "LatencyPilot.Benchmarking",
            "Optimization",
            "GpuAutoAffinitySession.cs"));

        StringAssert.Contains(sessionSource, "RelativeMedianAbsoluteDeviation");
        StringAssert.Contains(sessionSource, "noise lowers selection confidence but does not erase");
        StringAssert.Contains(sessionSource, "MaximumOriginalAttemptCount");
        StringAssert.Contains(sessionSource, "RequiredRunCount");
        Assert.IsFalse(
            sessionSource.Contains("GpuRepeatabilityClusterSelector.Select(", StringComparison.Ordinal),
            "v3 must not cherry-pick a quiet three-run cluster and discard the rest of the valid observations.");
        Assert.IsFalse(
            sessionSource.Contains("candidate mutation is not allowed", StringComparison.OrdinalIgnoreCase),
            "Broad but structurally valid Original variability is uncertainty evidence, not a reason to suppress the whole candidate search.");
    }

    [AuditCase]
    public void OriginalAcquisitionUsesThreeRunsAndExtendsToFiveOnlyWhenRobustNoiseIsHigh()
    {
        var sessionSource = File.ReadAllText(FindRepositoryFile(
            "src",
            "LatencyPilot.Benchmarking",
            "Optimization",
            "GpuAutoAffinitySession.cs"));

        StringAssert.Contains(sessionSource, "QualifyOriginalAsync(");
        StringAssert.Contains(
            sessionSource,
            "observations.Count < GpuRepeatabilityClusterSelector.MaximumOriginalAttemptCount",
            "Original noise estimation must remain bounded at five scored observations.");
        StringAssert.Contains(
            sessionSource,
            "observations.Count < GpuRepeatabilityClusterSelector.RequiredRunCount",
            "At least three scored Original observations are required before estimating variability.");
        StringAssert.Contains(
            sessionSource,
            "evaluation.PrimaryRelativeNoise <= GpuRepeatabilityClusterSelector.RelativeTolerance",
            "A quiet three-run baseline should avoid spending time on unnecessary extra Original samples.");
        StringAssert.Contains(
            sessionSource,
            "UsedNoiseAwareFallback: original.TotalObservationCount > GpuRepeatabilityClusterSelector.RequiredRunCount",
            "The report must disclose when extra Original observations were needed because the initial sample was noisy.");

        var wrapperSource = File.ReadAllText(FindRepositoryFile(
            "tools",
            "LatencyPilot.GateAValidation",
            "ProgressReportingGpuAutoAffinityBackend.cs"));
        Assert.IsFalse(
            wrapperSource.Contains("ApplyBoundedOriginalBaselineRecovery", StringComparison.Ordinal) ||
            wrapperSource.Contains("pendingOriginalRetryIndex", StringComparison.Ordinal) ||
            wrapperSource.Contains("ControlTrialDrifted = true", StringComparison.Ordinal),
            "Progress reporting must observe core-session behavior rather than manufacture contamination.");
    }

    [AuditCase]
    public void BenchmarkSettlesObserverStartupBeforeOpeningTheScoredQpcWindow()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "src",
            "LatencyPilot.GpuBenchmark",
            "BenchmarkWorkload.cs"));
        StringAssert.Contains(source, "ObserverSettleDuration");
        StringAssert.Contains(source, "observer-settle");

        var settleStart = source.IndexOf("observer-settle", StringComparison.Ordinal);
        var qpcStart = source.IndexOf("var startedAtQpc = Stopwatch.GetTimestamp();", StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, settleStart);
        Assert.IsGreaterThan(settleStart, qpcStart,
            "Unscored settle work must complete before the benchmark records its scored QPC start boundary.");

        var presentMonSource = File.ReadAllText(FindRepositoryFile(
            "src",
            "LatencyPilot.Platform.Windows",
            "Devices",
            "PresentMonConsoleFrameMetricsReader.cs"));
        StringAssert.Contains(
            presentMonSource,
            "WaitForTraceSessionReadyAsync",
            "PresentMon startup must wait for the uniquely named ETW session rather than treating process liveness as capture readiness.");
        StringAssert.Contains(
            presentMonSource,
            "TraceEventSession.GetActiveSessionNames()",
            "PresentMon startup must query the real ETW session using the already-maintained TraceEvent dependency.");
        Assert.IsFalse(
            presentMonSource.Contains("RecordingStartedMessage", StringComparison.Ordinal) ||
            presentMonSource.Contains("StartupProbeDelay", StringComparison.Ordinal),
            "Readiness must not depend on redirected console buffering or a fixed startup sleep.");
    }

    [AuditCase]
    public async Task PresentMonConsoleCropsInTheSameQpcDomainAsTheBenchmark()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "src",
            "LatencyPilot.Platform.Windows",
            "Devices",
            "PresentMonConsoleFrameMetricsReader.cs"));
        StringAssert.Contains(source, "--qpc_time");
        Assert.IsFalse(
            source.Contains("startInfo.ArgumentList.Add(\"--date_time\")", StringComparison.Ordinal),
            "PresentMon correlation must use QPC rather than crossing through wall-clock/local-time conversion.");

        var artifactSource = File.ReadAllText(FindRepositoryFile(
            "src",
            "LatencyPilot.Core",
            "Benchmarking",
            "GpuBenchmarkTrialArtifact.cs"));
        StringAssert.Contains(artifactSource, "StartedAtQpc");
        StringAssert.Contains(artifactSource, "EndedAtQpc");
        StringAssert.Contains(artifactSource, "QpcFrequency");

        var gateBackendSource = File.ReadAllText(FindRepositoryFile(
            "tools",
            "LatencyPilot.GateAValidation",
            "GpuAutoAffinityGateABackend.cs"));
        StringAssert.Contains(gateBackendSource, "artifact.StartedAtQpc");
        StringAssert.Contains(gateBackendSource, "artifact.EndedAtQpc");
        StringAssert.Contains(gateBackendSource, "artifact.QpcFrequency");

        var parse = typeof(PresentMonConsoleFrameMetricsReader).GetMethods(
                BindingFlags.Static | BindingFlags.NonPublic)
            .SingleOrDefault(static method =>
                method.Name == "ParseAsync" &&
                method.GetParameters().Any(parameter =>
                    string.Equals(parameter.Name, "benchmarkStartedAtQpc", StringComparison.Ordinal)));
        Assert.IsNotNull(parse,
            "PresentMon CSV parsing must accept the benchmark QPC bounds explicitly.");

        var temp = Path.Combine(Path.GetTempPath(), $"latencypilot-qpc-test-{Guid.NewGuid():N}.csv");
        try
        {
            await File.WriteAllTextAsync(
                temp,
                "ProcessID,SwapChainAddress,CPUStartQPC,FrameTime,CPUBusy,CPUWait\n" +
                "77,1,900,8,5,3\n" +
                "77,1,1100,8,5,3\n" +
                "77,1,1500,8,5,3\n" +
                "77,1,2100,8,5,3\n");

            var benchmarkStartUtc = DateTimeOffset.UnixEpoch;
            var benchmarkEndUtc = benchmarkStartUtc.AddSeconds(10);
            var invocation = (Task)parse.Invoke(null, new object?[]
            {
                temp,
                (uint)77,
                TimeSpan.FromSeconds(10),
                1000L,
                2000L,
                100L,
                benchmarkStartUtc,
                benchmarkEndUtc,
                "PresentMon-2.5.1.exe",
                CancellationToken.None,
            })!;
            await invocation.ConfigureAwait(false);
            var snapshot = (PresentMonFrameCaptureSnapshot)invocation.GetType()
                .GetProperty("Result")!
                .GetValue(invocation)!;

            Assert.AreEqual(PresentMonWorkloadCaptureStatus.Available, snapshot.Status);
            Assert.AreEqual(2, snapshot.Frames.Count,
                "Only QPC rows inside the benchmark measurement window may contribute to PresentMon guardrails.");
            Assert.IsGreaterThan(0d, snapshot.ActualWindowMilliseconds);
        }
        finally
        {
            File.Delete(temp);
        }
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
}
