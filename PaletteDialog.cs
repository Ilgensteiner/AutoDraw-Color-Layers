using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using SkiaSharp;

namespace Autodraw;

internal sealed class PaletteDialog : Window
{
    private readonly CheckBox[] _swatches;
    private readonly TextBox _custom;
    private readonly TextBlock _error;

    public PaletteDialog(SKColor[] current)
    {
        Title = UiText.T("Available colors", "Verfügbare Farben");
        Width = 600;
        Height = 470;
        MinWidth = 415;
        MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var standards = DrawingPalette.Standard;
        var usesCustom = current.Any(color => !standards.Contains(color));
        _swatches = standards.Select(color => new CheckBox
        {
            Width = 164,
            IsChecked = usesCustom || current.Contains(color),
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 5,
                Children =
                {
                    new Border { Width = 14, Height = 14, Background = PaletteColor.Swatch(color),
                        BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1) },
                    new TextBlock { Text = $"{PaletteColor.Name(color)} {PaletteColor.Hex(color)}" }
                }
            }
        }).ToArray();

        _custom = new TextBox
        {
            Text = usesCustom ? string.Join(" ", current.Select(PaletteColor.Hex)) : string.Empty,
            Watermark = "#123456 #E53935"
        };
        _error = new TextBlock { Foreground = Brushes.OrangeRed, TextWrapping = TextWrapping.Wrap };
        var wrap = new WrapPanel();
        foreach (var swatch in _swatches) wrap.Children.Add(swatch);

        var all = new Button { Content = UiText.T("Use all standard colors", "Alle Standardfarben wählen") };
        all.Click += (_, _) =>
        {
            _custom.Text = string.Empty;
            foreach (var swatch in _swatches) swatch.IsChecked = true;
        };
        var apply = new Button { Content = UiText.T("Apply", "Übernehmen"), IsDefault = true };
        var cancel = new Button { Content = UiText.T("Cancel", "Abbrechen"), IsCancel = true };
        apply.Click += (_, _) => Apply();
        cancel.Click += (_, _) => Close();

        Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Thickness(16), Spacing = 10,
                Children =
                {
                    new TextBlock { TextWrapping = TextWrapping.Wrap,
                        Text = UiText.T("Select the inks available on your target. Paper white is transparent.",
                            "Wähle die Farben, die im Zielfenster verfügbar sind. Papierweiß bleibt transparent.") },
                    wrap,
                    all,
                    new TextBlock { TextWrapping = TextWrapping.Wrap,
                        Text = UiText.T("Optional custom palette. Enter up to 16 hex colors to replace the selection.",
                            "Optional eigene Palette. Bis zu 16 Hexfarben ersetzen die Auswahl oben.") },
                    _custom,
                    _error,
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8, Children = { cancel, apply } }
                }
            }
        };
    }

    private void Apply()
    {
        var colors = string.IsNullOrWhiteSpace(_custom.Text)
            ? DrawingPalette.Standard.Where((_, index) => _swatches[index].IsChecked == true).ToArray()
            : Parse(_custom.Text);
        if (colors is not { Length: > 0 })
        {
            _error.Text = UiText.T("Choose at least one color. A custom palette needs 1–16 hex colors without paper whites.",
                "Wähle mindestens eine Farbe. Eigene Paletten brauchen 1–16 Hexfarben ohne Papierweiß.");
            return;
        }
        Close(colors);
    }

    internal static SKColor[]? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var entries = Regex.Split(text.Trim(), @"[\s,;]+").Where(token => token.Length > 0).ToArray();
        if (entries.Length is < 1 or > ColorLayers.MaxColors) return null;
        var colors = new SKColor[entries.Length];
        for (var i = 0; i < entries.Length; i++)
        {
            var hex = entries[i].TrimStart('#');
            if (hex.Length != 6 || !uint.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var rgb))
                return null;
            var color = new SKColor(rgb | 0xFF000000u);
            if (DrawingPalette.IsPaper(color.Red, color.Green, color.Blue)) return null;
            colors[i] = color;
        }
        return colors.Distinct().ToArray();
    }
}
