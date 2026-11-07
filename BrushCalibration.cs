using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using SharpHook;
using SharpHook.Native;

namespace Autodraw;

internal sealed class BrushCalibration : DesktopOverlay
{
    private readonly Ellipse _outer = new() { Stroke = Brushes.Black, StrokeThickness = 3, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly Ellipse _ring = new() { Stroke = Brushes.White, StrokeThickness = 1, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _hint = new() { Foreground = Brushes.White };
    private readonly Action<int> _apply;
    private readonly bool _contour;
    private bool _closed;
    private int _diameter;

    public BrushCalibration(int diameter, Action<int> apply, bool contour)
    {
        _diameter = diameter;
        _apply = apply;
        _contour = contour;
        Title = UiText.T("ColorLayers · Brush calibration", "ColorLayers · Stift kalibrieren");
        Width = 380;
        var panel = new StackPanel();
        panel.Children.Add(new Grid { Children = { _outer, _ring } });
        panel.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.Parse("#F021252B")),
            Padding = new Thickness(9),
            Child = _hint
        });
        Content = panel;
        Closed += (_, _) =>
        {
            _closed = true;
            Input.MousePosUpdate -= MouseMoved;
            Input.taskHook.KeyReleased -= KeyReleased;
        };
    }

    public void Start()
    {
        Input.MousePosUpdate += MouseMoved;
        Input.taskHook.KeyReleased += KeyReleased;
        Place(CursorPosition());
        Show();
    }

    private void MouseMoved(object? sender, EventArgs e)
    {
        var point = CursorPosition();
        Dispatcher.UIThread.Post(() => { if (!_closed) Place(point); });
    }

    private void Place(PixelPoint center)
    {
        var scale = Screens.ScreenFromPoint(center)?.Scaling ?? 1;
        var ringSize = _diameter / scale;
        _ring.Width = _ring.Height = ringSize + 1;
        _outer.Width = _outer.Height = ringSize + 3;
        Position = new PixelPoint(center.X - (int)(Width * scale / 2),
            center.Y - (int)((_outer.Height * scale) / 2));
        Height = _outer.Height + 85;
        var brush = _contour ? UiText.T("Outline brush", "Konturstift") : UiText.T("Brush", "Stift");
        _hint.Text = UiText.T(
            $"{brush}: {_diameter} px\nMake a dot in the target with this brush.\nMatch the circle to the dot using Page Up / Down.\nEnter saves · Esc cancels",
            $"{brush}: {_diameter} px\nSetze im Ziel einen Punkt mit diesem Stift.\nPasse den Kreis mit Bild ↑ / ↓ an den Punkt an.\nEnter speichert · Esc bricht ab");
    }

    private void KeyReleased(object? sender, KeyboardHookEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_closed) return;
            var key = e.Data.KeyCode;
            if (key == KeyCode.VcEscape || Config.IsStopDrawingKey(key))
                Close();
            else if (key is KeyCode.VcEnter or KeyCode.VcNumPadEnter)
            {
                _apply(_diameter);
                Close();
            }
            else
            {
                var delta = PlacementScale.StepFor(key);
                if (delta != 0)
                {
                    _diameter = Math.Clamp(_diameter + delta, 1, 128);
                    Place(CursorPosition());
                }
            }
        });
    }
}
