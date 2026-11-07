using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SharpHook;
using SharpHook.Native;
using SkiaSharp;

namespace Autodraw;

internal sealed class ColorPreview : DesktopOverlay
{
    private readonly ColorLayers _originalLayers;
    private readonly PixelSize _originalSize;
    private ColorLayers _layers;
    private Bitmap _image;
    private Bitmap? _forecast;
    private CancellationTokenSource? _forecastRequest;
    private bool _showForecast = true;
    private PixelSize _imageSize;
    private readonly Image _positioningImage;
    private readonly CancellationTokenSource _stop = new();
    private PixelPoint _origin;
    private PixelPoint _center;
    private int _zoomStep;
    private readonly int _brushDiameter;
    private readonly int _contourDiameter;
    private readonly bool _fast;
    private readonly OutlineMode _outlineMode;
    private ColorLayers.Pass[]? _prepared;
    private IntPtr _target;
    private int _layer;
    private bool _locked;
    private bool _positionReady;
    private bool _running;
    private bool _closed;
    private volatile bool _paused;

    public ColorPreview(ColorLayers layers, SKBitmap image, int brushDiameter, int contourDiameter, bool fast,
        OutlineMode outlineMode)
    {
        _originalLayers = _layers = layers;
        _brushDiameter = brushDiameter;
        _contourDiameter = contourDiameter;
        _fast = fast;
        _outlineMode = outlineMode;
        _image = image.ConvertToAvaloniaBitmap();
        _originalSize = _imageSize = new PixelSize(image.Width, image.Height);
        _positioningImage = new Image
        {
            Source = _image,
            HorizontalAlignment = HorizontalAlignment.Left,
            Opacity = 0.58
        };
        Title = UiText.T("ColorLayers · Drawing overlay", "ColorLayers · Zeichenvorschau");
        Closed += (_, _) =>
        {
            _closed = true;
            _stop.Cancel();
            Input.MousePosUpdate -= MouseMoved;
            Input.taskHook.KeyPressed -= KeyPressed;
            Input.taskHook.KeyReleased -= KeyReleased;
            _forecastRequest?.Cancel();
            _forecast?.Dispose();
            _image.Dispose();
            if (!_running)
                _stop.Dispose();
        };
    }

    public void ShowPreview()
    {
        var cursor = CursorPosition();
        SetPosition(cursor);
        if (Screens.ScreenFromPoint(cursor) is { } screen)
            ResizePreview(PlacementScale.FitStep(_originalSize, screen.WorkingArea));
        Input.MousePosUpdate += MouseMoved;
        Input.taskHook.KeyPressed += KeyPressed;
        Input.taskHook.KeyReleased += KeyReleased;
        ShowPositioning();
        RefreshForecast();
    }

    private void MouseMoved(object? sender, EventArgs e)
    {
        var mouse = CursorPosition();
        Dispatcher.UIThread.Post(() =>
        {
            if (!_closed && !_locked && !_running && _layer == 0)
                SetPosition(mouse);
        });
    }

    private void SetPosition(PixelPoint center)
    {
        _center = center;
        var scale = Screens.ScreenFromPoint(center)?.Scaling ?? 1;
        _origin = new PixelPoint(center.X - _imageSize.Width / 2, center.Y - _imageSize.Height / 2);
        Width = Math.Max(370, _imageSize.Width / scale);
        _positioningImage.Width = _imageSize.Width / scale;
        _positioningImage.Height = _imageSize.Height / scale;
        Height = _positioningImage.Height + (NeedsOutlineWarning ? 112 : 95);
        Position = _origin;
        _positionReady = true;
    }

    private void ShowPositioning()
    {
        var panel = new StackPanel();
        panel.Children.Add(_positioningImage);
        panel.Children.Add(CreatePositioningStatus());
        Content = panel;
        Show();
    }

    private string StartKey => Config.Keybind_StartDrawing == KeyCode.VcInsert
        ? UiText.T("Insert", "Einfügen") : Config.Keybind_StartDrawing.ToString().Replace("Vc", "");

    private bool HasBrushForecast => _brushDiameter > 1 || _contourDiameter > 1 ||
        _outlineMode == OutlineMode.OmitNarrow;
    private ColorLayers.Pass? CurrentPass => _prepared is { } passes && _layer < passes.Length ? passes[_layer] : null;
    private bool NeedsOutlineWarning => CurrentPass?.UsesOutlineBrush == true;

    private Border CreatePositioningStatus()
    {
        var status = _forecastRequest is not null ? UiText.T("Preparing drawing…", "Zeichnung wird vorbereitet …")
            : CurrentPass is null ? UiText.T("No strokes at this size. Enlarge the image or use a thinner brush.",
                "Hier passt kein Strich. Motiv vergrößern oder einen dünneren Stift wählen.")
            : UiText.T($"Select this color, align and press {StartKey}", $"Farbe wählen, ausrichten und {StartKey} drücken");
        var view = HasBrushForecast
            ? _showForecast && _forecast is not null
                ? UiText.T("Forecast · F7 shows reference", "Prognose · F7 zeigt die Vorlage")
                : UiText.T("Reference · F7 shows forecast", "Vorlage · F7 zeigt die Prognose")
            : UiText.T("Reference", "Vorlage");
        var placement = _locked ? UiText.T("Position locked · Enter unlocks", "Position fixiert · Enter löst")
            : UiText.T("Enter locks the position", "Enter fixiert die Position");
        return CreateStatus(
            $"{status}\n{PlacementScale.ShortcutHint}  •  {_imageSize.Width} × {_imageSize.Height} px\n{view}\n{placement} · {UiText.T("Del/End stops", "Entf/Ende stoppt")}");
    }

    private void UpdatePositioning()
    {
        _positioningImage.Source = _showForecast && _forecast is not null ? _forecast : _image;
        if (Content is StackPanel panel)
            panel.Children[1] = CreatePositioningStatus();
    }

    private async void RefreshForecast()
    {
        _forecastRequest?.Cancel();
        var request = new CancellationTokenSource();
        _forecastRequest = request;
        _positioningImage.Source = _image;
        _forecast?.Dispose();
        _forecast = null;
        _prepared = null;
        UpdatePositioning();

        try
        {
            var layers = _layers;
            await Task.Delay(100, request.Token);
            var prepared = await Task.Run(() =>
            {
                var passes = layers.PreparePasses(_brushDiameter, _contourDiameter, _outlineMode, request.Token);
                var bitmap = HasBrushForecast ? layers.CreateProjectedPreview(passes, request.Token) : null;
                return (Passes: passes, Bitmap: bitmap);
            }, request.Token);
            using var projected = prepared.Bitmap;
            if (!_closed && ReferenceEquals(_forecastRequest, request))
            {
                _prepared = prepared.Passes;
                if (projected is not null)
                    _forecast = projected.ConvertToAvaloniaBitmap();
                SetPosition(_center);
            }
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            System.Diagnostics.Debug.WriteLine(error);
        }
        finally
        {
            if (ReferenceEquals(_forecastRequest, request))
            {
                _forecastRequest = null;
                if (!_closed)
                    UpdatePositioning();
            }
            request.Dispose();
        }
    }

    private void ResizePreview(int delta)
    {
        var step = _zoomStep + delta;
        if (PlacementScale.SizeAt(_originalSize, step) is not { } size)
            return;
        if (size == _imageSize)
        {
            _zoomStep = step;
            return;
        }

        var layers = _originalLayers.Resize(size.Width, size.Height);
        if (layers.Palette.Length == 0)
            return;
        using var source = layers.CreatePreview();
        var image = source.ConvertToAvaloniaBitmap();
        _positioningImage.Source = image;
        _image.Dispose();
        _image = image;
        _layers = layers;
        _imageSize = size;
        _zoomStep = step;
        SetPosition(_center);
        if (Content is StackPanel)
        {
            UpdatePositioning();
            RefreshForecast();
        }
    }

    private Border CreateStatus(string instruction)
    {
        var pass = CurrentPass;
        var heading = pass is null ? string.Empty :
            $"{UiText.T("Color", "Farbe")} {_layer + 1}/{_prepared!.Length}  {PaletteColor.Name(pass.Ink)} · {PaletteColor.Hex(pass.Ink)} · {pass.Diameter} px\n";
        var description = new StackPanel
        {
            Children =
            {
                new TextBlock
                {
                    Foreground = Brushes.White,
                    Text = heading + instruction,
                    FontSize = 12
                }
            }
        };
        if (NeedsOutlineWarning)
            description.Children.Add(new TextBlock
            {
                Foreground = new SolidColorBrush(Color.Parse("#FF8C89")), FontWeight = FontWeight.Bold,
                Text = UiText.OutlineWarning, FontSize = 12
            });
        return new Border
        {
            Background = new SolidColorBrush(Color.Parse("#F021252B")),
            Padding = new Thickness(8, 4),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    new Border
                    {
                        Width = 22, Height = 22,
                        Background = pass is null ? Brushes.Transparent : PaletteColor.Swatch(pass.Ink),
                        BorderBrush = Brushes.White, BorderThickness = new Thickness(1)
                    },
                    description
                }
            }
        };
    }

    private void ShowStatus(string instruction)
    {
        Width = 360;
        Height = NeedsOutlineWarning ? 65 : 48;
        var screen = Screens.ScreenFromPoint(_origin) ?? Screens.Primary;
        var scale = screen?.Scaling ?? 1;
        var below = _origin.Y + _imageSize.Height + 8;
        if (screen is not null && below + Height * scale > screen.WorkingArea.Bottom)
            below = Math.Max(screen.WorkingArea.Y, _origin.Y - (int)((Height + 6) * scale));
        Position = new PixelPoint(_origin.X, below);
        Content = CreateStatus(instruction);
        Show();
    }

    private void KeyReleased(object? sender, KeyboardHookEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_closed)
                return;

            var key = e.Data.KeyCode;
            if (Config.IsStopDrawingKey(key))
            {
                StopDrawing();
                return;
            }
            if (_running)
            {
                if (key == Config.Keybind_PauseDrawing)
                    _paused = !_paused;
                return;
            }
            if (key == Config.Keybind_StartDrawing)
            {
                _ = DrawCurrentLayer();
                return;
            }
            if (_layer == 0 && _target == IntPtr.Zero)
            {
                if (key == Config.Keybind_LockPreview || key is KeyCode.VcEnter or KeyCode.VcNumPadEnter)
                {
                    _locked = !_locked;
                    UpdatePositioning();
                }
                else if (key == Config.Keybind_ClearLock)
                {
                    _locked = false;
                    UpdatePositioning();
                }
                else if (key == KeyCode.VcF7 && HasBrushForecast)
                {
                    _showForecast = !_showForecast;
                    UpdatePositioning();
                }
                else
                {
                    var delta = PlacementScale.StepFor(key);
                    if (delta != 0)
                        ResizePreview(delta);
                }
            }
        });
    }

    private void KeyPressed(object? sender, KeyboardHookEventArgs e)
    {
        if (Config.IsStopDrawingKey(e.Data.KeyCode))
            Dispatcher.UIThread.Post(StopDrawing, DispatcherPriority.Send);
    }

    private void StopDrawing()
    {
        if (_closed)
            return;
        if (_running)
            _stop.Cancel();
        else
            Close();
    }

    private async Task DrawCurrentLayer()
    {
        if (!_positionReady || _forecastRequest is not null || CurrentPass is not { } pass)
            return;

        var foreground = GetForegroundWindow();
        GetWindowThreadProcessId(foreground, out var processId);
        if (foreground == IntPtr.Zero || processId == Environment.ProcessId)
            return;
        if (_target != IntPtr.Zero && _target != foreground)
        {
            ShowStatus(UiText.T($"Return to the drawing window, then {StartKey}",
                $"Zum Zeichenfenster zurückkehren, dann {StartKey}"));
            return;
        }

        if (_target == IntPtr.Zero)
            _origin = _positioningImage.PointToScreen(new Point(0, 0));
        _target = foreground;
        _locked = true;
        Input.MousePosUpdate -= MouseMoved;
        _running = true;
        Hide();
        var token = _stop.Token;
        var origin = _origin;
        var finished = false;
        var interrupted = UiText.T($"Check the target, then press {StartKey}",
            $"Zielfenster prüfen, dann {StartKey} drücken");
        try
        {
            finished = await Task.Run(async () =>
            {
                return await Drawing.DrawStrokes(pass.Path, origin,
                    () => GetForegroundWindow() == foreground,
                    (x, y) => PhysicalMouse.WindowAt(x, y) == foreground,
                    () => _paused, _stop.Cancel, token, _fast && !pass.IsDark);
            }, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            System.Diagnostics.Debug.WriteLine(error);
            interrupted = UiText.T($"Drawing interrupted. Press {StartKey} to retry.",
                $"Zeichnen unterbrochen. {StartKey} startet erneut.");
        }
        finally
        {
            _running = false;
            _paused = false;
            if (_closed)
                _stop.Dispose();
        }

        if (_closed)
            return;
        if (token.IsCancellationRequested || (finished && ++_layer == _prepared!.Length))
        {
            Close();
            return;
        }
        ShowStatus(finished
            ? UiText.T($"Select this color, then press {StartKey}", $"Farbe wählen, dann {StartKey} drücken")
            : interrupted);
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
}
