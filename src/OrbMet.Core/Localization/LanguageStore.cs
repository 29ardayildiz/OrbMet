using System.Text.Json;
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

    private readonly string _path;

    public LanguageStore(string path)
    {
        _path = string.IsNullOrWhiteSpace(path)
            ? throw new ArgumentException("A file path is required.", nameof(path))
            : path;
    }

    /// <summary>The picked language, or null when none was picked or the file is unreadable.</summary>
    public UiLanguage? Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return null;
            }

            var root = JsonNode.Parse(File.ReadAllText(_path)) as JsonObject;
            return root?[Key] is JsonValue value && value.TryGetValue<string>(out var code)
                ? UiLanguages.FromCode(code)
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Returns false when the file could not be written.</summary>
    public bool Save(UiLanguage language)
    {
        try
        {
            var root = ReadObject() ?? new JsonObject();
            root[Key] = language.Code();
            AtomicFile.WriteAllText(_path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    // A broken file is replaced: it holds nothing but OrbMet's own choices.
    private JsonObject? ReadObject()
    {
        try
        {
            return File.Exists(_path) ? JsonNode.Parse(File.ReadAllText(_path)) as JsonObject : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
