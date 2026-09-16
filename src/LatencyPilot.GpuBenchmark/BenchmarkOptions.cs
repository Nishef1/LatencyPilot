using System.Globalization;

namespace LatencyPilot.GpuBenchmark;

internal sealed record BenchmarkOptions(
    Guid SessionId,
    int Width,
    int Height,
    TimeSpan Duration,
    int WorkerCount,
    int Seed,
    string OutputPath)
{
    private const int MinimumWidth = 320;
    private const int MaximumWidth = 7680;
    private const int MinimumHeight = 240;
    private const int MaximumHeight = 4320;
    private const double MinimumDurationSeconds = 5;
    private const double MaximumDurationSeconds = 300;
    private const int MaximumWorkers = 64;

    internal static BenchmarkOptions Parse(IReadOnlyList<string> args)
    {
        var values = ParseNamedArguments(args);
        var sessionId = ParseRequiredGuid(values, "session-id");
        var width = ParseRequiredInt(values, "width", MinimumWidth, MaximumWidth);
        var height = ParseRequiredInt(values, "height", MinimumHeight, MaximumHeight);
        var durationSeconds = ParseRequiredDouble(
            values,
            "duration-seconds",
            MinimumDurationSeconds,
            MaximumDurationSeconds);
        var workerCount = ParseRequiredInt(values, "worker-count", 1, MaximumWorkers);
        var seed = ParseRequiredInt(values, "seed", 0, int.MaxValue);
        var outputPath = ParseRequiredString(values, "output");

        return new BenchmarkOptions(
            sessionId,
            width,
            height,
            TimeSpan.FromSeconds(durationSeconds),
            workerCount,
            seed,
            Path.GetFullPath(outputPath));
    }

    private static Dictionary<string, string> ParseNamedArguments(IReadOnlyList<string> args)
    {
        if (args.Count == 0 || args.Count % 2 != 0)
        {
            throw new ArgumentException(
                "Benchmark options must be provided as --name value pairs.",
                nameof(args));
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < args.Count; index += 2)
        {
            var name = args[index];
            if (!name.StartsWith("--", StringComparison.Ordinal) || name.Length <= 2)
            {
                throw new ArgumentException($"Invalid option name '{name}'.", nameof(args));
            }

            var key = name[2..];
            if (!values.TryAdd(key, args[index + 1]))
            {
                throw new ArgumentException($"Option '--{key}' was supplied more than once.", nameof(args));
            }
        }

        return values;
    }

    private static Guid ParseRequiredGuid(IReadOnlyDictionary<string, string> values, string key)
    {
        var value = ParseRequiredString(values, key);
        if (!Guid.TryParseExact(value, "D", out var result) || result == Guid.Empty)
        {
            throw new ArgumentException($"Option '--{key}' must be a non-empty GUID in D format.");
        }

        return result;
    }

    private static int ParseRequiredInt(
        IReadOnlyDictionary<string, string> values,
        string key,
        int minimum,
        int maximum)
    {
        var value = ParseRequiredString(values, key);
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ||
            result < minimum ||
            result > maximum)
        {
            throw new ArgumentOutOfRangeException(
                key,
                value,
                $"Option '--{key}' must be in [{minimum}, {maximum}].");
        }

        return result;
    }

    private static double ParseRequiredDouble(
        IReadOnlyDictionary<string, string> values,
        string key,
        double minimum,
        double maximum)
    {
        var value = ParseRequiredString(values, key);
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ||
            !double.IsFinite(result) ||
            result < minimum ||
            result > maximum)
        {
            throw new ArgumentOutOfRangeException(
                key,
                value,
                $"Option '--{key}' must be finite and in [{minimum}, {maximum}].");
        }

        return result;
    }

    private static string ParseRequiredString(IReadOnlyDictionary<string, string> values, string key)
    {
        if (!values.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"Missing required option '--{key}'.");
        }

        return value.Trim();
    }
}
