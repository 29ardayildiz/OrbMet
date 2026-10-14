using System.Text.Json;
using System.Text.Json.Nodes;

namespace OrbMet.Core.Storage;

/// <summary>
/// OrbMet's own settings.json: one JSON object whose keys belong to different
/// stores. Each write keeps the other keys. A broken file is replaced, since it
/// holds nothing but OrbMet's own choices.
/// </summary>
public sealed class SettingsFile
{
    private readonly string _path;

    public SettingsFile(string path)
    {
        _path = string.IsNullOrWhiteSpace(path)
            ? throw new ArgumentException("A file path is required.", nameof(path))
            : path;
    }

    /// <summary>The value under <paramref name="key"/>, or null when it is missing or the file is unreadable.</summary>
    public JsonNode? Read(string key)
    {
        try
        {
            return ReadObject()?[key];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Sets one key, keeping the rest. Returns false when the file could not be written.</summary>
    public bool Write(string key, JsonNode value)
    {
        try
        {
            var root = ReadObject() ?? new JsonObject();
            root[key] = value;
            AtomicFile.WriteAllText(_path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

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
