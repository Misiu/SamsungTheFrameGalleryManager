using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using FrameGallery.Core;

namespace FrameGallery.Desktop;

static class SecretProtectors
{
    public static ISecretProtector Create() =>
        OperatingSystem.IsWindows() ? new DpapiSecretProtector() : new UnencryptedSecretProtector();
}

/// <summary>Windows: tokens are encrypted with DPAPI (only this user on this computer can read them).</summary>
[SupportedOSPlatform("windows")]
sealed class DpapiSecretProtector : ISecretProtector
{
    public string Protect(string plainText) =>
        Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(plainText), null, DataProtectionScope.CurrentUser));

    public string? Unprotect(string protectedText)
    {
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(protectedText), null, DataProtectionScope.CurrentUser)); }
        catch (Exception e) when (e is CryptographicException or FormatException) { return null; }   // a different user or computer: the TV is paired again
    }
}

/// <summary>
/// ponytail: macOS and Linux have no protector yet (Keychain and libsecret are still to be written), so tokens are stored as they are;
/// <see cref="TvStore"/> keeps the file readable by the owner only. Upgrade: a Keychain / libsecret implementation of <see cref="ISecretProtector"/>.
/// </summary>
sealed class UnencryptedSecretProtector : ISecretProtector
{
    const string Prefix = "plain:";
    public string Protect(string plainText) => Prefix + plainText;
    public string? Unprotect(string protectedText) => protectedText.StartsWith(Prefix, StringComparison.Ordinal) ? protectedText[Prefix.Length..] : null;
}
