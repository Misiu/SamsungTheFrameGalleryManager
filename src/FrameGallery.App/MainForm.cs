using System.Diagnostics;
using System.Reflection;
using FrameGallery.Core;
using FrameGallery.Localization;

namespace FrameGallery.App;

static class Ui
{
    /// <summary>The product name; shown in message box titles. It is a brand, so it is not translated.</summary>
    public const string AppTitle = "Frame Gallery";

    /// <summary>
    /// The application icon, read from the exe's own resources: it is embedded once, by ApplicationIcon in the project file,
    /// and not a second time as a resource of the forms. Null keeps the default window icon.
    /// </summary>
    public static Icon? AppIcon { get; } = LoadAppIcon();

    static Icon? LoadAppIcon()
    {
        try
        {
            // The apphost (or the bootstrapper that replaces it) carries the icon. When the app is started as
            // "dotnet FrameGallery.App.dll" the process is dotnet.exe, so prefer our own exe next to the assembly.
            var exe = Path.Combine(AppContext.BaseDirectory, Assembly.GetEntryAssembly()!.GetName().Name + ".exe");
            return Icon.ExtractAssociatedIcon(File.Exists(exe) ? exe : Environment.ProcessPath!);
        }
        catch (Exception e)   // purely cosmetic, so it must never stop the app
        {
            TvLog.Write($"[UI] could not read the application icon: {e.Message}");
            return null;
        }
    }

    /// <summary>Shows an error to the user; technical details (always in English) go to the log.</summary>
    public static void ShowError(IWin32Window owner, Exception e)
    {
        TvLog.Write($"[ERR] {TvLog.Describe(e)}");
        MessageBox.Show(owner, e is TvException ? e.Message : Loc.Get("Ui_UnexpectedError", e.Message),
            AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    public static string? Ask(IWin32Window owner, string title, string prompt)
    {
        using var f = new Form { Text = title, FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent,
            MaximizeBox = false, MinimizeBox = false, ClientSize = new Size(340, 110), Icon = AppIcon };
        var box = new TextBox { Left = 12, Top = 34, Width = 316 };
        var ok = new Button { Text = "OK", Left = 172, Top = 70, Width = 75, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = Loc.Get("Ui_Cancel"), Left = 253, Top = 70, Width = 75, DialogResult = DialogResult.Cancel };
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
    readonly ToolStripButton _scan = new(Loc.Get("Main_Scan")) { ToolTipText = Loc.Get("Main_ScanTip") };
    readonly ToolStripButton _addIp = new(Loc.Get("Main_AddIp"));
    readonly ToolStripButton _remove = new(Loc.Get("Main_Remove"));
    readonly ToolStripButton _log = new(Loc.Get("Main_ShowLog")) { Alignment = ToolStripItemAlignment.Right };
    readonly ToolStripStatusLabel _status = new(Loc.Get("Main_Hint"));
    bool _busy;

    public MainForm()
    {
        Text = Ui.AppTitle;
        Icon = Ui.AppIcon;
        Size = new Size(820, 440);
        StartPosition = FormStartPosition.CenterScreen;

        _list.Columns.Add(Loc.Get("Main_ColName"), 280);
        _list.Columns.Add(Loc.Get("Main_ColModel"), 150);
        _list.Columns.Add(Loc.Get("Main_ColIp"), 120);
        _list.Columns.Add(Loc.Get("Main_ColType"), 130);
        _list.Columns.Add(Loc.Get("Main_ColPaired"), 90);

        var tools = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden };
        tools.Items.AddRange([_scan, _addIp, _remove, _log]);
        var statusStrip = new StatusStrip();
        statusStrip.Items.Add(_status);

        // Dock order matters: the fill control is added first, the edge-docked ones after it.
        Controls.Add(_list);
        Controls.Add(statusStrip);
        Controls.Add(tools);

        _scan.Click += async (_, _) => await Busy(Loc.Get("Main_ScanBusy"), ScanAsync);
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
            var item = new ListViewItem([tv.Name, tv.Model, tv.Ip,
                tv.IsFrame ? Loc.Get("Main_TypeFrame") : Loc.Get("Main_TypeOther"),
                tv.IsPaired ? Loc.Get("Main_Yes") : Loc.Get("Main_No")]) { Tag = tv };
            if (!tv.IsFrame) item.ForeColor = SystemColors.GrayText;
            _list.Items.Add(item);
        }
        _list.EndUpdate();
    }

    /// <summary>Locks the UI for the duration of a network operation and shows errors to the user.</summary>
    async Task Busy(string text, Func<CancellationToken, Task> work)
    {
        if (_busy) return;
        _busy = true;
        UseWaitCursor = true; _scan.Enabled = _addIp.Enabled = _remove.Enabled = _list.Enabled = false;
        _status.Text = text;
        try { await work(CancellationToken.None); }
        catch (Exception e) { Ui.ShowError(this, e); _status.Text = Loc.Get("Main_Failed"); }
        finally
        {
            UseWaitCursor = false; _scan.Enabled = _addIp.Enabled = _remove.Enabled = _list.Enabled = true;
            _busy = false;
        }
    }

    /// <summary>Fetches the TV's description from an address and saves it to the list.</summary>
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
            catch (TvException e) { TvLog.Write($"[SCAN] {ip}: {TvLog.Describe(e)}"); }
        }
        RefreshList();
        _status.Text = ips.Count == 0 ? Loc.Get("Main_ScanNone") : Loc.Get("Main_ScanDone", ok, ips.Count);
    }

    async Task AddByIpAsync()
    {
        var text = Ui.Ask(this, Loc.Get("Main_AddTitle"), Loc.Get("Main_AddPrompt"));
        if (string.IsNullOrEmpty(text)) return;
        await Busy(Loc.Get("Main_Connecting", text), async ct =>
        {
            var ip = Lan.RequirePrivateIPv4(text).ToString();
            var tv = await ProbeAndStoreAsync(ip, ct);
            RefreshList();
            _status.Text = tv.IsFrame ? Loc.Get("Main_Added", tv.Name) : Loc.Get("Main_AddedNoArt", tv.Name);
        });
    }

    void RemoveSelected()
    {
        if (Selected is not { } tv) return;
        if (MessageBox.Show(this, Loc.Get("Main_RemoveConfirm", tv.Name), Ui.AppTitle,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        _store.Remove(tv); _store.Save(); RefreshList();
    }

    async Task OpenSelectedAsync()
    {
        if (Selected is not { } tv) return;
        if (!tv.IsFrame)
        {
            MessageBox.Show(this, Loc.Get("Main_NoArt"), Ui.AppTitle);
            return;
        }

        ArtClient? art = null;
        await Busy(Loc.Get("Main_Connecting", tv.Name), async ct =>
        {
            var tls = new TlsPolicy(tv.CertSha256);
            if (_store.GetToken(tv) is null)
            {
                _status.Text = Loc.Get("Main_PairPrompt");
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
        _status.Text = Loc.Get("Main_Hint");
    }

    static void OpenLog()
    {
        try { if (!File.Exists(AppPaths.Log)) File.WriteAllText(AppPaths.Log, ""); Process.Start(new ProcessStartInfo(AppPaths.Log) { UseShellExecute = true }); }
        catch (Exception e) { TvLog.Write($"[ERR] could not open the log: {e.Message}"); }
    }
}
