using System.ComponentModel;
using System.Text.Json;
using LatencyPilot.Benchmarking.Baselines;
using LatencyPilot.Benchmarking.Candidates;
using LatencyPilot.Benchmarking.Optimization;
using LatencyPilot.Core.System;
using LatencyPilot.Platform.Windows.System;
using LatencyPilot.Protocol;

internal sealed record BaselineEvidenceCandidatePlanResult(
    string SourceRevisionId,
    int WindowCount,
    bool CpuSetMetadataAvailable,
    WorkloadStabilityResult WorkloadStability,
    IReadOnlyList<GpuAffinityCandidate> Candidates);

internal static class BaselineEvidenceCandidatePlan
{
    private const string EvidenceSchema = "latencypilot-evidence-v9";
    private const string BaselinePurpose = "repeated-decision-baseline";
    private const string RealWorldScenario = "RealWorld";
    private const long MaximumEvidenceBytes = 32L * 1024L * 1024L;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        MaxDepth = 64,
    };

    internal static BaselineEvidenceCandidatePlanResult Create(
        string evidencePath,
        string expectedSourceRevision)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(evidencePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedSourceRevision);

        if (!GpuOptimizationSourceRevisionPolicy.IsValidFullRevision(expectedSourceRevision))
        {
            throw new ArgumentException(
                "Expected source revision must be an exact 40-character hexadecimal Git commit id.",
                nameof(expectedSourceRevision));
        }

        var fullPath = Path.GetFullPath(evidencePath);
        var file = new FileInfo(fullPath);
        if (!file.Exists)
        {
            throw new FileNotFoundException("The baseline evidence file was not found.", fullPath);
        }

        if (file.Length is <= 0 or > MaximumEvidenceBytes)
        {
            throw new InvalidDataException(
                $"Baseline evidence must be between 1 byte and {MaximumEvidenceBytes:N0} bytes.");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(
                File.ReadAllBytes(fullPath),
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 64,
                });
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Baseline evidence is not valid strict JSON.", exception);
        }

        using (document)
        {
            var root = document.RootElement;
            RequireObject(root, "evidence root");
            RequireString(root, "schema", EvidenceSchema);
            RequireString(root, "purpose", BaselinePurpose);
            RequireInt32(root, "protocolVersion", ProtocolVersion.Current);
            RequireString(root, "baselineMethodVersion", BaselineQualityAnalyzer.MethodVersion);

            var sourceRevisionId = TryGetOptionalString(root, "sourceRevisionId");
            if (!GpuOptimizationSourceRevisionPolicy.IsExactMatch(expectedSourceRevision, sourceRevisionId))
            {
                throw new InvalidDataException(
                    $"Baseline evidence source revision '{sourceRevisionId ?? "unavailable"}' does not exactly match expected commit '{expectedSourceRevision}'.");
            }

            var measurementContext = RequireProperty(root, "measurementContext");
            RequireObject(measurementContext, "measurementContext");
            RequireString(measurementContext, "scenario", RealWorldScenario);

            var topology = ProcessorTopologyReader.Capture();
            EnsureEvidenceTopologyMatchesCurrent(root, topology);
            if (topology.ProcessorGroupCount != 1)
            {
                throw new NotSupportedException(
                    "Gate A GPU affinity candidate planning currently requires exactly one processor group.");
            }

            var windowsElement = RequireProperty(root, "windows");
            var windows = DeserializeRequired<BaselineWindowEvidence[]>(windowsElement, "windows");
            var quality = BaselineQualityAnalyzer.Analyze(windows);
            if (!quality.IsValidForComparison)
            {
                throw new InvalidDataException(
                    "Baseline evidence does not recompute as a valid baseline-quality-v2 comparison baseline.");
            }

            var persistedQuality = RequireProperty(root, "quality");
            RequireObject(persistedQuality, "quality");
            RequireString(persistedQuality, "methodVersion", BaselineQualityAnalyzer.MethodVersion);
            RequireString(persistedQuality, "status", BaselineQualityStatus.Valid.ToString());
            RequireInt32(persistedQuality, "totalWindowCount", quality.TotalWindowCount);
            RequireInt32(persistedQuality, "validCaptureWindowCount", quality.ValidCaptureWindowCount);

            var workloadStability = ReadWorkloadStability(root, windows);
            var optimizerEligibility = GpuOptimizationBaselineReadiness.Evaluate(quality, workloadStability);
            ValidatePersistedReadiness(root, workloadStability, optimizerEligibility);
            if (!optimizerEligibility.IsEligible)
            {
                throw new InvalidDataException(
                    $"Baseline evidence is not eligible for GPU optimization. {optimizerEligibility.Reason}");
            }

            var pressureWindows = ReadProcessorWindows(root, windows, topology);
            var pressure = ProcessorPressureEvidenceBuilder.Create(topology, pressureWindows);

            ProcessorCpuSetSnapshot? cpuSets = null;
            var cpuSetMetadataAvailable = false;
            try
            {
                cpuSets = ProcessorCpuSetReader.Capture();
                cpuSetMetadataAvailable = true;
            }
            catch (Exception exception) when (exception is
                Win32Exception or
                InvalidDataException or
                OverflowException)
            {
                // CPU-set ownership/parking metadata improves ranking but is not
                // required to interpret the already-valid baseline. The planner
                // remains topology- and pressure-aware when this optional source
                // cannot be read.
            }

            var candidates = GpuAffinityCandidatePlanner.Create(topology, pressure, cpuSets);
            if (candidates.Count == 0)
            {
                throw new InvalidDataException("No bounded GPU affinity candidate could be derived from the evidence.");
            }

            return new BaselineEvidenceCandidatePlanResult(
                sourceRevisionId!,
                windows.Length,
                cpuSetMetadataAvailable,
                workloadStability,
                candidates);
        }
    }

    private static WorkloadStabilityResult ReadWorkloadStability(
        JsonElement root,
        BaselineWindowEvidence[] windows)
    {
        var runtimeWindows = RequireProperty(root, "runtimeWindows");
        if (runtimeWindows.ValueKind != JsonValueKind.Array || runtimeWindows.GetArrayLength() != windows.Length)
        {
            throw new InvalidDataException(
                $"Baseline evidence runtimeWindows/windows are misaligned: runtimeWindows={GetArrayLengthOrNegative(runtimeWindows)}, windows={windows.Length}.");
        }

        var evidence = new List<WorkloadWindowEvidence>(windows.Length);
        var runtimeIndex = 0;
        foreach (var runtimeWindow in runtimeWindows.EnumerateArray())
        {
            runtimeIndex++;
            RequireObject(runtimeWindow, $"runtime window {runtimeIndex}");
            RequireInt32(runtimeWindow, "windowNumber", runtimeIndex);

            double? systemCpuBusyPercent = null;
            if (runtimeWindow.TryGetProperty("context", out var context) && context.ValueKind != JsonValueKind.Null)
            {
                if (context.ValueKind != JsonValueKind.Object)
                {
                    throw new InvalidDataException(
                        $"Baseline evidence runtime window {runtimeIndex} context must be an object or null.");
                }

                systemCpuBusyPercent = ReadOptionalFiniteDouble(context, "systemCpuBusyPercent");
            }

            var window = windows[runtimeIndex - 1];
            evidence.Add(new WorkloadWindowEvidence(
                window.WindowNumber,
                window.ActualDurationMilliseconds,
                window.DpcEventCount,
                window.IsrEventCount,
                systemCpuBusyPercent));
        }

        return WorkloadStabilityAnalyzer.Analyze(evidence);
    }

    private static void ValidatePersistedReadiness(
        JsonElement root,
        WorkloadStabilityResult workloadStability,
        GpuOptimizationBaselineEligibility optimizerEligibility)
    {
        var persistedWorkload = RequireProperty(root, "workloadStability");
        RequireObject(persistedWorkload, "workloadStability");
        RequireString(persistedWorkload, "methodVersion", WorkloadStabilityAnalyzer.MethodVersion);
        RequireString(persistedWorkload, "status", workloadStability.Status.ToString());
        RequireBoolean(
            persistedWorkload,
            "isEligibleForExperiment",
            workloadStability.IsEligibleForExperiment);

        var persistedOptimizer = RequireProperty(root, "optimizerEligibility");
        RequireObject(persistedOptimizer, "optimizerEligibility");
        RequireString(persistedOptimizer, "target", GpuOptimizationBaselineReadiness.Target);
        RequireBoolean(persistedOptimizer, "isEligible", optimizerEligibility.IsEligible);
    }

    private static List<IReadOnlyList<ProcessorInterruptCountEvidence>> ReadProcessorWindows(
        JsonElement root,
        BaselineWindowEvidence[] windows,
        ProcessorTopologySnapshot topology)
    {
        var captures = RequireProperty(root, "captures");
        if (captures.ValueKind != JsonValueKind.Array || captures.GetArrayLength() != windows.Length)
        {
            throw new InvalidDataException(
                $"Baseline evidence captures/windows are misaligned: captures={GetArrayLengthOrNegative(captures)}, windows={windows.Length}.");
        }

        var expectedProcessors = topology.Cores
            .SelectMany(static core => core.LogicalProcessors)
            .Distinct()
            .ToHashSet();
        var requestIds = new HashSet<Guid>();
        var result = new List<IReadOnlyList<ProcessorInterruptCountEvidence>>(windows.Length);

        var captureIndex = 0;
        foreach (var capture in captures.EnumerateArray())
        {
            captureIndex++;
            RequireObject(capture, $"capture {captureIndex}");

            var requestIdElement = RequireProperty(capture, "requestId");
            if (requestIdElement.ValueKind != JsonValueKind.String ||
                !requestIdElement.TryGetGuid(out var requestId) ||
                requestId == Guid.Empty ||
                !requestIds.Add(requestId))
            {
                throw new InvalidDataException($"Capture {captureIndex} has an empty, invalid, or duplicate requestId.");
            }

            var window = windows[captureIndex - 1];
            RequireInt32(capture, "requestedDurationMilliseconds", window.RequestedDurationMilliseconds);
            RequireFiniteDoubleNear(
                capture,
                "actualDurationMilliseconds",
                window.ActualDurationMilliseconds,
                tolerance: 0.001d);

            var processors = RequireProperty(capture, "processors");
            if (processors.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException($"Capture {captureIndex} processors must be an array.");
            }

            var seenProcessors = new HashSet<LogicalProcessorId>();
            var evidence = new List<ProcessorInterruptCountEvidence>(expectedProcessors.Count);
            long dpcTotal = 0;
            long isrTotal = 0;

            foreach (var processor in processors.EnumerateArray())
            {
                RequireObject(processor, $"capture {captureIndex} processor");
                var processorNumber = ReadNonNegativeInt32(processor, "processorNumber");
                if (processorNumber > byte.MaxValue)
                {
                    throw new InvalidDataException(
                        $"Capture {captureIndex} processor {processorNumber} cannot be represented by the single-group planner.");
                }

                var id = new LogicalProcessorId(0, checked((byte)processorNumber));
                if (!expectedProcessors.Contains(id))
                {
                    throw new InvalidDataException(
                        $"Capture {captureIndex} contains processor {id}, which is absent from the current topology.");
                }

                if (!seenProcessors.Add(id))
                {
                    throw new InvalidDataException($"Capture {captureIndex} contains duplicate processor evidence for {id}.");
                }

                var dpc = RequireProperty(processor, "dpc");
                var isr = RequireProperty(processor, "isr");
                RequireObject(dpc, $"capture {captureIndex} processor {id} DPC distribution");
                RequireObject(isr, $"capture {captureIndex} processor {id} ISR distribution");
                var dpcCount = ReadNonNegativeInt32(dpc, "count");
                var isrCount = ReadNonNegativeInt32(isr, "count");

                dpcTotal = checked(dpcTotal + dpcCount);
                isrTotal = checked(isrTotal + isrCount);
                evidence.Add(new ProcessorInterruptCountEvidence(id, dpcCount, isrCount));
            }

            if (!seenProcessors.SetEquals(expectedProcessors))
            {
                var missing = expectedProcessors
                    .Where(processor => !seenProcessors.Contains(processor))
                    .OrderBy(static processor => processor.Group)
                    .ThenBy(static processor => processor.Number);
                throw new InvalidDataException(
                    $"Capture {captureIndex} is missing current logical processor evidence: {string.Join(", ", missing)}.");
            }

            if (dpcTotal != window.DpcEventCount || isrTotal != window.IsrEventCount)
            {
                throw new InvalidDataException(
                    $"Capture {captureIndex} per-processor totals do not match its baseline window: " +
                    $"DPC {dpcTotal}/{window.DpcEventCount}, ISR {isrTotal}/{window.IsrEventCount}.");
            }

            result.Add(evidence.ToArray());
        }

        return result;
    }

    private static void EnsureEvidenceTopologyMatchesCurrent(
        JsonElement root,
        ProcessorTopologySnapshot topology)
    {
        var environment = RequireProperty(root, "environment");
        RequireObject(environment, "environment");
        var evidenceTopology = RequireProperty(environment, "topology");
        RequireObject(evidenceTopology, "environment.topology");

        RequireInt32(evidenceTopology, "packageCount", topology.Packages.Count);
        RequireInt32(evidenceTopology, "physicalCoreCount", topology.PhysicalCoreCount);
        RequireInt32(evidenceTopology, "logicalProcessorCount", topology.LogicalProcessorCount);
        RequireInt32(evidenceTopology, "processorGroupCount", topology.ProcessorGroupCount);
        RequireInt32(evidenceTopology, "smtCoreCount", topology.SmtCoreCount);
    }

    private static T DeserializeRequired<T>(JsonElement element, string name)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(element.GetRawText(), JsonOptions)
                ?? throw new InvalidDataException($"Baseline evidence '{name}' was empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Baseline evidence '{name}' has an invalid shape.", exception);
        }
    }

    private static JsonElement RequireProperty(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var value))
        {
            throw new InvalidDataException($"Baseline evidence is missing required property '{propertyName}'.");
        }

        return value;
    }

    private static void RequireObject(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException($"Baseline evidence '{name}' must be a JSON object.");
        }
    }

    private static void RequireString(JsonElement parent, string propertyName, string expected)
    {
        var value = RequireProperty(parent, propertyName);
        if (value.ValueKind != JsonValueKind.String ||
            !string.Equals(value.GetString(), expected, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Baseline evidence '{propertyName}' must be '{expected}'.");
        }
    }

    private static void RequireBoolean(JsonElement parent, string propertyName, bool expected)
    {
        var value = RequireProperty(parent, propertyName);
        if (value.ValueKind is not JsonValueKind.True and not JsonValueKind.False ||
            value.GetBoolean() != expected)
        {
            throw new InvalidDataException(
                $"Baseline evidence '{propertyName}' is inconsistent with recomputed readiness.");
        }
    }

    private static string? TryGetOptionalString(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException($"Baseline evidence '{propertyName}' must be a string or null.");
        }

        return value.GetString();
    }

    private static double? ReadOptionalFiniteDouble(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Number ||
            !value.TryGetDouble(out var result) ||
            !double.IsFinite(result))
        {
            throw new InvalidDataException(
                $"Baseline evidence '{propertyName}' must be a finite number or null when present.");
        }

        return result;
    }

    private static void RequireInt32(JsonElement parent, string propertyName, int expected)
    {
        var actual = ReadNonNegativeInt32(parent, propertyName);
        if (actual != expected)
        {
            throw new InvalidDataException(
                $"Baseline evidence '{propertyName}' is {actual}; expected {expected}.");
        }
    }

    private static int ReadNonNegativeInt32(JsonElement parent, string propertyName)
    {
        var value = RequireProperty(parent, propertyName);
        if (value.ValueKind != JsonValueKind.Number ||
            !value.TryGetInt32(out var result) ||
            result < 0)
        {
            throw new InvalidDataException(
                $"Baseline evidence '{propertyName}' must be a non-negative 32-bit integer.");
        }

        return result;
    }

    private static void RequireFiniteDoubleNear(
        JsonElement parent,
        string propertyName,
        double expected,
        double tolerance)
    {
        var value = RequireProperty(parent, propertyName);
        if (value.ValueKind != JsonValueKind.Number ||
            !value.TryGetDouble(out var actual) ||
            !double.IsFinite(actual) ||
            Math.Abs(actual - expected) > tolerance)
        {
            throw new InvalidDataException(
                $"Baseline evidence '{propertyName}' is inconsistent with its window record.");
        }
    }

    private static int GetArrayLengthOrNegative(JsonElement element) =>
        element.ValueKind == JsonValueKind.Array ? element.GetArrayLength() : -1;
}
