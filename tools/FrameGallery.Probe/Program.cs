// Narzędzie diagnostyczne (tylko odczyt): sprawdza wykrywanie i rozmowę z TV bez UI.
//   probe scan                 – szukanie TV (SSDP) i opis każdego
//   probe list [ip]            – parowanie (jeśli trzeba), lista obrazów i test miniatur
using FrameGallery.App;
using FrameGallery.Core;

namespace FrameGallery.App
{
    static class AppPaths
    {
        public static string Dir { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FrameGallery");
    }
}

static class Probe
{
    static async Task<int> Main(string[] args)
    {
        TvLog.Line += Console.WriteLine;
        TvLog.AttachFile(Path.Combine(AppContext.BaseDirectory, "logs", "log.txt"), "Probe " + string.Join(' ', args));   // jak w aplikacji: podfolder logs obok exe
        var store = new TvStore(Path.Combine(AppPaths.Dir, "tvs.json"), new DpapiSecretProtector());
        try
        {
            switch (args.FirstOrDefault())
            {
                case "scan": return await Scan(store);
                case "list": return await List(store, args.ElementAtOrDefault(1));
                case "roundtrip": return await RoundTrip(store);
                case "select": return await Select(store);
                case "prepare": return Prepare(args.ElementAtOrDefault(1) ?? throw new TvException("podaj ścieżkę pliku"));
                case "idle": return await Idle(store, int.Parse(args.ElementAtOrDefault(1) ?? "90"));
                default: Console.WriteLine("użycie: probe scan | list [ip] | roundtrip | prepare <plik> | idle [sekundy]"); return 1;
            }
        }
        catch (TvException e) { Console.WriteLine($"BŁĄD: {e.Message}"); return 2; }
    }

    static async Task<int> Scan(TvStore store)
    {
        var ips = await SsdpDiscovery.ScanAsync();
        Console.WriteLine($"SSDP: {ips.Count} urządzeń Samsung");
        foreach (var ip in ips)
        {
            try
            {
                var tls = new TlsPolicy(null);
                var info = await TvInfoClient.GetAsync(ip.ToString(), tls);
                store.Upsert(info, tls.Observed);
                Console.WriteLine($"  {info.Ip}  {info.Name}  model={info.ModelName}  frame={info.IsFrame}  {info.Width}x{info.Height}  id={info.Id}  cert={(tls.Observed is null ? "(http)" : tls.Observed[..12] + "…")}");
            }
            catch (TvException e) { Console.WriteLine($"  {ip}: {e.Message}"); }
        }
        store.Save();
        return 0;
    }

    /// <summary>
    /// ZMIENIA galerię TV, ale tylko o jeden własny obraz: wysyła wygenerowany obraz testowy, sprawdza, że jest na liście,
    /// i usuwa WYŁĄCZNIE ten obraz (po content_id zwróconym przez TV). Odmawia, jeśli id pokrywa się z istniejącym obrazem.
    /// </summary>
    static async Task<int> RoundTrip(TvStore store)
    {
        var tv = store.Items.FirstOrDefault(t => t.IsFrame) ?? throw new TvException("Brak TV w magazynie. Najpierw: probe scan");
        if (store.GetToken(tv) is null) throw new TvException("TV nie jest sparowany. Najpierw: probe list");
        var tls = new TlsPolicy(tv.CertSha256);
        await using var art = await ArtClient.ConnectAsync(tv.Ip, store.GetToken(tv), tls, default);

        var before = (await art.ListAsync(default)).Select(i => i.ContentId).ToHashSet();
        var jpeg = MakeTestImage(tv.Width, tv.Height);
        Console.WriteLine($"Przed: {before.Count} wpisów. Wysyłam obraz testowy {tv.Width}x{tv.Height}, {jpeg.Length / 1024} KB…");

        var id = await art.UploadAsync(jpeg, default);
        Console.WriteLine($"Wysłano, content_id={id}");
        if (before.Contains(id))
        {
            Console.WriteLine("TV zwróciło id istniejącego obrazu – przerywam i NIC nie usuwam.");
            return 3;
        }

        var mid = await art.ListAsync(default);
        Console.WriteLine($"Po uploadzie: {mid.Count} wpisów, nowy obraz na liście: {mid.Any(i => i.ContentId == id)}");

        await art.DeleteAsync([id], default);
        var after = await art.ListAsync(default);
        Console.WriteLine($"Po usunięciu: {after.Count} wpisów, obraz testowy usunięty: {after.All(i => i.ContentId != id)}, stan jak na początku: {after.Select(i => i.ContentId).ToHashSet().SetEquals(before)}");
        return 0;
    }

    /// <summary>
    /// ZMIENIA to, co TV wyświetla: pokazuje jedno z Twoich zdjęć (select_image), potwierdza przez get_current_artwork
    /// i przywraca poprzednio wyświetlany obraz, jeśli udało się go odczytać.
    /// </summary>
    static async Task<int> Select(TvStore store)
    {
        var tv = store.Items.FirstOrDefault(t => t.IsFrame && t.IsPaired) ?? throw new TvException("Brak sparowanego TV. Najpierw: probe list");
        var tls = new TlsPolicy(tv.CertSha256);
        await using var art = await ArtClient.ConnectAsync(tv.Ip, store.GetToken(tv), tls, default);

        var mine = (await art.ListAsync(default)).Where(i => i.CategoryId == ArtClient.MyPhotosCategory).ToList();
        if (mine.Count == 0) throw new TvException("Brak własnych zdjęć na TV do testu.");

        string? previous = null;
        try { previous = await art.GetCurrentArtworkAsync(default); }
        catch (TvException e) { Console.WriteLine($"Nie odczytano bieżącego obrazu ({e.Message}); po teście nie będzie przywracania."); }
        Console.WriteLine($"Aktualnie wyświetlany: {previous ?? "(nieznany)"}");

        var target = mine.FirstOrDefault(i => i.ContentId != previous) ?? mine[0];
        Console.WriteLine($"Pokazuję {target.ContentId}…");
        await art.SelectImageAsync(target.ContentId, show: true, default);
        Console.WriteLine("TV potwierdziło select_image.");

        await Task.Delay(3000);
        string? now = null;
        try { now = await art.GetCurrentArtworkAsync(default); } catch (TvException e) { Console.WriteLine($"Odczyt po zmianie nie powiódł się: {e.Message}"); }
        Console.WriteLine($"Po zmianie wyświetlany: {now ?? "(nieznany)"}  (zgodny z celem: {now == target.ContentId})");

        if (previous is not null && previous != target.ContentId)
        {
            await art.SelectImageAsync(previous, show: true, default);
            Console.WriteLine($"Przywrócono poprzedni obraz: {previous}");
        }
        return 0;
    }

    /// <summary>Offline: ten sam potok co przy dodawaniu zdjęcia w aplikacji (wczytanie, domyślny kadr 16:9, JPEG). Bez TV.</summary>
    static int Prepare(string path)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var bmp = ImageIO.Load(path);
        Console.WriteLine($"Wczytano {bmp.Width}x{bmp.Height} w {sw.ElapsedMilliseconds} ms");
        var crop = CropMath.Initial(bmp.Width, bmp.Height, 3840.0 / 2160);
        sw.Restart();
        var jpeg = ImageIO.CropToJpeg(bmp, crop, 3840, 2160);
        Console.WriteLine($"Kadr {crop.W:0}x{crop.H:0} → JPEG {jpeg.Length:N0} B ({jpeg.Length / 1024} KB) w {sw.ElapsedMilliseconds} ms");
        using var back = System.Drawing.Image.FromStream(new MemoryStream(jpeg));
        Console.WriteLine($"Kontrola: JPEG={(jpeg[0] == 0xFF && jpeg[1] == 0xD8)}, odczyt {back.Width}x{back.Height}, rama uploadu {ArtFrameSize(jpeg)} B");

        // to samo przez wieloplatformowy potok (SkiaSharp)
        sw.Restart();
        using var skia = FrameGallery.Imaging.ImagePipeline.Load(path);
        var skiaJpeg = FrameGallery.Imaging.ImagePipeline.CropToJpeg(skia, CropMath.Initial(skia.Width, skia.Height, 3840.0 / 2160), 3840, 2160);
        using var skiaBack = SkiaSharp.SKBitmap.Decode(skiaJpeg);
        Console.WriteLine($"Skia: {skia.Width}x{skia.Height} → JPEG {skiaJpeg.Length:N0} B ({skiaJpeg.Length / 1024} KB), odczyt {skiaBack.Width}x{skiaBack.Height}, razem {sw.ElapsedMilliseconds} ms");
        return 0;
    }

    static int ArtFrameSize(byte[] jpeg) => jpeg.Length + 4 + 160;   // przybliżenie: długość + nagłówek

    /// <summary>Tylko odczyt: czy TV zrywa kanał Art po bezczynności (tyle ile trwa kadrowanie w GUI)?</summary>
    static async Task<int> Idle(TvStore store, int seconds)
    {
        var tv = store.Items.FirstOrDefault(t => t.IsFrame && t.IsPaired) ?? throw new TvException("Brak sparowanego TV. Najpierw: probe list");
        var tls = new TlsPolicy(tv.CertSha256);
        await using var art = await ArtClient.ConnectAsync(tv.Ip, store.GetToken(tv), tls, default);
        Console.WriteLine($"Lista przed bezczynnością: {(await art.ListAsync(default)).Count} wpisów. Czekam {seconds} s bez ruchu…");
        await Task.Delay(TimeSpan.FromSeconds(seconds));
        try { Console.WriteLine($"Lista po bezczynności: OK, {(await art.ListAsync(default)).Count} wpisów"); return 0; }
        catch (Exception e) { TvLog.Write($"[ERR] po bezczynności: {e.GetType().Name}: {e.Message}"); Console.WriteLine($"PO BEZCZYNNOŚCI BŁĄD: {e.GetType().Name}: {e.Message}"); return 4; }
    }

    static byte[] MakeTestImage(int w, int h)
    {
        using var bmp = new System.Drawing.Bitmap(w, h);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
            using var brush = new System.Drawing.Drawing2D.LinearGradientBrush(new System.Drawing.Rectangle(0, 0, w, h),
                System.Drawing.Color.FromArgb(30, 60, 120), System.Drawing.Color.FromArgb(240, 140, 60), 45f);
            g.FillRectangle(brush, 0, 0, w, h);
            using var font = new System.Drawing.Font("Segoe UI", h / 14f, System.Drawing.FontStyle.Bold);
            g.DrawString($"FrameGallery test\n{DateTime.Now:yyyy-MM-dd HH:mm}", font, System.Drawing.Brushes.White, w * 0.06f, h * 0.3f);
        }
        using var ms = new MemoryStream();
        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Jpeg);
        return ms.ToArray();
    }

    static async Task<int> List(TvStore store, string? ip)
    {
        var tv = store.Items.FirstOrDefault(t => ip is null ? t.IsFrame : t.Ip == ip)
                 ?? throw new TvException("Brak TV w magazynie. Najpierw: probe scan");
        var tls = new TlsPolicy(tv.CertSha256);
        if (store.GetToken(tv) is null)
        {
            Console.WriteLine(">>> ZATWIERDŹ POŁĄCZENIE NA TV pilotem („Zezwól”) – masz 60 s <<<");
            await using var pair = await TvChannel.ConnectAsync(tv.Ip, TvChannel.RemoteChannel, null, tls, false, TimeSpan.FromSeconds(60), default);
            store.SetToken(tv, pair.Token);
            Console.WriteLine($"sparowano, token {(store.GetToken(tv) is null ? "NIE nadesłany" : "otrzymany (zapisany szyfrowany DPAPI)")}");
        }
        tv.CertSha256 ??= tls.Observed;
        store.Save();

        await using var art = await ArtClient.ConnectAsync(tv.Ip, store.GetToken(tv), tls, default);
        var items = await art.ListAsync(default);
        Console.WriteLine($"Obrazów na TV: {items.Count}");
        foreach (var g in items.GroupBy(i => i.CategoryId ?? "(brak)"))
            Console.WriteLine($"  kategoria {g.Key}: {g.Count()}");
        foreach (var i in items.Take(8))
            Console.WriteLine($"  {i.ContentId}  {i.CategoryId}  matte={i.MatteId}  {i.Width}x{i.Height}  {i.ImageDate}");

        var mine = items.Where(i => i.CategoryId == ArtClient.MyPhotosCategory).Take(3).Select(i => i.ContentId).ToList();
        var sample = mine.Count > 0 ? mine : items.Take(3).Select(i => i.ContentId).ToList();
        if (sample.Count > 0)
        {
            var thumbs = await art.GetThumbnailsAsync(sample, default);
            foreach (var (id, bytes) in thumbs)
                Console.WriteLine($"  miniatura {id}: {bytes.Length} B, JPEG={(bytes.Length > 2 && bytes[0] == 0xFF && bytes[1] == 0xD8)}");
            Console.WriteLine($"miniatur odebrano: {thumbs.Count}/{sample.Count}");
        }
        return 0;
    }
}
