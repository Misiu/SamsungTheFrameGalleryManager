// Diagnostic tool: checks discovery and the conversation with the TV without any UI.
//   probe scan              - find TVs (SSDP) and describe each one (reads from the TV only)
//   probe list [ip]         - pair if needed, list the images and test thumbnails (reads from the TV only)
//   probe idle [seconds]    - check that the connection survives inactivity (reads from the TV only)
//   probe prepare <file>    - run a photo through the image pipeline offline (no TV involved)
//   probe roundtrip         - CHANGES the TV: uploads a generated test image, then deletes only that image
//   probe select            - CHANGES what the TV displays: shows one of your photos, then restores the previous one
using FrameGallery.Core;
using FrameGallery.Desktop;   // AppPaths and SecretProtectors, compiled into this tool from the app

static class Probe
{
    static async Task<int> Main(string[] args)
    {
        TvLog.Line += Console.WriteLine;
        TvLog.AttachFile(Path.Combine(AppContext.BaseDirectory, "logs", "log.txt"), "Probe " + string.Join(' ', args));   // like the app: a "logs" folder next to the exe
        var store = new TvStore(Path.Combine(AppPaths.Dir, "tvs.json"), SecretProtectors.Create());
        try
        {
            switch (args.FirstOrDefault())
            {
                case "scan": return await Scan(store);
                case "list": return await List(store, args.ElementAtOrDefault(1));
                case "roundtrip": return await RoundTrip(store);
                case "select": return await Select(store);
                case "prepare": return Prepare(args.ElementAtOrDefault(1) ?? throw new InvalidOperationException("Give the path of a photo."));
                case "idle": return await Idle(store, int.Parse(args.ElementAtOrDefault(1) ?? "90"));
                default: Console.WriteLine("usage: probe scan | list [ip] | idle [seconds] | prepare <file> | roundtrip | select"); return 1;
            }
        }
        catch (Exception e) when (e is TvException or InvalidOperationException)
        {
            Console.WriteLine($"ERROR: {Describe(e)}");
            return 2;
        }
    }

    static string Describe(Exception e) => e is TvException t ? t.EnglishMessage : e.Message;

    static async Task<int> Scan(TvStore store)
    {
        var ips = await SsdpDiscovery.ScanAsync();
        Console.WriteLine($"SSDP: {ips.Count} Samsung device(s)");
        foreach (var ip in ips)
        {
            try
            {
                var tls = new TlsPolicy(null);
                var info = await TvInfoClient.GetAsync(ip.ToString(), tls);
                store.Upsert(info, tls.Observed);
                Console.WriteLine($"  {info.Ip}  {info.Name}  model={info.ModelName}  frame={info.IsFrame}  {info.Width}x{info.Height}  id={info.Id}  cert={(tls.Observed is null ? "(http)" : tls.Observed[..12] + "…")}");
            }
            catch (TvException e) { Console.WriteLine($"  {ip}: {Describe(e)}"); }
        }
        store.Save();
        return 0;
    }

    /// <summary>
    /// CHANGES the TV's gallery, but only by one image of its own: uploads a generated test image, checks that it is listed,
    /// and deletes ONLY that image (by the content_id the TV returned). Refuses if the id matches an existing image.
    /// </summary>
    static async Task<int> RoundTrip(TvStore store)
    {
        var tv = store.Items.FirstOrDefault(t => t.IsFrame) ?? throw new InvalidOperationException("No TV in the store. Run: probe scan");
        if (store.GetToken(tv) is null) throw new InvalidOperationException("The TV is not paired. Run: probe list");
        var tls = new TlsPolicy(tv.CertSha256);
        await using var art = await ArtClient.ConnectAsync(tv.Ip, store.GetToken(tv), tls, default);

        var before = (await art.ListAsync(default)).Select(i => i.ContentId).ToHashSet();
        var jpeg = MakeTestImage(tv.Width, tv.Height);
        Console.WriteLine($"Before: {before.Count} distinct images. Uploading a {tv.Width}x{tv.Height} test image, {jpeg.Length / 1024} KB…");

        var id = await art.UploadAsync(jpeg, default);
        Console.WriteLine($"Uploaded, content_id={id}");
        if (before.Contains(id))
        {
            Console.WriteLine("The TV returned the id of an existing image - aborting WITHOUT deleting anything.");
            return 3;
        }

        var mid = await art.ListAsync(default);
        Console.WriteLine($"After upload: {mid.Count} entries, the new image is listed: {mid.Any(i => i.ContentId == id)}");

        await art.DeleteAsync([id], default);
        var after = await art.ListAsync(default);
        Console.WriteLine($"After delete: {after.Count} entries, test image removed: {after.All(i => i.ContentId != id)}, same as at the start: {after.Select(i => i.ContentId).ToHashSet().SetEquals(before)}");
        return 0;
    }

    /// <summary>
    /// CHANGES what the TV displays: shows one of your photos (select_image), confirms it with get_current_artwork
    /// and restores the previously displayed image if it could be read.
    /// </summary>
    static async Task<int> Select(TvStore store)
    {
        var tv = store.Items.FirstOrDefault(t => t.IsFrame && t.IsPaired) ?? throw new InvalidOperationException("No paired TV. Run: probe list");
        var tls = new TlsPolicy(tv.CertSha256);
        await using var art = await ArtClient.ConnectAsync(tv.Ip, store.GetToken(tv), tls, default);

        var mine = (await art.ListAsync(default)).Where(i => i.CategoryId == ArtClient.MyPhotosCategory).ToList();
        if (mine.Count == 0) throw new InvalidOperationException("There are no photos of your own on the TV to test with.");

        string? previous = null;
        try { previous = await art.GetCurrentArtworkAsync(default); }
        catch (TvException e) { Console.WriteLine($"Could not read the current image ({Describe(e)}); nothing will be restored afterwards."); }
        Console.WriteLine($"Currently displayed: {previous ?? "(unknown)"}");

        var target = mine.FirstOrDefault(i => i.ContentId != previous) ?? mine[0];
        Console.WriteLine($"Showing {target.ContentId}…");
        await art.SelectImageAsync(target.ContentId, show: true, default);
        Console.WriteLine("The TV confirmed select_image.");

        await Task.Delay(3000);
        string? now = null;
        try { now = await art.GetCurrentArtworkAsync(default); } catch (TvException e) { Console.WriteLine($"Reading the image after the change failed: {Describe(e)}"); }
        Console.WriteLine($"Displayed after the change: {now ?? "(unknown)"}  (matches the target: {now == target.ContentId})");

        if (previous is not null && previous != target.ContentId)
        {
            await art.SelectImageAsync(previous, show: true, default);
            Console.WriteLine($"Restored the previous image: {previous}");
        }
        return 0;
    }

    /// <summary>Offline: the same pipeline as adding a photo in the app (load, default 16:9 crop, JPEG). No TV involved.</summary>
    static int Prepare(string path)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var bitmap = FrameGallery.Imaging.ImagePipeline.Load(path);
        Console.WriteLine($"Loaded {bitmap.Width}x{bitmap.Height} in {sw.ElapsedMilliseconds} ms");
        var crop = CropMath.Initial(bitmap.Width, bitmap.Height, 3840.0 / 2160);
        sw.Restart();
        var jpeg = FrameGallery.Imaging.ImagePipeline.CropToJpeg(bitmap, crop, 3840, 2160);
        using var back = SkiaSharp.SKBitmap.Decode(jpeg);
        Console.WriteLine($"Crop {crop.W:0}x{crop.H:0} -> JPEG {jpeg.Length:N0} B ({jpeg.Length / 1024} KB) in {sw.ElapsedMilliseconds} ms");
        Console.WriteLine($"Check: JPEG={(jpeg[0] == 0xFF && jpeg[1] == 0xD8)}, decoded {back.Width}x{back.Height}");
        return 0;
    }

    /// <summary>Read-only: does the TV drop the Art channel after inactivity (about as long as cropping takes in the GUI)?</summary>
    static async Task<int> Idle(TvStore store, int seconds)
    {
        var tv = store.Items.FirstOrDefault(t => t.IsFrame && t.IsPaired) ?? throw new InvalidOperationException("No paired TV. Run: probe list");
        var tls = new TlsPolicy(tv.CertSha256);
        await using var art = await ArtClient.ConnectAsync(tv.Ip, store.GetToken(tv), tls, default);
        Console.WriteLine($"Listing before inactivity: {(await art.ListAsync(default)).Count} entries. Waiting {seconds} s without traffic…");
        await Task.Delay(TimeSpan.FromSeconds(seconds));
        try { Console.WriteLine($"Listing after inactivity: OK, {(await art.ListAsync(default)).Count} entries"); return 0; }
        catch (Exception e) { TvLog.Write($"[ERR] after inactivity: {TvLog.Describe(e)}"); Console.WriteLine($"ERROR AFTER INACTIVITY: {TvLog.Describe(e)}"); return 4; }
    }

    static byte[] MakeTestImage(int w, int h)
    {
        using var bitmap = new SkiaSharp.SKBitmap(w, h);
        using var canvas = new SkiaSharp.SKCanvas(bitmap);
        using var paint = new SkiaSharp.SKPaint { IsAntialias = true };
        paint.Shader = SkiaSharp.SKShader.CreateLinearGradient(new SkiaSharp.SKPoint(0, 0), new SkiaSharp.SKPoint(w, h),
            [new SkiaSharp.SKColor(30, 60, 120), new SkiaSharp.SKColor(240, 140, 60)], SkiaSharp.SKShaderTileMode.Clamp);
        canvas.DrawRect(0, 0, w, h, paint);
        paint.Shader = null;
        paint.Color = SkiaSharp.SKColors.White;
        using var font = new SkiaSharp.SKFont { Size = h / 14f, Embolden = true };
        canvas.DrawText("FrameGallery test", w * 0.06f, h * 0.4f, font, paint);
        canvas.DrawText($"{DateTime.Now:yyyy-MM-dd HH:mm}", w * 0.06f, h * 0.55f, font, paint);
        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        return image.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 90).ToArray();
    }

    static async Task<int> List(TvStore store, string? ip)
    {
        var tv = store.Items.FirstOrDefault(t => ip is null ? t.IsFrame : t.Ip == ip)
                 ?? throw new InvalidOperationException("No TV in the store. Run: probe scan");
        var tls = new TlsPolicy(tv.CertSha256);
        if (store.GetToken(tv) is null)
        {
            Console.WriteLine(">>> APPROVE THE CONNECTION ON THE TV with the remote (\"Allow\") - you have 60 s <<<");
            await using var pair = await TvChannel.ConnectAsync(tv.Ip, TvChannel.RemoteChannel, null, tls, false, TimeSpan.FromSeconds(60), default);
            store.SetToken(tv, pair.Token);
            Console.WriteLine($"Paired, token {(store.GetToken(tv) is null ? "NOT sent by the TV" : "received (stored encrypted with DPAPI)")}");
        }
        tv.CertSha256 ??= tls.Observed;
        store.Save();

        await using var art = await ArtClient.ConnectAsync(tv.Ip, store.GetToken(tv), tls, default);
        var items = await art.ListAsync(default);
        Console.WriteLine($"Images on the TV: {items.Count}");
        foreach (var g in items.GroupBy(i => i.CategoryId ?? "(none)"))
            Console.WriteLine($"  category {g.Key}: {g.Count()}");
        foreach (var i in items.Take(8))
            Console.WriteLine($"  {i.ContentId}  {i.CategoryId}  matte={i.MatteId}  {i.Width}x{i.Height}  {i.ImageDate}");

        var mine = items.Where(i => i.CategoryId == ArtClient.MyPhotosCategory).Take(3).Select(i => i.ContentId).ToList();
        var sample = mine.Count > 0 ? mine : items.Take(3).Select(i => i.ContentId).ToList();
        if (sample.Count > 0)
        {
            var thumbs = await art.GetThumbnailsAsync(sample, default);
            foreach (var (id, bytes) in thumbs)
                Console.WriteLine($"  thumbnail {id}: {bytes.Length} B, JPEG={(bytes.Length > 2 && bytes[0] == 0xFF && bytes[1] == 0xD8)}");
            Console.WriteLine($"thumbnails received: {thumbs.Count}/{sample.Count}");
        }
        return 0;
    }
}
