using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using FrameGallery.Core;
using FrameGallery.Imaging;
using FrameGallery.Localization;
using SkiaSharp;

namespace FrameGallery.Desktop.Views;

/// <summary>Crops a photo to the TV screen's aspect ratio. Closes with the JPEG at the TV's resolution, or with null when skipped.</summary>
public partial class CropWindow : Window
{
    readonly string _path;
    readonly int _targetWidth, _targetHeight;
    SKBitmap? _bitmap;

    /// <summary>Set when the file could not be opened as an image; the window then closes by itself.</summary>
    public Exception? LoadError { get; private set; }

    public CropWindow(string path, int targetWidth, int targetHeight)
    {
        InitializeComponent();
        _path = path;
        _targetWidth = targetWidth;
        _targetHeight = targetHeight;
        Title = Loc.Get("Crop_Title", Path.GetFileName(path));

        Canvas.CropChanged += (_, _) => UpdateInfo();
        Opened += async (_, _) => await LoadAsync();
        Closed += (_, _) => _bitmap?.Dispose();
    }

    async Task LoadAsync()
    {
        BusyText.Text = Loc.Get("Crop_Loading");
        try
        {
            // Decoding a big photo and building the preview take a moment, so they run off the UI thread.
            var (bitmap, preview) = await Task.Run(() =>
            {
                var loaded = ImagePipeline.Load(_path);
                return (loaded, BitmapConverter.ToAvalonia(loaded));
            });
            _bitmap = bitmap;
            Canvas.SetImage(preview, bitmap.Width, bitmap.Height, (double)_targetWidth / _targetHeight);
            BusyOverlay.IsVisible = false;
            UseButton.IsEnabled = true;
        }
        catch (Exception e)
        {
            LoadError = e;
            Close(null);
        }
    }

    void UpdateInfo()
    {
        var c = Canvas.Crop;
        var low = c.W < _targetWidth * 0.6;
        Info.Text = Loc.Get("Crop_Info", c.W, c.H, _targetWidth, _targetHeight) + (low ? Loc.Get("Crop_Low") : "");
        Info.Foreground = low ? Brushes.DarkOrange : null;
    }

    void Skip_Click(object? sender, RoutedEventArgs e) => Close(null);

    async void Use_Click(object? sender, RoutedEventArgs e)
    {
        UseButton.IsEnabled = SkipButton.IsEnabled = false;
        BusyText.Text = Loc.Get("Crop_Encoding");
        BusyOverlay.IsVisible = true;

        var bitmap = _bitmap!;
        var crop = Canvas.Crop;
        var jpeg = await Task.Run(() => ImagePipeline.CropToJpeg(bitmap, crop, _targetWidth, _targetHeight));
        Close(jpeg);
    }
}
