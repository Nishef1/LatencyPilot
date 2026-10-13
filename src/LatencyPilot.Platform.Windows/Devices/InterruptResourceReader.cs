using LatencyPilot.Core.Devices;
using LatencyPilot.Platform.Windows.Interop;

namespace LatencyPilot.Platform.Windows.Devices;

internal static class InterruptResourceReader
{
    public static InterruptResourceSnapshot Capture(uint deviceInstance)
    {
        // The ConfigMgr resource-descriptor APIs return CR_CALL_NOT_IMPLEMENTED
        // under WOW64 on Windows 8+, and the product supports Windows 11 x64.
        // Never reinterpret a 32-bit descriptor as the 64-bit placement shape.
        if (!Environment.Is64BitProcess)
        {
            return InterruptResourceSnapshot.ApiUnavailable(ConfigurationManager.CallNotImplemented);
        }

        var result = ConfigurationManager.CM_Get_First_Log_Conf(
            out var logConfiguration,
            deviceInstance,
            ConfigurationManager.AllocatedLogConfiguration);

        if (result == ConfigurationManager.NoMoreLogConfigurations)
        {
            return InterruptResourceSnapshot.NoAllocatedConfiguration(result);
        }

        if (result == ConfigurationManager.CallNotImplemented)
        {
            return InterruptResourceSnapshot.ApiUnavailable(result);
        }

        if (result != ConfigurationManager.Success)
        {
            return InterruptResourceSnapshot.ReadFailed(result);
        }

        var activeGroupCount = Kernel32.GetActiveProcessorGroupCount();
        var resources = new List<AllocatedInterruptResourceSnapshot>();
        nint currentDescriptor = 0;
        var sourceHandle = logConfiguration;
        uint? failureStatus = null;
        var apiUnavailable = false;
        var nonNativeReadFailure = false;
        var affinityUnspecified = false;

        try
        {
            while (failureStatus is null && !nonNativeReadFailure)
            {
                result = ConfigurationManager.CM_Get_Next_Res_Des(
                    out var nextDescriptor,
                    sourceHandle,
                    ConfigurationManager.ResourceTypeIrq,
                    0,
                    0);

                if (currentDescriptor != 0)
                {
                    var freeResult = ConfigurationManager.CM_Free_Res_Des_Handle(currentDescriptor);
                    currentDescriptor = 0;
                    if (freeResult != ConfigurationManager.Success)
                    {
                        failureStatus = freeResult;
                        if (result == ConfigurationManager.Success && nextDescriptor != 0)
                        {
                            _ = ConfigurationManager.CM_Free_Res_Des_Handle(nextDescriptor);
                        }
                        break;
                    }
                }

                if (result == ConfigurationManager.NoMoreResourceDescriptors)
                {
                    break;
                }

                if (result == ConfigurationManager.CallNotImplemented)
                {
                    failureStatus = result;
                    apiUnavailable = true;
                    break;
                }

                if (result != ConfigurationManager.Success)
                {
                    failureStatus = result;
                    break;
                }

                currentDescriptor = nextDescriptor;
                sourceHandle = nextDescriptor;
                if (!TryReadIrqDescriptor(
                    nextDescriptor,
                    out var resource,
                    out var readFailureStatus,
                    out var descriptorAffinityUnspecified))
                {
                    if (descriptorAffinityUnspecified)
                    {
                        // Keep walking: every descriptor still has to be freed and a
                        // later descriptor may be structurally invalid.
                        affinityUnspecified = true;
                        continue;
                    }

                    if (readFailureStatus is null)
                    {
                        nonNativeReadFailure = true;
                    }
                    else
                    {
                        failureStatus = readFailureStatus;
                    }

                    break;
                }

                resources.Add(NormalizeImpossibleGroup(resource, activeGroupCount));
            }
        }
        finally
        {
            if (currentDescriptor != 0)
            {
                var freeResult = ConfigurationManager.CM_Free_Res_Des_Handle(currentDescriptor);
                if (failureStatus is null && !nonNativeReadFailure && freeResult != ConfigurationManager.Success)
                {
                    failureStatus = freeResult;
                }
            }

            var freeLogConfigurationResult = ConfigurationManager.CM_Free_Log_Conf_Handle(logConfiguration);
            if (failureStatus is null &&
                !nonNativeReadFailure &&
                freeLogConfigurationResult != ConfigurationManager.Success)
            {
                failureStatus = freeLogConfigurationResult;
            }
        }

        if (apiUnavailable && failureStatus == ConfigurationManager.CallNotImplemented)
        {
            return InterruptResourceSnapshot.ApiUnavailable(failureStatus.Value);
        }

        if (failureStatus is not null || nonNativeReadFailure)
        {
            return InterruptResourceSnapshot.ReadFailed(failureStatus);
        }

        // A descriptor without a processor target cannot prove placement; report
        // the real condition instead of a failed read and expose no partial list.
        return affinityUnspecified
            ? InterruptResourceSnapshot.AffinityUnspecified()
            : InterruptResourceSnapshot.Available(resources.ToArray());
    }

    // Observed on Windows 11 build 26300 (single processor group, 16 logical CPUs):
    // every message-signaled IRQ descriptor reports IRQD_Group = 1 with a correct
    // single-CPU IRQD_Affinity, while legacy descriptors report group 0. A group
    // that does not exist cannot be a real placement, and with exactly one active
    // group the KAFFINITY mask can only refer to group 0. The ConfigMgr value is
    // preserved in ReportedProcessorGroup; multi-group machines are left untouched
    // and stay fail-closed.
    internal static AllocatedInterruptResourceSnapshot NormalizeImpossibleGroup(
        AllocatedInterruptResourceSnapshot resource,
        ushort activeGroupCount) =>
        activeGroupCount == 1 && resource.ProcessorGroup != 0
            ? resource with
            {
                ProcessorGroup = 0,
                ReportedProcessorGroup = resource.ProcessorGroup,
            }
            : resource;

    private static unsafe bool TryReadIrqDescriptor(
        nint resourceDescriptor,
        out AllocatedInterruptResourceSnapshot resource,
        out uint? failureStatus,
        out bool affinityUnspecified)
    {
        affinityUnspecified = false;
        var result = ConfigurationManager.CM_Get_Res_Des_Data_Size(out var size, resourceDescriptor, 0);
        if (result != ConfigurationManager.Success)
        {
            resource = default!;
            failureStatus = result;
            return false;
        }

        if (size < AllocatedIrqDescriptorParser.Descriptor64Size || size > int.MaxValue)
        {
            resource = default!;
            failureStatus = null;
            return false;
        }

        var buffer = new byte[(int)size];
        fixed (byte* pointer = buffer)
        {
            result = ConfigurationManager.CM_Get_Res_Des_Data(resourceDescriptor, pointer, size, 0);
        }

        if (result != ConfigurationManager.Success)
        {
            resource = default!;
            failureStatus = result;
            return false;
        }

        if (!AllocatedIrqDescriptorParser.TryParseResourceList(buffer, out resource))
        {
            affinityUnspecified = AllocatedIrqDescriptorParser.IsUnspecifiedAffinity(buffer);
            failureStatus = null;
            return false;
        }

        failureStatus = null;
        return true;
    }
}
