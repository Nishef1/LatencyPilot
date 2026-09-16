using LatencyPilot.GpuBenchmark;

return BenchmarkProgram.Run(args);

internal static class BenchmarkProgram
{
    internal static int Run(string[] args)
    {
        Guid sessionId = Guid.Empty;
        try
        {
            var options = BenchmarkOptions.Parse(args);
            sessionId = options.SessionId;
            BenchmarkProtocol.WriteProgress(
                Console.Out,
                options.SessionId,
                "Initialized",
                0d,
                "Benchmark host initialized. Direct3D 12 workload setup follows in the next implementation stage.");
            return 0;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            BenchmarkProtocol.WriteFailure(
                Console.Out,
                sessionId,
                "invalid-options",
                exception.Message);
            return 2;
        }
    }
}
