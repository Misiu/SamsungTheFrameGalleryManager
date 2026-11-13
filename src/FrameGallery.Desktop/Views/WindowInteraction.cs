using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using FrameGallery.Core;
using FrameGallery.Desktop.ViewModels;
using FrameGallery.Localization;

namespace FrameGallery.Desktop.Views;

/// <summary>
/// <see cref="IInteraction"/> on top of the window: small dialogs and the file picker are windows over it, the gallery and the crop
/// screen are screens of the <see cref="ShellViewModel"/> inside it.
/// </summary>
sealed class WindowInteraction(Window owner, ShellViewModel shell) : IInteraction
{
    static readonly string[] ImagePatterns = ["*.jpg", "*.jpeg", "*.png", "*.bmp", "*.gif", "*.tif", "*.tiff", "*.webp"];

    public Task MessageAsync(string text) => DialogWindow.MessageAsync(owner, text);

    public Task<bool> ConfirmAsync(string text) => DialogWindow.ConfirmAsync(owner, text);

    public Task<string?> AskAsync(string title, string prompt) => DialogWindow.AskAsync(owner, title, prompt);

    public async Task<IReadOnlyList<string>> PickImagesAsync()
    {
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Loc.Get("Gal_PickTitle"),
            AllowMultiple = true,
            FileTypeFilter = [new FilePickerFileType(Loc.Get("Gal_FilterImages")) { Patterns = ImagePatterns }],
        });
        return files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
    }

    public async Task<byte[]?> CropAsync(string path, int targetWidth, int targetHeight)
    {
        var screen = new CropViewModel(path, targetWidth, targetHeight);
        var shown = shell.ShowAsync(screen);
        var jpeg = await screen.Result;   // throws when the file cannot be opened as an image
        await shown;
        return jpeg;
    }

    public Task ShowGalleryAsync(TvRecord tv, IArtClient art)
    {
        var screen = new GalleryViewModel(tv, art, this);
        var shown = shell.ShowAsync(screen);
        _ = LoadGalleryAsync(screen, art);
        return shown;
    }

    static async Task LoadGalleryAsync(GalleryViewModel screen, IArtClient art)
    {
        await screen.ReloadAsync();
        // A hook for testing the crop screen and the upload progress without the system file dialog (demo mode only).
        if (art is Demo.DemoArtClient && Environment.GetEnvironmentVariable("FRAMEGALLERY_DEMO_ADD") is { Length: > 0 } file)
            await screen.AddFilesAsync([file]);
    }

    public void OpenLog()
    {
        try
        {
            if (!File.Exists(AppPaths.Log)) File.WriteAllText(AppPaths.Log, "");
            Process.Start(new ProcessStartInfo(AppPaths.Log) { UseShellExecute = true });
        }
        catch (Exception e) { TvLog.Write($"[ERR] could not open the log: {e.Message}"); }
    }
}
