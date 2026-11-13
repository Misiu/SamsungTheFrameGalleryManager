using CommunityToolkit.Mvvm.ComponentModel;
using FrameGallery.Localization;

namespace FrameGallery.Desktop.ViewModels;

/// <summary>
/// The crop screen of one photo. The view does the loading and the cropping (it owns the picture and the crop canvas) and reports
/// back with <see cref="Complete"/> or <see cref="Fail"/>; leaving by Back or Skip gives null.
/// </summary>
public sealed partial class CropViewModel(string path, int targetWidth, int targetHeight) : ScreenViewModel
{
    readonly TaskCompletionSource<byte[]?> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public string Path { get; } = path;
    public int TargetWidth { get; } = targetWidth;
    public int TargetHeight { get; } = targetHeight;

    public override string Title => Loc.Get("Crop_Title", FileNames.Shorten(System.IO.Path.GetFileName(Path)));

    /// <summary>The JPEG in the TV's resolution; null when the photo was skipped; faulted when the file cannot be opened.</summary>
    public Task<byte[]?> Result => _result.Task;

    /// <summary>The cropped picture is being encoded; Back waits until it is done.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanLeave))] bool _isEncoding;

    public override bool CanLeave => !IsEncoding;

    public void Complete(byte[]? jpeg)
    {
        _result.TrySetResult(jpeg);
        RequestClose();
    }

    public void Fail(Exception error)
    {
        _result.TrySetException(error);
        RequestClose();
    }

    public override void OnLeaving() => _result.TrySetResult(null);
}
