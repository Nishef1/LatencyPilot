using System.Buffers.Binary;
using LatencyPilot.Core.Devices;

namespace LatencyPilot.Platform.Windows.Devices;

internal static class AllocatedIrqDescriptorParser
{
    // Windows cfgmgr32.h defines IRQ_DES_64 as this 24-byte header on
    // processor-group-aware 64-bit builds. IRQ_RESOURCE_64 may contain
    // additional IRQ_RANGE storage after the header, so callers may supply
    // a larger buffer; allocated resource lists themselves require Count=0.
    internal const int Descriptor64Size = 24;

    // cfgmgr32.h: IRQType_Range == sizeof(IRQ_RANGE). With processor groups,
    // IRQ_RANGE is ULONG + ULONG + USHORT + USHORT = 12 bytes.
    internal const uint IrqTypeRange = 12;

    internal static bool TryParseResourceList(
        ReadOnlySpan<byte> data,
        out AllocatedInterruptResourceSnapshot resource)
    {
        resource = default!;
        if (data.Length < Descriptor64Size)
        {
            return false;
        }

        var count = BinaryPrimitives.ReadUInt32LittleEndian(data[0..4]);
        var type = BinaryPrimitives.ReadUInt32LittleEndian(data[4..8]);
        if (count != 0 || type != IrqTypeRange)
        {
            return false;
        }

        var flags = BinaryPrimitives.ReadUInt16LittleEndian(data[8..10]);
        var group = BinaryPrimitives.ReadUInt16LittleEndian(data[10..12]);
        var allocatedIrq = BinaryPrimitives.ReadUInt32LittleEndian(data[12..16]);
        var affinity = BinaryPrimitives.ReadUInt64LittleEndian(data[16..24]);

        // IRQD_Affinity is documented as the processor bitmask for an
        // allocated resource (ulong.MaxValue means all processors). A zero
        // mask names no processor and therefore cannot serve as trustworthy
        // placement evidence; fail closed rather than inventing a target.
        if (affinity == 0)
        {
            return false;
        }

        resource = new AllocatedInterruptResourceSnapshot(
            allocatedIrq,
            group,
            affinity,
            flags);
        return true;
    }
}
