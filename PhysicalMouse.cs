using System;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia;

namespace Autodraw;

internal static class PhysicalMouse
{
    internal readonly record struct Desktop(int X, int Y, int Width, int Height);

    public static PixelPoint? Position() =>
        GetPhysicalCursorPos(out var point) ? new PixelPoint(point.X, point.Y) : null;

    public static Desktop GetDesktop()
    {
        var previous = SetThreadDpiAwarenessContext(new IntPtr(-4));
        try
        {
            return new Desktop(GetSystemMetrics(76), GetSystemMetrics(77),
                GetSystemMetrics(78), GetSystemMetrics(79));
        }
        finally
        {
            if (previous != IntPtr.Zero) SetThreadDpiAwarenessContext(previous);
        }
    }

    public static bool MoveTo(Desktop desktop, int x, int y)
    {
        var dx = (long)x - desktop.X;
        var dy = (long)y - desktop.Y;
        if (dx < 0 || dy < 0 || dx >= desktop.Width || dy >= desktop.Height)
            return false;
        var move = new MouseInput
        {
            // Select the center of a physical pixel in Windows' 16-bit desktop coordinate space.
            X = (int)((dx * 65536 + 32768) / desktop.Width),
            Y = (int)((dy * 65536 + 32768) / desktop.Height),
            Flags = 0x0001 | 0x2000 | 0x4000 | 0x8000 // Move, no coalescing, virtual desktop, absolute.
        };
        if (!Send(move))
            return false;
        var point = new PixelPoint(x, y);
        return Position() == point || SpinWait.SpinUntil(() => Position() == point, 16);
    }

    public static IntPtr WindowAt(int x, int y) =>
        GetAncestor(WindowFromPhysicalPoint(new NativePoint { X = x, Y = y }), 2);

    public static bool EmergencyStopDown =>
        GetAsyncKeyState(0x2E) < 0 || GetAsyncKeyState(0x23) < 0;

    public static bool SetLeftButton(bool down) =>
        Send(new MouseInput { Flags = down ? 0x0002u : 0x0004u });

    private static bool Send(MouseInput mouse)
    {
        var input = new NativeInput { Mouse = mouse };
        return SendInput(1, ref input, Marshal.SizeOf<NativeInput>()) == 1;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeInput
    {
        public uint Type;
        public MouseInput Mouse;
    }

    [DllImport("user32.dll")] private static extern bool GetPhysicalCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPhysicalPoint(NativePoint point);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int virtualKey);
    [DllImport("user32.dll")] private static extern uint SendInput(uint count, ref NativeInput input, int size);
}
