using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using LatencyPilot.Benchmarking.Baselines;
using LatencyPilot.Protocol;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace LatencyPilot.App.Services;

internal static class EvidenceExportService
{
    private const string EvidenceSchema = "latencypilot-evidence-v2";

    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public static string CreateObservationJson(
        string productVersion,
        KernelLatencyCaptureResponse capture) =>
        JsonSerializer.Serialize(
            new ObservationEvidenceDocument(
                EvidenceSchema,
                productVersion,
                ProtocolVersion.Current,
                DateTimeOffset.UtcNow,
                capture),
            JsonOptions);

    public static string CreateBaselineJson(
        string productVersion,
        IReadOnlyList<KernelLatencyCaptureResponse> captures,
        IReadOnlyList<BaselineWindowEvidence> windows,
        BaselineQualityResult quality) =>
        CreateBaselineJson(
            productVersion,
            captures.ToArray(),
            windows.ToArray(),
            quality);

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
        KernelLatencyCaptureResponse[] captures,
        BaselineWindowEvidence[] windows,
        BaselineQualityResult quality) =>
        JsonSerializer.Serialize(
            new BaselineEvidenceDocument(
                EvidenceSchema,
                productVersion,
                ProtocolVersion.Current,
                DateTimeOffset.UtcNow,
                quality.MethodVersion,
                captures,
                windows,
                quality),
            JsonOptions);

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

    private sealed record ObservationEvidenceDocument(
        string Schema,
        string ProductVersion,
        int ProtocolVersion,
        DateTimeOffset ExportedAtUtc,
        KernelLatencyCaptureResponse Capture);

    private sealed record BaselineEvidenceDocument(
        string Schema,
        string ProductVersion,
        int ProtocolVersion,
        DateTimeOffset ExportedAtUtc,
        string BaselineMethodVersion,
        KernelLatencyCaptureResponse[] Captures,
        BaselineWindowEvidence[] Windows,
        BaselineQualityResult Quality);
}