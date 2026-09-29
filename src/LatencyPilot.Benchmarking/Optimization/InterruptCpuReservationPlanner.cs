using LatencyPilot.Core.Devices;
using LatencyPilot.Core.System;

namespace LatencyPilot.Benchmarking.Optimization;

public sealed record InterruptCpuReservation(
    string DeviceInstanceId,
    string DisplayName,
    ulong AffinityMask,
    IReadOnlyList<LogicalProcessorId> Processors);

public sealed record InterruptCpuReservationSnapshot(
    IReadOnlyList<InterruptCpuReservation> Reservations)
{
    public IReadOnlyList<LogicalProcessorId> Processors =>
        Reservations
            .SelectMany(static reservation => reservation.Processors)
            .Distinct()
            .OrderBy(static processor => processor.Group)
            .ThenBy(static processor => processor.Number)
            .ToArray();

    public bool IsEmpty => Reservations.Count == 0;
}

public static class InterruptCpuReservationPlanner
{
    public static InterruptCpuReservationSnapshot Capture(
        DeviceInventorySnapshot inventory,
        string? excludedTargetInstanceId = null)
    {
        ArgumentNullException.ThrowIfNull(inventory);

        var reservations = new List<InterruptCpuReservation>();
        foreach (var device in inventory.Devices)
        {
            if (!string.IsNullOrWhiteSpace(excludedTargetInstanceId) &&
                string.Equals(
                    device.InstanceId,
                    excludedTargetInstanceId,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var configuration = device.InterruptConfiguration;
            if (configuration.ReadStatus != InterruptConfigurationReadStatus.Available ||
                configuration.DevicePolicy != 4 ||
                configuration.AssignmentSetOverrideMask is not { } mask ||
                mask == 0)
            {
                continue;
            }

            var processors = new List<LogicalProcessorId>();
            for (byte processor = 0; processor < 64; processor++)
            {
                if ((mask & (1UL << processor)) != 0)
                {
                    processors.Add(new LogicalProcessorId(0, processor));
                }
            }

            if (processors.Count == 0)
            {
                continue;
            }

            reservations.Add(new InterruptCpuReservation(
                device.InstanceId,
                device.DisplayName,
                mask,
                processors.AsReadOnly()));
        }

        return new InterruptCpuReservationSnapshot(
            reservations
                .OrderBy(static reservation => reservation.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static reservation => reservation.DeviceInstanceId, StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }
}
