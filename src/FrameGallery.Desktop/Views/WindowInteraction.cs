using System.Diagnostics;
using System.Runtime.ExceptionServices;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using FrameGallery.Core;
using FrameGallery.Desktop.ViewModels;
using FrameGallery.Localization;

namespace FrameGallery.Desktop.Views;

/// <summary><see cref="IInteraction"/> on top of a window: its dialogs, pickers and child windows.</summary>
sealed class WindowInteraction(Window owner) : IInteraction
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
        var window = new CropWindow(path, targetWidth, targetHeight);
        var jpeg = await window.ShowDialog<byte[]?>(owner);
        if (window.LoadError is { } error) ExceptionDispatchInfo.Capture(error).Throw();   // the file could not be opened as an image
        return jpeg;
    }

    public Task ShowGalleryAsync(TvRecord tv, IArtClient art) => new GalleryWindow(tv, art).ShowDialog(owner);

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
