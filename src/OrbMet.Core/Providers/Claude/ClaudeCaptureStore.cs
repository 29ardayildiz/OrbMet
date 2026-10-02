using System.Text.Json;
using OrbMet.Core.Storage;

namespace OrbMet.Core.Providers.Claude;

/// <summary>
/// Banks Claude captures on disk, one file per session so a slow writer in one
/// session cannot overwrite another's fresh reading. Writes are atomic; reads
/// tolerate a half-written, corrupt or concurrently replaced file.
/// </summary>
public sealed class ClaudeCaptureStore
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    private readonly string _directory;

    public ClaudeCaptureStore(string directory)
    {
        _directory = string.IsNullOrWhiteSpace(directory)
            ? throw new ArgumentException("A capture directory is required.", nameof(directory))
            : directory;
    }

    public string CaptureDirectory => _directory;

    /// <summary>Writes a capture, replacing this session's previous file in one step.</summary>
    public void Save(ClaudeCaptureRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        Directory.CreateDirectory(_directory);

        var file = new ClaudeCaptureFileDto
        {
            SchemaVersion = 1,
            CapturedAtUtc = record.CapturedAtUtc,
            SessionHash = record.SessionHash,
            Windows = record.Windows
                .Select(window => new ClaudeCaptureWindowDto
                {
                    Key = window.Key,
                    UsedPercent = window.UsedPercent,
                    ResetsAtUtc = window.ResetsAtUtc,
                })
                .ToList(),
        };

        var json = JsonSerializer.Serialize(file, Options);
        AtomicFile.WriteAllText(PathFor(record.SessionHash), json);
    }

    /// <summary>
    /// The observation to show: the newest record that still carries a window
    /// whose reset time has not passed, falling back to the newest record when
    /// every window has already reset.
    /// </summary>
    public ClaudeCaptureRecord? ReadNewest(DateTimeOffset now)
    {
        ClaudeCaptureRecord? newest = null;
        ClaudeCaptureRecord? newestCurrent = null;

        foreach (var record in ReadAll())
        {
            if (newest is null || record.CapturedAtUtc > newest.CapturedAtUtc)
            {
                newest = record;
            }

            if (HasCurrentWindow(record, now) &&
                (newestCurrent is null || record.CapturedAtUtc > newestCurrent.CapturedAtUtc))
            {
                newestCurrent = record;
            }
        }

        return newestCurrent ?? newest;
    }

    public IReadOnlyList<ClaudeCaptureRecord> ReadAll()
    {
        if (!Directory.Exists(_directory))
        {
            return Array.Empty<ClaudeCaptureRecord>();
        }

        var records = new List<ClaudeCaptureRecord>();
        foreach (var path in Directory.EnumerateFiles(_directory, "*.json"))
        {
            if (Path.GetFileName(path).Contains(".tmp-", StringComparison.Ordinal))
            {
                continue;
            }

            var record = TryRead(path);
            if (record is not null)
            {
                records.Add(record);
            }
        }

        return records;
    }

    private static bool HasCurrentWindow(ClaudeCaptureRecord record, DateTimeOffset now) =>
        record.Windows.Any(window => window.ResetsAtUtc is null || window.ResetsAtUtc > now);

    private ClaudeCaptureRecord? TryRead(string path)
    {
        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

            var file = JsonSerializer.Deserialize<ClaudeCaptureFileDto>(stream, Options);
            if (file is null || file.Windows is null)
            {
                return null;
            }

            var windows = file.Windows
                .Where(window => !string.IsNullOrWhiteSpace(window.Key))
                .Select(window => new ClaudeCaptureWindow
                {
                    Key = window.Key!,
                    UsedPercent = window.UsedPercent,
                    ResetsAtUtc = window.ResetsAtUtc,
                })
                .ToList();

            return new ClaudeCaptureRecord
            {
                CapturedAtUtc = file.CapturedAtUtc,
                SessionHash = file.SessionHash,
                Windows = windows,
            };
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

    private string PathFor(string? sessionHash)
    {
        var name = string.IsNullOrWhiteSpace(sessionHash) ? "nosession" : sessionHash;
        return Path.Combine(_directory, $"claude-{name}.json");
    }

    private sealed class ClaudeCaptureFileDto
    {
        public int SchemaVersion { get; set; }

        public DateTimeOffset CapturedAtUtc { get; set; }

        public string? SessionHash { get; set; }

        public List<ClaudeCaptureWindowDto>? Windows { get; set; }
    }

    private sealed class ClaudeCaptureWindowDto
    {
        public string? Key { get; set; }

        public double? UsedPercent { get; set; }

        public DateTimeOffset? ResetsAtUtc { get; set; }
    }
}
