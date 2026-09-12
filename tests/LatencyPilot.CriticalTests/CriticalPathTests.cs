using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using LatencyPilot.Benchmarking.Comparisons;
using LatencyPilot.Benchmarking.Statistics;
using LatencyPilot.Core.Experiments;
using LatencyPilot.Core.Metrics;
using LatencyPilot.Core.Results;
using LatencyPilot.Platform.Windows.Devices;
using LatencyPilot.Platform.Windows.System;
using LatencyPilot.Protocol;
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
    public void ExperimentLifecycleRejectsIllegalTransitions()
    {
        ExperimentStateMachine.EnsureTransition(
            ExperimentState.Planned,
            ExperimentState.MeasuringBaseline);

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            ExperimentStateMachine.EnsureTransition(ExperimentState.Planned, ExperimentState.Kept));
    }

    [TestMethod]
    public void BenchmarkVerdictMatrixPreservesPrimaryAndGuardrailSemantics()
    {
        AssertVerdict(
            "insufficient samples",
            100,
            80,
            ExperimentVerdict.Inconclusive,
            count: 5);
        AssertVerdict(
            "inside noise threshold",
            100,
            98,
            ExperimentVerdict.NoMeasurableDifference);
        AssertVerdict(
            "clear primary improvement",
            100,
            80,
            ExperimentVerdict.Improved);
        AssertVerdict(
            "clear primary regression",
            100,
            120,
            ExperimentVerdict.Regressed);
        AssertVerdict(
            "improvement plus guardrail regression",
            100,
            80,
            ExperimentVerdict.Tradeoff,
            guardrailBaseline: 10,
            guardrailCandidate: 12);
        AssertVerdict(
            "neutral primary plus guardrail regression",
            100,
            99,
            ExperimentVerdict.Regressed,
            guardrailBaseline: 10,
            guardrailCandidate: 12);
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
        var request = new ObservationRequest(
            ProtocolVersion.Current,
            Guid.NewGuid(),
            ObservationCommand.GetStatus,
            null);

        using (var roundTrip = new MemoryStream())
        {
            await PipeMessageFraming.WriteAsync(
                roundTrip,
                request,
                ObservationProtocol.MaximumRequestBytes);
            roundTrip.Position = 0;

            var decoded = await PipeMessageFraming.ReadAsync<ObservationRequest>(
                roundTrip,
                ObservationProtocol.MaximumRequestBytes);
            Assert.AreEqual(request, decoded);
        }

        var json = $$"""
            {"protocolVersion":{{ProtocolVersion.Current}},"requestId":"{{request.RequestId}}","command":1,"kernelLatencyCapture":null,"unexpected":true}
            """;
        using (var unknownMember = FrameUtf8(json))
        {
            await AssertThrowsAsync<JsonException>(() =>
                PipeMessageFraming.ReadAsync<ObservationRequest>(
                    unknownMember,
                    ObservationProtocol.MaximumRequestBytes).AsTask());
        }

        using (var oversized = new MemoryStream())
        {
            var header = new byte[sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(header, 1024);
            await oversized.WriteAsync(header);
            oversized.Position = 0;

            await AssertThrowsAsync<InvalidDataException>(() =>
                PipeMessageFraming.ReadAsync<ObservationRequest>(oversized, 16).AsTask());
        }

        using (var truncated = new MemoryStream())
        {
            var header = new byte[sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(header, 16);
            await truncated.WriteAsync(header);
            await truncated.WriteAsync("{}"u8.ToArray());
            truncated.Position = 0;

            await AssertThrowsAsync<EndOfStreamException>(() =>
                PipeMessageFraming.ReadAsync<ObservationRequest>(truncated, 64).AsTask());
        }
    }

    [TestMethod]
    public void ObservationProtocolSurfaceRemainsReadOnly()
    {
        var commands = Enum.GetValues<ObservationCommand>();

        CollectionAssert.AreEqual(
            new[]
            {
                ObservationCommand.GetStatus,
                ObservationCommand.CaptureKernelLatency,
            },
            commands);
    }

    [TestMethod]
    public void WindowsReadOnlyInventoryCaptureIsInternallyConsistent()
    {
        var topology = ProcessorTopologyReader.Capture();
        var logicalProcessors = topology.Cores
            .SelectMany(static core => core.LogicalProcessors)
            .Distinct()
            .ToArray();
        var devices = DeviceInventoryReader.CapturePresentDevices();

        Assert.IsTrue(topology.PhysicalCoreCount > 0);
        Assert.IsTrue(topology.Packages.Count > 0);
        Assert.AreEqual(logicalProcessors.Length, topology.LogicalProcessorCount);
        Assert.IsTrue(topology.ProcessorGroupCount > 0);
        Assert.IsTrue(devices.PresentDeviceCount > 0);
        Assert.IsTrue(devices.Devices.All(static device => !string.IsNullOrWhiteSpace(device.InstanceId)));
        Assert.IsTrue(devices.DevicesWithDriverMetadataCount > 0);
    }

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
            result = BenchmarkComparer.Compare(
                baselineSeries,
                candidateSeries,
                policy: Policy);
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
            Assert.Fail(
                $"Expected {typeof(TException).Name}, but {exception.GetType().Name} was thrown: {exception.Message}");
        }

        Assert.Fail($"Expected {typeof(TException).Name}, but no exception was thrown.");
        throw new InvalidOperationException("Unreachable after Assert.Fail.");
    }

    private static MetricSeries Series(string name, double value, int count = 20) =>
        new(name, MetricDirection.LowerIsBetter, Enumerable.Repeat(value, count));
}
