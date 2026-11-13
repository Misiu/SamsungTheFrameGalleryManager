using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using FrameGallery.Desktop.ViewModels;
using FrameGallery.Imaging;
using FrameGallery.Localization;
using SkiaSharp;

namespace FrameGallery.Desktop.Views;

/// <summary>Crops a photo to the TV screen's aspect ratio and hands the JPEG (at the TV's resolution) to its view model.</summary>
public partial class CropView : UserControl
{
    SKBitmap? _bitmap;
    bool _detached;

    public CropView()
    {
        InitializeComponent();
        Canvas.CropChanged += (_, _) => UpdateInfo();
    }

    CropViewModel Vm => (CropViewModel)DataContext!;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _detached = false;
        if (DataContext is CropViewModel vm) _ = LoadAsync(vm);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _detached = true;
        _bitmap?.Dispose();
        _bitmap = null;
    }

    async Task LoadAsync(CropViewModel vm)
    {
        BusyText.Text = Loc.Get("Crop_Loading");
        try
        {
            // Decoding a big photo and building the preview take a moment, so they run off the UI thread.
            var (bitmap, preview) = await Task.Run(() =>
            {
                var loaded = ImagePipeline.Load(vm.Path);
                return (loaded, BitmapConverter.ToAvalonia(loaded));
            });
            if (_detached) { bitmap.Dispose(); return; }   // the user went back while the photo was loading

            _bitmap = bitmap;
            Canvas.SetImage(preview, bitmap.Width, bitmap.Height, (double)vm.TargetWidth / vm.TargetHeight);
            BusyOverlay.IsVisible = false;
            UseButton.IsEnabled = true;
        }
        catch (Exception e)
        {
            vm.Fail(e);   // the file could not be opened as an image; the gallery tells the user
        }
    }

    void UpdateInfo()
    {
        var c = Canvas.Crop;
        var low = c.W < Vm.TargetWidth * 0.6;
        Info.Text = Loc.Get("Crop_Info", c.W, c.H, Vm.TargetWidth, Vm.TargetHeight) + (low ? Loc.Get("Crop_Low") : "");
        Info.Foreground = low ? Brushes.DarkOrange : null;
    }

    async void Use_Click(object? sender, RoutedEventArgs e)
    {
        var vm = Vm;
        UseButton.IsEnabled = false;
        vm.IsEncoding = true;
        BusyText.Text = Loc.Get("Crop_Encoding");
        BusyOverlay.IsVisible = true;

        var bitmap = _bitmap!;
        var crop = Canvas.Crop;
        try
        {
            var jpeg = await Task.Run(() => ImagePipeline.CropToJpeg(bitmap, crop, vm.TargetWidth, vm.TargetHeight));
            vm.IsEncoding = false;
            vm.Complete(jpeg);
        }
        catch (Exception ex)
        {
            vm.IsEncoding = false;
            vm.Fail(ex);
        }
    }
}
