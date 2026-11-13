using System.Diagnostics;
using FrameGallery.Core;

namespace FrameGallery.App;

static class Ui
{
    /// <summary>Komunikat dla użytkownika; szczegóły techniczne trafiają do logu.</summary>
    public static void ShowError(IWin32Window owner, Exception e)
    {
        TvLog.Write($"[ERR] {e.GetType().Name}: {e.Message}");
        MessageBox.Show(owner, e is TvException ? e.Message : $"Nieoczekiwany błąd: {e.Message}\nSzczegóły w logu.",
            "Frame Gallery", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    public static string? Ask(IWin32Window owner, string title, string prompt)
    {
        using var f = new Form { Text = title, FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent,
            MaximizeBox = false, MinimizeBox = false, ClientSize = new Size(340, 110) };
        var box = new TextBox { Left = 12, Top = 34, Width = 316 };
        var ok = new Button { Text = "OK", Left = 172, Top = 70, Width = 75, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Anuluj", Left = 253, Top = 70, Width = 75, DialogResult = DialogResult.Cancel };
        f.Controls.AddRange([new Label { Text = prompt, Left = 12, Top = 12, AutoSize = true }, box, ok, cancel]);
        f.AcceptButton = ok; f.CancelButton = cancel;
        return f.ShowDialog(owner) == DialogResult.OK ? box.Text.Trim() : null;
    }
}

sealed class MainForm : Form
{
    readonly TvStore _store = new(Path.Combine(AppPaths.Dir, "tvs.json"), new DpapiSecretProtector());
    readonly ListView _list = new()
    {
        Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false, GridLines = true,
    };
    readonly ToolStripButton _scan = new("Szukaj TV") { ToolTipText = "Wyszukaj telewizory Samsung w sieci lokalnej (SSDP)" };
    readonly ToolStripButton _addIp = new("Dodaj po IP…");
    readonly ToolStripButton _remove = new("Usuń z listy");
    readonly ToolStripButton _log = new("Pokaż log") { Alignment = ToolStripItemAlignment.Right };
    readonly ToolStripStatusLabel _status = new("Dwukrotnie kliknij TV, aby otworzyć galerię.");
    bool _busy;

    public MainForm()
    {
        Text = "Frame Gallery";
        Size = new Size(820, 440);
        StartPosition = FormStartPosition.CenterScreen;

        _list.Columns.Add("Nazwa", 280);
        _list.Columns.Add("Model", 150);
        _list.Columns.Add("Adres IP", 120);
        _list.Columns.Add("Typ", 130);
        _list.Columns.Add("Parowanie", 90);

        var tools = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden };
        tools.Items.AddRange([_scan, _addIp, _remove, _log]);
        var statusStrip = new StatusStrip();
        statusStrip.Items.Add(_status);

        Controls.Add(_list);
        Controls.Add(statusStrip);
        Controls.Add(tools);

        _scan.Click += async (_, _) => await Busy("Szukam TV w sieci (ok. 5 s)…", ScanAsync);
        _addIp.Click += async (_, _) => await AddByIpAsync();
        _remove.Click += (_, _) => RemoveSelected();
        _log.Click += (_, _) => OpenLog();
        _list.DoubleClick += async (_, _) => await OpenSelectedAsync();
        _list.KeyDown += (_, e) => { if (e.KeyCode == Keys.Delete) RemoveSelected(); };

        RefreshList();
    }

    TvRecord? Selected => _list.SelectedItems.Count == 1 ? (TvRecord?)_list.SelectedItems[0].Tag : null;

    void RefreshList()
    {
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var tv in _store.Items)
        {
            var item = new ListViewItem([tv.Name, tv.Model, tv.Ip, tv.IsFrame ? "The Frame" : "Samsung TV (bez Art Mode)",
                tv.IsPaired ? "tak" : "nie"]) { Tag = tv };
            if (!tv.IsFrame) item.ForeColor = SystemColors.GrayText;
            _list.Items.Add(item);
        }
        _list.EndUpdate();
    }

    /// <summary>Blokuje UI na czas operacji sieciowej i pokazuje błędy użytkownikowi.</summary>
    async Task Busy(string text, Func<CancellationToken, Task> work)
    {
        if (_busy) return;
        _busy = true;
        UseWaitCursor = true; _scan.Enabled = _addIp.Enabled = _remove.Enabled = _list.Enabled = false;
        _status.Text = text;
        try { await work(CancellationToken.None); }
        catch (Exception e) { Ui.ShowError(this, e); _status.Text = "Operacja nie powiodła się."; }
        finally
        {
            UseWaitCursor = false; _scan.Enabled = _addIp.Enabled = _remove.Enabled = _list.Enabled = true;
            _busy = false;
        }
    }

    /// <summary>Pobiera dane TV pod adresem i zapisuje je na liście.</summary>
    async Task<TvRecord> ProbeAndStoreAsync(string ip, CancellationToken ct)
    {
        var tls = new TlsPolicy(null);
        var info = await TvInfoClient.GetAsync(ip, tls, ct);
        var rec = _store.Upsert(info, tls.Observed);
        _store.Save();
        return rec;
    }

    async Task ScanAsync(CancellationToken ct)
    {
        var ips = await SsdpDiscovery.ScanAsync(ct: ct);
        var ok = 0;
        foreach (var ip in ips)
        {
            try { await ProbeAndStoreAsync(ip.ToString(), ct); ok++; }
            catch (TvException e) { TvLog.Write($"[SCAN] {ip}: {e.Message}"); }
        }
        RefreshList();
        _status.Text = ips.Count == 0
            ? "Nie znaleziono TV. Sprawdź, czy TV jest włączony i w tej samej sieci, albo dodaj go po IP."
            : $"Znaleziono i dodano: {ok} z {ips.Count} urządzeń.";
    }

    async Task AddByIpAsync()
    {
        var text = Ui.Ask(this, "Dodaj TV", "Adres IP telewizora (np. 192.168.1.50):");
        if (string.IsNullOrEmpty(text)) return;
        await Busy($"Łączę z {text}…", async ct =>
        {
            var ip = Lan.RequirePrivateIPv4(text).ToString();
            var tv = await ProbeAndStoreAsync(ip, ct);
            RefreshList();
            _status.Text = tv.IsFrame ? $"Dodano: {tv.Name}" : $"Dodano {tv.Name}, ale to urządzenie nie zgłasza Art Mode (The Frame).";
        });
    }

    void RemoveSelected()
    {
        if (Selected is not { } tv) return;
        if (MessageBox.Show(this, $"Usunąć „{tv.Name}” z listy? Zapamiętane sparowanie zostanie skasowane.", "Frame Gallery",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        _store.Remove(tv); _store.Save(); RefreshList();
    }

    async Task OpenSelectedAsync()
    {
        if (Selected is not { } tv) return;
        if (!tv.IsFrame)
        {
            MessageBox.Show(this, "To urządzenie nie zgłasza obsługi Art Mode, więc galeria jest niedostępna.", "Frame Gallery");
            return;
        }

        ArtClient? art = null;
        await Busy($"Łączę z {tv.Name}…", async ct =>
        {
            var tls = new TlsPolicy(tv.CertSha256);
            if (_store.GetToken(tv) is null)
            {
                _status.Text = "Zatwierdź połączenie na TV pilotem (wybierz „Zezwól”)…";
                await using var pair = await TvChannel.ConnectAsync(tv.Ip, TvChannel.RemoteChannel, null, tls,
                    waitReady: false, TimeSpan.FromSeconds(60), ct);
                _store.SetToken(tv, pair.Token);
            }
            tv.CertSha256 ??= tls.Observed;
            _store.Save();
            art = await ArtClient.ConnectAsync(tv.Ip, _store.GetToken(tv), tls, ct);
        });
        RefreshList();
        if (art is null) return;

        await using (art)
        {
            using var gallery = new GalleryForm(tv, art);
            gallery.ShowDialog(this);
        }
        _status.Text = "Dwukrotnie kliknij TV, aby otworzyć galerię.";
    }

    static void OpenLog()
    {
        try { if (!File.Exists(AppPaths.Log)) File.WriteAllText(AppPaths.Log, ""); Process.Start(new ProcessStartInfo(AppPaths.Log) { UseShellExecute = true }); }
        catch (Exception e) { TvLog.Write($"[ERR] otwarcie logu: {e.Message}"); }
    }
}
