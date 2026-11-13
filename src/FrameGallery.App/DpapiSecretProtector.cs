using System.Security.Cryptography;
using System.Text;
using FrameGallery.Core;

namespace FrameGallery.App;

/// <summary>Windows: tokens are encrypted with DPAPI (only this user on this computer can read them).</summary>
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
