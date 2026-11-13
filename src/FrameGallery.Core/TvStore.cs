using System.Text.Json;
using System.Text.Json.Serialization;

namespace FrameGallery.Core;

/// <summary>
/// Szyfrowanie sekretów zależy od platformy (Windows: DPAPI, macOS: Keychain, Linux: libsecret, Android: Keystore),
/// więc Core go nie zakłada: aplikacja podaje implementację. Brak domyślnej, żeby nie zapisywać tokenów jawnym tekstem.
/// </summary>
public interface ISecretProtector
{
    string Protect(string plainText);

    /// <returns>Odszyfrowany tekst albo null, gdy się nie da (np. inny użytkownik/komputer).</returns>
    string? Unprotect(string protectedText);
}

public sealed class TvRecord
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Model { get; set; } = "";
    public string Ip { get; set; } = "";
    public bool IsFrame { get; set; }
    public int Width { get; set; } = 3840;
    public int Height { get; set; } = 2160;

    /// <summary>Przypięty odcisk SHA-256 certyfikatu TV (TOFU).</summary>
    public string? CertSha256 { get; set; }

    /// <summary>Token sparowania po <see cref="ISecretProtector.Protect"/>; odczyt i zapis przez <see cref="TvStore"/>.</summary>
    public string? TokenProtected { get; set; }

    [JsonIgnore] public bool IsPaired => !string.IsNullOrEmpty(TokenProtected);
}

/// <summary>Lista znanych TV w pliku JSON. Tokeny są szyfrowane przez podany <see cref="ISecretProtector"/>.</summary>
public sealed class TvStore
{
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    readonly string _path;
    readonly ISecretProtector _protector;

    public List<TvRecord> Items { get; private set; } = [];

    public TvStore(string path, ISecretProtector protector)
    {
        _path = path;
        _protector = protector;
        try { if (File.Exists(path)) Items = JsonSerializer.Deserialize<List<TvRecord>>(File.ReadAllText(path)) ?? []; }
        catch (Exception e) when (e is JsonException or IOException)
        {
            TvLog.Write($"[STORE] nie wczytano {Path.GetFileName(path)}: {e.Message}");
        }
    }

    public string? GetToken(TvRecord tv) =>
        string.IsNullOrEmpty(tv.TokenProtected) ? null : _protector.Unprotect(tv.TokenProtected);

    public void SetToken(TvRecord tv, string? token) =>
        tv.TokenProtected = string.IsNullOrEmpty(token) ? null : _protector.Protect(token);

    /// <summary>Dodaje TV albo odświeża dane istniejącego (po Id); zachowuje token i przypięty certyfikat.</summary>
    public TvRecord Upsert(TvInfo info, string? observedCert)
    {
        Items.RemoveAll(t => t.Ip == info.Ip && t.Id != info.Id);   // pod tym IP jest już inne urządzenie
        var rec = Items.FirstOrDefault(t => t.Id == info.Id);
        if (rec is null) Items.Add(rec = new TvRecord { Id = info.Id });
        rec.Name = info.Name; rec.Model = info.ModelName; rec.Ip = info.Ip; rec.IsFrame = info.IsFrame;
        rec.Width = info.Width; rec.Height = info.Height;
        rec.CertSha256 ??= observedCert;
        return rec;
    }

    public void Remove(TvRecord tv) => Items.Remove(tv);

    public void Save()
    {
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(Items, Json));
        File.Move(tmp, _path, overwrite: true);
    }
}
