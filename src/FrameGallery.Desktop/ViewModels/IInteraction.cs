using FrameGallery.Core;

namespace FrameGallery.Desktop.ViewModels;

/// <summary>
/// What the view models need from the windows (dialogs, pickers, other windows), so they stay free of UI types and easy to test.
/// </summary>
public interface IInteraction
{
    Task MessageAsync(string text);
    Task<bool> ConfirmAsync(string text);

    /// <returns>The typed text, or null when the dialog was cancelled.</returns>
    Task<string?> AskAsync(string title, string prompt);

    Task<IReadOnlyList<string>> PickImagesAsync();

    /// <summary>Shows the crop window for a photo.</summary>
    /// <returns>The JPEG in the TV's resolution, or null when the user skipped the photo.</returns>
    Task<byte[]?> CropAsync(string path, int targetWidth, int targetHeight);

    /// <summary>Shows the gallery of a TV and returns when it is closed.</summary>
    Task ShowGalleryAsync(TvRecord tv, IArtClient art);

    void OpenLog();
}
