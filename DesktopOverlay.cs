using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;

namespace Autodraw;

internal abstract class DesktopOverlay : Window
{
    protected DesktopOverlay()
    {
        ShowActivated = false;
        ShowInTaskbar = false;
        SystemDecorations = SystemDecorations.None;
        Topmost = true;
        CanResize = false;
        Background = Brushes.Transparent;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Opened += (_, _) => MakeClickThrough();
    }

    protected static PixelPoint CursorPosition() =>
        PhysicalMouse.Position() ?? new PixelPoint((int)Input.mousePos.X, (int)Input.mousePos.Y);

    private void MakeClickThrough()
    {
        var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero)
            return;
        const int exStyle = -20;
        const int layered = 0x80000;
        const int transparent = 0x20;
        const int noActivate = 0x08000000;
        var style = GetWindowLong(handle, exStyle);
        SetWindowLong(handle, exStyle, style | layered | transparent | noActivate);
        if ((style & layered) == 0)
            SetLayeredWindowAttributes(handle, 0, 255, 2);
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong(IntPtr window, int index, int value);
    [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(IntPtr window, uint color, byte alpha, uint flags);
}
