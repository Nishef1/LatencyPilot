using System.Runtime.InteropServices;
using LatencyPilot.Core.Devices;
using LatencyPilot.Platform.Windows.Interop;

namespace LatencyPilot.Platform.Windows.Devices;

internal static class InterruptResourceReader
{
    public static InterruptResourceSnapshot Capture(uint deviceInstance)
    {
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

        var resources = new List<AllocatedInterruptResourceSnapshot>();
        nint currentDescriptor = 0;
        var sourceHandle = logConfiguration;
        uint? failureStatus = null;
        var apiUnavailable = false;
        var nonNativeReadFailure = false;

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
                    out var readFailureStatus))
                {
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

                resources.Add(resource);
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

        return InterruptResourceSnapshot.Available(resources.ToArray());
    }

    private static unsafe bool TryReadIrqDescriptor(
        nint resourceDescriptor,
        out AllocatedInterruptResourceSnapshot resource,
        out uint? failureStatus)
    {
        var result = ConfigurationManager.CM_Get_Res_Des_Data_Size(out var size, resourceDescriptor, 0);
        if (result != ConfigurationManager.Success)
        {
            resource = default!;
            failureStatus = result;
            return false;
        }

        var descriptorSize = checked((uint)Marshal.SizeOf<IrqDescriptor64>());
        if (size < descriptorSize || size > int.MaxValue)
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

        var descriptor = MemoryMarshal.Read<IrqDescriptor64>(buffer);
        resource = new AllocatedInterruptResourceSnapshot(
            descriptor.AllocatedIrq,
            descriptor.Group,
            descriptor.Affinity,
            descriptor.Flags);
        failureStatus = null;
        return true;
    }
}
