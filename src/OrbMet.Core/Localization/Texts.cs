using System.Globalization;
using System.Resources;

namespace OrbMet.Core.Localization;

/// <summary>
/// OrbMet's wording in one language, from Strings.resx (English) and
/// Strings.tr.resx (Turkish). Keys are the resource names. A missing key
/// shows as [Key] rather than throwing, and a test keeps both files' keys
/// the same.
/// </summary>
public sealed class Texts
{
    internal static readonly ResourceManager Resources =
        new("OrbMet.Core.Localization.Strings", typeof(Texts).Assembly);

    private static readonly Texts EnglishTexts = new(UiLanguage.English);
    private static readonly Texts TurkishTexts = new(UiLanguage.Turkish);

    private Texts(UiLanguage language)
    {
        Language = language;
        Culture = language.Culture();
    }

    public static Texts For(UiLanguage language) => language == UiLanguage.Turkish ? TurkishTexts : EnglishTexts;

    public UiLanguage Language { get; }

    /// <summary>Formats dates (month names) and picks the resource file.</summary>
    public CultureInfo Culture { get; }

    public string this[string key] => Resources.GetString(key, Culture) ?? $"[{key}]";

    public string Format(string key, params object?[] args) => string.Format(Culture, this[key], args);
}
