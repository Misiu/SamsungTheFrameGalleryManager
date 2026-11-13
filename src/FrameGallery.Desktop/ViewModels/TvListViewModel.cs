using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FrameGallery.Core;
using FrameGallery.Localization;

namespace FrameGallery.Desktop.ViewModels;

/// <summary>The first screen, the list of TVs: find them, add one by IP, remove one, open its gallery.</summary>
public sealed partial class TvListViewModel : ScreenViewModel
{
    readonly TvStore _store;
    readonly IInteraction _ui;
    readonly bool _demo;

    public override bool HasBack => false;

    public ObservableCollection<TvItemViewModel> Tvs { get; } = [];

    /// <summary>A network operation is running; the window shows a spinner over the list.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsEmpty))] bool _isBusy;
    [ObservableProperty] string _busyText = "";
    [ObservableProperty] string _status = Loc.Get("Main_HintOpen");

    /// <summary>No TVs yet and nothing is running: the window shows a hint instead of an empty list.</summary>
    public bool IsEmpty => Tvs.Count == 0 && !IsBusy;

    /// <param name="demo">Demo mode: no network, a fake TV and a fake gallery (see <see cref="Demo.DemoArtClient"/>).</param>
    public TvListViewModel(TvStore store, IInteraction ui, bool demo = false)
    {
        _store = store;
        _ui = ui;
        _demo = demo;
        Tvs.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));
        Refresh();
    }

    void Refresh()
    {
        Tvs.Clear();
        foreach (var tv in _store.Items) Tvs.Add(new TvItemViewModel(tv));
    }

    /// <summary>Runs a network operation behind the spinner and shows its error, if any, to the user.</summary>
    async Task RunBusyAsync(string text, Func<Task> work)
    {
        if (IsBusy) return;
        IsBusy = true;
        BusyText = text;
        try { await work(); }
        catch (Exception e)
        {
            TvLog.Write($"[ERR] {TvLog.Describe(e)}");
            Status = Loc.Get("Main_Failed");
            await _ui.MessageAsync(e is TvException ? e.Message : Loc.Get("Ui_UnexpectedError", e.Message));
        }
        finally { IsBusy = false; }
    }

    /// <summary>Fetches the TV's description from an address and saves it to the list.</summary>
    async Task<TvRecord> ProbeAndStoreAsync(string ip, CancellationToken ct)
    {
        var tls = new TlsPolicy(null);
        var info = await TvInfoClient.GetAsync(ip, tls, ct);
        var record = _store.Upsert(info, tls.Observed);
        _store.Save();
        return record;
    }

    [RelayCommand]
    Task ScanAsync() => RunBusyAsync(Loc.Get("Main_Scanning"), async () =>
    {
        if (_demo)
        {
            await Task.Delay(3500);   // long enough to admire the spinner
            _store.Upsert(new TvInfo("demo-2", "55\" The Frame (demo)", "QE55LS03D", "192.168.1.98", true, 3840, 2160), null);
            Refresh();
            Status = Loc.Get("Main_ScanDone", 2, 2);
            return;
        }
        var ips = await SsdpDiscovery.ScanAsync();
        var ok = 0;
        foreach (var ip in ips)
        {
            try { await ProbeAndStoreAsync(ip.ToString(), CancellationToken.None); ok++; }
            catch (TvException e) { TvLog.Write($"[SCAN] {ip}: {TvLog.Describe(e)}"); }
        }
        Refresh();
        Status = ips.Count == 0 ? Loc.Get("Main_ScanNone") : Loc.Get("Main_ScanDone", ok, ips.Count);
    });

    [RelayCommand]
    async Task AddByIpAsync()
    {
        var text = await _ui.AskAsync(Loc.Get("Main_AddTitle"), Loc.Get("Main_AddPrompt"));
        if (string.IsNullOrWhiteSpace(text)) return;

        await RunBusyAsync(Loc.Get("Main_Connecting", text), async () =>
        {
            var ip = Lan.RequirePrivateIPv4(text).ToString();
            var tv = await ProbeAndStoreAsync(ip, CancellationToken.None);
            Refresh();
            Status = tv.IsFrame ? Loc.Get("Main_Added", tv.Name) : Loc.Get("Main_AddedNoArt", tv.Name);
        });
    }

    [RelayCommand]
    async Task RemoveAsync(TvItemViewModel? item)
    {
        if (item is null || !await _ui.ConfirmAsync(Loc.Get("Main_RemoveConfirm", item.Name))) return;
        _store.Remove(item.Record);
        _store.Save();
        Refresh();
    }

    [RelayCommand]
    async Task OpenAsync(TvItemViewModel? item)
    {
        if (item is null) return;
        var tv = item.Record;
        if (!tv.IsFrame)
        {
            await _ui.MessageAsync(Loc.Get("Main_NoArt"));
            return;
        }

        IArtClient? art = null;
        await RunBusyAsync(Loc.Get("Main_Connecting", tv.Name), async () =>
        {
            if (_demo)
            {
                await Task.Delay(2200);
                art = new Demo.DemoArtClient();
                return;
            }
            var tls = new TlsPolicy(tv.CertSha256);
            if (_store.GetToken(tv) is null)
            {
                BusyText = Loc.Get("Main_PairPrompt");   // the TV is waiting for the user to press "Allow"
                await using var pair = await TvChannel.ConnectAsync(tv.Ip, TvChannel.RemoteChannel, null, tls,
                    waitReady: false, TimeSpan.FromSeconds(60), CancellationToken.None);
                _store.SetToken(tv, pair.Token);
            }
            tv.CertSha256 ??= tls.Observed;
            _store.Save();
            art = await ArtClient.ConnectAsync(tv.Ip, _store.GetToken(tv), tls, CancellationToken.None);
        });
        Refresh();   // the TV may be paired now
        if (art is null) return;

        await using (art) await _ui.ShowGalleryAsync(tv, art);
        Status = Loc.Get("Main_HintOpen");
    }

    [RelayCommand]
    void ShowLog() => _ui.OpenLog();
}
