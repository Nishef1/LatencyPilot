using LatencyPilot.Benchmarking.Comparisons;

namespace LatencyPilot.Benchmarking.Optimization;

public enum OptimizationProfileKind
{
    CompetitiveGaming = 1,
    General = 2,
    AudioSensitive = 3,
}

public enum OptimizationSubsystem
{
    Gpu = 1,
    Usb = 2,
    Network = 3,
}

public sealed class OptimizationProfile
{
    private readonly HashSet<OptimizationSubsystem> enabledSubsystems;

    internal OptimizationProfile(
        string id,
        int version,
        OptimizationProfileKind kind,
        ComparisonPolicy comparisonPolicy,
        IEnumerable<OptimizationSubsystem> enabledSubsystems)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentOutOfRangeException.ThrowIfLessThan(version, 1);
        ArgumentNullException.ThrowIfNull(comparisonPolicy);
        ArgumentNullException.ThrowIfNull(enabledSubsystems);
        comparisonPolicy.Validate();

        Id = id;
        Version = version;
        Kind = kind;
        ComparisonPolicy = comparisonPolicy;
        this.enabledSubsystems = new HashSet<OptimizationSubsystem>(enabledSubsystems);

        if (this.enabledSubsystems.Any(static subsystem => !Enum.IsDefined(subsystem)))
        {
            throw new ArgumentOutOfRangeException(nameof(enabledSubsystems));
        }
    }

    public string Id { get; }

    public int Version { get; }

    public OptimizationProfileKind Kind { get; }

    public ComparisonPolicy ComparisonPolicy { get; }

    public IReadOnlySet<OptimizationSubsystem> EnabledSubsystems => enabledSubsystems;

    public bool IsSubsystemEnabled(OptimizationSubsystem subsystem)
    {
        if (!Enum.IsDefined(subsystem))
        {
            throw new ArgumentOutOfRangeException(nameof(subsystem));
        }

        return enabledSubsystems.Contains(subsystem);
    }

    public OptimizationProfile WithSubsystemEnabled(OptimizationSubsystem subsystem, bool enabled)
    {
        if (!Enum.IsDefined(subsystem))
        {
            throw new ArgumentOutOfRangeException(nameof(subsystem));
        }

        var next = new HashSet<OptimizationSubsystem>(enabledSubsystems);
        if (enabled)
        {
            next.Add(subsystem);
        }
        else
        {
            next.Remove(subsystem);
        }

        return new OptimizationProfile(Id, Version, Kind, ComparisonPolicy, next);
    }
}

public static class OptimizationProfiles
{
    private static readonly OptimizationSubsystem[] AllSubsystems =
    [
        OptimizationSubsystem.Gpu,
        OptimizationSubsystem.Usb,
        OptimizationSubsystem.Network,
    ];

    private static readonly OptimizationProfile CompetitiveGaming = new(
        "competitive-gaming-v1",
        1,
        OptimizationProfileKind.CompetitiveGaming,
        new ComparisonPolicy(
            MinimumSamples: 20,
            MinimumRelativeChange: 0.03,
            GuardrailRegressionLimit: 0.03,
            EvaluationPercentile: 0.99),
        AllSubsystems);

    private static readonly OptimizationProfile General = new(
        "general-v1",
        1,
        OptimizationProfileKind.General,
        new ComparisonPolicy(
            MinimumSamples: 20,
            MinimumRelativeChange: 0.03,
            GuardrailRegressionLimit: 0.05,
            EvaluationPercentile: 0.99),
        AllSubsystems);

    private static readonly OptimizationProfile AudioSensitive = new(
        "audio-sensitive-v1",
        1,
        OptimizationProfileKind.AudioSensitive,
        new ComparisonPolicy(
            MinimumSamples: 20,
            MinimumRelativeChange: 0.03,
            GuardrailRegressionLimit: 0.02,
            EvaluationPercentile: 0.99),
        AllSubsystems);

    public static OptimizationProfile Get(OptimizationProfileKind kind) => kind switch
    {
        OptimizationProfileKind.CompetitiveGaming => CompetitiveGaming,
        OptimizationProfileKind.General => General,
        OptimizationProfileKind.AudioSensitive => AudioSensitive,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
