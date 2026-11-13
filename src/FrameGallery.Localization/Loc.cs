using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Resources;

namespace FrameGallery.Localization;

/// <summary>
/// Access to user-facing text. The language follows the system (CurrentUICulture); when there is no translation
/// we fall back culture → language → neutral (English). A missing key returns the key itself,
/// so the mistake is visible without throwing.
/// The language can be forced with the FRAMEGALLERY_LANG environment variable (e.g. "pl", "en"),
/// which is handy for tests and bug reports.
/// </summary>
/// <remarks>
/// Strings.resx is English (the neutral fallback). Every Strings.&lt;language&gt;.resx is compiled into this same assembly
/// (not into a satellite assembly, see the project file) and found here by its resource name, so the library can be merged
/// into the application assembly and shipped without extra folders.
/// </remarks>
public static class Loc
{
    const string BaseName = "FrameGallery.Localization.Strings";
    static readonly Assembly Asm = typeof(Loc).Assembly;
    static readonly ResourceManager Neutral = new(BaseName, Asm);
    static readonly ConcurrentDictionary<string, ResourceManager?> Languages = new();

    /// <summary>A forced culture (null = the system language). Initialised from FRAMEGALLERY_LANG at startup.</summary>
    public static CultureInfo? Culture { get; set; } = FromEnvironment();

    public static string Get(string key, params object?[] args) => GetFor(Culture ?? CultureInfo.CurrentUICulture, key, args);

    public static string GetFor(CultureInfo culture, string key, params object?[] args)
    {
        string? text = null;
        // pl-PL → pl; anything without a translation ends up on the neutral English text.
        for (var c = culture; text is null && !string.IsNullOrEmpty(c.Name); c = c.Parent)
            text = ForLanguage(c.Name)?.GetString(key, CultureInfo.InvariantCulture);
        text ??= Neutral.GetString(key, CultureInfo.InvariantCulture);

        if (text is null) return key;
        return args.Length == 0 ? text : string.Format(culture, text, args);
    }

    /// <summary>The text entries of one culture (without its parents); used by the translation consistency test.</summary>
    internal static Dictionary<string, string> Entries(CultureInfo culture)
    {
        var manager = culture.Name.Length == 0 ? Neutral : ForLanguage(culture.Name);
        var result = new Dictionary<string, string>();
        var set = manager?.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: false);
        if (set is null) return result;
        foreach (System.Collections.DictionaryEntry e in set)
            if (e.Key is string k && e.Value is string v) result[k] = v;
        return result;
    }

    /// <summary>The resources of one language, or null when this assembly has none for it.</summary>
    static ResourceManager? ForLanguage(string language) => Languages.GetOrAdd(language, l =>
    {
        var name = $"{BaseName}.{l}";
        return Array.IndexOf(Asm.GetManifestResourceNames(), name + ".resources") >= 0 ? new ResourceManager(name, Asm) : null;
    });

    static CultureInfo? FromEnvironment()
    {
        var name = Environment.GetEnvironmentVariable("FRAMEGALLERY_LANG");
        if (string.IsNullOrWhiteSpace(name)) return null;
        try { return CultureInfo.GetCultureInfo(name.Trim()); }
        catch (CultureNotFoundException) { return null; }
    }
}
