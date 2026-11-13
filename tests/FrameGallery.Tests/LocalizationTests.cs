using System.Globalization;
using System.Text.RegularExpressions;
using FrameGallery.Core;
using FrameGallery.Localization;

namespace FrameGallery.Tests;

public class LocalizationTests
{
    static readonly CultureInfo Polish = CultureInfo.GetCultureInfo("pl-PL");
    static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    static Dictionary<string, string> English => Loc.Entries(CultureInfo.InvariantCulture);
    static Dictionary<string, string> PolishEntries => Loc.Entries(CultureInfo.GetCultureInfo("pl"));

    [Fact]
    public void Polish_and_english_define_exactly_the_same_keys()
    {
        Assert.NotEmpty(English);
        Assert.Empty(English.Keys.Except(PolishEntries.Keys));   // a key without a Polish translation
        Assert.Empty(PolishEntries.Keys.Except(English.Keys));   // a Polish key without an English original
    }

    [Fact]
    public void Placeholders_match_between_languages()
    {
        static HashSet<string> Placeholders(string s) => Regex.Matches(s, @"\{(\d+)").Select(m => m.Groups[1].Value).ToHashSet();
        foreach (var (key, en) in English)
            Assert.True(Placeholders(en).SetEquals(Placeholders(PolishEntries[key])), $"Placeholders differ for {key}");
    }

    [Fact]
    public void Polish_texts_are_really_translated()
    {
        // These are the same in both languages on purpose (a product name and a word that is identical in Polish).
        string[] sameInBoth = ["Main_TypeFrame", "Main_ColModel"];
        var untranslated = English.Where(kv => PolishEntries[kv.Key] == kv.Value).Select(kv => kv.Key).Except(sameInBoth);
        Assert.Empty(untranslated);
    }

    [Fact]
    public void System_language_is_used_and_unknown_languages_fall_back_to_english()
    {
        Assert.Equal("Szukaj TV", Loc.GetFor(Polish, "Main_Scan"));
        Assert.Equal("Find TVs", Loc.GetFor(CultureInfo.GetCultureInfo("en-US"), "Main_Scan"));
        Assert.Equal("Find TVs", Loc.GetFor(German, "Main_Scan"));            // no German translation: English
        Assert.Equal("Find TVs", Loc.GetFor(CultureInfo.InvariantCulture, "Main_Scan"));
    }

    [Fact]
    public void Placeholders_are_filled_in_the_requested_language()
    {
        Assert.Equal("Znaleziono i dodano: 2 z 3 urządzeń.", Loc.GetFor(Polish, "Main_ScanDone", 2, 3));
        Assert.Equal("Found and added: 2 of 3 devices.", Loc.GetFor(German, "Main_ScanDone", 2, 3));
    }

    [Fact]
    public void A_missing_key_returns_the_key_instead_of_throwing() =>
        Assert.Equal("No_Such_Key", Loc.GetFor(Polish, "No_Such_Key"));

    [Fact]
    public void Every_key_used_in_the_source_exists()
    {
        var root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "FrameGallery.slnx"))) root = Path.GetDirectoryName(root);
        Assert.NotNull(root);

        var usage = new Regex(@"(?:Loc\.Get|new TvException|new TvConnectionLostException)\(\s*""([A-Za-z_]+)""");
        var used = Directory.EnumerateFiles(Path.Combine(root!, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .SelectMany(f => usage.Matches(File.ReadAllText(f)).Select(m => (File: Path.GetFileName(f), Key: m.Groups[1].Value)))
            .ToList();

        Assert.NotEmpty(used);
        Assert.Empty(used.Where(u => !English.ContainsKey(u.Key)).Select(u => $"{u.File}: {u.Key}"));
    }

    [Fact]
    public void Log_messages_stay_in_english_whatever_the_ui_language()
    {
        var previous = Loc.Culture;
        try
        {
            Loc.Culture = Polish;
            var e = Assert.Throws<TvException>(() => Lan.RequirePrivateIPv4("8.8.8.8"));
            Assert.Contains("lokalnej", e.Message);                                   // what the user sees
            Assert.StartsWith("The address must be on the local network", e.EnglishMessage);   // what goes to the log
        }
        finally { Loc.Culture = previous; }
    }
}
