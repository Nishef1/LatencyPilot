using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
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

internal static class EvidenceExportService
{
    private const string EvidenceSchema = "latencypilot-evidence-v6";

    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public static string CreateObservationJson(
        string productVersion,
        MeasurementScenario measurementScenario,
        KernelLatencyCaptureResponse capture,
        RuntimeMeasurementContextInterval? runtimeContext) =>
        JsonSerializer.Serialize(
            new ObservationEvidenceDocument(
                EvidenceSchema,
                productVersion,
                TryGetSourceRevisionId(),
                ProtocolVersion.Current,
                DateTimeOffset.UtcNow,
                CreateEnvironment(),
                CreateMeasurementContext(measurementScenario),
                runtimeContext,
                capture),
            JsonOptions);

    public static string CreateBaselineJson(
        string productVersion,
        MeasurementScenario measurementScenario,
        IReadOnlyList<KernelLatencyCaptureResponse> captures,
        IReadOnlyList<BaselineWindowEvidence> windows,
        IReadOnlyList<MeasurementRuntimeWindow> runtimeWindows,
        BaselineQualityResult quality) =>
        CreateBaselineJson(
            productVersion,
            measurementScenario,
            captures.ToArray(),
            windows.ToArray(),
            runtimeWindows.ToArray(),
            quality);

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

    public static async Task<string?> SaveAsync(
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
        return result.Path;
    }

    private static string CreateBaselineJson(
        string productVersion,
        MeasurementScenario measurementScenario,
        KernelLatencyCaptureResponse[] captures,
        BaselineWindowEvidence[] windows,
        MeasurementRuntimeWindow[] runtimeWindows,
        BaselineQualityResult quality) =>
        JsonSerializer.Serialize(
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
                runtimeWindows,
                quality),
            JsonOptions);

    private static EvidenceMeasurementContext CreateMeasurementContext(MeasurementScenario scenario) =>
        new(
            scenario,
            GetMeasurementDisplayName(scenario),
            GetMeasurementGuidance(scenario));

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

        return new EvidenceEnvironment(
            RuntimeInformation.OSDescription,
            System.Environment.OSVersion.VersionString,
            RuntimeInformation.OSArchitecture.ToString(),
            RuntimeInformation.ProcessArchitecture.ToString(),
            System.Environment.ProcessorCount,
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

    private sealed record ObservationEvidenceDocument(
        string Schema,
        string ProductVersion,
        string? SourceRevisionId,
        int ProtocolVersion,
        DateTimeOffset ExportedAtUtc,
        EvidenceEnvironment Environment,
        EvidenceMeasurementContext MeasurementContext,
        RuntimeMeasurementContextInterval? RuntimeContext,
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
        MeasurementRuntimeWindow[] RuntimeWindows,
        BaselineQualityResult Quality);
}
