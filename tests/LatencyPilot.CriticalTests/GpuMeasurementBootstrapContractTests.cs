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
    public void OriginalBaselineCanRecoverOnAttemptFiveButBroadInstabilityStillFails()
    {
        var selectorType = typeof(GpuAutoAffinitySession).Assembly.GetType(
            "LatencyPilot.Benchmarking.Optimization.GpuRepeatabilityClusterSelector",
            throwOnError: true)!;
        var select = selectorType.GetMethod(
            "Select",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("GPU repeatability selector entry point was not found.");
        var maximumOriginalAttempts = selectorType.GetField(
            "MaximumOriginalAttemptCount",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.IsNotNull(
            maximumOriginalAttempts,
            "Original baseline acquisition needs a distinct five-attempt ceiling instead of sharing the four-attempt finalist budget.");
        Assert.AreEqual(5, (int)maximumOriginalAttempts.GetRawConstantValue()!);

        // Physical Gate A shape from 2026-09-20: the first scored Original was a
        // cold observer/bootstrap outlier. Four runs still have no bounded 3-run
        // regime, while a fifth ordinary sample makes the stable regime explicit.
        var firstFour = new[] { 68.5d, 160.8d, 152.8d, 172.2d };
        Assert.IsNull(
            select.Invoke(null, [firstFour]),
            "The first four physical-shape samples must not manufacture a cluster by silently widening the recovery band.");

        var fiveRuns = new[] { 68.5d, 160.8d, 152.8d, 172.2d, 155.5d };
        var recovered = select.Invoke(null, [fiveRuns]);
        Assert.IsNotNull(
            recovered,
            "A fifth bounded Original attempt must be able to recover a coherent three-run regime after one cold-start outlier.");
        var indexes = (int[])recovered.GetType().GetProperty("Indexes")!.GetValue(recovered)!;
        Assert.AreEqual(3, indexes.Length);
        Assert.IsFalse(indexes.Contains(0),
            "The cold-start outlier must remain in the audit trail but must not define baseline uncertainty once a coherent regime exists.");

        var broadlyUnstable = new[] { 70d, 88d, 111d, 139d, 176d };
        Assert.IsNull(
            select.Invoke(null, [broadlyUnstable]),
            "Five attempts are a hard ceiling, not permission to cherry-pick a baseline from genuinely broad instability.");
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
