using System.Text;
using System.Text.Json;
using LatencyPilot.GpuBenchmark;
using LatencyPilot.Platform.Windows.System;

var parsed = BenchmarkOptionsParser.Parse(args);
if (!parsed.Success)
{
    Console.Error.WriteLine(parsed.Error);
    return 2;
}

var options = parsed.Options!;
try
{
    Directory.CreateDirectory(Path.GetDirectoryName(options.OutputPath)!);
    var topology = ProcessorTopologyReader.Capture();
    if (topology.ProcessorGroupCount != 1)
    {
        throw new NotSupportedException("gpu-affinity-benchmark-v1 currently requires exactly one Windows processor group.");
    }
    if (options.WorkerCount > topology.PhysicalCoreCount)
    {
        throw new ArgumentOutOfRangeException(nameof(options.WorkerCount), $"Requested {options.WorkerCount} workers but Windows reports only {topology.PhysicalCoreCount} physical cores.");
    }

    var workerMap = topology.Cores.Take(options.WorkerCount)
        .Select(static core => core.LogicalProcessors
            .OrderBy(static processor => processor.Group)
            .ThenBy(static processor => processor.Number)
            .First())
        .ToArray();

    BenchmarkProtocol.WriteProgress(
        Console.Out,
        options.SessionId,
        "initializing",
        0d,
        $"Initializing D3D12 benchmark at {options.Width}x{options.Height} with {workerMap.Length} physical-core workers.");

    using var renderer = new D3D12BenchmarkRenderer(options.Width, options.Height, workerMap, options.Seed);
    var benchmark = new BenchmarkWorkload(options, Console.Out, workerMap);
    var frozen = await benchmark.CalibrateAsync(renderer);
    BenchmarkProtocol.WriteProgress(
        Console.Out,
        options.SessionId,
        "calibrated",
        0d,
        $"Frozen workload: {frozen.CommandBatchesPerWorker} command batches and {frozen.SimulationIterationsPerWorker} simulation iterations per worker.");

    var artifact = await benchmark.RunTrialAsync(renderer, frozen);
    File.WriteAllText(options.OutputPath, JsonSerializer.Serialize(artifact, BenchmarkJson.Options), new UTF8Encoding(false));
    BenchmarkProtocol.WriteProgress(Console.Out, options.SessionId, "complete", 1d, $"Benchmark artifact written to {options.OutputPath}.");
    return 0;
}
catch (OperationCanceledException exception)
{
    BenchmarkProtocol.WriteFailure(Console.Out, options.SessionId, "cancelled", exception.Message);
    return 3;
}
catch (Exception exception)
{
    BenchmarkProtocol.WriteFailure(Console.Out, options.SessionId, "failed", exception.Message);
    return 1;
}
