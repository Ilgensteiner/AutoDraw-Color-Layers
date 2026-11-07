using System;
using Avalonia;
using SharpHook.Native;

namespace Autodraw;

internal static class PlacementScale
{
    public static string ShortcutHint => UiText.T("Page Up / Down or Num + / −", "Bild ↑ / ↓ oder Num + / −");

    public static int StepFor(KeyCode key) =>
        key switch
        {
            KeyCode.VcPageUp or KeyCode.VcNumPad9 or KeyCode.VcNumPadAdd => 1,
            KeyCode.VcPageDown or KeyCode.VcNumPad3 or KeyCode.VcNumPadSubtract or KeyCode.VcMinus => -1,
            KeyCode.VcEquals => 1,
            _ => 0
        };

    public static int FitStep(PixelSize original, PixelRect workArea)
    {
        if (original.Width <= workArea.Width && original.Height + 100 <= workArea.Height)
            return 0;
        var factor = Math.Min(workArea.Width * 0.8 / original.Width,
            Math.Max(1, workArea.Height - 100) * 0.8 / original.Height);
        return Math.Min(0, (int)Math.Floor(Math.Log(factor, 1.1)));
    }

    public static PixelSize? SizeAt(PixelSize original, int step)
    {
        var factor = Math.Pow(1.1, step);
        var width = Math.Round(original.Width * factor);
        var height = Math.Round(original.Height * factor);
        if (width < 1 || height < 1 ||
            width > Math.Max(8192, original.Width) || height > Math.Max(8192, original.Height) ||
            width * height > Math.Max(16_777_216L, (long)original.Width * original.Height))
            return null;

        return new PixelSize((int)width, (int)height);
    }
}
