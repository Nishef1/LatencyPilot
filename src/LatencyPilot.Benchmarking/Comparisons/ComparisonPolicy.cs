namespace LatencyPilot.Benchmarking.Comparisons;

public sealed record ComparisonPolicy(
    int MinimumSamples = 20,
    double MinimumRelativeChange = 0.03,
    double GuardrailRegressionLimit = 0.05,
    double EvaluationPercentile = 0.99)
{
    public void Validate()
    {
        if (MinimumSamples < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(MinimumSamples));
        }

        if (!double.IsFinite(MinimumRelativeChange) || MinimumRelativeChange is <= 0 or >= 1)
        {
            throw new ArgumentOutOfRangeException(nameof(MinimumRelativeChange));
        }

        if (!double.IsFinite(GuardrailRegressionLimit) || GuardrailRegressionLimit is <= 0 or >= 1)
        {
            throw new ArgumentOutOfRangeException(nameof(GuardrailRegressionLimit));
        }

        if (!double.IsFinite(EvaluationPercentile) || EvaluationPercentile is <= 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(EvaluationPercentile));
        }
    }
}
