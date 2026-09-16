using System.ComponentModel;
using System.Runtime.InteropServices;

namespace LatencyPilot.GpuBenchmark;

internal sealed class BenchmarkWindow : IDisposable
{
    private const int UseDefault = unchecked((int)0x80000000);
    private const uint OverlappedWindow = 0x00CF0000;
    private const uint Visible = 0x10000000;
    private const uint ClassOwnDc = 0x0020;
    private const uint WmClose = 0x0010;
    private const uint WmQuit = 0x0012;
    private const uint PmRemove = 0x0001;

    private readonly WindowProc windowProc;
    private readonly string className;
    private bool disposed;

    internal BenchmarkWindow(int width, int height)
    {
        Width = width;
        Height = height;
        windowProc = WindowProcedure;
        className = $"LatencyPilotGpuBenchmark_{Environment.ProcessId}_{Guid.NewGuid():N}";

        var instance = GetModuleHandleW(null);
        var windowClass = new WindowClass
        {
            Size = (uint)Marshal.SizeOf<WindowClass>(),
            Style = ClassOwnDc,
            WindowProcedure = Marshal.GetFunctionPointerForDelegate(windowProc),
            Instance = instance,
            ClassName = className,
        };

        if (RegisterClassExW(in windowClass) == 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Unable to register benchmark window class.");
        }

        Handle = CreateWindowExW(
            0,
            className,
            "LatencyPilot GPU Benchmark",
            OverlappedWindow | Visible,
            UseDefault,
            UseDefault,
            width,
            height,
            IntPtr.Zero,
            IntPtr.Zero,
            instance,
            IntPtr.Zero);

        if (Handle == IntPtr.Zero)
        {
            var error = Marshal.GetLastPInvokeError();
            UnregisterClassW(className, instance);
            throw new Win32Exception(error, "Unable to create benchmark window.");
        }

        ShowWindow(Handle, 5);
        UpdateWindow(Handle);
    }

    internal IntPtr Handle { get; }
    internal int Width { get; }
    internal int Height { get; }

    internal bool PumpMessages()
    {
        while (PeekMessageW(out var message, IntPtr.Zero, 0, 0, PmRemove))
        {
            if (message.MessageId == WmQuit) return false;
            TranslateMessage(in message);
            DispatchMessageW(in message);
        }
        return !disposed && IsWindow(Handle);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (Handle != IntPtr.Zero) DestroyWindow(Handle);
        UnregisterClassW(className, GetModuleHandleW(null));
        GC.KeepAlive(windowProc);
    }

    private static IntPtr WindowProcedure(IntPtr window, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == WmClose)
        {
            DestroyWindow(window);
            return IntPtr.Zero;
        }
        return DefWindowProcW(window, message, wParam, lParam);
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr WindowProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        internal uint Size;
        internal uint Style;
        internal IntPtr WindowProcedure;
        internal int ClassExtra;
        internal int WindowExtra;
        internal IntPtr Instance;
        internal IntPtr Icon;
        internal IntPtr Cursor;
        internal IntPtr Background;
        internal string? MenuName;
        internal string ClassName;
        internal IntPtr SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        internal int X;
        internal int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowMessage
    {
        internal IntPtr Window;
        internal uint MessageId;
        internal nuint WParam;
        internal nint LParam;
        internal uint Time;
        internal Point Point;
        internal uint Private;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr GetModuleHandleW(string? moduleName);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ushort RegisterClassExW(in WindowClass windowClass);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool UnregisterClassW(string className, IntPtr instance);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(uint extendedStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] private static extern bool UpdateWindow(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr DefWindowProcW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool PeekMessageW(out WindowMessage message, IntPtr window, uint minimum, uint maximum, uint remove);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(in WindowMessage message);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessageW(in WindowMessage message);
}
