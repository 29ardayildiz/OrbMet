using System.Text.Json;
using System.Text.Json.Nodes;
using OrbMet.Core.Storage;

namespace OrbMet.Core.Providers.Claude;

/// <summary>
/// What OrbMet changed in Claude's settings, kept so the change can be undone
/// exactly. The previous statusLine object is stored whole, including fields
/// OrbMet does not read, because removing them would alter the user's own
/// status line. This file is OrbMet's own bookkeeping, not Claude's settings.
/// </summary>
public sealed record ClaudeIntegrationRecord
{
    public int SchemaVersion { get; init; } = 1;

    /// <summary>The exact command string OrbMet installed, used to prove ownership on removal.</summary>
    public string? InstalledCommand { get; init; }

    /// <summary>Whether a statusLine object existed before OrbMet.</summary>
    public bool HadPreviousStatusLine { get; init; }

    /// <summary>The previous statusLine object, verbatim, or null when there was none.</summary>
    public JsonNode? PreviousStatusLine { get; init; }

    public string? SettingsPath { get; init; }

    public DateTimeOffset InstalledAtUtc { get; init; }
}

/// <summary>Reads and writes OrbMet's single Claude integration record.</summary>
public sealed class ClaudeIntegrationRecordStore
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    private readonly string _path;

    public ClaudeIntegrationRecordStore(string path)
    {
        _path = string.IsNullOrWhiteSpace(path)
            ? throw new ArgumentException("An integration record path is required.", nameof(path))
            : path;
    }

    public ClaudeIntegrationRecord? Read()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return null;
            }

            using var stream = new FileStream(
                _path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            return JsonSerializer.Deserialize<ClaudeIntegrationRecord>(stream, Options);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Write(ClaudeIntegrationRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(record, Options));
    }

    public void Delete()
    {
        try
        {
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }
        }
        catch (IOException)
        {
            // A stale record is safer than a lost one; the next connect overwrites it.
        }
    }
}
