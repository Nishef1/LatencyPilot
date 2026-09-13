using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using LatencyPilot.Benchmarking.Baselines;
using LatencyPilot.Platform.Windows.System;
using LatencyPilot.Protocol;
using Microsoft.Windows.Storage.Pickers;

namespace LatencyPilot.App.Services;

internal enum MeasurementScenario
{
    RealWorld = 1,
    IdleBaseline = 2,
    BeforeAfter = 3,
}

internal sealed record MeasurementRuntimeWindow(
    int WindowNumber,
    RuntimeMeasurementContextInterval? Context);

internal sealed record EvidenceSaveResult(
    string Path,
    string Sha256)
{
    public override string ToString() => $"{Path} · SHA-256 {Sha256}";
}

internal static class EvidenceExportService
{
    private const string EvidenceSchema = "latencypilot-evidence-v7";

    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public static string CreateObservationJson(
        string productVersion,
        MeasurementScenario measurementScenario,
        KernelLatencyCaptureResponse capture,
        RuntimeMeasurementContextInterval? runtimeContext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productVersion);
        ArgumentNullException.ThrowIfNull(capture);
        ValidateMeasurementScenario(measurementScenario);
        if (capture.RequestId == Guid.Empty)
        {
            throw new InvalidDataException("Observation evidence cannot be exported with an empty capture RequestId.");
        }

        return JsonSerializer.Serialize(
            new ObservationEvidenceDocument(
                EvidenceSchema,
                productVersion,
                TryGetSourceRevisionId(),
                ProtocolVersion.Current,
                DateTimeOffset.UtcNow,
                CreateEnvironment(),
                CreateMeasurementContext(measurementScenario),
                CreateRuntimeContextEvidence(runtimeContext),
                capture),
            JsonOptions);
    }

    public static string CreateBaselineJson(
        string productVersion,
        MeasurementScenario measurementScenario,
        IReadOnlyList<KernelLatencyCaptureResponse> captures,
        IReadOnlyList<BaselineWindowEvidence> windows,
        IReadOnlyList<MeasurementRuntimeWindow> runtimeWindows,
        BaselineQualityResult quality)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productVersion);
        ArgumentNullException.ThrowIfNull(captures);
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(runtimeWindows);
        ArgumentNullException.ThrowIfNull(quality);
        ValidateMeasurementScenario(measurementScenario);

        var captureArray = captures.ToArray();
        var windowArray = windows.ToArray();
        var runtimeWindowArray = runtimeWindows.ToArray();
        ValidateBaselineEvidenceAlignment(captureArray, windowArray, runtimeWindowArray, quality);

        return CreateBaselineJson(
            productVersion,
            measurementScenario,
            captureArray,
            windowArray,
            runtimeWindowArray,
            quality);
    }

    public static string GetMeasurementDisplayName(MeasurementScenario scenario) =>
        scenario switch
        {
            MeasurementScenario.RealWorld => "Real-world workload",
            MeasurementScenario.IdleBaseline => "Controlled idle",
            MeasurementScenario.BeforeAfter => "Before / after comparison",
            _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Unknown measurement scenario."),
        };

    public static string GetMeasurementGuidance(MeasurementScenario scenario) =>
        scenario switch
        {
            MeasurementScenario.RealWorld =>
                "Keep the apps or game that reproduce the issue open; their activity is part of the evidence.",
            MeasurementScenario.IdleBaseline =>
                "Close unnecessary apps and avoid starting unrelated work while the controlled idle measurement runs.",
            MeasurementScenario.BeforeAfter =>
                "Use the same apps, workload, power state and background activity on both sides of the comparison.",
            _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Unknown measurement scenario."),
        };

    public static string CreateSuggestedFileName(string evidenceType, DateTimeOffset startedAtUtc) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"LatencyPilot-{evidenceType}-{startedAtUtc.UtcDateTime:yyyyMMddTHHmmssfffZ}");

    public static async Task<EvidenceSaveResult?> SaveAsync(
        LatencyPilot.App.MainWindow owner,
        string json,
        string suggestedFileName)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ArgumentException.ThrowIfNullOrWhiteSpace(suggestedFileName);

        var picker = new FileSavePicker(owner.AppWindow.Id)
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = suggestedFileName,
            DefaultFileExtension = ".json",
            SettingsIdentifier = "EvidenceExport",
        };
        picker.FileTypeChoices.Add("JSON evidence", [".json"]);

        var result = await picker.PickSaveFileAsync();
        if (result is null || string.IsNullOrWhiteSpace(result.Path))
        {
            return null;
        }

        await File.WriteAllTextAsync(result.Path, json);
        await using var stream = File.OpenRead(result.Path);
        using var sha256 = SHA256.Create();
        var digest = await sha256.ComputeHashAsync(stream);
        return new EvidenceSaveResult(result.Path, Convert.ToHexString(digest));
    }

    private static string CreateBaselineJson(
        string productVersion,
        MeasurementScenario measurementScenario,
        KernelLatencyCaptureResponse[] captures,
        BaselineWindowEvidence[] windows,
        MeasurementRuntimeWindow[] runtimeWindows,
        BaselineQualityResult quality)
    {
        var evidenceRuntimeWindows = runtimeWindows
            .Select(static window => new EvidenceRuntimeWindow(
                window.WindowNumber,
                CreateRuntimeContextEvidence(window.Context)))
            .ToArray();

        return JsonSerializer.Serialize(
            new BaselineEvidenceDocument(
                EvidenceSchema,
                productVersion,
                TryGetSourceRevisionId(),
                ProtocolVersion.Current,
                DateTimeOffset.UtcNow,
                CreateEnvironment(),
                CreateMeasurementContext(measurementScenario),
                quality.MethodVersion,
                captures,
                windows,
                evidenceRuntimeWindows,
                quality),
            JsonOptions);
    }

    private static void ValidateMeasurementScenario(MeasurementScenario scenario)
    {
        if (scenario is not MeasurementScenario.RealWorld and
            not MeasurementScenario.IdleBaseline and
            not MeasurementScenario.BeforeAfter)
        {
            throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Unknown measurement scenario.");
        }
    }

    private static void ValidateBaselineEvidenceAlignment(
        KernelLatencyCaptureResponse[] captures,
        BaselineWindowEvidence[] windows,
        MeasurementRuntimeWindow[] runtimeWindows,
        BaselineQualityResult quality)
    {
        if (captures.Length == 0)
        {
            throw new InvalidDataException("Baseline evidence requires at least one completed capture.");
        }

        if (captures.Length != windows.Length || captures.Length != runtimeWindows.Length)
        {
            throw new InvalidDataException(
                $"Baseline evidence is misaligned: captures={captures.Length}, windows={windows.Length}, runtimeWindows={runtimeWindows.Length}.");
        }

        if (!string.Equals(quality.MethodVersion, BaselineQualityAnalyzer.MethodVersion, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Baseline evidence method mismatch: quality reports '{quality.MethodVersion}', expected '{BaselineQualityAnalyzer.MethodVersion}'.");
        }

        if (quality.TotalWindowCount != windows.Length)
        {
            throw new InvalidDataException(
                $"Baseline quality reports {quality.TotalWindowCount} window(s), but {windows.Length} window evidence record(s) are present.");
        }

        if (quality.ValidCaptureWindowCount < 0 || quality.ValidCaptureWindowCount > windows.Length)
        {
            throw new InvalidDataException("Baseline quality reports an impossible valid-capture window count.");
        }

        var requestIds = new HashSet<Guid>();
        for (var index = 0; index < captures.Length; index++)
        {
            var expectedWindowNumber = index + 1;
            var capture = captures[index];
            var window = windows[index];
            var runtimeWindow = runtimeWindows[index];

            if (window.WindowNumber != expectedWindowNumber || runtimeWindow.WindowNumber != expectedWindowNumber)
            {
                throw new InvalidDataException(
                    $"Baseline evidence sequence mismatch at position {expectedWindowNumber}: window={window.WindowNumber}, runtimeWindow={runtimeWindow.WindowNumber}.");
            }

            if (capture.StartedAtUtc != window.StartedAtUtc)
            {
                throw new InvalidDataException(
                    $"Baseline evidence timestamp mismatch in window {expectedWindowNumber}.");
            }

            if (capture.RequestId == Guid.Empty || !requestIds.Add(capture.RequestId))
            {
                throw new InvalidDataException(
                    $"Baseline evidence window {expectedWindowNumber} has an empty or duplicate capture RequestId.");
            }
        }
    }

    private static EvidenceMeasurementContext CreateMeasurementContext(MeasurementScenario scenario) =>
        new(
            scenario,
            GetMeasurementDisplayName(scenario),
            GetMeasurementGuidance(scenario));

    private static EvidenceRuntimeMeasurementContext? CreateRuntimeContextEvidence(
        RuntimeMeasurementContextInterval? context) =>
        context is null
            ? null
            : new EvidenceRuntimeMeasurementContext(
                context.SystemCpuBusyPercent,
                CreatePowerEvidence(context.StartPower),
                CreatePowerEvidence(context.EndPower),
                context.PowerContextChanged);

    private static EvidenceSystemPowerSnapshot CreatePowerEvidence(SystemPowerSnapshot power) =>
        new(
            power.LineState,
            power.BatteryPresent,
            power.Charging,
            power.BatteryPercent,
            power.BatterySaverEnabled,
            power.ActiveSchemeId,
            power.UserConfiguredPowerModeId,
            power.UserConfiguredPowerMode);

    private static EvidenceEnvironment CreateEnvironment()
    {
        EvidenceTopology? topology = null;
        try
        {
            var snapshot = ProcessorTopologyReader.Capture();
            topology = new EvidenceTopology(
                snapshot.Packages.Count,
                snapshot.PhysicalCoreCount,
                snapshot.LogicalProcessorCount,
                snapshot.ProcessorGroupCount,
                snapshot.SmtCoreCount);
        }
        catch (Exception exception) when (
            exception is InvalidDataException or
            InvalidOperationException or
            System.ComponentModel.Win32Exception)
        {
            // Evidence export is best-effort context enrichment. A topology read
            // failure must not invalidate an otherwise completed observation.
        }

        var system = SystemInventoryReader.Capture();
        return new EvidenceEnvironment(
            system.OperatingSystem,
            System.Environment.OSVersion.VersionString,
            system.OsArchitecture,
            system.ProcessArchitecture,
            system.ProcessAvailableProcessorCount,
            System.Environment.Version.ToString(),
            topology);
    }

    private static string? TryGetSourceRevisionId()
    {
        foreach (var path in new[]
                 {
                     Path.Combine(AppContext.BaseDirectory, "BUILD_INFO.txt"),
                     Path.Combine(AppContext.BaseDirectory, "..", "BUILD_INFO.txt"),
                 })
        {
            try
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                var commitLine = File.ReadLines(path)
                    .FirstOrDefault(static line => line.StartsWith("commit=", StringComparison.Ordinal));
                var buildInfoRevision = ValidateRevisionId(commitLine?["commit=".Length..]);
                if (buildInfoRevision is not null)
                {
                    return buildInfoRevision;
                }
            }
            catch (Exception exception) when (
                exception is IOException or
                UnauthorizedAccessException)
            {
                // Release metadata is provenance enrichment. If it cannot be read,
                // fall back to assembly metadata rather than invalidating evidence.
            }
        }

        var informationalVersion = typeof(EvidenceExportService).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        if (string.IsNullOrWhiteSpace(informationalVersion))
        {
            return null;
        }

        var separator = informationalVersion.LastIndexOf('+');
        return separator < 0 || separator == informationalVersion.Length - 1
            ? null
            : ValidateRevisionId(informationalVersion[(separator + 1)..]);
    }

    private static string? ValidateRevisionId(string? candidate) =>
        candidate is { Length: >= 7 and <= 40 } && candidate.All(Uri.IsHexDigit)
            ? candidate
            : null;

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new HexUInt64JsonConverter());
        return options;
    }

    private sealed class HexUInt64JsonConverter : JsonConverter<ulong>
    {
        public override ulong Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options) =>
            throw new NotSupportedException("LatencyPilot evidence export is write-only.");

        public override void Write(
            Utf8JsonWriter writer,
            ulong value,
            JsonSerializerOptions options) =>
            writer.WriteStringValue(string.Create(CultureInfo.InvariantCulture, $"0x{value:X16}"));
    }

    private sealed record EvidenceTopology(
        int PackageCount,
        int PhysicalCoreCount,
        int LogicalProcessorCount,
        int ProcessorGroupCount,
        int SmtCoreCount);

    private sealed record EvidenceEnvironment(
        string OperatingSystem,
        string OperatingSystemVersion,
        string OsArchitecture,
        string ProcessArchitecture,
        int ProcessAvailableProcessorCount,
        string DotNetRuntimeVersion,
        EvidenceTopology? Topology);

    private sealed record EvidenceMeasurementContext(
        MeasurementScenario Scenario,
        string DisplayName,
        string Guidance);

    private sealed record EvidenceSystemPowerSnapshot(
        SystemPowerLineState LineState,
        bool? BatteryPresent,
        bool? Charging,
        int? BatteryPercent,
        bool? BatterySaverEnabled,
        Guid? ActiveSchemeId,
        Guid? UserConfiguredPowerModeId,
        UserConfiguredPowerMode? UserConfiguredPowerMode);

    private sealed record EvidenceRuntimeMeasurementContext(
        double? SystemCpuBusyPercent,
        EvidenceSystemPowerSnapshot StartPower,
        EvidenceSystemPowerSnapshot EndPower,
        bool PowerContextChanged);

    private sealed record EvidenceRuntimeWindow(
        int WindowNumber,
        EvidenceRuntimeMeasurementContext? Context);

    private sealed record ObservationEvidenceDocument(
        string Schema,
        string ProductVersion,
        string? SourceRevisionId,
        int ProtocolVersion,
        DateTimeOffset ExportedAtUtc,
        EvidenceEnvironment Environment,
        EvidenceMeasurementContext MeasurementContext,
        EvidenceRuntimeMeasurementContext? RuntimeContext,
        KernelLatencyCaptureResponse Capture);

    private sealed record BaselineEvidenceDocument(
        string Schema,
        string ProductVersion,
        string? SourceRevisionId,
        int ProtocolVersion,
        DateTimeOffset ExportedAtUtc,
        EvidenceEnvironment Environment,
        EvidenceMeasurementContext MeasurementContext,
        string BaselineMethodVersion,
        KernelLatencyCaptureResponse[] Captures,
        BaselineWindowEvidence[] Windows,
        EvidenceRuntimeWindow[] RuntimeWindows,
        BaselineQualityResult Quality);
}
