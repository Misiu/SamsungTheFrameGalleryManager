using System.Globalization;
using System.Resources;

namespace FrameGallery.Localization;

/// <summary>
/// Access to user-facing text. The language follows the system (CurrentUICulture); when there is no translation,
/// the .NET resource fallback applies: culture → language → neutral (English). A missing key returns the key itself,
/// so the mistake is visible without throwing.
/// The language can be forced with the FRAMEGALLERY_LANG environment variable (e.g. "pl", "en"),
/// which is handy for tests and bug reports.
/// </summary>
public static class Loc
{
    const string BaseName = "FrameGallery.Localization.Strings";
    static readonly ResourceManager Rm = new(BaseName, typeof(Loc).Assembly);

    /// <summary>A forced culture (null = the system language). Initialised from FRAMEGALLERY_LANG at startup.</summary>
    public static CultureInfo? Culture { get; set; } = FromEnvironment();

    public static string Get(string key, params object?[] args) => GetFor(Culture ?? CultureInfo.CurrentUICulture, key, args);

    public static string GetFor(CultureInfo culture, string key, params object?[] args)
    {
        var text = Rm.GetString(key, culture);
        if (text is null) return key;
        return args.Length == 0 ? text : string.Format(culture, text, args);
    }

    /// <summary>The text entries of one culture (without its parents); used by the translation consistency test.</summary>
    internal static Dictionary<string, string> Entries(CultureInfo culture)
    {
        var set = Rm.GetResourceSet(culture, createIfNotExists: true, tryParents: false);
        var result = new Dictionary<string, string>();
        if (set is null) return result;
        foreach (System.Collections.DictionaryEntry e in set)
            if (e.Key is string k && e.Value is string v) result[k] = v;
        return result;
    }

    static CultureInfo? FromEnvironment()
    {
        var name = Environment.GetEnvironmentVariable("FRAMEGALLERY_LANG");
        if (string.IsNullOrWhiteSpace(name)) return null;
        try { return CultureInfo.GetCultureInfo(name.Trim()); }
        catch (CultureNotFoundException) { return null; }
    }
}
