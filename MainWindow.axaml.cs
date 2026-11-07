using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using SkiaSharp;

namespace Autodraw;

public partial class MainWindow : Window
{
    public static MainWindow? CurrentMainWindow;
    private readonly ImageProcessing.Filters _currentFilters = new()
        { Invert = false, MaxThreshold = 127, AlphaThreshold = 200 };
    private OpenAIPrompt? _aiPrompt;
    private Bitmap? _displayedBitmap;
    private bool _inChange;

    private readonly Regex _numberRegex = new(@"[^0-9]");

    private long _lastMem;
    private long _memoryPressure;
    private long _lastTime = DateTime.Now.ToFileTime();
    private int _maxBlackThreshold = 127;
    private int _alphaThresh = 200;

    private int _minBlackThreshold;
    private SKBitmap? _preFxBitmap = new(318, 318, true);
    private SKBitmap? _processedBitmap;
    private SKBitmap? _colorSource;
    private ColorLayers? _colorLayers;
    private Window? _drawingPreview;
    private BrushCalibration? _brushCalibration;
    private bool _closing;
    private bool _hasContourPreference;
    private SKColor[]? _allowedPalette;
    private bool _hasImage;

    private SKBitmap? _rawBitmap = new(318, 318, true);

    private Settings? _settings;


    public MainWindow()
    {
        InitializeComponent();

        if (Design.IsDesignMode) return;

        this.AttachDevTools();

        // Set language to user-specified language 
        var installedLanguage = CultureInfo.InstalledUICulture.TwoLetterISOLanguageName;
        Thread.CurrentThread.CurrentCulture = new CultureInfo(Config.GetEntry("userlang") ?? installedLanguage);
        Thread.CurrentThread.CurrentUICulture = new CultureInfo(Config.GetEntry("userlang") ?? installedLanguage);
        Utils.Log(installedLanguage);

        CurrentMainWindow = this;
        // Onboarding
        //if (!File.Exists(Config.ConfigPath)) 
        //new Onboarding(CurrentMainWindow);
        Config.init();


        // Taskbar
        CloseAppButton.Click += (_, _) => Close();
        MinimizeAppButton.Click += MinimizeAppOnClick;
        SettingsButton.Click += OpenSettingsOnClick;
        DevButton.Click += (_, _) =>
        {
            OpenSettings();
            _settings?.ShowAbout();
            _settings?.Activate();
        };

        // Base
        Closing += (_, _) => Cleanup();
        OpenButton.Click += OpenButtonOnClick;
        ProcessButton.Click += ProcessButtonOnClick;
        RunButton.Click += RunButtonOnClick;
        if (int.TryParse(Config.GetEntry("BrushDiameter"), out var brushDiameter))
            BrushWidthInput.Value = Math.Clamp(brushDiameter, 1, 128);
        _hasContourPreference = int.TryParse(Config.GetEntry("ContourDiameter"), out var contourDiameter) &&
            contourDiameter > 0;
        ContourWidthInput.Value = _hasContourPreference ? Math.Clamp(contourDiameter, 1, 128) : BrushDiameter;
        ExtraContourInput.IsChecked = bool.TryParse(Config.GetEntry("ExtraContourBrush"), out var extraContour)
            ? extraContour : _hasContourPreference;
        AccuracyInput.SelectedIndex = Config.GetEntry("ColorLayersAccuracy") == "fast" ? 1 : 0;
        LanguageInput.SelectedIndex = Config.GetEntry("ColorLayersLanguage") == "de" ? 1 : 0;
        _allowedPalette = PaletteDialog.Parse(Config.GetEntry("ColorLayersPalette")) ?? DrawingPalette.Standard;
        if (Enum.TryParse<OutlineMode>(Config.GetEntry("ColorLayersOutlineMode"), out var outlineMode) &&
            Enum.IsDefined(outlineMode))
            OutlineModeInput.SelectedIndex = (int)outlineMode;
        BrushWidthInput.ValueChanged += (_, _) =>
        {
            UpdateModeControls();
            Config.SetEntry("BrushDiameter", BrushDiameter.ToString());
        };
        ContourWidthInput.ValueChanged += (_, _) =>
        {
            _hasContourPreference = true;
            Config.SetEntry("ContourDiameter", SavedContourDiameter.ToString());
        };
        ExtraContourInput.IsCheckedChanged += (_, _) =>
        {
            if (ExtraContourInput.IsChecked == true && !_hasContourPreference)
                ContourWidthInput.Value = BrushDiameter;
            UpdateModeControls();
            Config.SetEntry("ExtraContourBrush", (ExtraContourInput.IsChecked == true).ToString());
        };
        AccuracyInput.SelectionChanged += (_, _) =>
            Config.SetEntry("ColorLayersAccuracy", AccuracyInput.SelectedIndex == 1 ? "fast" : "detailed");
        OutlineModeInput.SelectionChanged += (_, _) =>
        {
            UpdateModeControls();
            Config.SetEntry("ColorLayersOutlineMode", CurrentOutlineMode.ToString());
        };
        PaletteButton.Click += async (_, _) => await ChoosePalette();
        LanguageInput.SelectionChanged += (_, _) =>
        {
            ApplyLanguage();
            Config.SetEntry("ColorLayersLanguage", UiText.German ? "de" : "en");
        };
        CalibrateBrushButton.Click += (_, _) => CalibrateBrush(BrushWidthInput, false);
        CalibrateContourButton.Click += (_, _) => CalibrateBrush(ContourWidthInput, true);
        ColorCountInput.ValueChanged += (_, _) =>
        {
            UpdateModeControls();
            if (_processedBitmap is not null)
                ProcessImage();
        };
        UpdateModeControls();
        ApplyLanguage();

        ImageAIGeneration.Click += ImageAIGenerationOnClick;
        ImageSaveImage.Click += ImageSaveImageOnClick;
        ImageClearImage.Click += ImageClearImageOnClick;

        // Inputs
        SizeSlider.ValueChanged += SizeSliderOnValueChanged;
        WidthInput.TextChanging += WidthInputOnTextChanged;
        HeightInput.TextChanging += HeightInputOnTextChanged;


        PercentageNumber.TextChanging += PercentageNumberOnTextChanged;

        DrawIntervalElement.Text = Drawing.DefaultInterval.ToString();
        ClickDelayElement.Text = Drawing.DefaultClickDelay.ToString();
        DrawIntervalElement.TextChanging += DrawIntervalOnTextChanging;
        ClickDelayElement.TextChanging += ClickDelayOnTextChanging;
        minBlackThresholdElement.TextChanging += minBlackThresholdElementOnTextChanging;
        maxBlackThresholdElement.TextChanging += maxBlackThresholdElementOnTextChanging;
        AlphaThresholdElement.TextChanging += AlphaThresholdOnTextChanging;

        FreeDrawCheckbox.Click += FreeDrawCheckboxOnClick;

        EventHandler<TextChangingEventArgs> textChangeEvent = (sender, e) => HandleTextChange(e);
        HorizontalFilterText.TextChanging += textChangeEvent;
        VerticalFilterText.TextChanging += textChangeEvent;
        BorderAdvancedText.TextChanging += textChangeEvent;
        OutlineAdvancedText.TextChanging += textChangeEvent;
        InlineAdvancedText.TextChanging += textChangeEvent;
        InlineBorderAdvancedText.TextChanging += textChangeEvent;
        ErosionAdvancedText.TextChanging += textChangeEvent;

        // Config
        RefreshConfigsButton.Click += RefreshConfigList;
        SelectFolderElement.Click += SetConfigFolderViaDialog;
        SaveConfigButton.Click += SaveConfigViaDialog;
        OpenConfigElement.Click += LoadConfigViaDialog;
        LoadSelectButton.Click += LoadSelectedConfig;
        RefreshConfigList(this, null);

        Input.Start();
    }

    // User Configuration Handles

    //*
    public static FilePickerFileType ConfigsFileFilter { get; } = new("AutoDraw Config Files")
    {
        Patterns = new[] { "*.drawcfg" }
    };

    public static FilePickerFileType PngFileFilter { get; } = new("Portable Network Graphics")
    {
        Patterns = new[] { "*.png" }
    };

    private void ImageAIGenerationOnClick(object? sender, RoutedEventArgs e)
    {
        if (_aiPrompt is not null) return;
        _aiPrompt = new OpenAIPrompt();
        _aiPrompt.Show();
        _aiPrompt.Closed += AiPromptOnClosed;
    }

    private void AiPromptOnClosed(object? sender, EventArgs e)
    {
        _aiPrompt = null;
    }


    // Core Functions

    public void Cleanup()
    {
        _closing = true;
        _brushCalibration?.Close();
        _drawingPreview?.Close();
        _settings?.Close();
        _aiPrompt?.Close();
        if (Utils.LogObject != null) Utils.LogObject.Close();
        Input.Stop();
        Drawing.Halt();
    }

    private void SetPath(int path)
    {
        PatternSelection.SelectedIndex =
            path == 12345678 ? 0 :
            path == 14627358 ? 1 :
            path == 26573481 ? 2 :
            0;
        UpdatePath();
    }

    private void UpdatePath()
    {
        Drawing.PathValue =
            PatternSelection.SelectedIndex == 0 ? 12345678
            : PatternSelection.SelectedIndex == 1 ? 14627358
            : PatternSelection.SelectedIndex == 2 ? 26573481
            : 12345678;
    }

    private ImageProcessing.Filters GetSelectFilters()
    {
        // Generic Filters
        _currentFilters.MinThreshold = (byte)_minBlackThreshold;
        _currentFilters.MaxThreshold = (byte)_maxBlackThreshold;
        _currentFilters.AlphaThreshold = (byte)_alphaThresh;

        // Primary Filters

        //// Generic Filters
        _currentFilters.Invert = InvertFilterCheck.IsChecked ?? false;
        _currentFilters.Outline = OutlineFilterCheck.IsChecked ?? false;

        //// Pattern Filters
        _currentFilters.Crosshatch = CrosshatchFilterCheck.IsChecked ?? false;
        _currentFilters.DiagCrosshatch = DiagCrossFilterCheck.IsChecked ?? false;
        _currentFilters.HorizontalLines = int.Parse(HorizontalFilterText.Text ?? "0");
        _currentFilters.VerticalLines = int.Parse(VerticalFilterText.Text ?? "0");

        //// Experimental Filters
        _currentFilters.BorderAdvanced = int.Parse(BorderAdvancedText.Text ?? "0");
        _currentFilters.OutlineAdvanced = int.Parse(OutlineAdvancedText.Text ?? "0");
        _currentFilters.InlineAdvanced = int.Parse(InlineAdvancedText.Text ?? "0");
        _currentFilters.InlineBorderAdvanced = int.Parse(InlineBorderAdvancedText.Text ?? "0");
        _currentFilters.ErosionAdvanced = int.Parse(ErosionAdvancedText.Text ?? "0");

        // Dither Filters
        // **Yet to be implemented**

        UpdatePath();

        return _currentFilters;
    }

    // External Window Opening/Closing Handles

    private void OpenSettingsOnClick(object? sender, RoutedEventArgs e) => OpenSettings();

    private void OpenSettings()
    {
        if (_settings is not null) return;
        _settings = new Settings();
        _settings.Show();
        _settings.Closed += Settings_Closed;
    }

    private void Settings_Closed(object? sender, EventArgs e)
    {
        _settings = null;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && e.GetPosition(this).Y <= 20)
            BeginMoveDrag(e);
    }


    // Base UI Handles

    private int BrushDiameter => (int)Math.Round(BrushWidthInput.Value ?? 1, MidpointRounding.AwayFromZero);
    private int SavedContourDiameter =>
        (int)Math.Round(ContourWidthInput.Value ?? BrushDiameter, MidpointRounding.AwayFromZero);
    private int ContourDiameter => ExtraContourInput.IsChecked == true && CurrentOutlineMode != OutlineMode.OmitNarrow
        ? SavedContourDiameter : 0;
    private int ColorCount => (int)Math.Round(ColorCountInput.Value ?? 0, MidpointRounding.AwayFromZero);
    private bool FastMode => ColorCount > 0 && AccuracyInput.SelectedIndex == 1;
    private OutlineMode CurrentOutlineMode => (OutlineMode)Math.Max(0, OutlineModeInput.SelectedIndex);
    private SKColor[] AllowedPalette => _allowedPalette ?? DrawingPalette.Standard;

    private void UpdateModeControls()
    {
        var monochrome = ColorCount == 0;
        LegacyControls.IsVisible = LegacyFiltersTab.IsVisible = monochrome;
        LegacyPatternControl.IsVisible = FreeDrawCheckbox.IsVisible = monochrome && BrushDiameter == 1 &&
            ContourDiameter == 0 && CurrentOutlineMode == OutlineMode.Automatic;
        AccuracySettings.IsVisible = !monochrome;
        PaletteButton.IsEnabled = !monochrome;
        ExtraContourInput.IsEnabled = CurrentOutlineMode != OutlineMode.OmitNarrow;
        ContourSettings.IsVisible = ExtraContourInput.IsChecked == true && ExtraContourInput.IsEnabled;
    }

    private async System.Threading.Tasks.Task ChoosePalette()
    {
        var palette = await new PaletteDialog(AllowedPalette).ShowDialog<SKColor[]?>(this);
        if (palette is null || _closing) return;
        _allowedPalette = palette;
        Config.SetEntry("ColorLayersPalette", string.Join(" ", palette.Select(PaletteColor.Hex)));
        UpdatePaletteButton();
        if (_processedBitmap is not null) ProcessImage();
    }

    private void UpdatePaletteButton() => PaletteButton.Content = $"Palette… ({AllowedPalette.Length})";

    private void ApplyLanguage()
    {
        UiText.German = LanguageInput.SelectedIndex == 1;
        GeneralTab.Header = UiText.T("General", "Allgemein");
        LegacyFiltersTab.Header = "Filter";
        ColorCountLabel.Text = UiText.T("Color layers (0 = monochrome)", "Farblayer (0 = Schwarzweiß)");
        AccuracyLabel.Text = UiText.T("Accuracy", "Genauigkeit");
        DetailedItem.Content = UiText.T("Detailed", "Detailliert");
        FastItem.Content = UiText.T("Fast", "Schnell");
        ToolTip.SetTip(AccuracyInput, UiText.T("Fast reuses the same stroke plan and moves faster. Dark layers retain the detailed pace.",
            "Schnell nutzt denselben Strichplan und bewegt die Maus zügiger. Dunkle Ebenen behalten das detaillierte Tempo."));
        UpdatePaletteButton();
        OutlineModeLabel.Text = UiText.T("Outline order", "Konturen");
        OutlineAutoItem.Content = UiText.T("Automatic", "Automatisch");
        OutlineFirstItem.Content = UiText.T("Dark layers first", "Dunkle Ebenen zuerst");
        OutlineLastItem.Content = UiText.T("Dark layers last", "Dunkle Ebenen zuletzt");
        OutlineOmitItem.Content = UiText.T("Skip thin outlines", "Schmale Konturen weglassen");
        ToolTip.SetTip(OutlineModeInput, UiText.T("Automatic uses a narrower outline brush last, puts moderately wide dark layers first, and simplifies very thick outlines.",
            "Automatisch nutzt einen dünneren Konturstift zuletzt, zeichnet mittelbreite dunkle Ebenen zuerst und vereinfacht sehr dicke Konturen."));
        LanguageLabel.Text = UiText.T("Language", "Sprache");
        BrushWidthLabel.Text = UiText.T("Brush width (screen pixels)", "Stiftbreite (Bildschirm-Pixel)");
        CalibrateBrushButton.Content = CalibrateContourButton.Content = UiText.T("Calibrate brush", "Kalibrieren");
        ExtraContourInput.Content = UiText.T("Separate outline brush", "Extra Kontur Stift");
        ContourWarning.Text = UiText.OutlineWarning;
        IntervalLabel.Content = UiText.T("Interval", "Intervall");
        ClickDelayLabel.Content = UiText.T("Click Delay", "Klickpause");
        ProcessButton.Content = UiText.T("Process Image", "Bild verarbeiten");
        RunButton.Content = UiText.T("Begin Drawing", "Zeichnen vorbereiten");
        ImageSaveImage.Header = UiText.T("Save image", "Bild speichern");
        ImageClearImage.Header = UiText.T("Clear image", "Bild entfernen");
        ShowPalette();
        _settings?.RefreshLanguage();
    }

    private void CalibrateBrush(NumericUpDown input, bool contour)
    {
        if (_brushCalibration is not null || _drawingPreview is not null || Drawing.IsDrawing)
            return;
        var calibration = new BrushCalibration(contour ? SavedContourDiameter : BrushDiameter,
            diameter => input.Value = diameter, contour);
        _brushCalibration = calibration;
        var previousState = WindowState;
        calibration.Closed += (_, _) =>
        {
            _brushCalibration = null;
            if (!_closing)
                WindowState = previousState;
        };
        WindowState = WindowState.Minimized;
        calibration.Start();
    }

    private void ShowPalette()
    {
        PalettePreview.Children.Clear();
        if (_colorLayers is null)
            return;
        foreach (var color in _colorLayers.Palette)
        {
            var entry = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                Margin = new Thickness(5, 2, 8, 2),
                Children =
                {
                    new Border
                    {
                        Width = 13, Height = 13, CornerRadius = new CornerRadius(2),
                        Background = PaletteColor.Swatch(color),
                        BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1)
                    },
                    new TextBlock { Text = PaletteColor.Name(color), FontSize = 11 }
                }
            };
            ToolTip.SetTip(entry, PaletteColor.Hex(color));
            PalettePreview.Children.Add(entry);
        }
    }

    private void ShowImagePreview()
    {
        ImagePreview.Image = _displayedBitmap;
        if (_displayedBitmap is not null)
            Dispatcher.UIThread.Post(() =>
            {
                if (ImagePreview.Image is not { } bitmap || ImagePreview.Bounds.Width <= 0 ||
                    ImagePreview.Bounds.Height <= 0)
                    return;
                ImagePreview.ZoomToFit();
                var exactFit = 100 * Math.Min(ImagePreview.Bounds.Width / bitmap.Size.Width,
                    ImagePreview.Bounds.Height / bitmap.Size.Height);
                ImagePreview.Zoom = Math.Max(1, (int)Math.Floor(exactFit));
            }, DispatcherPriority.Background);
    }

    private void ProcessButtonOnClick(object? sender, RoutedEventArgs e)
    {
        if (_hasImage)
            ProcessImage();
    }

    private void ProcessImage()
    {
        var colorCount = ColorCount;
        var source = _preFxBitmap!;
        _colorLayers = null;
        if (colorCount > 0)
        {
            // Remove paper before scaling so white JPEG edges cannot blend into the ink palette.
            _colorSource ??= ColorLayers.RemoveWhiteBackground(_rawBitmap!);
            using var scaled = _colorSource.Width == source.Width && _colorSource.Height == source.Height
                ? null
                : _colorSource.Resize(new SKSizeI(source.Width, source.Height), SKFilterQuality.High);
            _colorLayers = ColorLayers.Create(scaled ?? _colorSource, Math.Min(colorCount, AllowedPalette.Length), AllowedPalette);
        }
        var processed = _colorLayers?.CreatePreview() ?? ImageProcessing.Process(source, GetSelectFilters());
        _processedBitmap?.Dispose();
        _displayedBitmap?.Dispose();

        _processedBitmap = processed;
        _displayedBitmap = _processedBitmap.ConvertToAvaloniaBitmap();
        ShowImagePreview();
        ShowPalette();
    }

    public void ImportImage(string? path, byte[]? img = null)
    {
        var decoded = img is null ? SKBitmap.Decode(path) : SKBitmap.Decode(img);
        if (decoded is null)
        {
            new MessageBox().ShowMessageBox("Error!", "The image could not be opened.", "error");
            return;
        }
        _rawBitmap?.Dispose();
        _preFxBitmap?.Dispose();
        _colorSource?.Dispose();
        _colorSource = null;
        _displayedBitmap?.Dispose();
        _rawBitmap = decoded.NormalizeColor();
        _preFxBitmap = _rawBitmap.Copy();
        _displayedBitmap = _rawBitmap.ConvertToAvaloniaBitmap();
        _processedBitmap?.Dispose();
        _processedBitmap = null;
        _colorLayers = null;
        _hasImage = true;
        ShowImagePreview();
        ShowPalette();

        _inChange = true;
        SizeSlider.Value = 100;

        PercentageNumber.Text = $"{Math.Round(SizeSlider.Value)}%";
        WidthInput.Text = _displayedBitmap.Size.Width.ToString();
        HeightInput.Text = _displayedBitmap.Size.Height.ToString();
        _inChange = false;
    }

    private async void OpenButtonOnClick(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = UiText.T("Open image", "Bild öffnen"),
            FileTypeFilter = new[] { FilePickerFileTypes.ImageAll },
            AllowMultiple = false
        });

        if (file.Count == 1) ImportImage(file[0].TryGetLocalPath());
    }

    private void RunButtonOnClick(object? sender, RoutedEventArgs e)
    {
        UpdatePath();
        if (_processedBitmap == null)
        {
            new MessageBox().ShowMessageBox("Error!", "Please select and process an image beforehand.", "error");
            return;
        }

        if (_drawingPreview is not null || _brushCalibration is not null || Drawing.IsDrawing) return;
        var layers = _colorLayers ?? (BrushDiameter > 1 || ContourDiameter > 0 || CurrentOutlineMode != OutlineMode.Automatic
            ? ColorLayers.Create(_processedBitmap, 1) : null);
        if (layers is { Palette.Length: 0 })
        {
            new MessageBox().ShowMessageBox("No colors to draw", "The image only contains white or transparent pixels.");
            return;
        }

        if (layers is not null)
        {
            using var monochrome = _colorLayers is null ? layers.CreatePreview() : null;
            var preview = new ColorPreview(layers, monochrome ?? _processedBitmap, BrushDiameter, ContourDiameter,
                FastMode, CurrentOutlineMode);
            _drawingPreview = preview;
            preview.Closed += (_, _) => _drawingPreview = null;
            WindowState = WindowState.Minimized;
            preview.ShowPreview();
            return;
        }

        var monochromePreview = new Preview();
        _drawingPreview = monochromePreview;
        monochromePreview.Closed += (_, _) => _drawingPreview = null;
        monochromePreview.ReadyDraw(_processedBitmap);
        WindowState = WindowState.Minimized;
    }

    private async void ImageSaveImageOnClick(object? sender, RoutedEventArgs e)
    {
        if (_processedBitmap is null) return;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Processed Image",
            FileTypeChoices = new[] { PngFileFilter }
        });

        if (file is not null)
        {
            var encodedData = _processedBitmap.Encode(SKEncodedImageFormat.Png, 100);
            await using var stream = await file.OpenWriteAsync();

            encodedData.SaveTo(stream);
        }
    }

    private void ImageClearImageOnClick(object? sender, RoutedEventArgs e)
    {
        _rawBitmap?.Dispose();
        _preFxBitmap?.Dispose();
        _colorSource?.Dispose();
        _colorSource = null;
        _processedBitmap?.Dispose();
        _displayedBitmap?.Dispose();
        _rawBitmap = new SKBitmap(318, 318, true);
        _preFxBitmap = new SKBitmap(318, 318, true);
        _processedBitmap = null;
        _colorLayers = null;
        _hasImage = false;
        _displayedBitmap = null;
        ShowImagePreview();
        ShowPalette();
    }

    // Inputs Handles

    private void HandleTextChange(TextChangingEventArgs e)
    {
        var source = (TextBox)e.Source;
        source.Text = _numberRegex.Replace(source.Text, "");
        e.Handled = true;

        if (source.Text.Length < 1) source.Text = "0";
    }

    private void ResizeImage(double width, double height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (GC.GetTotalMemory(false) < _lastMem) GC.RemoveMemoryPressure(_lastMem);
        _lastMem = GC.GetTotalMemory(false);

        if (_processedBitmap == null)
        {
            var resizedBitmap = _rawBitmap.Resize(new SKSizeI((int)width, (int)height), SKFilterQuality.High);
            _preFxBitmap.Dispose();
            _preFxBitmap = resizedBitmap;
            _displayedBitmap?.Dispose();
            _displayedBitmap = resizedBitmap.ConvertToAvaloniaBitmap();
            ShowImagePreview();
            GC.AddMemoryPressure(resizedBitmap.ByteCount);
            _memoryPressure += resizedBitmap.ByteCount;
        }
        else if (_processedBitmap != null)
        {
            var resizedBitmap = _rawBitmap.Resize(new SKSizeI((int)width, (int)height), SKFilterQuality.High);
            _preFxBitmap.Dispose();
            _preFxBitmap = resizedBitmap;
            ProcessImage();
            GC.AddMemoryPressure(resizedBitmap.ByteCount);
            _memoryPressure += resizedBitmap.ByteCount;
        }
    }

    private void SizeSliderOnValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_inChange) return;
        if (DateTime.Now.ToFileTime() - _lastTime < 333_333) return;
        _lastTime = DateTime.Now.ToFileTime();

        ResizeImage(_rawBitmap.Width * SizeSlider.Value / 100, _rawBitmap.Height * SizeSlider.Value / 100);

        _inChange = true;
        PercentageNumber.Text = $"{Math.Round(SizeSlider.Value)}%";
        WidthInput.Text = _displayedBitmap.Size.Width.ToString();
        HeightInput.Text = _displayedBitmap.Size.Height.ToString();
        _inChange = false;
    }

    private void PercentageNumberOnTextChanged(object? sender, TextChangingEventArgs e)
    {
        if (PercentageNumber.Text == null) return;
        if (_inChange) return;
        var numberText = _numberRegex.Replace(PercentageNumber.Text, "");
        PercentageNumber.Text = numberText + "%";
        e.Handled = true;

        if (numberText.Length < 1) return;
        var setNumber = int.Parse(numberText);
        if (setNumber < 1) return;
        if (setNumber > 500)
        {
            PercentageNumber.Text = "500%";
            return;
        }

        ResizeImage(_rawBitmap.Width * setNumber / 100, _rawBitmap.Height * setNumber / 100);

        _inChange = true;
        WidthInput.Text = _displayedBitmap.Size.Width.ToString();
        HeightInput.Text = _displayedBitmap.Size.Height.ToString();
        _inChange = false;
    }

    private void HeightInputOnTextChanged(object? sender, TextChangingEventArgs e)
    {
        if (HeightInput.Text == null) return;
        if (_inChange) return;
        var numberText = _numberRegex.Replace(HeightInput.Text, "");
        HeightInput.Text = numberText;
        e.Handled = true;

        if (numberText.Length < 1) return;
        var setNumber = int.Parse(numberText);
        if (setNumber < 1) return;
        if (setNumber > 8096)
        {
            PercentageNumber.Text = "8096";
            return;
        }

        if (LockAspectRatioCheckBox.IsChecked ?? false) ResizeImage(int.Parse(WidthInput.Text), setNumber);
        else ResizeImage(_rawBitmap.Width / (float)_rawBitmap.Height * setNumber, setNumber);

        _inChange = true;
        PercentageNumber.Text = $"{Math.Round(SizeSlider.Value)}%";
        WidthInput.Text = _displayedBitmap.Size.Width.ToString();
        _inChange = false;
    }

    private void WidthInputOnTextChanged(object? sender, TextChangingEventArgs e)
    {
        if (WidthInput.Text == null) return;
        if (_inChange) return;
        var numberText = _numberRegex.Replace(WidthInput.Text, "");
        WidthInput.Text = numberText;
        e.Handled = true;

        if (numberText.Length < 1) return;
        var setNumber = int.Parse(numberText);
        switch (setNumber)
        {
            case < 1:
                PercentageNumber.Text = "1";
                return;
            case > 4096:
                PercentageNumber.Text = "4096";
                return;
        }

        if (LockAspectRatioCheckBox.IsChecked ?? false) ResizeImage(setNumber, int.Parse(HeightInput.Text));
        else ResizeImage(setNumber, _rawBitmap.Height / (float)_rawBitmap.Width * setNumber);

        _inChange = true;
        PercentageNumber.Text = $"{Math.Round(SizeSlider.Value)}%";
        HeightInput.Text = _displayedBitmap.Size.Height.ToString();
        _inChange = false;
    }


    private void DrawIntervalOnTextChanging(object? sender, TextChangingEventArgs e)
    {
        HandleTextChange(e);
        Drawing.Interval = int.TryParse(DrawIntervalElement.Text, out var interval) ? interval : Drawing.DefaultInterval;
    }

    private void ClickDelayOnTextChanging(object? sender, TextChangingEventArgs e)
    {
        HandleTextChange(e);
        Drawing.ClickDelay = int.TryParse(ClickDelayElement.Text, out var clickDelay) ? clickDelay : Drawing.DefaultClickDelay;
    }

    private void minBlackThresholdElementOnTextChanging(object? sender, TextChangingEventArgs e)
    {
        HandleTextChange(e);
        _minBlackThreshold = int.TryParse(minBlackThresholdElement.Text, out var black) ? black : 127;
    }

    private void maxBlackThresholdElementOnTextChanging(object? sender, TextChangingEventArgs e)
    {
        HandleTextChange(e);
        _maxBlackThreshold = int.TryParse(maxBlackThresholdElement.Text, out var black) ? black : 127;
    }

    private void AlphaThresholdOnTextChanging(object? sender, TextChangingEventArgs e)
    {
        HandleTextChange(e);
        _alphaThresh = int.TryParse(AlphaThresholdElement.Text, out var alpha) ? alpha : 127;
    }

    private void FreeDrawCheckboxOnClick(object? sender, RoutedEventArgs e)
    {
        Drawing.FreeDraw2 = FreeDrawCheckbox.IsChecked ?? false;
    }

    // Toolbar Handles

    private void MinimizeAppOnClick(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private async void PasteControl(object? sender, RoutedEventArgs e)
    {
        if (Clipboard is not { } clipboard) return;
        try
        {
            if (await clipboard.GetDataAsync(DataFormats.Files) is IEnumerable<IStorageItem> files &&
                files.FirstOrDefault()?.TryGetLocalPath() is { } path)
            {
                ImportImage(path);
                return;
            }
            if (await clipboard.GetDataAsync("PNG") is byte[] image && image.Length > 0)
            {
                ImportImage(string.Empty, image);
                return;
            }
        }
        catch (Exception)
        {
            // Clipboard ownership can change during the asynchronous read.
        }
        new MessageBox().ShowMessageBox(UiText.T("Paste image", "Bild einfügen"),
            UiText.T("No supported image was found in the clipboard.", "Die Zwischenablage enthält kein unterstütztes Bild."), "error");
    }

    public async void SetConfigFolderViaDialog(object? sender, RoutedEventArgs e)
    {
        var folder = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { AllowMultiple = false });
        if (folder.Count != 1) return;
        Config.SetEntry("ConfigFolder", folder[0].TryGetLocalPath());
        RefreshConfigList(this, null);
    }

    public void LoadConfig(string? path)
    {
        // TODO: use the warning box (Not implemented yet) system to make it return a "This config does not exist!"
        if (!path.EndsWith(".drawcfg")) return;
        var lines = File.ReadAllLines(path);
        SelectedConfigLabel.Content =
            $"{Properties.Resources.ConfigSelected} - {Path.GetFileNameWithoutExtension(path)}";

        DrawIntervalElement.Text = lines.Length > 0 ? lines[0] : Drawing.DefaultInterval.ToString();

        ClickDelayElement.Text = lines.Length > 1 ? lines[1] : Drawing.DefaultClickDelay.ToString();

        //maxBlackThresholdElement.Text = lines.Length > 2 ? lines[2] : "127";
        //AlphaThresholdElement.Text = lines.Length > 3 ? lines[3] : "200";
        // Silly!!

        if (lines.Length <= 4) return;
        if (!bool.TryParse(lines[4], out var _fd2)) return;
        FreeDrawCheckbox.IsChecked = _fd2;
        Drawing.FreeDraw2 = _fd2;

        if (lines.Length <= 5) return;
        if (!int.TryParse(lines[5], out var _path)) return;
        SetPath(_path);

        minBlackThresholdElement.Text = lines.Length > 6 ? lines[6] : "0";
    }

    public async void SaveConfigViaDialog(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Config",
            FileTypeChoices = new[] { ConfigsFileFilter }
        });

        if (file is not null)
        {
            UpdatePath();
            await using var stream = await file.OpenWriteAsync();
            await using var streamWriter = new StreamWriter(stream);

            string?[] values =
            {
                DrawIntervalElement.Text,
                ClickDelayElement.Text,
                maxBlackThresholdElement.Text,
                AlphaThresholdElement.Text,
                FreeDrawCheckbox.IsChecked.ToString(),
                Drawing.PathValue.ToString(),
                minBlackThresholdElement.Text
            };

            await streamWriter.WriteAsync(string.Join("\r\n", values));
        }
    }

    public async void LoadConfigViaDialog(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Load Config",
            FileTypeFilter = new[] { ConfigsFileFilter },
            AllowMultiple = false
        });

        if (file.Count == 1) LoadConfig(file[0].TryGetLocalPath());
    }

    public void RefreshConfigList(object? sender, RoutedEventArgs? e)
    {
        var configFolder = Config.GetEntry("ConfigFolder");
        if (configFolder == null) return;
        if (!Directory.Exists(configFolder)) return;
        var files = Directory.GetFiles(configFolder, "*.drawcfg");
        var fileNames = files.Select(f => Path.GetFileNameWithoutExtension(f)).ToArray();
        ConfigsListBox.ClearValue(ItemsControl.ItemsSourceProperty);
        ConfigsListBox.Items.Clear();
        ConfigsListBox.ItemsSource = fileNames;
    }

    public void LoadSelectedConfig(object? sender, RoutedEventArgs e)
    {
        if (ConfigsListBox.SelectedItem == null) return;
        var selectedItem = ConfigsListBox.SelectedItem.ToString();
        if (selectedItem == null) return;
        LoadConfig($"{Path.Combine(Config.GetEntry("ConfigFolder"), selectedItem)}.drawcfg");
    }

}
