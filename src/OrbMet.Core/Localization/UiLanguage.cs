using System.Globalization;

namespace OrbMet.Core.Localization;

/// <summary>The languages OrbMet's own text comes in.</summary>
public enum UiLanguage
{
    English,
    Turkish,
}

public static class UiLanguages
{
    public static IReadOnlyList<UiLanguage> All { get; } = Enum.GetValues<UiLanguage>();

    /// <summary>The short code stored in settings.json: "en" or "tr".</summary>
    public static string Code(this UiLanguage language) => language switch
    {
        UiLanguage.Turkish => "tr",
        _ => "en",
    };

    /// <summary>The language's own name, so it reads right whichever is on.</summary>
    public static string NativeName(this UiLanguage language) => language switch
    {
        UiLanguage.Turkish => "Türkçe",
        _ => "English",
    };

    public static CultureInfo Culture(this UiLanguage language) => CultureInfo.GetCultureInfo(language switch
    {
        UiLanguage.Turkish => "tr-TR",
        _ => "en-US",
    });

    public static UiLanguage? FromCode(string? code) => code?.Trim().ToLowerInvariant() switch
    {
        "tr" => UiLanguage.Turkish,
        "en" => UiLanguage.English,
        _ => null,
    };

    /// <summary>
    /// Before the user picks one: Turkish on a Turkish Windows, English on
    /// every other.
    /// </summary>
    public static UiLanguage ForCulture(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return culture.TwoLetterISOLanguageName == "tr" ? UiLanguage.Turkish : UiLanguage.English;
    }
}
