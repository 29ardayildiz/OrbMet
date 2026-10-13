using System.Text.Json;
using System.Text.Json.Serialization;
using OrbMet.Core.Storage;

namespace OrbMet.Core.Placement;

/// <summary>
/// Remembers where the strip was left, in one small JSON file. A missing or
/// unreadable file means the default place, never an error the user sees; a
/// failed save keeps the strip where it is and is tried again on the next drop.
/// </summary>
public sealed class PanelPositionStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;

    public PanelPositionStore(string path)
    {
        _path = string.IsNullOrWhiteSpace(path)
            ? throw new ArgumentException("A file path is required.", nameof(path))
            : path;
    }

    public PanelPosition Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return PanelPosition.Default;
            }

            var stored = JsonSerializer.Deserialize<PanelPosition>(File.ReadAllText(_path), Options);
            return stored is null || !Enum.IsDefined(stored.Dock) ? PanelPosition.Default : stored;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return PanelPosition.Default;
        }
    }

    /// <summary>Returns false when the file could not be written.</summary>
    public bool Save(PanelPosition position)
    {
        ArgumentNullException.ThrowIfNull(position);

        try
        {
            AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(position, Options));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
