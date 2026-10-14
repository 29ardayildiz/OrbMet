using System.Text.Json.Nodes;

namespace OrbMet.Core.Storage;

/// <summary>
/// Whether OrbMet has already opened Connections by itself, so a first run
/// that finds Claude Code unconnected does it once and never again. Kept in
/// settings.json as <c>{"ConnectionsOffered": true}</c>.
/// </summary>
public sealed class WelcomeStore
{
    private const string Key = "ConnectionsOffered";

    private readonly SettingsFile _file;

    public WelcomeStore(string path)
    {
        _file = new SettingsFile(path);
    }

    public bool ConnectionsOffered =>
        _file.Read(Key) is JsonValue value && value.TryGetValue<bool>(out var offered) && offered;

    /// <summary>Returns false when the file could not be written.</summary>
    public bool MarkConnectionsOffered() => _file.Write(Key, JsonValue.Create(true));
}
