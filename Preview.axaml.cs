using System;
using System.Numerics;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SharpHook;
using SharpHook.Native;
using SkiaSharp;

namespace Autodraw;

public partial class Preview : Window
{
    public bool hasStarted = false;
    public SKBitmap? inputBitmap;
    public long lastMovement;
    public Bitmap? renderedBitmap;
    private double scale = 1;
    private SKBitmap? _originalBitmap;
    private int _zoomStep;

    public Preview()
    {
        InitializeComponent();
        scale = Screens.ScreenFromWindow(this).Scaling;
        Closing += OnClosing;
        Input.MousePosUpdate += UpdateMousePosition;
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        renderedBitmap?.Dispose();
        inputBitmap?.Dispose();
        _originalBitmap?.Dispose();
        inputBitmap = null;
        _originalBitmap = null;
        Closing -= OnClosing;
        Input.MousePosUpdate -= UpdateMousePosition;
        Input.taskHook.KeyReleased -= Keybind;
    }

    private void UpdateMousePosition(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            var currUnix = ((DateTimeOffset)DateTime.UtcNow).ToUnixTimeMilliseconds();
            if (currUnix < lastMovement + 10) return;
            lastMovement = currUnix;
            var usedPos = Drawing.UseLastPos ? Drawing.LastPos : Input.mousePos;
            var x = usedPos.X - ((Width / 2) * scale);
            var y = usedPos.Y - ((Height / 2) * scale);
            Position = new PixelPoint((int)x, (int)y);
        });
    }

    private void Keybind(object? sender, KeyboardHookEventArgs e)
    {
        if (e.Data.KeyCode == Config.Keybind_StartDrawing)
        {
            Dispatcher.UIThread.Invoke(() =>
            {
                if ((inputBitmap ?? _originalBitmap) is not { } bitmap)
                    return;
                var center = new Vector2(Position.X + bitmap.Width / 2, Position.Y + bitmap.Height / 2);
                if (bitmap == _originalBitmap)
                    _originalBitmap = null;
                inputBitmap = null;
                Close();
                _ = Task.Run(async () =>
                {
                    using (bitmap)
                        await Drawing.Draw(bitmap, center);
                });
            });
            return;
        }

        if (Config.IsStopDrawingKey(e.Data.KeyCode))
        {
            Dispatcher.UIThread.Invoke(Close);
            Dispatcher.UIThread.Invoke(() =>
            {
                if (Application.Current.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                {
                    desktop.MainWindow.WindowState = WindowState.Normal;
                    desktop.MainWindow.BringIntoView();
                }
            });
            Input.taskHook.KeyReleased -= Keybind;
            return;
        }

        if (e.Data.KeyCode == Config.Keybind_LockPreview)
        {
            if (Drawing.LastPos.X == 0 && Drawing.LastPos.Y == 0) Drawing.LastPos = Input.mousePos;
            Config.SetEntry("Preview_LastLockedX", Drawing.LastPos.X.ToString());
            Config.SetEntry("Preview_LastLockedY", Drawing.LastPos.Y.ToString());
            Drawing.UseLastPos = !Drawing.UseLastPos;
        }

        else if (e.Data.KeyCode == Config.Keybind_ClearLock)
        {
            Drawing.LastPos = new Vector2(0,0);
            Config.SetEntry("Preview_LastLockedX", "0");
            Config.SetEntry("Preview_LastLockedY", "0");
            Drawing.UseLastPos = false;
        }
        else
        {
            var delta = PlacementScale.StepFor(e.Data.KeyCode);
            if (delta != 0)
                Dispatcher.UIThread.Invoke(() => ResizePreview(delta));
        }
    }

    private void ResizePreview(int delta)
    {
        if (_originalBitmap is not { } original)
            return;
        var originalSize = new PixelSize(original.Width, original.Height);
        var step = _zoomStep + delta;
        if (PlacementScale.SizeAt(originalSize, step) is not { } size)
            return;
        var current = inputBitmap ?? original;
        var center = new PixelPoint(Position.X + current.Width / 2, Position.Y + current.Height / 2);
        if (size.Width != current.Width || size.Height != current.Height)
        {
            var resized = size == originalSize ? null :
                original.Resize(new SKSizeI(size.Width, size.Height), SKFilterQuality.None);
            if (resized is null && size != originalSize)
                return;
            inputBitmap?.Dispose();
            inputBitmap = resized;
            SetImage(resized ?? original);
            Position = new PixelPoint(center.X - size.Width / 2, center.Y - size.Height / 2);
        }
        _zoomStep = step;
    }

    private void SetImage(SKBitmap bitmap)
    {
        renderedBitmap?.Dispose();
        renderedBitmap = bitmap.ConvertToAvaloniaBitmap();
        PreviewImage.Source = renderedBitmap;

        Width = (bitmap.Width) / scale;
        Height = (bitmap.Height) / scale;
    }

    public void ReadyDraw(SKBitmap bitmap)
    {
        _originalBitmap = bitmap.Copy();
        SetImage(_originalBitmap);
        Input.taskHook.KeyReleased += Keybind;
        UpdateMousePosition(this, EventArgs.Empty);
        Show();
    }
}
