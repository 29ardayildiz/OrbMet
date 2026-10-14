using System.Text.Json.Nodes;
using OrbMet.Core.Storage;

namespace OrbMet.Core.Localization;

/// <summary>
/// The language the user picked, in OrbMet's settings.json as
/// <c>{"Language": "en"}</c>. Nothing is written until the user picks one,
/// so until then OrbMet follows the Windows language. Other keys in the file
/// are kept.
/// </summary>
public sealed class LanguageStore
{
    private const string Key = "Language";

    private readonly SettingsFile _file;

    public LanguageStore(string path)
    {
        _file = new SettingsFile(path);
    }

    /// <summary>The picked language, or null when none was picked or the file is unreadable.</summary>
    public UiLanguage? Load() =>
        _file.Read(Key) is JsonValue value && value.TryGetValue<string>(out var code)
            ? UiLanguages.FromCode(code)
            : null;

    /// <summary>Returns false when the file could not be written.</summary>
    public bool Save(UiLanguage language) => _file.Write(Key, JsonValue.Create(language.Code()));
}
