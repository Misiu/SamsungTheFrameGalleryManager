using System.Runtime.InteropServices;
using FrameGallery.Core;
using FrameGallery.Localization;

namespace FrameGallery.App;

sealed class GalleryForm : Form
{
    const int ThumbW = 240, ThumbH = 135, ThumbBatch = 12;

    readonly TvRecord _tv;
    readonly ArtClient _art;
    readonly CancellationTokenSource _cts = new();
    readonly ImageList _images = new() { ImageSize = new Size(ThumbW, ThumbH), ColorDepth = ColorDepth.Depth32Bit };
    readonly ListView _list = new() { Dock = DockStyle.Fill, View = View.LargeIcon, MultiSelect = true, HideSelection = false };
    readonly ToolStripButton _add = new(Loc.Get("Gal_Add"));
    readonly ToolStripButton _delete = new(Loc.Get("Gal_Delete"));
    readonly ToolStripButton _refresh = new(Loc.Get("Gal_Refresh"));
    readonly ToolStripButton _all = new(Loc.Get("Gal_All")) { CheckOnClick = true, ToolTipText = Loc.Get("Gal_AllTip") };
    readonly ContextMenuStrip _menu = new();
    readonly ToolStripMenuItem _miShow = new(Loc.Get("Gal_MenuShow"));
    readonly ToolStripMenuItem _miDelete = new(Loc.Get("Gal_MenuDelete"));
    readonly ToolStripStatusLabel _status = new("");
    bool _busy;

    public GalleryForm(TvRecord tv, ArtClient art)
    {
        _tv = tv; _art = art;
        Text = Loc.Get("Gal_Title", tv.Name);
        Icon = Ui.AppIcon;
        Size = new Size(1000, 680);
        StartPosition = FormStartPosition.CenterParent;

        using (var ph = new Bitmap(ThumbW, ThumbH)) using (var g = Graphics.FromImage(ph))
        {
            g.Clear(Color.FromArgb(55, 55, 55));
            _images.Images.Add("ph", (Bitmap)ph.Clone());
        }
        _list.LargeImageList = _images;

        // Right-click menu. The ListView selects the item under the cursor on right-click by itself.
        _menu.Items.AddRange([_miShow, _miDelete]);
        _list.ContextMenuStrip = _menu;
        _menu.Opening += (_, e) =>
        {
            var selected = SelectedItems();
            if (selected.Count == 0 || _busy) { e.Cancel = true; return; }
            var own = selected.Count(i => i.CategoryId == ArtClient.MyPhotosCategory);
            _miShow.Enabled = selected.Count == 1;
            _miDelete.Enabled = own > 0;
            _miDelete.Text = own > 1 ? Loc.Get("Gal_MenuDeleteN", own) : Loc.Get("Gal_MenuDelete");
        };
        _miShow.Click += async (_, _) => await ShowOnTvAsync();
        _miDelete.Click += async (_, _) => await DeleteAsync();

        var tools = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden };
        tools.Items.AddRange([_add, _delete, _refresh, new ToolStripSeparator(), _all]);
        var statusStrip = new StatusStrip();
        statusStrip.Items.Add(_status);
        Controls.Add(_list);
        Controls.Add(statusStrip);
        Controls.Add(tools);

        _add.Click += async (_, _) => await AddAsync();
        _delete.Click += async (_, _) => await DeleteAsync();
        _refresh.Click += async (_, _) => await ReloadAsync();
        _all.Click += async (_, _) => await ReloadAsync();
        _list.KeyDown += async (_, e) => { if (e.KeyCode == Keys.Delete) await DeleteAsync(); };

        // Dragging image files onto the window (or the list) works like picking them in "Add photos…".
        AllowDrop = _list.AllowDrop = true;
        DragEnter += OnDragOver; DragOver += OnDragOver; DragDrop += OnDrop;
        _list.DragEnter += OnDragOver; _list.DragOver += OnDragOver; _list.DragDrop += OnDrop;
        Shown += async (_, _) => await ReloadAsync();
        FormClosing += (_, _) => _cts.Cancel();
    }

    /// <summary>Disables the buttons for the duration of an operation and shows errors.</summary>
    async Task Run(string text, Func<Task> work)
    {
        _busy = true;
        _add.Enabled = _delete.Enabled = _refresh.Enabled = _all.Enabled = false;
        _status.Text = text;
        try { await work(); }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested) { }
        catch (Exception e) { Ui.ShowError(this, e); _status.Text = Loc.Get("Main_Failed"); }
        finally
        {
            _busy = false;
            if (!IsDisposed) _add.Enabled = _delete.Enabled = _refresh.Enabled = _all.Enabled = true;
        }
    }

    async Task ReloadAsync()
    {
        if (_busy) return;
        await Run(Loc.Get("Gal_Loading"), async () =>
        {
            var all = await _art.ListAsync(_cts.Token);
            // The TV lists the same image under several categories (e.g. MY-C0002 and MY-C0008): keep one entry, "My photos" first.
            var unique = all.OrderBy(i => i.CategoryId == ArtClient.MyPhotosCategory ? 0 : 1).DistinctBy(i => i.ContentId).ToList();
            var shown = _all.Checked ? unique : unique.Where(i => i.CategoryId == ArtClient.MyPhotosCategory).ToList();

            _list.BeginUpdate();
            _list.Items.Clear();
            for (var i = _images.Images.Count - 1; i >= 1; i--) _images.Images.RemoveAt(i);   // only the placeholder stays
            foreach (var item in shown)
                _list.Items.Add(new ListViewItem(Label(item), "ph") { Tag = item, Name = item.ContentId });
            _list.EndUpdate();
            _status.Text = _all.Checked ? Loc.Get("Gal_Count", shown.Count) : Loc.Get("Gal_CountOf", shown.Count, unique.Count);

            await LoadThumbnailsAsync(shown.Select(i => i.ContentId).ToList());
        });
    }

    /// <summary>The TV's date can be truncated ("2026:11:13 10:07:1"), so only the minutes are used.</summary>
    static string Label(ArtItem i) =>
        i.ImageDate is { Length: >= 16 } s
        && DateTime.TryParseExact(s[..16], "yyyy:MM:dd HH:mm", null, System.Globalization.DateTimeStyles.None, out var d)
            ? $"{d:yyyy-MM-dd HH:mm}" : i.ContentId;

    async Task LoadThumbnailsAsync(IReadOnlyList<string> ids)
    {
        for (var start = 0; start < ids.Count; start += ThumbBatch)
        {
            _cts.Token.ThrowIfCancellationRequested();
            var batch = ids.Skip(start).Take(ThumbBatch).ToList();
            try
            {
                var thumbs = await _art.GetThumbnailsAsync(batch, _cts.Token);
                foreach (var (id, bytes) in thumbs)
                {
                    if (!_list.Items.ContainsKey(id)) continue;
                    try
                    {
                        using var ms = new MemoryStream(bytes);
                        using var src = Image.FromStream(ms);
                        using (var thumb = ImageIO.Letterbox(src, ThumbW, ThumbH)) _images.Images.Add(id, thumb);   // the ImageList keeps its own copy
                        if (_list.Items[id] is { } li) li.ImageKey = id;
                    }
                    catch (ArgumentException) { TvLog.Write($"[GALLERY] unreadable thumbnail {id}"); }
                }
                _status.Text = Loc.Get("Gal_Thumbs", Math.Min(start + ThumbBatch, ids.Count), ids.Count);
            }
            catch (TvException e)   // missing thumbnails do not block deleting or adding
            {
                TvLog.Write($"[GALLERY] thumbnails: {TvLog.Describe(e)}");
                _status.Text = Loc.Get("Gal_ThumbsFailed");
            }
        }
        if (!_cts.IsCancellationRequested) _status.Text = Loc.Get("Gal_Count", ids.Count);
    }

    List<ArtItem> SelectedItems() => _list.SelectedItems.Cast<ListViewItem>().Select(i => (ArtItem)i.Tag!).ToList();

    async Task ShowOnTvAsync()
    {
        if (_busy || SelectedItems() is not [var item]) return;
        await Run(Loc.Get("Gal_Showing"), async () =>
        {
            await _art.SelectImageAsync(item.ContentId, show: true, _cts.Token);
            _status.Text = Loc.Get("Gal_Shown", Label(item));
        });
    }

    async Task DeleteAsync()
    {
        if (_busy) return;
        var selected = SelectedItems();
        // Only the user's own photos are deleted; Art Store and factory Samsung images stay (they can only be restored on the TV).
        var own = selected.Where(i => i.CategoryId == ArtClient.MyPhotosCategory).Select(i => i.ContentId).ToList();
        if (own.Count == 0)
        {
            if (selected.Count > 0) MessageBox.Show(this, Loc.Get("Gal_OnlyOwn"), Ui.AppTitle);
            return;
        }

        var skipped = selected.Count - own.Count;
        var text = Loc.Get("Gal_DeleteConfirm", own.Count) + (skipped > 0 ? "\n\n" + Loc.Get("Gal_DeleteSkipped", skipped) : "");
        if (MessageBox.Show(this, text, Ui.AppTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;

        TvLog.Write($"[UI] deleting {own.Count} image(s)");
        await Run(Loc.Get("Gal_Deleting"), () => _art.DeleteAsync(own, _cts.Token));
        await ReloadAsync();   // always refresh, even after an error, so the list matches the TV
    }

    static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff"];

    static string[] DroppedImages(DragEventArgs e) =>
        e.Data?.GetData(DataFormats.FileDrop) is string[] files
            ? files.Where(f => ImageExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase)).ToArray()
            : [];

    void OnDragOver(object? sender, DragEventArgs e) =>
        e.Effect = !_busy && DroppedImages(e).Length > 0 ? DragDropEffects.Copy : DragDropEffects.None;

    async void OnDrop(object? sender, DragEventArgs e)
    {
        var files = DroppedImages(e);
        if (files.Length == 0 || _busy) return;
        TvLog.Write($"[UI] dropped {files.Length} file(s)");
        try
        {
            await Task.Yield();   // leave the drop handler so the crop dialog does not block Explorer
            await AddFilesAsync(files);
        }
        catch (Exception ex) { Ui.ShowError(this, ex); }
    }

    Task AddAsync()
    {
        if (_busy) return Task.CompletedTask;
        using var dlg = new OpenFileDialog
        {
            Title = Loc.Get("Gal_PickTitle"), Multiselect = true,
            Filter = $"{Loc.Get("Gal_FilterImages")}|{string.Join(';', ImageExtensions.Select(x => "*" + x))}|{Loc.Get("Gal_FilterAll")}|*.*",
        };
        return dlg.ShowDialog(this) == DialogResult.OK ? AddFilesAsync(dlg.FileNames) : Task.CompletedTask;
    }

    /// <summary>The path shared by "Add photos…" and drag &amp; drop: crop, upload, refresh.</summary>
    async Task AddFilesAsync(IEnumerable<string> paths)
    {
        if (_busy) return;
        var uploaded = 0;
        foreach (var path in paths)
        {
            byte[] jpeg;
            try
            {
                using var bmp = ImageIO.Load(path);
                using var crop = new CropForm(bmp, _tv.Width, _tv.Height, Path.GetFileName(path));
                if (crop.ShowDialog(this) != DialogResult.OK)   // "Skip"
                {
                    TvLog.Write($"[UI] skipped {Path.GetFileName(path)}");
                    continue;
                }
                jpeg = crop.Result;
                TvLog.Write($"[UI] crop of {Path.GetFileName(path)}: source {bmp.Width}x{bmp.Height}, result {_tv.Width}x{_tv.Height} JPEG {jpeg.Length} B");
            }
            catch (Exception e) when (e is ArgumentException or OutOfMemoryException or IOException or ExternalException)
            {
                TvLog.Write($"[UI] could not open {Path.GetFileName(path)} ({new FileInfo(path).Length} B): {e.GetType().Name}: {e.Message}");
                MessageBox.Show(this, Loc.Get("Gal_CannotOpen", Path.GetFileName(path), e.GetType().Name), Ui.AppTitle);
                continue;
            }

            var ok = false;
            await Run(Loc.Get("Gal_Uploading", Path.GetFileName(path), jpeg.Length / 1024), async () =>
            {
                await _art.UploadAsync(jpeg, _cts.Token);
                ok = true;
            });
            if (!ok) break;   // the error is already shown; do not continue with the remaining files
            uploaded++;
        }
        if (uploaded > 0) await ReloadAsync();
    }
}
