using LatencyPilot.Benchmarking.Baselines;
using LatencyPilot.Benchmarking.Optimization;
using LatencyPilot.Core.Results;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LatencyPilot.CriticalTests;

[TestClass]
public sealed class OptimizationProfileTests
{
    private static readonly string[] ExpectedDominatingMetricOrder =
    [
        "input-report-p99",
        "frame-time-p99",
        "network-jitter-p99",
    ];

    [TestMethod]
    public void ProfilesAndParetoPolicyRemainTransparentAndFailClosed()
    {
        var competitive = OptimizationProfiles.Get(OptimizationProfileKind.CompetitiveGaming);
        var general = OptimizationProfiles.Get(OptimizationProfileKind.General);
        var audio = OptimizationProfiles.Get(OptimizationProfileKind.AudioSensitive);

        Assert.AreEqual("competitive-gaming-v1", competitive.Id);
        Assert.AreEqual("general-v1", general.Id);
        Assert.AreEqual("audio-sensitive-v1", audio.Id);
        Assert.AreEqual(1, competitive.Version);
        competitive.ComparisonPolicy.Validate();
        general.ComparisonPolicy.Validate();
        audio.ComparisonPolicy.Validate();
        Assert.IsTrue(competitive.IsSubsystemEnabled(OptimizationSubsystem.Gpu));
        Assert.IsTrue(competitive.IsSubsystemEnabled(OptimizationSubsystem.Usb));
        Assert.IsTrue(competitive.IsSubsystemEnabled(OptimizationSubsystem.Network));

        var networkOptOut = competitive.WithSubsystemEnabled(OptimizationSubsystem.Network, enabled: false);
        Assert.IsFalse(networkOptOut.IsSubsystemEnabled(OptimizationSubsystem.Network));
        Assert.IsTrue(networkOptOut.IsSubsystemEnabled(OptimizationSubsystem.Gpu));
        Assert.AreEqual(competitive.ComparisonPolicy, networkOptOut.ComparisonPolicy);
        Assert.AreEqual(competitive.Id, networkOptOut.Id);

        var stableWorkload = WorkloadStabilityAnalyzer.Analyze(
        [
            new WorkloadWindowEvidence(1, 20_000, 30_000, 12_000, 10.0),
            new WorkloadWindowEvidence(2, 20_000, 31_000, 12_200, 10.4),
            new WorkloadWindowEvidence(3, 20_000, 29_500, 11_900, 9.8),
            new WorkloadWindowEvidence(4, 20_000, 30_500, 12_100, 10.2),
            new WorkloadWindowEvidence(5, 20_000, 30_200, 12_050, 10.1),
        ]);
        Assert.AreEqual(WorkloadStabilityStatus.Stable, stableWorkload.Status);
        Assert.IsTrue(stableWorkload.IsEligibleForExperiment);
        Assert.AreEqual(0, stableWorkload.Reasons.Count);

        var stableQuality = BaselineQualityAnalyzer.Analyze(
        [
            new BaselineWindowEvidence(1, DateTimeOffset.UnixEpoch, 20_000, 20_000, true, null, 30_000, 100, 12_000, 50),
            new BaselineWindowEvidence(2, DateTimeOffset.UnixEpoch.AddSeconds(21), 20_000, 20_000, true, null, 31_000, 101, 12_200, 50.5),
            new BaselineWindowEvidence(3, DateTimeOffset.UnixEpoch.AddSeconds(42), 20_000, 20_000, true, null, 29_500, 99, 11_900, 49.5),
            new BaselineWindowEvidence(4, DateTimeOffset.UnixEpoch.AddSeconds(63), 20_000, 20_000, true, null, 30_500, 100.5, 12_100, 50.2),
            new BaselineWindowEvidence(5, DateTimeOffset.UnixEpoch.AddSeconds(84), 20_000, 20_000, true, null, 30_200, 100.2, 12_050, 50.1),
        ]);
        Assert.IsTrue(GpuOptimizationBaselineReadiness.IsEligible(stableQuality, stableWorkload));

        var changingWorkload = WorkloadStabilityAnalyzer.Analyze(
        [
            new WorkloadWindowEvidence(1, 20_302.5, 39_533, 17_003, 12.519),
            new WorkloadWindowEvidence(2, 20_302.5, 28_979, 11_158, 13.383),
            new WorkloadWindowEvidence(3, 20_302.5, 27_815, 11_315, 9.493),
            new WorkloadWindowEvidence(4, 20_302.5, 27_537, 11_645, 7.537),
            new WorkloadWindowEvidence(5, 20_302.5, 26_604, 11_207, 6.423),
        ]);
        Assert.AreEqual(WorkloadStabilityStatus.Changing, changingWorkload.Status);
        Assert.IsFalse(changingWorkload.IsEligibleForExperiment);
        Assert.IsFalse(GpuOptimizationBaselineReadiness.IsEligible(stableQuality, changingWorkload));
        Assert.IsTrue(changingWorkload.Reasons.Any(static reason =>
            reason.Contains("workload activity", StringComparison.OrdinalIgnoreCase)));

        var spikyWorkload = WorkloadStabilityAnalyzer.Analyze(
        [
            new WorkloadWindowEvidence(1, 20_000, 30_000, 12_000, 10.0),
            new WorkloadWindowEvidence(2, 20_000, 30_000, 12_000, 10.0),
            new WorkloadWindowEvidence(3, 20_000, 80_000, 40_000, 35.0),
            new WorkloadWindowEvidence(4, 20_000, 30_000, 12_000, 10.0),
            new WorkloadWindowEvidence(5, 20_000, 30_000, 12_000, 10.0),
        ]);
        Assert.AreEqual(WorkloadStabilityStatus.Changing, spikyWorkload.Status);
        Assert.IsFalse(spikyWorkload.IsEligibleForExperiment);
        Assert.IsTrue(spikyWorkload.Signals.Any(static signal => signal.HasExtremeWindow));
        Assert.IsFalse(GpuOptimizationBaselineReadiness.IsEligible(stableQuality, spikyWorkload));

        var incompleteWorkload = WorkloadStabilityAnalyzer.Analyze(
        [
            new WorkloadWindowEvidence(1, 20_000, 30_000, 12_000, 10.0),
            new WorkloadWindowEvidence(2, 20_000, 30_000, 12_000, 10.0),
            new WorkloadWindowEvidence(3, 20_000, 30_000, 12_000, 10.0),
            new WorkloadWindowEvidence(4, 20_000, 30_000, 12_000, 10.0),
        ]);
        Assert.AreEqual(WorkloadStabilityStatus.Insufficient, incompleteWorkload.Status);
        Assert.IsFalse(incompleteWorkload.IsEligibleForExperiment);
        Assert.IsFalse(GpuOptimizationBaselineReadiness.IsEligible(stableQuality, incompleteWorkload));

        var dominates = ParetoDecisionPolicy.Evaluate(
        [
            new ParetoMetricOutcome("input-report-p99", ExperimentVerdict.Improved),
            new ParetoMetricOutcome("frame-time-p99", ExperimentVerdict.NoMeasurableDifference),
            new ParetoMetricOutcome("network-jitter-p99", ExperimentVerdict.Improved),
        ]);
        Assert.AreEqual(ParetoRelation.Dominates, dominates.Relation);
        CollectionAssert.AreEqual(
            ExpectedDominatingMetricOrder,
            dominates.Outcomes.Select(static outcome => outcome.MetricName).ToArray());

        Assert.AreEqual(
            ParetoRelation.Dominated,
            ParetoDecisionPolicy.Evaluate(
            [
                new ParetoMetricOutcome("input-report-p99", ExperimentVerdict.Regressed),
                new ParetoMetricOutcome("frame-time-p99", ExperimentVerdict.NoMeasurableDifference),
            ]).Relation);
        Assert.AreEqual(
            ParetoRelation.Equivalent,
            ParetoDecisionPolicy.Evaluate(
            [
                new ParetoMetricOutcome("input-report-p99", ExperimentVerdict.NoMeasurableDifference),
                new ParetoMetricOutcome("frame-time-p99", ExperimentVerdict.NoMeasurableDifference),
            ]).Relation);
        Assert.AreEqual(
            ParetoRelation.Tradeoff,
            ParetoDecisionPolicy.Evaluate(
            [
                new ParetoMetricOutcome("input-report-p99", ExperimentVerdict.Improved),
                new ParetoMetricOutcome("frame-time-p99", ExperimentVerdict.Regressed),
            ]).Relation);
        Assert.AreEqual(
            ParetoRelation.Tradeoff,
            ParetoDecisionPolicy.Evaluate(
            [
                new ParetoMetricOutcome("input-report-p99", ExperimentVerdict.Tradeoff),
            ]).Relation);
        Assert.AreEqual(
            ParetoRelation.Inconclusive,
            ParetoDecisionPolicy.Evaluate(
            [
                new ParetoMetricOutcome("input-report-p99", ExperimentVerdict.Improved),
                new ParetoMetricOutcome("frame-time-p99", ExperimentVerdict.Inconclusive),
            ]).Relation);
        Assert.AreEqual(ParetoRelation.Inconclusive, ParetoDecisionPolicy.Evaluate([]).Relation);
        Assert.ThrowsExactly<ArgumentException>(() => ParetoDecisionPolicy.Evaluate(
        [
            new ParetoMetricOutcome("same", ExperimentVerdict.Improved),
            new ParetoMetricOutcome("same", ExperimentVerdict.NoMeasurableDifference),
        ]));
        Assert.IsNull(typeof(ParetoDecisionResult).GetProperty("Score"));
    }
}
