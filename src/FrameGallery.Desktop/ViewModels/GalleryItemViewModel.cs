using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using FrameGallery.Core;

namespace FrameGallery.Desktop.ViewModels;

/// <summary>
/// One tile of the gallery. It appears as soon as the list is known, with its own spinner; when the thumbnail arrives
/// <see cref="IsLoaded"/> turns on and the view fades the picture in.
/// </summary>
public sealed partial class GalleryItemViewModel(ArtItem item, string label) : ObservableObject
{
    public ArtItem Item { get; } = item;
    public string Label { get; } = label;
    public bool IsOwn => Item.CategoryId == ArtClient.MyPhotosCategory;

    [ObservableProperty] Bitmap? _thumbnail;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowSpinner))] bool _isLoaded;

    /// <summary>The thumbnail could not be fetched or decoded; the tile shows an icon instead of the spinner.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowSpinner))] bool _isFailed;

    public bool ShowSpinner => !IsLoaded && !IsFailed;
}
