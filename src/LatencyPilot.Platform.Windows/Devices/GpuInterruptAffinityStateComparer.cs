using System.Buffers.Binary;
using Microsoft.Win32;

namespace LatencyPilot.Platform.Windows.Devices;

public static class GpuInterruptAffinityStateComparer
{
    public static bool MatchesOriginal(
        GpuInterruptAffinitySnapshot actual,
        GpuInterruptAffinitySnapshot original)
    {
        ArgumentNullException.ThrowIfNull(actual);
        ArgumentNullException.ThrowIfNull(original);

        return string.Equals(
                   actual.DeviceInstanceId,
                   original.DeviceInstanceId,
                   StringComparison.OrdinalIgnoreCase) &&
               actual.AffinityPolicyKeyExisted == original.AffinityPolicyKeyExisted &&
               ValuesEqual(actual.DevicePolicy, original.DevicePolicy) &&
               ValuesEqual(actual.AssignmentSetOverride, original.AssignmentSetOverride);
    }

    public static bool MatchesCandidate(
        GpuInterruptAffinitySnapshot actual,
        GpuInterruptAffinityCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(actual);
        ArgumentNullException.ThrowIfNull(candidate);

        if (!TryDecodeDword(actual.DevicePolicy, out var policy) ||
            policy != GpuInterruptAffinityPolicyStore.IrqPolicySpecifiedProcessors ||
            !TryDecodeMask(actual.AssignmentSetOverride, out var mask))
        {
            return false;
        }

        return candidate.ProcessorGroup == 0 &&
               candidate.ProcessorNumber < 64 &&
               candidate.AffinityMask != 0 &&
               candidate.ProcessorNumber ==
                   GpuInterruptAffinityCandidate.GetPrimaryProcessorNumber(candidate.AffinityMask) &&
               mask == candidate.AffinityMask;
    }

    private static bool ValuesEqual(RegistryValueSnapshot left, RegistryValueSnapshot right) =>
        left.Exists == right.Exists &&
        left.Kind == right.Kind &&
        left.Data.AsSpan().SequenceEqual(right.Data);

    private static bool TryDecodeDword(RegistryValueSnapshot value, out uint result)
    {
        if (value.Exists && value.Kind == RegistryValueKind.DWord && value.Data.Length == sizeof(uint))
        {
            result = BinaryPrimitives.ReadUInt32LittleEndian(value.Data);
            return true;
        }

        result = 0;
        return false;
    }

    private static bool TryDecodeMask(RegistryValueSnapshot value, out ulong result)
    {
        if (!value.Exists || value.Kind is null)
        {
            result = 0;
            return false;
        }

        switch (value.Kind.Value)
        {
            case RegistryValueKind.DWord when value.Data.Length == sizeof(uint):
                result = BinaryPrimitives.ReadUInt32LittleEndian(value.Data);
                return true;
            case RegistryValueKind.QWord when value.Data.Length == sizeof(ulong):
                result = BinaryPrimitives.ReadUInt64LittleEndian(value.Data);
                return true;
            case RegistryValueKind.Binary when value.Data.Length is > 0 and <= sizeof(ulong):
                Span<byte> padded = stackalloc byte[sizeof(ulong)];
                padded.Clear();
                value.Data.AsSpan().CopyTo(padded);
                result = BinaryPrimitives.ReadUInt64LittleEndian(padded);
                return true;
            default:
                result = 0;
                return false;
        }
    }
}
