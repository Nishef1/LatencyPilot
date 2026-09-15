using LatencyPilot.Benchmarking.Optimization;
using LatencyPilot.Core.Devices;
using LatencyPilot.Platform.Windows.Devices;
using LatencyPilot.Platform.Windows.System;
using LatencyPilot.Service;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LatencyPilot.CriticalTests;

[TestClass]
public sealed class GpuRuntimePlacementContractTests
{
    [TestMethod]
    public void GateAPlacementAndCaptureContinuityFailClosed()
    {
        var confirmed = new GpuInterruptRuntimePlacementEvidence(
            "PCI\\VEN_10DE&DEV_TEST",
            "nvlddmkm",
            3,
            200,
            200,
            0,
            5,
            [new ProcessorObservedInterruptCount(3, 200)]);

        Assert.IsTrue(confirmed.ConfirmsRequestedPlacement);
        Assert.IsTrue(
            GpuInterruptRuntimePlacementVerifier.ConfirmsGateAPlacement(
                storedCandidateBefore: true,
                storedCandidateAfter: true,
                captureIsValid: true,
                confirmed));

        Assert.IsFalse(
            GpuInterruptRuntimePlacementVerifier.ConfirmsGateAPlacement(
                storedCandidateBefore: false,
                storedCandidateAfter: true,
                captureIsValid: true,
                confirmed));
        Assert.IsFalse(
            GpuInterruptRuntimePlacementVerifier.ConfirmsGateAPlacement(
                storedCandidateBefore: true,
                storedCandidateAfter: false,
                captureIsValid: true,
                confirmed));
        Assert.IsFalse(
            GpuInterruptRuntimePlacementVerifier.ConfirmsGateAPlacement(
                storedCandidateBefore: true,
                storedCandidateAfter: true,
                captureIsValid: false,
                confirmed));

        var missingRuntimeEvidence = confirmed with
        {
            MatchingResolvedIsrEventCount = 0,
            TargetProcessorIsrEventCount = 0,
            ObservedProcessors = [],
        };
        Assert.IsFalse(missingRuntimeEvidence.ConfirmsRequestedPlacement);
        Assert.IsFalse(
            GpuInterruptRuntimePlacementVerifier.ConfirmsGateAPlacement(
                storedCandidateBefore: true,
                storedCandidateAfter: true,
                captureIsValid: true,
                missingRuntimeEvidence));

        var inconsistentResolvedCounts = confirmed with
        {
            TargetProcessorIsrEventCount = 199,
            OffTargetIsrEventCount = 0,
            ObservedProcessors = [new ProcessorObservedInterruptCount(3, 199)],
        };
        Assert.IsFalse(inconsistentResolvedCounts.ConfirmsRequestedPlacement);

        var offTargetEvidence = confirmed with
        {
            TargetProcessorIsrEventCount = 199,
            OffTargetIsrEventCount = 1,
            ObservedProcessors =
            [
                new ProcessorObservedInterruptCount(3, 199),
                new ProcessorObservedInterruptCount(4, 1),
            ],
        };
        Assert.IsFalse(offTargetEvidence.ConfirmsRequestedPlacement);

        var expectedRevision = new string('a', 40);
        Assert.IsTrue(GpuOptimizationSourceRevisionPolicy.IsExactMatch(expectedRevision, expectedRevision.ToUpperInvariant()));
        Assert.IsFalse(GpuOptimizationSourceRevisionPolicy.IsExactMatch(expectedRevision, new string('a', 39)));
        Assert.IsFalse(GpuOptimizationSourceRevisionPolicy.IsExactMatch(expectedRevision, new string('b', 40)));
        Assert.IsFalse(GpuOptimizationSourceRevisionPolicy.IsExactMatch(expectedRevision, null));
        Assert.IsFalse(GpuOptimizationSourceRevisionPolicy.IsExactMatch("dirty", expectedRevision));

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
                GraphicsDevices =
                [
                    new PresentMonGraphicsDeviceSnapshot(
                        7,
                        0,
                        "Wrong GPU",
                        new GraphicsAdapterLuid(1, 2)),
                ],
            }).IsUsable);

        var balancedScheme = new Guid("381b4222-f694-41f0-9685-ff5bb260df2e");
        var stablePower = new SystemPowerSnapshot(
            SystemPowerLineState.Online,
            BatteryPresent: false,
            Charging: null,
            BatteryPercent: null,
            BatterySaverEnabled: false,
            ActiveSchemeId: balancedScheme,
            ActiveSchemeName: "Balanced",
            UserConfiguredPowerModeId: Guid.Empty,
            UserConfiguredPowerMode: UserConfiguredPowerMode.Balanced);
        var stableContext = new GpuOptimizationCaptureContinuitySnapshot(
            ProcessId: 42,
            ProcessStartedAtUtc: DateTimeOffset.UnixEpoch,
            ProcessName: "game",
            ProcessSessionId: 1,
            ActiveConsoleSessionId: 1,
            Power: stablePower,
            AwakeTime: new SystemAwakeTimeSnapshot(1_000, 10_000_000),
            GraphicsTarget: resolved.Identity);
        var laterContext = stableContext with
        {
            AwakeTime = new SystemAwakeTimeSnapshot(31_000, 310_000_000),
        };

        Assert.IsTrue(GpuOptimizationCaptureContinuity.Evaluate(stableContext, laterContext).IsStable);
        Assert.IsFalse(GpuOptimizationCaptureContinuity.Evaluate(
            stableContext,
            stableContext with { ProcessStartedAtUtc = stableContext.ProcessStartedAtUtc.AddSeconds(1) }).IsStable);
        Assert.IsFalse(GpuOptimizationCaptureContinuity.Evaluate(
            stableContext,
            stableContext with { ActiveConsoleSessionId = 2 }).IsStable);
        Assert.IsFalse(GpuOptimizationCaptureContinuity.Evaluate(
            stableContext with { ProcessSessionId = 2 },
            stableContext with { ProcessSessionId = 2 }).IsStable);
        Assert.IsFalse(GpuOptimizationCaptureContinuity.Evaluate(
            stableContext,
            stableContext with
            {
                Power = stablePower with { BatterySaverEnabled = true },
            }).IsStable);
        Assert.IsFalse(GpuOptimizationCaptureContinuity.Evaluate(
            stableContext,
            laterContext with
            {
                AwakeTime = new SystemAwakeTimeSnapshot(36_000, 310_000_000),
            }).IsStable);
        Assert.IsFalse(GpuOptimizationCaptureContinuity.Evaluate(
            stableContext,
            laterContext with
            {
                GraphicsTarget = laterContext.GraphicsTarget with { PresentMonDeviceId = 8 },
            }).IsStable);
    }
}
