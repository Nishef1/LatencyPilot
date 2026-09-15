using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using LatencyPilot.Benchmarking.Baselines;
using LatencyPilot.Benchmarking.Comparisons;
using LatencyPilot.Benchmarking.Statistics;
using LatencyPilot.Core.Devices;
using LatencyPilot.Core.Experiments;
using LatencyPilot.Core.Metrics;
using LatencyPilot.Core.Results;
using LatencyPilot.Persistence;
using LatencyPilot.Platform.Windows.Devices;
using LatencyPilot.Platform.Windows.Interop;
using LatencyPilot.Platform.Windows.System;
using LatencyPilot.Protocol;
using Microsoft.Win32;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LatencyPilot.CriticalTests;

[TestClass]
public sealed class CriticalPathTests
{
    private static readonly ComparisonPolicy Policy = new(
        MinimumSamples: 20,
        MinimumRelativeChange: 0.03,
        GuardrailRegressionLimit: 0.05,
        EvaluationPercentile: 0.99);

    [TestMethod]
    public void ExperimentLifecycleAndDurableJournalRejectUnsafeTransitions()
    {
        ExperimentStateMachine.EnsureTransition(
            ExperimentState.Planned,
            ExperimentState.MeasuringBaseline);

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            ExperimentStateMachine.EnsureTransition(ExperimentState.Planned, ExperimentState.Kept));

        AssertTransactionalRegistryCommitAndRollback();

        var tempDirectory = Path.Combine(Path.GetTempPath(), "LatencyPilot.CriticalTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        try
        {
            var journal = new MutationJournal(Path.Combine(tempDirectory, "journal.db"));
            journal.Initialize();

            var firstId = Guid.NewGuid();
            var prepared = journal.CreatePrepared(
                firstId,
                "gpu-interrupt-affinity",
                "PCI\\VEN_TEST&DEV_TEST",
                "{\"devicePolicy\":null,\"assignmentSetOverride\":null}",
                "{\"devicePolicy\":4,\"assignmentSetOverride\":2}",
                DateTimeOffset.UnixEpoch);

            Assert.AreEqual(MutationJournalState.Prepared, prepared.State);
            Assert.AreEqual(0L, prepared.Revision);
            Assert.IsTrue(journal.HasUnresolved());

            Assert.ThrowsExactly<InvalidOperationException>(() =>
                journal.CreatePrepared(
                    Guid.NewGuid(),
                    "gpu-interrupt-affinity",
                    "PCI\\VEN_OTHER&DEV_OTHER",
                    "{}",
                    "{}"));

            var applying = journal.Transition(
                firstId,
                prepared.Revision,
                MutationJournalState.Prepared,
                MutationJournalState.Applying,
                nowUtc: DateTimeOffset.UnixEpoch.AddSeconds(1));
            Assert.AreEqual(MutationJournalState.Applying, applying.State);

            var recovery = journal.Transition(
                firstId,
                applying.Revision,
                MutationJournalState.Applying,
                MutationJournalState.RecoveryRequired,
                "simulated interruption",
                DateTimeOffset.UnixEpoch.AddSeconds(2));
            Assert.AreEqual(MutationJournalState.RecoveryRequired, recovery.State);
            Assert.AreEqual("simulated interruption", recovery.FailureReason);

            Assert.ThrowsExactly<InvalidOperationException>(() =>
                journal.Transition(
                    firstId,
                    recovery.Revision,
                    MutationJournalState.RecoveryRequired,
                    MutationJournalState.Kept));

            var reverting = journal.Transition(
                firstId,
                recovery.Revision,
                MutationJournalState.RecoveryRequired,
                MutationJournalState.Reverting,
                nowUtc: DateTimeOffset.UnixEpoch.AddSeconds(3));
            var reverted = journal.Transition(
                firstId,
                reverting.Revision,
                MutationJournalState.Reverting,
                MutationJournalState.Reverted,
                nowUtc: DateTimeOffset.UnixEpoch.AddSeconds(4));

            Assert.IsTrue(reverted.IsTerminal);
            Assert.IsFalse(journal.HasUnresolved());

            // Terminal recovery must unblock the next experiment; stale revisions must not.
            var second = journal.CreatePrepared(
                Guid.NewGuid(),
                "gpu-interrupt-affinity",
                "PCI\\VEN_NEXT&DEV_NEXT",
                "{}",
                "{}");
            Assert.ThrowsExactly<InvalidOperationException>(() =>
                journal.Transition(
                    second.ExperimentId,
                    expectedRevision: 99,
                    MutationJournalState.Prepared,
                    MutationJournalState.Applying));
        }
        finally
        {
            try
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
            catch
            {
                // SQLite pooling can briefly retain file handles on some runners. Cleanup is best-effort.
            }
        }
    }

    [TestMethod]
    public void BenchmarkVerdictMatrixPreservesPrimaryAndGuardrailSemantics()
    {
        AssertVerdict("insufficient samples", 100, 80, ExperimentVerdict.Inconclusive, count: 5);
        AssertVerdict("inside noise threshold", 100, 98, ExperimentVerdict.NoMeasurableDifference);
        AssertVerdict("clear primary improvement", 100, 80, ExperimentVerdict.Improved);
        AssertVerdict("clear primary regression", 100, 120, ExperimentVerdict.Regressed);
        AssertVerdict("improvement plus guardrail regression", 100, 80, ExperimentVerdict.Tradeoff, guardrailBaseline: 10, guardrailCandidate: 12);
        AssertVerdict("neutral primary plus guardrail regression", 100, 99, ExperimentVerdict.Regressed, guardrailBaseline: 10, guardrailCandidate: 12);
        foreach (var invalidValue in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
                (Policy with { MinimumRelativeChange = invalidValue }).Validate());
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
                (Policy with { GuardrailRegressionLimit = invalidValue }).Validate());
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
                (Policy with { EvaluationPercentile = invalidValue }).Validate());
        }

        AssertVerdict("primary and guardrail regression", 100, 120, ExperimentVerdict.Regressed, guardrailBaseline: 10, guardrailCandidate: 12);
        var baseline = Series("DPC p99", 100);
        var regression = Series("DPC p99", 120);
        Assert.AreEqual(ExperimentVerdict.Inconclusive, BenchmarkComparer.Compare(
            baseline,
            regression,
            [(Series("USB jitter", 10), Series("USB jitter", 12, count: 5))],
            Policy).Verdict);
        Assert.ThrowsExactly<ArgumentException>(() => BenchmarkComparer.Compare(
            baseline,
            regression,
            [(Series("USB jitter", 10), new MetricSeries("USB jitter", MetricDirection.HigherIsBetter, Enumerable.Repeat(12d, 20)))],
            Policy));

        var overflowPairs = new[]
        {
            (Series("overflow", double.Epsilon), Series("overflow", double.MaxValue)),
            (Series("overflow", -double.MaxValue), Series("overflow", double.MaxValue)),
            (
                new MetricSeries("overflow", MetricDirection.LowerIsBetter, [-double.MaxValue, double.MaxValue]),
                new MetricSeries("overflow", MetricDirection.LowerIsBetter, [-double.MaxValue, double.MaxValue]))
        };
        var overflowPolicy = Policy with { MinimumSamples = 2, EvaluationPercentile = 0.5 };
        foreach (var (overflowBaseline, overflowCandidate) in overflowPairs)
        {
            Assert.AreEqual(ExperimentVerdict.Inconclusive, BenchmarkComparer.Compare(
                overflowBaseline, overflowCandidate, policy: overflowPolicy).Verdict);
            Assert.AreEqual(ExperimentVerdict.Inconclusive, BenchmarkComparer.Compare(
                baseline, regression, [(overflowBaseline, overflowCandidate)], overflowPolicy).Verdict);
        }
    }

    [TestMethod]
    public void BaselineQualityGateRequiresCleanStableRepeatedWindows()
    {
        BaselineWindowEvidence[] stable =
        [
            Window(1, 100.0, 50.0),
            Window(2, 102.0, 51.0),
            Window(3, 99.0, 49.5),
            Window(4, 101.0, 50.5),
            Window(5, 100.0, 50.0),
        ];

        var stableResult = BaselineQualityAnalyzer.Analyze(stable);
        Assert.AreEqual(BaselineQualityStatus.Valid, stableResult.Status);
        Assert.IsTrue(stableResult.IsValidForComparison);
        Assert.AreEqual(0, stableResult.Reasons.Count);

        Assert.ThrowsExactly<ArgumentException>(() =>
            BaselineQualityAnalyzer.Analyze(
                stable,
                new BaselineQualityPolicy(MaximumRelativeNoiseFloor: 0.31)));

        var extraWindow = stable.Append(Window(6, 100.0, 50.0)).ToArray();
        var extraWindowResult = BaselineQualityAnalyzer.Analyze(extraWindow);
        Assert.AreEqual(BaselineQualityStatus.Inconclusive, extraWindowResult.Status);
        Assert.IsFalse(extraWindowResult.IsValidForComparison);
        Assert.IsTrue(extraWindowResult.Reasons.Any(static reason =>
            reason.Contains("exactly 5", StringComparison.OrdinalIgnoreCase)));

        var drifted = stable
            .Select(window => window.WindowNumber >= 4
                ? window with { DpcP99Microseconds = 145.0, IsrP99Microseconds = 72.0 }
                : window)
            .ToArray();
        var driftedResult = BaselineQualityAnalyzer.Analyze(drifted);
        Assert.AreEqual(BaselineQualityStatus.Inconclusive, driftedResult.Status);
        Assert.IsTrue(driftedResult.Reasons.Any(static reason => reason.Contains("drift", StringComparison.OrdinalIgnoreCase)));

        var lost = stable.ToArray();
        lost[2] = lost[2] with
        {
            CaptureIntegrityValid = false,
            CaptureIntegrityIssue = "ETW lost 4 events.",
        };
        var lostResult = BaselineQualityAnalyzer.Analyze(lost);
        Assert.AreEqual(BaselineQualityStatus.Inconclusive, lostResult.Status);
        Assert.IsFalse(lostResult.IsValidForComparison);
        Assert.IsTrue(lostResult.Reasons.Any(static reason => reason.Contains("ETW lost", StringComparison.Ordinal)));

        var tooShort = stable.ToArray();
        tooShort[0] = tooShort[0] with
        {
            RequestedDurationMilliseconds = 5_000,
            ActualDurationMilliseconds = 5_000d,
        };
        var tooShortResult = BaselineQualityAnalyzer.Analyze(tooShort);
        Assert.AreEqual(BaselineQualityStatus.Inconclusive, tooShortResult.Status);
        Assert.IsTrue(tooShortResult.Reasons.Any(static reason =>
            reason.Contains("inadequate duration", StringComparison.OrdinalIgnoreCase)));

        var undersampled = stable.ToArray();
        undersampled[1] = undersampled[1] with { DpcEventCount = 999 };
        var undersampledResult = BaselineQualityAnalyzer.Analyze(undersampled);
        Assert.AreEqual(BaselineQualityStatus.Inconclusive, undersampledResult.Status);
        Assert.IsTrue(undersampledResult.Reasons.Any(static reason =>
            reason.Contains("insufficient event evidence", StringComparison.OrdinalIgnoreCase)));

        var gapped = stable
            .Select(window => window.WindowNumber >= 3
                ? window with { WindowNumber = window.WindowNumber + 1 }
                : window)
            .ToArray();
        Assert.ThrowsExactly<ArgumentException>(() => BaselineQualityAnalyzer.Analyze(gapped));

        var wallClockAdjusted = stable.ToArray();
        wallClockAdjusted[3] = wallClockAdjusted[2] with
        {
            WindowNumber = 4,
            StartedAtUtc = wallClockAdjusted[2].StartedAtUtc.AddMinutes(-1),
            DpcP99Microseconds = 101.0,
            IsrP99Microseconds = 50.5,
        };
        var adjustedResult = BaselineQualityAnalyzer.Analyze(wallClockAdjusted);
        Assert.AreEqual(BaselineQualityStatus.Valid, adjustedResult.Status);
        Assert.IsTrue(adjustedResult.IsValidForComparison);
    }

    [TestMethod]
    public void NonFiniteMeasurementIsRejected()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new MetricSeries("DPC p99", MetricDirection.LowerIsBetter, [100, double.NaN]));
    }

    [TestMethod]
    public void PercentileEstimatorUsesOneDocumentedInterpolationRule()
    {
        double[] samples = [1, 2, 3, 4, 100];

        var p50 = Percentiles.Calculate(samples, 0.50);
        var p95 = Percentiles.Calculate(samples, 0.95);
        var p99 = Percentiles.Calculate(samples, 0.99);
        var p999 = Percentiles.Calculate(samples, 0.999);

        Assert.AreEqual(3d, p50, 0.000001);
        Assert.AreEqual(80.8d, p95, 0.000001);
        Assert.AreEqual(96.16d, p99, 0.000001);
        Assert.AreEqual(99.616d, p999, 0.000001);
        Assert.AreEqual(p95, Percentiles.CalculateSorted(samples, 0.95), 0.000001);
        Assert.IsTrue(p50 <= p95 && p95 <= p99 && p99 <= p999 && p999 <= samples[^1]);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            Percentiles.Calculate(samples, double.NaN));
    }

    [TestMethod]
    public async Task PipeFramingFailsClosedOnMalformedOrUnknownInput()
    {
        var request = new ObservationRequest(ProtocolVersion.Current, Guid.NewGuid(), ObservationCommand.GetStatus, null);

        using (var roundTrip = new MemoryStream())
        {
            await PipeMessageFraming.WriteAsync(roundTrip, request, ObservationProtocol.MaximumRequestBytes);
            roundTrip.Position = 0;
            var decoded = await PipeMessageFraming.ReadAsync<ObservationRequest>(roundTrip, ObservationProtocol.MaximumRequestBytes);
            Assert.AreEqual(request, decoded);
        }

        var captureRequestId = Guid.NewGuid();
        var emptyDistribution = new LatencyDistribution(0, null, null, null, null, null);
        var capture = new KernelLatencyCaptureResponse(
            captureRequestId,
            DateTimeOffset.UnixEpoch,
            5_000,
            5_000d,
            0,
            0,
            0,
            false,
            0,
            0,
            false,
            false,
            emptyDistribution,
            emptyDistribution,
            new LatencyThresholdSummary(100d, 0, 0, 0),
            new LatencyThresholdSummary(25d, 0, 0, 0),
            [],
            [],
            []);
        var response = new ObservationResponse(
            ProtocolVersion.Current,
            captureRequestId,
            ObservationResponseStatus.Ok,
            ObservationErrorCode.None,
            null,
            null,
            capture);
        using (var responseRoundTrip = new MemoryStream())
        {
            await PipeMessageFraming.WriteAsync(responseRoundTrip, response, ObservationProtocol.MaximumResponseBytes);
            responseRoundTrip.Position = 0;
            var decoded = await PipeMessageFraming.ReadAsync<ObservationResponse>(responseRoundTrip, ObservationProtocol.MaximumResponseBytes);
            Assert.AreEqual(captureRequestId, decoded.KernelLatencyCapture?.RequestId);
            Assert.IsNull(decoded.KernelLatencyCapture?.Dpc.P999Microseconds);
        }

        var json = $$"""
            {"protocolVersion":{{ProtocolVersion.Current}},"requestId":"{{request.RequestId}}","command":1,"kernelLatencyCapture":null,"unexpected":true}
            """;
        using (var unknownMember = FrameUtf8(json))
        {
            await AssertThrowsAsync<JsonException>(() =>
                PipeMessageFraming.ReadAsync<ObservationRequest>(unknownMember, ObservationProtocol.MaximumRequestBytes).AsTask());
        }

        using (var oversized = new MemoryStream())
        {
            var header = new byte[sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(header, 1024);
            await oversized.WriteAsync(header);
            oversized.Position = 0;
            await AssertThrowsAsync<InvalidDataException>(() => PipeMessageFraming.ReadAsync<ObservationRequest>(oversized, 16).AsTask());
        }

        using (var truncated = new MemoryStream())
        {
            var header = new byte[sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(header, 16);
            await truncated.WriteAsync(header);
            await truncated.WriteAsync("{}"u8.ToArray());
            truncated.Position = 0;
            await AssertThrowsAsync<EndOfStreamException>(() => PipeMessageFraming.ReadAsync<ObservationRequest>(truncated, 64).AsTask());
        }
    }

    [TestMethod]
    public void ObservationProtocolSurfaceRemainsReadOnly()
    {
        var commands = Enum.GetValues<ObservationCommand>();
        CollectionAssert.AreEqual(
            new[] { ObservationCommand.GetStatus, ObservationCommand.CaptureKernelLatency },
            commands);
    }

    [TestMethod]
    public void WindowsReadOnlyInventoryCaptureIsInternallyConsistent()
    {
        var topology = ProcessorTopologyReader.Capture();
        var logicalProcessors = topology.Cores.SelectMany(static core => core.LogicalProcessors).Distinct().ToArray();
        var devices = DeviceInventoryReader.CapturePresentDevices();
        var representativeDevices = RepresentativeDeviceEvidenceSelector.Select(devices);
        var runtimeContext = RuntimeMeasurementContextReader.Capture();

        Assert.IsTrue(topology.PhysicalCoreCount > 0);
        Assert.IsTrue(topology.Packages.Count > 0);
        Assert.AreEqual(logicalProcessors.Length, topology.LogicalProcessorCount);
        Assert.IsTrue(topology.ProcessorGroupCount > 0);
        Assert.IsTrue(devices.PresentDeviceCount > 0);
        Assert.IsTrue(devices.Devices.All(static device => !string.IsNullOrWhiteSpace(device.InstanceId)));
        Assert.IsTrue(devices.DevicesWithDriverMetadataCount > 0);

        Assert.IsTrue(representativeDevices
            .GroupBy(static device => device.Kind)
            .All(static group => group.Count() <= 3));
        Assert.IsTrue(representativeDevices.All(device => devices.Devices.Contains(device.Device)));
        Assert.IsTrue(representativeDevices
            .Where(static device => device.Kind == RepresentativeDeviceKind.XhciController)
            .All(static device => string.Equals(device.Device.ServiceName, "USBXHCI", StringComparison.OrdinalIgnoreCase)));

        Assert.IsTrue(runtimeContext.SystemLoad.KernelTime100Nanoseconds >= runtimeContext.SystemLoad.IdleTime100Nanoseconds);
        Assert.IsTrue(Enum.IsDefined(runtimeContext.Power.LineState));
        if (runtimeContext.Power.BatteryPercent is not null)
        {
            Assert.IsTrue(runtimeContext.Power.BatteryPercent is >= 0 and <= 100);
        }
        if (runtimeContext.Power.UserConfiguredPowerMode is not null)
        {
            Assert.IsTrue(Enum.IsDefined(runtimeContext.Power.UserConfiguredPowerMode.Value));
        }

        var calculatedBusy = RuntimeMeasurementContextReader.CalculateSystemCpuBusyPercent(
            new SystemLoadSnapshot(100, 500, 300),
            new SystemLoadSnapshot(200, 800, 500));
        Assert.IsNotNull(calculatedBusy);
        Assert.AreEqual(80d, calculatedBusy.Value, 0.000001d);

        var usbPorts = new[]
        {
            new UsbHubPortSnapshot(
                "\\\\?\\usb#root_hub30#test",
                "USB\\ROOT_HUB30\\TEST",
                "PCI\\VEN_TEST&DEV_XHCI",
                3,
                "{745A17A0-74D3-11D0-B6FE-00A0C90F57DA}\\0001",
                UsbPortConnectionStatus.Connected,
                UsbDeviceSpeed.High,
                false),
        };
        var exactUsbRoute = UsbPortRouteCorrelator.Resolve(
            "{745a17a0-74d3-11d0-b6fe-00a0c90f57da}\\0001",
            "PCI\\VEN_TEST&DEV_XHCI",
            usbPorts);
        Assert.AreEqual(UsbPortRouteResolutionStatus.Available, exactUsbRoute.Status);
        Assert.AreEqual(3u, exactUsbRoute.Port?.ConnectionIndex);

        var missingUsbRoute = UsbPortRouteCorrelator.Resolve(
            "{745A17A0-74D3-11D0-B6FE-00A0C90F57DA}\\9999",
            "PCI\\VEN_TEST&DEV_XHCI",
            usbPorts);
        Assert.AreEqual(UsbPortRouteResolutionStatus.NotFound, missingUsbRoute.Status);
        Assert.IsNull(missingUsbRoute.Port);

        var duplicateUsbRoute = UsbPortRouteCorrelator.Resolve(
            usbPorts[0].DriverKeyName,
            "PCI\\VEN_TEST&DEV_XHCI",
            [usbPorts[0], usbPorts[0] with { ConnectionIndex = 4 }]);
        Assert.AreEqual(UsbPortRouteResolutionStatus.Ambiguous, duplicateUsbRoute.Status);
        Assert.IsNull(duplicateUsbRoute.Port);
    }

    private static void AssertTransactionalRegistryCommitAndRollback()
    {
        var subKeyPath = $"Software\\LatencyPilot.CriticalTests\\TxR-{Guid.NewGuid():N}";
        try
        {
            using (var transaction = TransactionalRegistry.Begin("LatencyPilot critical-test rollback"))
            {
                using var key = transaction.CreateOrOpenKey(RegistryHive.CurrentUser, subKeyPath);
                key.SetValue("first", 1, RegistryValueKind.DWord);
                key.SetValue("second", 2, RegistryValueKind.DWord);
            }

            using (var rolledBack = Registry.CurrentUser.OpenSubKey(subKeyPath, writable: false))
            {
                Assert.IsNull(rolledBack, "Closing an uncommitted registry transaction must roll back all values.");
            }

            using (var transaction = TransactionalRegistry.Begin("LatencyPilot critical-test commit"))
            {
                using var key = transaction.CreateOrOpenKey(RegistryHive.CurrentUser, subKeyPath);
                key.SetValue("first", 1, RegistryValueKind.DWord);
                key.SetValue("second", 2, RegistryValueKind.DWord);
                transaction.Commit();
            }

            using var committed = Registry.CurrentUser.OpenSubKey(subKeyPath, writable: false);
            Assert.IsNotNull(committed);
            Assert.AreEqual(1, committed.GetValue("first"));
            Assert.AreEqual(2, committed.GetValue("second"));
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(subKeyPath, throwOnMissingSubKey: false);
        }
    }

    private static BaselineWindowEvidence Window(int number, double dpcP99, double isrP99) =>
        new(
            number,
            DateTimeOffset.UnixEpoch.AddSeconds(number),
            20_000,
            20_000d,
            true,
            null,
            2_000,
            dpcP99,
            2_000,
            isrP99);

    private static void AssertVerdict(
        string scenario,
        double baseline,
        double candidate,
        ExperimentVerdict expected,
        int count = 20,
        double? guardrailBaseline = null,
        double? guardrailCandidate = null)
    {
        var baselineSeries = Series("DPC p99", baseline, count);
        var candidateSeries = Series("DPC p99", candidate, count);

        ComparisonResult result;
        if (guardrailBaseline is not null && guardrailCandidate is not null)
        {
            result = BenchmarkComparer.Compare(
                baselineSeries,
                candidateSeries,
                [(Series("USB jitter", guardrailBaseline.Value), Series("USB jitter", guardrailCandidate.Value))],
                Policy);
            CollectionAssert.Contains(result.RegressedGuardrails.ToList(), "USB jitter", scenario);
        }
        else
        {
            result = BenchmarkComparer.Compare(baselineSeries, candidateSeries, policy: Policy);
        }

        Assert.AreEqual(expected, result.Verdict, scenario);
    }

    private static MemoryStream FrameUtf8(string json)
    {
        var payload = Encoding.UTF8.GetBytes(json);
        var stream = new MemoryStream();
        var header = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        stream.Write(header);
        stream.Write(payload);
        stream.Position = 0;
        return stream;
    }

    private static async Task<TException> AssertThrowsAsync<TException>(Func<Task> action)
        where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException exception)
        {
            return exception;
        }
        catch (Exception exception)
        {
            Assert.Fail($"Expected {typeof(TException).Name}, but {exception.GetType().Name} was thrown: {exception.Message}");
        }

        Assert.Fail($"Expected {typeof(TException).Name}, but no exception was thrown.");
        throw new InvalidOperationException("Unreachable after Assert.Fail.");
    }

    private static MetricSeries Series(string name, double value, int count = 20) =>
        new(name, MetricDirection.LowerIsBetter, Enumerable.Repeat(value, count));
}
