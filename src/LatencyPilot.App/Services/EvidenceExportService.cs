using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using LatencyPilot.Benchmarking.Baselines;
using LatencyPilot.Platform.Windows.System;
using LatencyPilot.Protocol;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace LatencyPilot.App.Services;

internal enum MeasurementScenario
{
    RealWorld = 1,
    IdleBaseline = 2,
    BeforeAfter = 3,
}

internal static class EvidenceExportService
{
    private const string EvidenceSchema = "latencypilot-evidence-v4";

    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public static string CreateObservationJson(
        string productVersion,
        MeasurementScenario measurementScenario,
        KernelLatencyCaptureResponse capture) =>
        JsonSerializer.Serialize(
            new ObservationEvidenceDocument(
                EvidenceSchema,
                productVersion,
                TryExtractSourceRevisionId(productVersion),
                ProtocolVersion.Current,
                DateTimeOffset.UtcNow,
                CreateEnvironment(),
                CreateMeasurementContext(measurementScenario),
                capture),
            JsonOptions);

    public static string CreateBaselineJson(
        string productVersion,
        MeasurementScenario measurementScenario,
        IReadOnlyList<KernelLatencyCaptureResponse> captures,
        IReadOnlyList<BaselineWindowEvidence> windows,
        BaselineQualityResult quality) =>
        CreateBaselineJson(
            productVersion,
            measurementScenario,
            captures.ToArray(),
            windows.ToArray(),
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

    public static Task<string?> SaveAsync(
        LatencyPilot.App.MainWindow owner,
        string json,
        string suggestedFileName) =>
        SaveAsync(
            WinRT.Interop.WindowNative.GetWindowHandle(owner),
            json,
            suggestedFileName);

    private static string CreateBaselineJson(
        string productVersion,
        MeasurementScenario measurementScenario,
        KernelLatencyCaptureResponse[] captures,
        BaselineWindowEvidence[] windows,
        BaselineQualityResult quality) =>
        JsonSerializer.Serialize(
            new BaselineEvidenceDocument(
                EvidenceSchema,
                productVersion,
                TryExtractSourceRevisionId(productVersion),
                ProtocolVersion.Current,
                DateTimeOffset.UtcNow,
                CreateEnvironment(),
                CreateMeasurementContext(measurementScenario),
                quality.MethodVersion,
                captures,
                windows,
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

    private static string? TryExtractSourceRevisionId(string productVersion)
    {
        var separator = productVersion.LastIndexOf('+');
        if (separator < 0 || separator == productVersion.Length - 1)
        {
            return null;
        }

        var candidate = productVersion[(separator + 1)..];
        return candidate.Length is >= 7 and <= 40 && candidate.All(Uri.IsHexDigit)
            ? candidate
            : null;
    }

    private static async Task<string?> SaveAsync(
        nint windowHandle,
        string json,
        string suggestedFileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ArgumentException.ThrowIfNullOrWhiteSpace(suggestedFileName);

        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = suggestedFileName,
            DefaultFileExtension = ".json",
        };
        picker.FileTypeChoices.Add("JSON evidence", [".json"]);

        WinRT.Interop.InitializeWithWindow.Initialize(picker, windowHandle);

        var file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return null;
        }

        await FileIO.WriteTextAsync(file, json);
        return string.IsNullOrWhiteSpace(file.Path) ? file.Name : file.Path;
    }

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
        BaselineQualityResult Quality);
}
