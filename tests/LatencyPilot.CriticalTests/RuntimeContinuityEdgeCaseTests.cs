using System.Net.NetworkInformation;
using LatencyPilot.Benchmarking.Baselines;
using LatencyPilot.Benchmarking.Candidates;
using LatencyPilot.Benchmarking.Comparisons;
using LatencyPilot.Benchmarking.Optimization;
using LatencyPilot.Core.Devices;
using LatencyPilot.Core.System;
using LatencyPilot.Platform.Windows.Devices;
using LatencyPilot.Platform.Windows.System;
using LatencyPilot.Service;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LatencyPilot.CriticalTests;

[TestClass]
public sealed class RuntimeContinuityEdgeCaseTests
{
    [TestMethod]
    public void GpuCaptureRejectsSleepAndHybridTargetAmbiguity()
    {
        var target = new PnPDeviceSnapshot(
            "PCI\\VEN_10DE&DEV_2484&SUBSYS_147A10DE&REV_A1\\4&TEST",
            new Guid("4D36E968-E325-11CE-BFC1-08002BE10318"),
            "Test NVIDIA GPU",
            "NVIDIA",
            "PCI",
            "nvlddmkm",
            new DriverMetadataSnapshot("1.0", "NVIDIA", "display.inf"),
            InterruptConfigurationSnapshot.Available(1, null, null, null),
            InterruptResourceSnapshot.Available([]));
        var luid = new GraphicsAdapterLuid(0x12345678, 0x10203040);
        var dxgi = new GraphicsAdapterSnapshot(
            0,
            "Test NVIDIA GPU",
            0x10DE,
            0x2484,
            0x147A10DE,
            0xA1,
            8UL * 1024 * 1024 * 1024,
            0,
            16UL * 1024 * 1024 * 1024,
            luid,
            0);
        var presentMon = new PresentMonDeviceInventory(
            PresentMonDiscoveryStatus.Available,
            [new PresentMonGraphicsDeviceSnapshot(7, 0, "Test NVIDIA GPU", luid)],
            "PresentMonAPI2.dll",
            0,
            null,
            DateTimeOffset.UnixEpoch);

        var resolved = GpuGraphicsTargetIdentityResolver.Resolve(
            target,
            new GraphicsAdapterInventory([dxgi], DateTimeOffset.UnixEpoch),
            presentMon);
        Assert.IsTrue(resolved.IsUsable);
        Assert.IsNotNull(resolved.Identity);
        Assert.AreEqual((uint)7, resolved.Identity.PresentMonDeviceId);

        var secondAdapter = new GraphicsAdapterSnapshot(
            1,
            "Integrated GPU",
            0x8086,
            0x1234,
            0,
            1,
            0,
            0,
            8UL * 1024 * 1024 * 1024,
            new GraphicsAdapterLuid(0x87654321, 0x01020304),
            0);
        Assert.IsFalse(GpuGraphicsTargetIdentityResolver.Resolve(
            target,
            new GraphicsAdapterInventory([dxgi, secondAdapter], DateTimeOffset.UnixEpoch),
            presentMon).IsUsable);
        Assert.IsFalse(GpuGraphicsTargetIdentityResolver.Resolve(
            target,
            new GraphicsAdapterInventory([dxgi], DateTimeOffset.UnixEpoch),
            presentMon with
            {
                GraphicsDevices = [new PresentMonGraphicsDeviceSnapshot(
                    7,
                    0,
                    "Wrong GPU",
                    new GraphicsAdapterLuid(1, 2))],
            }).IsUsable);

        var balancedScheme = new Guid("381b4222-f694-41f0-9685-ff5bb260df2e");
        var power = new SystemPowerSnapshot(
            SystemPowerLineState.Online,
            BatteryPresent: false,
            Charging: null,
            BatteryPercent: null,
            BatterySaverEnabled: false,
            ActiveSchemeId: balancedScheme,
            ActiveSchemeName: "Balanced",
            UserConfiguredPowerModeId: Guid.Empty,
            UserConfiguredPowerMode: UserConfiguredPowerMode.Balanced);
        var before = new GpuOptimizationCaptureContinuitySnapshot(
            42,
            DateTimeOffset.UnixEpoch,
            "game",
            1,
            1,
            power,
            new SystemAwakeTimeSnapshot(1_000, 10_000_000),
            resolved.Identity);
        var after = before with
        {
            AwakeTime = new SystemAwakeTimeSnapshot(31_000, 310_000_000),
        };
        Assert.IsTrue(GpuOptimizationCaptureContinuity.Evaluate(before, after).IsStable);
        Assert.IsTrue(RawInputTimingCapture.IsAwakeIntervalUsable(before.AwakeTime, after.AwakeTime));

        var slept = after with
        {
            AwakeTime = new SystemAwakeTimeSnapshot(36_000, 310_000_000),
        };
        Assert.IsFalse(GpuOptimizationCaptureContinuity.Evaluate(before, slept).IsStable);
        Assert.IsFalse(RawInputTimingCapture.IsAwakeIntervalUsable(before.AwakeTime, slept.AwakeTime));
        Assert.IsFalse(GpuOptimizationCaptureContinuity.Evaluate(
            before,
            after with
            {
                GraphicsTarget = after.GraphicsTarget with { PresentMonDeviceId = 8 },
            }).IsStable);

        var blockedBackend = new RejectingGraphicsPreflightBackend();
        var blockedRequest = new GpuOptimizationOrchestrationRequest(
            target.InstanceId,
            42,
            EligibleBaseline(),
            [new GpuAffinityCandidate(1, new LogicalProcessorId(0, 2), 0, true, 0.05)],
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(30),
            new ComparisonPolicy(
                MinimumSamples: 20,
                MinimumRelativeChange: 0.03,
                GuardrailRegressionLimit: 0.05,
                EvaluationPercentile: 0.99));
        var blocked = new GpuOptimizationOrchestrator(blockedBackend)
            .RunAsync(blockedRequest)
            .GetAwaiter()
            .GetResult();
        Assert.AreEqual(GpuOptimizationRecommendation.RestoreOriginal, blocked.Recommendation);
        Assert.IsTrue(blocked.Reasons.Any(static reason =>
            reason.Contains("hybrid", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("multi-GPU", StringComparison.OrdinalIgnoreCase)));
        Assert.IsFalse(blockedBackend.BackendAdvancedBeyondPreflight);
    }

    [TestMethod]
    public void NetworkBenchmarkRejectsRouteVpnWifiAndRssChurn()
    {
        var rss = new NetworkRssAdapterSnapshot(
            "Ethernet",
            "Test 10GbE Adapter",
            true,
            true,
            true,
            true,
            8,
            4,
            4,
            0,
            2,
            0,
            14,
            8,
            0,
            ["0:2", "0:4", "0:6", "0:8"],
            ["0:2", "0:4", "0:6", "0:8"])
        {
            PnpCorrelation = new NetworkRssPnpCorrelation(
                NetworkRssPnpCorrelationStatus.Available,
                "PCI\\VEN_TEST&DEV_NIC",
                null),
        };
        var target = new NetworkInterfaceContinuitySnapshot(
            "interface-1",
            "Test 10GbE Adapter",
            NetworkInterfaceType.Ethernet,
            OperationalStatus.Up,
            12,
            12,
            ["192.0.2.10"],
            ["192.0.2.1"],
            WifiConnectionContinuityStatus.NotApplicable,
            null,
            null);
        var before = new NetworkEnvironmentContinuitySnapshot(
            "PCI\\VEN_TEST&DEV_NIC",
            rss,
            target,
            ["interface-1"],
            new SystemAwakeTimeSnapshot(1_000, 10_000_000));
        var after = before with
        {
            AwakeTime = new SystemAwakeTimeSnapshot(21_000, 210_000_000),
        };

        Assert.IsTrue(NetworkEnvironmentContinuity.Evaluate(before, after).IsStable);
        Assert.IsFalse(NetworkEnvironmentContinuity.Evaluate(
            before,
            after with { UpInterfaceIds = ["interface-1", "vpn-interface"] }).IsStable);
        Assert.IsFalse(NetworkEnvironmentContinuity.Evaluate(
            before,
            after with
            {
                Rss = rss with { IndirectionTable = ["0:2", "0:4", "0:6", "0:10"] },
            }).IsStable);

        var wifiTarget = target with
        {
            InterfaceType = NetworkInterfaceType.Wireless80211,
            WifiStatus = WifiConnectionContinuityStatus.Available,
            WifiSsid = "TestWifi",
            WifiBssid = "00:11:22:33:44:55",
        };
        var wifiBefore = before with { TargetInterface = wifiTarget };
        var wifiAfter = after with
        {
            TargetInterface = wifiTarget with { WifiBssid = "00:11:22:33:44:66" },
        };
        Assert.IsFalse(NetworkEnvironmentContinuity.Evaluate(wifiBefore, wifiAfter).IsStable);
        Assert.IsFalse(NetworkEnvironmentContinuity.Evaluate(
            wifiBefore,
            after with
            {
                TargetInterface = wifiTarget with
                {
                    WifiStatus = WifiConnectionContinuityStatus.PermissionDenied,
                    WifiBssid = null,
                },
            }).IsStable);
    }

    private static GpuOptimizationBaselineEvidence EligibleBaseline()
    {
        var quality = BaselineQualityAnalyzer.Analyze(Enumerable.Range(1, 5)
            .Select(number => new BaselineWindowEvidence(
                number,
                DateTimeOffset.UnixEpoch,
                20_000,
                20_000,
                true,
                null,
                1_000,
                100,
                1_000,
                10))
            .ToArray());
        var workload = WorkloadStabilityAnalyzer.Analyze(
        [
            new WorkloadWindowEvidence(1, 20_000, 30_000, 12_000, 10.0),
            new WorkloadWindowEvidence(2, 20_000, 30_500, 12_100, 10.2),
            new WorkloadWindowEvidence(3, 20_000, 30_200, 12_050, 10.1),
            new WorkloadWindowEvidence(4, 20_000, 29_900, 11_950, 9.9),
            new WorkloadWindowEvidence(5, 20_000, 30_100, 12_000, 10.0),
        ]);

        return new GpuOptimizationBaselineEvidence(
            quality,
            Guid.NewGuid(),
            "scene-v1",
            "environment-v1",
            new string('a', 40),
            workload);
    }

    private sealed class RejectingGraphicsPreflightBackend : IGpuOptimizationExecutionBackend
    {
        public bool BackendAdvancedBeyondPreflight { get; private set; }

        public GpuGraphicsTargetIdentityResolution ResolveGraphicsTarget(
            string deviceInstanceId,
            string? presentMonApiPath,
            string? presentMonControlPipeName) =>
            new(false, null, "Hybrid or multi-GPU workload routing cannot be proven directly.");

        public GpuInterruptAffinitySnapshot CaptureOriginal(string deviceInstanceId)
        {
            BackendAdvancedBeyondPreflight = true;
            throw new InvalidOperationException("GPU mutation backend advanced past a rejected preflight.");
        }

        public Guid ApplyCandidate(string deviceInstanceId, GpuAffinityCandidate candidate)
        {
            BackendAdvancedBeyondPreflight = true;
            throw new InvalidOperationException("GPU mutation backend advanced past a rejected preflight.");
        }

        public void BeginMeasurement(Guid experimentId) =>
            throw new InvalidOperationException("GPU mutation backend advanced past a rejected preflight.");

        public Task<GpuOptimizationEvidenceCollectionResult> CaptureAsync(
            GpuOptimizationEvidenceRequest request,
            GpuInterruptAffinitySnapshot originalState,
            string? presentMonApiPath,
            string? presentMonControlPipeName,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("GPU mutation backend advanced past a rejected preflight.");

        public void AwaitDecision(Guid experimentId) =>
            throw new InvalidOperationException("GPU mutation backend advanced past a rejected preflight.");

        public void KeepCandidate(Guid experimentId) =>
            throw new InvalidOperationException("GPU mutation backend advanced past a rejected preflight.");

        public void Rollback(Guid experimentId) =>
            throw new InvalidOperationException("GPU mutation backend advanced past a rejected preflight.");
    }
}
