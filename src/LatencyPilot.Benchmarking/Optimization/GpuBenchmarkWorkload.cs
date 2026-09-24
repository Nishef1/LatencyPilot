using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using LatencyPilot.Core.System;

namespace LatencyPilot.Benchmarking.Optimization;

public enum GpuBenchmarkTrialRole
{
    Original,
    Candidate,
}

public sealed class GpuBenchmarkFrozenWorkload
{
    private const string IdentitySchema = "gpu-affinity-benchmark-workload-v2";
    private readonly IReadOnlyList<LogicalProcessorId> workerProcessors;
    private readonly IReadOnlyList<ulong> workerAffinityMasks;

    private GpuBenchmarkFrozenWorkload(
        int width,
        int height,
        IReadOnlyList<LogicalProcessorId> workerProcessors,
        IReadOnlyList<ulong> workerAffinityMasks,
        int simulationIterationsPerWorker,
        int commandBatchesPerWorker,
        int seed,
        string workloadIdentity)
    {
        Width = width;
        Height = height;
        this.workerProcessors = workerProcessors;
        this.workerAffinityMasks = workerAffinityMasks;
        SimulationIterationsPerWorker = simulationIterationsPerWorker;
        CommandBatchesPerWorker = commandBatchesPerWorker;
        Seed = seed;
        WorkloadIdentity = workloadIdentity;
    }

    public int Width { get; }

    public int Height { get; }

    public IReadOnlyList<LogicalProcessorId> WorkerProcessors => workerProcessors;

    public IReadOnlyList<ulong> WorkerAffinityMasks => workerAffinityMasks;

    public int SimulationIterationsPerWorker { get; }

    public int CommandBatchesPerWorker { get; }

    public int Seed { get; }

    public string WorkloadIdentity { get; }

    public static GpuBenchmarkFrozenWorkload Create(
        int width,
        int height,
        IEnumerable<LogicalProcessorId> workerProcessors,
        int simulationIterationsPerWorker,
        int commandBatchesPerWorker,
        int seed,
        IEnumerable<ulong>? workerAffinityMasks = null)
    {
        if (width is < 320 or > 7680)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        if (height is < 240 or > 4320)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }

        ArgumentNullException.ThrowIfNull(workerProcessors);
        var workers = workerProcessors.ToArray();
        if (workers.Length is < 1 or > 64)
        {
            throw new ArgumentOutOfRangeException(nameof(workerProcessors));
        }

        if (workers.Distinct().Count() != workers.Length)
        {
            throw new ArgumentException("Benchmark worker processors must be unique.", nameof(workerProcessors));
        }

        var processorGroup = workers[0].Group;
        if (workers.Any(worker => worker.Group != processorGroup))
        {
            throw new ArgumentException(
                "gpu-affinity-benchmark-v1 currently requires a single processor group.",
                nameof(workerProcessors));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(simulationIterationsPerWorker);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(commandBatchesPerWorker);
        ArgumentOutOfRangeException.ThrowIfNegative(seed);

        var masks = workerAffinityMasks?.ToArray() ??
            workers.Select(static worker => 1UL << worker.Number).ToArray();
        if (masks.Length != workers.Length ||
            masks.Any(static mask => mask == 0) ||
            workers.Where((worker, index) =>
                worker.Number >= 64 ||
                (masks[index] & (1UL << worker.Number)) == 0).Any())
        {
            throw new ArgumentException(
                "Benchmark worker affinity masks must match the worker map and include every representative logical processor.",
                nameof(workerAffinityMasks));
        }

        var frozenWorkers = Array.AsReadOnly(workers);
        var frozenMasks = Array.AsReadOnly(masks);
        return new GpuBenchmarkFrozenWorkload(
            width,
            height,
            frozenWorkers,
            frozenMasks,
            simulationIterationsPerWorker,
            commandBatchesPerWorker,
            seed,
            ComputeIdentity(
                width,
                height,
                workers,
                masks,
                simulationIterationsPerWorker,
                commandBatchesPerWorker,
                seed));
    }

    private static string ComputeIdentity(
        int width,
        int height,
        IReadOnlyList<LogicalProcessorId> workers,
        IReadOnlyList<ulong> workerAffinityMasks,
        int simulationIterationsPerWorker,
        int commandBatchesPerWorker,
        int seed)
    {
        var canonical = string.Join(
            '|',
            IdentitySchema,
            width.ToString(CultureInfo.InvariantCulture),
            height.ToString(CultureInfo.InvariantCulture),
            simulationIterationsPerWorker.ToString(CultureInfo.InvariantCulture),
            commandBatchesPerWorker.ToString(CultureInfo.InvariantCulture),
            seed.ToString(CultureInfo.InvariantCulture),
            string.Join(',', workers.Select(static processor =>
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{processor.Group}:{processor.Number}"))),
            string.Join(',', workerAffinityMasks.Select(static mask =>
                mask.ToString("X16", CultureInfo.InvariantCulture))));

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
    }
}

public sealed record GpuBenchmarkTrialDefinition
{
    private GpuBenchmarkTrialDefinition(
        GpuBenchmarkTrialRole role,
        GpuBenchmarkFrozenWorkload workload,
        LogicalProcessorId? candidateProcessor)
    {
        Role = role;
        Workload = workload;
        CandidateProcessor = candidateProcessor;
    }

    public GpuBenchmarkTrialRole Role { get; }

    public GpuBenchmarkFrozenWorkload Workload { get; }

    public LogicalProcessorId? CandidateProcessor { get; }

    public static GpuBenchmarkTrialDefinition Original(GpuBenchmarkFrozenWorkload workload)
    {
        ArgumentNullException.ThrowIfNull(workload);
        return new GpuBenchmarkTrialDefinition(GpuBenchmarkTrialRole.Original, workload, null);
    }

    public static GpuBenchmarkTrialDefinition Candidate(
        GpuBenchmarkFrozenWorkload workload,
        LogicalProcessorId candidateProcessor)
    {
        ArgumentNullException.ThrowIfNull(workload);
        if (workload.WorkerProcessors.Count > 0 &&
            candidateProcessor.Group != workload.WorkerProcessors[0].Group)
        {
            throw new ArgumentException(
                "The GPU affinity candidate must use the same processor group as the frozen benchmark workload.",
                nameof(candidateProcessor));
        }

        return new GpuBenchmarkTrialDefinition(
            GpuBenchmarkTrialRole.Candidate,
            workload,
            candidateProcessor);
    }
}
