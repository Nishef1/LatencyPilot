using System.Text.Json;

namespace LatencyPilot.Platform.Windows.Devices;

public sealed record GpuInterruptAffinityOriginalJournalPayload(
    int SchemaVersion,
    GpuInterruptAffinitySnapshot Snapshot);

public sealed record GpuInterruptAffinityCandidateJournalPayload(
    int SchemaVersion,
    GpuInterruptAffinityCandidate Candidate);

public static class GpuInterruptAffinityJournalCodec
{
    public const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        WriteIndented = false,
    };

    public static string SerializeOriginal(GpuInterruptAffinitySnapshot snapshot)
    {
        ValidateSnapshot(snapshot);
        return JsonSerializer.Serialize(
            new GpuInterruptAffinityOriginalJournalPayload(CurrentSchemaVersion, snapshot),
            JsonOptions);
    }

    public static string SerializeCandidate(GpuInterruptAffinityCandidate candidate)
    {
        ValidateCandidate(candidate);
        return JsonSerializer.Serialize(
            new GpuInterruptAffinityCandidateJournalPayload(CurrentSchemaVersion, candidate),
            JsonOptions);
    }

    public static GpuInterruptAffinitySnapshot DeserializeOriginal(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        var payload = JsonSerializer.Deserialize<GpuInterruptAffinityOriginalJournalPayload>(json, JsonOptions)
            ?? throw new InvalidDataException("GPU affinity original-state journal payload was empty.");
        if (payload.SchemaVersion != CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Unsupported GPU affinity original-state journal schema {payload.SchemaVersion}; expected {CurrentSchemaVersion}.");
        }

        ValidateSnapshot(payload.Snapshot);
        return payload.Snapshot;
    }

    public static GpuInterruptAffinityCandidate DeserializeCandidate(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        var payload = JsonSerializer.Deserialize<GpuInterruptAffinityCandidateJournalPayload>(json, JsonOptions)
            ?? throw new InvalidDataException("GPU affinity candidate-state journal payload was empty.");
        if (payload.SchemaVersion != CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Unsupported GPU affinity candidate-state journal schema {payload.SchemaVersion}; expected {CurrentSchemaVersion}.");
        }

        ValidateCandidate(payload.Candidate);
        return payload.Candidate;
    }

    private static void ValidateSnapshot(GpuInterruptAffinitySnapshot? snapshot)
    {
        if (snapshot is null)
        {
            throw new InvalidDataException("GPU affinity original-state journal payload is missing its snapshot.");
        }

        if (string.IsNullOrWhiteSpace(snapshot.DeviceInstanceId) || snapshot.DeviceInstanceId.Contains('\0'))
        {
            throw new InvalidDataException("GPU affinity journal snapshot contains an invalid device instance ID.");
        }

        ValidateRegistryValue(snapshot.DevicePolicy, "DevicePolicy");
        ValidateRegistryValue(snapshot.AssignmentSetOverride, "AssignmentSetOverride");
    }

    private static void ValidateRegistryValue(RegistryValueSnapshot value, string name)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(value.Data);

        if (value.Exists != (value.Kind is not null))
        {
            throw new InvalidDataException($"GPU affinity journal value '{name}' has inconsistent existence/kind metadata.");
        }

        if (!value.Exists && value.Data.Length != 0)
        {
            throw new InvalidDataException($"GPU affinity journal value '{name}' is missing but still contains data.");
        }
    }

    private static void ValidateCandidate(GpuInterruptAffinityCandidate? candidate)
    {
        if (candidate is null)
        {
            throw new InvalidDataException("GPU affinity candidate-state journal payload is missing its candidate.");
        }

        if (candidate.ProcessorGroup != 0 || candidate.ProcessorNumber >= 64)
        {
            throw new InvalidDataException("GPU affinity journal candidate is outside the v1 group-0 x64 boundary.");
        }

        if (candidate.AffinityMask == 0 ||
            candidate.ProcessorNumber != GpuInterruptAffinityCandidate.GetPrimaryProcessorNumber(candidate.AffinityMask))
        {
            throw new InvalidDataException(
                "GPU affinity journal candidate must contain a non-empty canonical group-0 KAFFINITY set.");
        }
    }
}
