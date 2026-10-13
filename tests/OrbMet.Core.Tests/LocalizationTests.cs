using System.Collections;
using System.Globalization;
using System.Resources;
using OrbMet.Core.Localization;

namespace OrbMet.Core.Tests;

public class LocalizationTests
{
    [Fact]
    public void BothLanguagesHaveTheSameKeysAndPlaceholders()
    {
        var english = Entries(CultureInfo.InvariantCulture);
        var turkish = Entries(CultureInfo.GetCultureInfo("tr"));

        Assert.NotEmpty(english);
        Assert.Equal(english.Keys.Order(), turkish.Keys.Order());
        foreach (var (key, value) in english)
        {
            for (var i = 0; i < 4; i++)
            {
                var placeholder = "{" + i + "}";
                Assert.True(
                    value.Contains(placeholder) == turkish[key].Contains(placeholder),
                    $"{key}: {placeholder} differs between languages");
            }
        }
    }

    [Fact]
    public void EachLanguageReadsItsOwnFile()
    {
        Assert.Equal("Refresh", Texts.For(UiLanguage.English)["Refresh"]);
        Assert.Equal("Yenile", Texts.For(UiLanguage.Turkish)["Refresh"]);
        Assert.Equal("[NoSuchKey]", Texts.For(UiLanguage.English)["NoSuchKey"]);
    }

    [Theory]
    [InlineData("tr-TR", UiLanguage.Turkish)]
    [InlineData("tr", UiLanguage.Turkish)]
    [InlineData("en-US", UiLanguage.English)]
    [InlineData("de-DE", UiLanguage.English)]
    [InlineData("ja-JP", UiLanguage.English)]
    public void UnpickedLanguageFollowsWindowsTurkishOrElseEnglish(string culture, UiLanguage expected)
    {
        Assert.Equal(expected, UiLanguages.ForCulture(CultureInfo.GetCultureInfo(culture)));
    }

    [Fact]
    public void NothingPickedLoadsAsNull()
    {
        using var temp = new TempDirectory();
        Assert.Null(new LanguageStore(Path.Combine(temp.Path, "settings.json")).Load());
    }

    [Fact]
    public void PickedLanguageIsStoredAndOtherSettingsAreKept()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "settings.json");
        File.WriteAllText(path, """{ "Other": 3 }""");
        var store = new LanguageStore(path);

        Assert.True(store.Save(UiLanguage.English));
        Assert.Equal(UiLanguage.English, store.Load());
        Assert.True(store.Save(UiLanguage.Turkish));
        Assert.Equal(UiLanguage.Turkish, store.Load());
        Assert.Contains("\"Other\": 3", File.ReadAllText(path));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{ "Language": "fr" }""")]
    [InlineData("""{ "Language": 7 }""")]
    [InlineData("[]")]
    public void UnreadableOrUnknownChoiceLoadsAsNull(string content)
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "settings.json");
        File.WriteAllText(path, content);

        Assert.Null(new LanguageStore(path).Load());
    }

    [Fact]
    public void ABrokenFileIsReplacedWhenALanguageIsPicked()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "settings.json");
        File.WriteAllText(path, "not json");
        var store = new LanguageStore(path);

        Assert.True(store.Save(UiLanguage.Turkish));
        Assert.Equal(UiLanguage.Turkish, store.Load());
    }

    private static Dictionary<string, string> Entries(CultureInfo culture)
    {
        var set = Texts.Resources.GetResourceSet(culture, createIfNotExists: true, tryParents: false)
            ?? throw new InvalidOperationException($"No resources for {culture.Name}");
        return set.Cast<DictionaryEntry>().ToDictionary(e => (string)e.Key, e => (string)e.Value!);
    }
}
