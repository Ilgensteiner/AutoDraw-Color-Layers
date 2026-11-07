using System;
using Avalonia.Media;
using SkiaSharp;

namespace Autodraw;

internal static class PaletteColor
{
    public static int Group(SKColor color)
    {
        var max = Math.Max(color.Red, Math.Max(color.Green, color.Blue));
        var min = Math.Min(color.Red, Math.Min(color.Green, color.Blue));
        if (max <= 55 && max - min <= 32)
            return 0;
        return max - min <= 24 ? 1 : 2;
    }

    public static string Name(SKColor color)
    {
        var group = Group(color);
        if (group < 2)
            return group == 0 ? UiText.T("Black", "Schwarz") : UiText.T("Gray", "Grau");
        color.ToHsv(out var hue, out _, out _);
        return hue switch
        {
            < 18 or >= 348 => UiText.T("Red", "Rot"),
            < 45 => "Orange",
            < 72 => UiText.T("Yellow", "Gelb"),
            < 160 => UiText.T("Green", "Grün"),
            < 195 => UiText.T("Turquoise", "Türkis"),
            < 255 => UiText.T("Blue", "Blau"),
            < 295 => UiText.T("Purple", "Violett"),
            _ => "Pink"
        };
    }

    public static string Hex(SKColor color) => $"#{color.Red:X2}{color.Green:X2}{color.Blue:X2}";

    public static IBrush Swatch(SKColor color) =>
        new SolidColorBrush(Color.FromRgb(color.Red, color.Green, color.Blue));
}
