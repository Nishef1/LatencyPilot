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

        EnsureSuccess(result, "CM_Get_First_Log_Conf");

        var resources = new List<AllocatedInterruptResourceSnapshot>();
        nint currentDescriptor = 0;
        var sourceHandle = logConfiguration;

        try
        {
            while (true)
            {
                result = ConfigurationManager.CM_Get_Next_Res_Des(
                    out var nextDescriptor,
                    sourceHandle,
                    ConfigurationManager.ResourceTypeIrq,
                    0,
                    0);

                if (currentDescriptor != 0)
                {
                    FreeResourceDescriptor(currentDescriptor);
                    currentDescriptor = 0;
                }

                if (result == ConfigurationManager.NoMoreResourceDescriptors)
                {
                    break;
                }

                if (result == ConfigurationManager.CallNotImplemented)
                {
                    return InterruptResourceSnapshot.ApiUnavailable(result);
                }

                EnsureSuccess(result, "CM_Get_Next_Res_Des");

                currentDescriptor = nextDescriptor;
                sourceHandle = nextDescriptor;
                resources.Add(ReadIrqDescriptor(nextDescriptor));
            }

            return InterruptResourceSnapshot.Available(resources.ToArray());
        }
        finally
        {
            if (currentDescriptor != 0)
            {
                FreeResourceDescriptor(currentDescriptor);
            }

            var freeResult = ConfigurationManager.CM_Free_Log_Conf_Handle(logConfiguration);
            EnsureSuccess(freeResult, "CM_Free_Log_Conf_Handle");
        }
    }

    private static unsafe AllocatedInterruptResourceSnapshot ReadIrqDescriptor(nint resourceDescriptor)
    {
        var result = ConfigurationManager.CM_Get_Res_Des_Data_Size(out var size, resourceDescriptor, 0);
        EnsureSuccess(result, "CM_Get_Res_Des_Data_Size");

        var descriptorSize = checked((uint)Marshal.SizeOf<IrqDescriptor64>());
        if (size < descriptorSize)
        {
            throw new InvalidDataException($"Allocated IRQ descriptor is {size} bytes; expected at least {descriptorSize} bytes.");
        }

        var buffer = new byte[checked((int)size)];
        fixed (byte* pointer = buffer)
        {
            result = ConfigurationManager.CM_Get_Res_Des_Data(resourceDescriptor, pointer, size, 0);
        }

        EnsureSuccess(result, "CM_Get_Res_Des_Data");

        var descriptor = MemoryMarshal.Read<IrqDescriptor64>(buffer);
        return new AllocatedInterruptResourceSnapshot(
            descriptor.AllocatedIrq,
            descriptor.Group,
            descriptor.Affinity,
            descriptor.Flags);
    }

    private static void FreeResourceDescriptor(nint resourceDescriptor)
    {
        var result = ConfigurationManager.CM_Free_Res_Des_Handle(resourceDescriptor);
        EnsureSuccess(result, "CM_Free_Res_Des_Handle");
    }

    private static void EnsureSuccess(uint result, string operation)
    {
        if (result != ConfigurationManager.Success)
        {
            throw new InvalidOperationException($"{operation} failed with CONFIGRET 0x{result:X8}.");
        }
    }
}
