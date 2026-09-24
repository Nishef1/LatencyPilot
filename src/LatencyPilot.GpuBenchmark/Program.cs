using System.Text;
using System.Text.Json;
using LatencyPilot.GpuBenchmark;
using LatencyPilot.Platform.Windows.System;

BenchmarkOptions options;
try
{
    options = BenchmarkOptions.Parse(args);
}
catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
{
    Console.Error.WriteLine(exception.Message);
    return 2;
}

try
{
    var topology = ProcessorTopologyReader.Capture();
    if (topology.ProcessorGroupCount != 1)
    {
        throw new NotSupportedException("gpu-affinity-benchmark-v5 currently requires exactly one Windows processor group.");
    }
    if (options.WorkerCount > topology.PhysicalCoreCount)
    {
        throw new InvalidOperationException(
            $"Requested {options.WorkerCount} workers but Windows reports only {topology.PhysicalCoreCount} physical cores.");
    }

    var workerCores = topology.Cores.Take(options.WorkerCount).ToArray();
    var workerMap = workerCores
        .Select(static core => core.LogicalProcessors
            .OrderBy(static processor => processor.Group)
            .ThenBy(static processor => processor.Number)
            .First())
        .ToArray();
    var workerAffinityMasks = workerCores
        .Select(static core =>
        {
            ulong mask = 0;
            foreach (var processor in core.LogicalProcessors)
            {
                if (processor.Number >= 64)
                {
                    throw new NotSupportedException(
                        "gpu-affinity-benchmark-v5 requires logical processor numbers below 64 in its single Windows processor group.");
                }
                mask |= 1UL << processor.Number;
            }
            return mask;
        })
        .ToArray();

    BenchmarkProtocol.WriteProgress(
        Console.Out,
        options.SessionId,
        "initializing",
        0d,
        $"Initializing D3D12 benchmark at {options.Width}x{options.Height} with {workerMap.Length} physical-core workers.");

    var benchmark = new BenchmarkWorkload(options, Console.Out, workerMap, workerAffinityMasks);
    await using var rendererOwner = await BenchmarkRendererOwner.CreateAsync(
        () => new D3D12BenchmarkRenderer(
            options.Width,
            options.Height,
            workerMap,
            workerAffinityMasks,
            options.Seed));
    var frozen = await rendererOwner.CalibrateAsync(benchmark);
    BenchmarkProtocol.WriteProgress(
        Console.Out,
        options.SessionId,
        "calibrated",
        0d,
        $"Frozen workload: {frozen.CommandBatchesPerWorker} command batches and {frozen.SimulationIterationsPerWorker} simulation iterations per worker.");

    if (options.IsControlledSession)
    {
        BenchmarkProtocol.WriteProgress(
            Console.Out,
            options.SessionId,
            "waiting-control",
            0d,
            "Frozen benchmark session is waiting for the authenticated Gate A controller.");
        var server = new BenchmarkControlServer(
            options,
            benchmark,
            rendererOwner,
            frozen);
        await server.RunAsync();
        BenchmarkProtocol.WriteProgress(
            Console.Out,
            options.SessionId,
            "complete",
            1d,
            "Controlled benchmark session completed without recalibrating the workload.");
        return 0;
    }

    var outputPath = options.OutputPath
        ?? throw new InvalidOperationException("Single-trial benchmark output path is missing.");
    Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
    var artifact = await rendererOwner.RunTrialAsync(benchmark, frozen, options.Duration);
    var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
    File.WriteAllText(outputPath, JsonSerializer.Serialize(artifact, jsonOptions), new UTF8Encoding(false));
    BenchmarkProtocol.WriteProgress(Console.Out, options.SessionId, "complete", 1d, $"Benchmark artifact written to {outputPath}.");
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
