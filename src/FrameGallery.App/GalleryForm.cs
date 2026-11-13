using System.Runtime.InteropServices;
using FrameGallery.Core;

namespace FrameGallery.App;

sealed class GalleryForm : Form
{
    const int ThumbW = 240, ThumbH = 135, ThumbBatch = 12;

    readonly TvRecord _tv;
    readonly ArtClient _art;
    readonly CancellationTokenSource _cts = new();
    readonly ImageList _images = new() { ImageSize = new Size(ThumbW, ThumbH), ColorDepth = ColorDepth.Depth32Bit };
    readonly ListView _list = new() { Dock = DockStyle.Fill, View = View.LargeIcon, MultiSelect = true, HideSelection = false };
    readonly ToolStripButton _add = new("Dodaj zdjęcia…");
    readonly ToolStripButton _delete = new("Usuń zaznaczone");
    readonly ToolStripButton _refresh = new("Odśwież");
    readonly ToolStripButton _all = new("Pokaż wszystko")
    {
        CheckOnClick = true,
        ToolTipText = "Pokaż też obrazy Art Store i fabryczne Samsunga (domyślnie tylko Twoje zdjęcia). Tych obrazów nie można tu usuwać.",
    };
    readonly ContextMenuStrip _menu = new();
    readonly ToolStripMenuItem _miShow = new("Pokaż na TV");
    readonly ToolStripMenuItem _miDelete = new("Usuń");
    readonly ToolStripStatusLabel _status = new("");
    bool _busy;

    public GalleryForm(TvRecord tv, ArtClient art)
    {
        _tv = tv; _art = art;
        Text = $"Galeria: {tv.Name}";
        Size = new Size(1000, 680);
        StartPosition = FormStartPosition.CenterParent;

        using (var ph = new Bitmap(ThumbW, ThumbH)) using (var g = Graphics.FromImage(ph))
        {
            g.Clear(Color.FromArgb(55, 55, 55));
            _images.Images.Add("ph", (Bitmap)ph.Clone());
        }
        _list.LargeImageList = _images;

        // Menu pod prawym przyciskiem myszy. ListView sam zaznacza element pod kursorem przy kliknięciu.
        _menu.Items.AddRange([_miShow, _miDelete]);
        _list.ContextMenuStrip = _menu;
        _menu.Opening += (_, e) =>
        {
            var selected = SelectedItems();
            if (selected.Count == 0 || _busy) { e.Cancel = true; return; }
            var own = selected.Count(i => i.CategoryId == ArtClient.MyPhotosCategory);
            _miShow.Enabled = selected.Count == 1;
            _miDelete.Enabled = own > 0;
            _miDelete.Text = own > 1 ? $"Usuń ({own})" : "Usuń";
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

        // Przeciągnij-i-upuść plików obrazów na okno (także na listę) działa jak wybór w „Dodaj zdjęcia…”.
        AllowDrop = _list.AllowDrop = true;
        DragEnter += OnDragOver; DragOver += OnDragOver; DragDrop += OnDrop;
        _list.DragEnter += OnDragOver; _list.DragOver += OnDragOver; _list.DragDrop += OnDrop;
        Shown += async (_, _) => await ReloadAsync();
        FormClosing += (_, _) => _cts.Cancel();
    }

    /// <summary>Blokuje przyciski na czas operacji i pokazuje błędy.</summary>
    async Task Run(string text, Func<Task> work)
    {
        _busy = true;
        _add.Enabled = _delete.Enabled = _refresh.Enabled = _all.Enabled = false;
        _status.Text = text;
        try { await work(); }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested) { }
        catch (Exception e) { Ui.ShowError(this, e); _status.Text = "Operacja nie powiodła się."; }
        finally
        {
            _busy = false;
            if (!IsDisposed) _add.Enabled = _delete.Enabled = _refresh.Enabled = _all.Enabled = true;
        }
    }

    async Task ReloadAsync()
    {
        if (_busy) return;
        await Run("Wczytuję galerię z TV…", async () =>
        {
            var all = await _art.ListAsync(_cts.Token);
            // TV podaje ten sam obraz w kilku kategoriach (np. MY-C0002 i MY-C0008): zostaw jeden wpis, „Moje zdjęcia” mają pierwszeństwo.
            var unique = all.OrderBy(i => i.CategoryId == ArtClient.MyPhotosCategory ? 0 : 1).DistinctBy(i => i.ContentId).ToList();
            var shown = _all.Checked ? unique : unique.Where(i => i.CategoryId == ArtClient.MyPhotosCategory).ToList();

            _list.BeginUpdate();
            _list.Items.Clear();
            for (var i = _images.Images.Count - 1; i >= 1; i--) _images.Images.RemoveAt(i);   // zostaje tylko placeholder
            foreach (var item in shown)
                _list.Items.Add(new ListViewItem(Label(item), "ph") { Tag = item, Name = item.ContentId });
            _list.EndUpdate();
            _status.Text = $"Obrazów: {shown.Count}" + (_all.Checked ? "" : $" (z {unique.Count} na TV; „Pokaż wszystko” pokaże też Art Store)");

            await LoadThumbnailsAsync(shown.Select(i => i.ContentId).ToList());
        });
    }

    /// <summary>Data z TV bywa obcięta („2026:11:13 10:07:1”), więc bierzemy tylko do minut.</summary>
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
                        using (var thumb = ImageIO.Letterbox(src, ThumbW, ThumbH)) _images.Images.Add(id, thumb);   // ImageList trzyma kopię
                        if (_list.Items[id] is { } li) li.ImageKey = id;
                    }
                    catch (ArgumentException) { TvLog.Write($"[GALLERY] nieczytelna miniatura {id}"); }
                }
                _status.Text = $"Miniatury: {Math.Min(start + ThumbBatch, ids.Count)}/{ids.Count}";
            }
            catch (TvException e)   // brak miniatur nie blokuje usuwania i dodawania
            {
                TvLog.Write($"[GALLERY] miniatury: {e.Message}");
                _status.Text = "Nie udało się pobrać części miniatur.";
            }
        }
        if (!_cts.IsCancellationRequested) _status.Text = $"Obrazów: {ids.Count}";
    }

    List<ArtItem> SelectedItems() => _list.SelectedItems.Cast<ListViewItem>().Select(i => (ArtItem)i.Tag!).ToList();

    async Task ShowOnTvAsync()
    {
        if (_busy || SelectedItems() is not [var item]) return;
        await Run("Wyświetlam na TV…", async () =>
        {
            await _art.SelectImageAsync(item.ContentId, show: true, _cts.Token);
            _status.Text = $"Wyświetlono na TV: {Label(item)}";
        });
    }

    async Task DeleteAsync()
    {
        if (_busy) return;
        var selected = SelectedItems();
        // Usuwamy tylko Twoje zdjęcia; obrazy Art Store i fabryczne Samsunga zostają (można je odzyskać tylko przez TV).
        var own = selected.Where(i => i.CategoryId == ArtClient.MyPhotosCategory).Select(i => i.ContentId).ToList();
        if (own.Count == 0)
        {
            if (selected.Count > 0)
                MessageBox.Show(this, "Można usuwać tylko własne zdjęcia (kategoria „Moje zdjęcia”). Obrazy Art Store i fabryczne są chronione.", "Frame Gallery");
            return;
        }

        var skipped = selected.Count - own.Count;
        var text = $"Usunąć {own.Count} obraz(ów) z TV? Tej operacji nie można cofnąć."
                   + (skipped > 0 ? $"\n\nPominięto {skipped} (obrazy Art Store lub fabryczne nie są usuwane)." : "");
        if (MessageBox.Show(this, text, "Frame Gallery", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;

        TvLog.Write($"[UI] usuwanie {own.Count} obraz(ów)");
        await Run("Usuwam…", () => _art.DeleteAsync(own, _cts.Token));
        await ReloadAsync();   // zawsze odśwież, nawet po błędzie, żeby lista zgadzała się z TV
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
        TvLog.Write($"[UI] upuszczono {files.Length} plik(ów)");
        try
        {
            await Task.Yield();   // wyjdź z obsługi upuszczenia, żeby okno kadrowania nie blokowało Eksploratora
            await AddFilesAsync(files);
        }
        catch (Exception ex) { Ui.ShowError(this, ex); }
    }

    Task AddAsync()
    {
        if (_busy) return Task.CompletedTask;
        using var dlg = new OpenFileDialog
        {
            Title = "Wybierz zdjęcia", Multiselect = true,
            Filter = $"Obrazy|{string.Join(';', ImageExtensions.Select(x => "*" + x))}|Wszystkie pliki|*.*",
        };
        return dlg.ShowDialog(this) == DialogResult.OK ? AddFilesAsync(dlg.FileNames) : Task.CompletedTask;
    }

    /// <summary>Wspólna ścieżka dla „Dodaj zdjęcia…” i przeciągnij-i-upuść: kadrowanie, wysyłka, odświeżenie.</summary>
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
                if (crop.ShowDialog(this) != DialogResult.OK)   // „Pomiń”
                {
                    TvLog.Write($"[UI] pominięto {Path.GetFileName(path)}");
                    continue;
                }
                jpeg = crop.Result;
                TvLog.Write($"[UI] kadr {Path.GetFileName(path)}: źródło {bmp.Width}x{bmp.Height}, wynik {_tv.Width}x{_tv.Height} JPEG {jpeg.Length} B");
            }
            catch (Exception e) when (e is ArgumentException or OutOfMemoryException or IOException or ExternalException)
            {
                TvLog.Write($"[UI] nie otwarto {Path.GetFileName(path)} ({new FileInfo(path).Length} B): {e.GetType().Name}: {e.Message}");
                MessageBox.Show(this, $"Nie można otworzyć pliku „{Path.GetFileName(path)}” jako obrazu ({e.GetType().Name}).\nSzczegóły w logu.", "Frame Gallery");
                continue;
            }

            var ok = false;
            await Run($"Wysyłam {Path.GetFileName(path)} ({jpeg.Length / 1024} KB)…", async () =>
            {
                await _art.UploadAsync(jpeg, _cts.Token);
                ok = true;
            });
            if (!ok) break;   // błąd już pokazany; nie ciągnij dalej kolejnych plików
            uploaded++;
        }
        if (uploaded > 0) await ReloadAsync();
    }
}
