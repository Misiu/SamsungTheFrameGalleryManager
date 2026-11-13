using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FrameGallery.Core;
using FrameGallery.Localization;

namespace FrameGallery.Desktop.ViewModels;

/// <summary>The gallery screen of one TV: tiles with thumbnails, deleting, showing on the TV and uploading photos.</summary>
public sealed partial class GalleryViewModel : ScreenViewModel
{
    const int ThumbBatch = 12;

    readonly TvRecord _tv;
    readonly IArtClient _art;
    readonly IInteraction _ui;
    readonly CancellationTokenSource _cts = new();

    public GalleryViewModel(TvRecord tv, IArtClient art, IInteraction ui)
    {
        _tv = tv;
        _art = art;
        _ui = ui;
        Items.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));
    }

    public override string Title => _tv.Name;

    /// <summary>Leaving in the middle of an upload would cut it short; everything else is simply cancelled.</summary>
    public override bool CanLeave => !IsUploading;

    public override void OnLeaving() => Cancel();
    public ObservableCollection<GalleryItemViewModel> Items { get; } = [];

    /// <summary>An operation is running; the buttons are disabled.</summary>
    [ObservableProperty] bool _isBusy;

    /// <summary>The list is being fetched, so there are no tiles yet and the window shows one big spinner.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsEmpty))] bool _isLoading;

    [ObservableProperty] string _status = "";

    /// <summary>Also show Art Store and factory images (read-only).</summary>
    [ObservableProperty] bool _showAll;

    [ObservableProperty] bool _isUploading;
    [ObservableProperty] bool _isUploadIndeterminate;
    [ObservableProperty] double _uploadProgress;
    [ObservableProperty] string _uploadText = "";

    public bool IsEmpty => !IsLoading && Items.Count == 0;

    partial void OnShowAllChanged(bool value) => _ = ReloadAsync();

    partial void OnIsUploadingChanged(bool value) => OnPropertyChanged(nameof(CanLeave));

    /// <summary>"Add photos…": pick files, then crop and upload them.</summary>
    [RelayCommand]
    async Task AddPhotosAsync()
    {
        if (IsBusy) return;
        var files = await _ui.PickImagesAsync();
        if (files.Count > 0) await AddFilesAsync(files);
    }

    public void Cancel() => _cts.Cancel();

    /// <summary>Runs an operation, disabling the buttons meanwhile, and shows its error, if any, to the user.</summary>
    async Task RunAsync(Func<Task> work)
    {
        if (IsBusy) return;
        IsBusy = true;
        try { await work(); }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested) { }
        catch (Exception e)
        {
            TvLog.Write($"[ERR] {TvLog.Describe(e)}");
            Status = Loc.Get("Main_Failed");
            await _ui.MessageAsync(e is TvException ? e.Message : Loc.Get("Ui_UnexpectedError", e.Message));
        }
        finally { IsBusy = false; }
    }

    public Task ReloadAsync() => RunAsync(async () =>
    {
        IsLoading = true;
        Status = Loc.Get("Gal_Loading");
        List<GalleryItemViewModel> tiles;
        try
        {
            var all = await _art.ListAsync(_cts.Token);
            // The TV lists the same image under several categories (e.g. MY-C0002 and MY-C0008): keep one entry, "My photos" first.
            var unique = all.OrderBy(i => i.CategoryId == ArtClient.MyPhotosCategory ? 0 : 1).DistinctBy(i => i.ContentId).ToList();
            var shown = ShowAll ? unique : unique.Where(i => i.CategoryId == ArtClient.MyPhotosCategory).ToList();

            // The tiles appear at once, each with its own spinner; the thumbnails follow.
            Items.Clear();
            tiles = shown.Select(i => new GalleryItemViewModel(i, Label(i))).ToList();
            foreach (var tile in tiles) Items.Add(tile);
            _total = unique.Count;
            ShowCount();
        }
        finally { IsLoading = false; }

        await LoadThumbnailsAsync(tiles);
    });

    /// <summary>The TV's date can be truncated ("2026:11:13 10:07:1"), so only the minutes are used.</summary>
    static string Label(ArtItem i) =>
        i.ImageDate is { Length: >= 16 } s
        && DateTime.TryParseExact(s[..16], "yyyy:MM:dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? $"{d:yyyy-MM-dd HH:mm}" : i.ContentId;

    async Task LoadThumbnailsAsync(List<GalleryItemViewModel> tiles)
    {
        for (var start = 0; start < tiles.Count; start += ThumbBatch)
        {
            _cts.Token.ThrowIfCancellationRequested();
            var batch = tiles.Skip(start).Take(ThumbBatch).ToList();
            try
            {
                var thumbs = await _art.GetThumbnailsAsync(batch.Select(t => t.Item.ContentId).ToList(), _cts.Token);
                foreach (var tile in batch)
                {
                    if (!thumbs.TryGetValue(tile.Item.ContentId, out var bytes)) continue;
                    try
                    {
                        tile.Thumbnail = await Task.Run(() => { using var ms = new MemoryStream(bytes); return new Bitmap(ms); });
                        tile.IsLoaded = true;   // the view fades the picture in
                    }
                    catch (Exception e) when (e is ArgumentException or InvalidOperationException or NotSupportedException)
                    {
                        TvLog.Write($"[GALLERY] unreadable thumbnail {tile.Item.ContentId}: {e.Message}");
                    }
                }
                Status = Loc.Get("Gal_Thumbs", Math.Min(start + ThumbBatch, tiles.Count), tiles.Count);
            }
            catch (TvException e)   // missing thumbnails do not block deleting or adding
            {
                TvLog.Write($"[GALLERY] thumbnails: {TvLog.Describe(e)}");
                Status = Loc.Get("Gal_ThumbsFailed");
            }
            foreach (var tile in batch.Where(t => !t.IsLoaded)) tile.IsFailed = true;
        }
        if (!_cts.IsCancellationRequested) Status = Loc.Get("Gal_Count", tiles.Count);
    }

    public Task ShowOnTvAsync(GalleryItemViewModel item) => RunAsync(async () =>
    {
        Status = Loc.Get("Gal_Showing");
        await _art.SelectImageAsync(item.Item.ContentId, show: true, _cts.Token);
        Status = Loc.Get("Gal_Shown", item.Label);
    });

    public async Task DeleteAsync(IReadOnlyList<GalleryItemViewModel> selected)
    {
        if (IsBusy) return;
        // Only the user's own photos are deleted; Art Store and factory Samsung images stay (they can only be restored on the TV).
        var own = selected.Where(i => i.IsOwn).Select(i => i.Item.ContentId).ToList();
        if (own.Count == 0)
        {
            if (selected.Count > 0) await _ui.MessageAsync(Loc.Get("Gal_OnlyOwn"));
            return;
        }

        var skipped = selected.Count - own.Count;
        var text = Loc.Get("Gal_DeleteConfirm", own.Count) + (skipped > 0 ? "\n\n" + Loc.Get("Gal_DeleteSkipped", skipped) : "");
        if (!await _ui.ConfirmAsync(text)) return;

        TvLog.Write($"[UI] deleting {own.Count} image(s)");
        var deleted = false;
        await RunAsync(async () =>
        {
            Status = Loc.Get("Gal_Deleting");
            await _art.DeleteAsync(own, _cts.Token);
            deleted = true;
        });
        if (!deleted)
        {
            await ReloadAsync();   // after an error the TV's state is unknown, so ask it
            return;
        }

        // The TV confirmed, so there is nothing to ask it: just drop the tiles.
        foreach (var tile in Items.Where(i => own.Contains(i.Item.ContentId)).ToList()) Items.Remove(tile);
        _total -= own.Count;
        ShowCount();
    }

    /// <summary>The number of distinct images on the TV, whether shown or not.</summary>
    int _total;

    void ShowCount() => Status = ShowAll ? Loc.Get("Gal_Count", Items.Count) : Loc.Get("Gal_CountOf", Items.Count, _total);

    /// <summary>
    /// Puts a just-uploaded photo at the front of the list. The thumbnail is made from the JPEG that was sent, so the TV is not asked
    /// for anything.
    /// </summary>
    async Task AddTileAsync(string contentId, byte[] jpeg)
    {
        var item = new ArtItem(contentId, ArtClient.MyPhotosCategory, "none", _tv.Width, _tv.Height, DateTime.Now.ToString("yyyy:MM:dd HH:mm:ss"));
        var tile = new GalleryItemViewModel(item, Label(item));
        Items.Insert(0, tile);
        _total++;
        ShowCount();
        try
        {
            tile.Thumbnail = await Task.Run(() => { using var ms = new MemoryStream(jpeg); return Bitmap.DecodeToWidth(ms, 480); });
            tile.IsLoaded = true;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or NotSupportedException)
        {
            TvLog.Write($"[GALLERY] cannot make a thumbnail of the new image: {e.Message}");
            tile.IsFailed = true;
        }
    }

    /// <summary>The path shared by "Add photos…" and drag &amp; drop: crop, upload with progress, refresh.</summary>
    public async Task AddFilesAsync(IEnumerable<string> paths)
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            foreach (var path in paths)
            {
                var name = FileNames.Shorten(Path.GetFileName(path));

                byte[]? jpeg;
                try { jpeg = await _ui.CropAsync(path, _tv.Width, _tv.Height); }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    TvLog.Write($"[UI] could not open {name}: {e.GetType().Name}: {e.Message}");
                    await _ui.MessageAsync(Loc.Get("Gal_CannotOpen", name, e.GetType().Name));
                    continue;
                }
                if (jpeg is null)   // "Skip"
                {
                    TvLog.Write($"[UI] skipped {name}");
                    continue;
                }
                TvLog.Write($"[UI] crop of {name}: result {_tv.Width}x{_tv.Height} JPEG {jpeg.Length} B");

                // Sending shows a real progress bar; after the last byte the TV still has to save the image, which is indeterminate.
                IsUploading = true;
                IsUploadIndeterminate = false;
                UploadProgress = 0;
                UploadText = Loc.Get("Gal_Sending", name);
                var progress = new Progress<double>(p =>
                {
                    UploadProgress = p * 100;
                    if (p < 1) return;
                    IsUploadIndeterminate = true;
                    UploadText = Loc.Get("Gal_Storing", name);
                });
                try
                {
                    var id = await _art.UploadAsync(jpeg, _cts.Token, progress);
                    IsUploading = false;
                    await AddTileAsync(id, jpeg);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    TvLog.Write($"[ERR] {TvLog.Describe(e)}");
                    await _ui.MessageAsync(e is TvException ? e.Message : Loc.Get("Ui_UnexpectedError", e.Message));
                    break;   // the error is shown; do not continue with the remaining files
                }
                finally { IsUploading = false; }
            }
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested) { }
        finally { IsBusy = false; }
    }
}
