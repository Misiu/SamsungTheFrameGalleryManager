using System.Text.Json;
using System.Text.Json.Serialization;

namespace FrameGallery.Core;

/// <summary>
/// Secret protection is platform-specific (Windows: DPAPI, macOS: Keychain, Linux: libsecret, Android: Keystore),
/// so Core does not assume one: the application supplies the implementation. There is deliberately no default,
/// so tokens can never end up stored as plain text.
/// </summary>
public interface ISecretProtector
{
    string Protect(string plainText);

    /// <returns>The decrypted text, or null when it cannot be decrypted (e.g. a different user or computer).</returns>
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

    /// <summary>The pinned SHA-256 thumbprint of the TV's certificate (trust on first use).</summary>
    public string? CertSha256 { get; set; }

    /// <summary>The pairing token after <see cref="ISecretProtector.Protect"/>; read and written through <see cref="TvStore"/>.</summary>
    public string? TokenProtected { get; set; }

    [JsonIgnore] public bool IsPaired => !string.IsNullOrEmpty(TokenProtected);
}

/// <summary>The list of known TVs in a JSON file. Tokens are encrypted with the supplied <see cref="ISecretProtector"/>.</summary>
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
            TvLog.Write($"[STORE] could not load {Path.GetFileName(path)}: {e.Message}");
        }
    }

    public string? GetToken(TvRecord tv) =>
        string.IsNullOrEmpty(tv.TokenProtected) ? null : _protector.Unprotect(tv.TokenProtected);

    public void SetToken(TvRecord tv, string? token) =>
        tv.TokenProtected = string.IsNullOrEmpty(token) ? null : _protector.Protect(token);

    /// <summary>Adds a TV or refreshes an existing one (by Id); keeps the token and the pinned certificate.</summary>
    public TvRecord Upsert(TvInfo info, string? observedCert)
    {
        Items.RemoveAll(t => t.Ip == info.Ip && t.Id != info.Id);   // a different device is now at this IP
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
