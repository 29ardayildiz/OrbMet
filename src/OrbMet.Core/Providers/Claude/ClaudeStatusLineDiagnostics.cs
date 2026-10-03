using System.Text.Json;
using OrbMet.Core.Storage;

namespace OrbMet.Core.Providers.Claude;

/// <summary>
/// Schema-level result of reading a status-line payload: whether the JSON was
/// readable, whether rate_limits was present, and which window names appeared.
/// It carries no values, paths or identifiers, so it is safe to record.
/// </summary>
public sealed record ClaudeStatusLineParseInfo(
    bool JsonReadable,
    bool HasRateLimits,
    IReadOnlyList<string> WindowKeys)
{
    public static readonly ClaudeStatusLineParseInfo Empty =
        new(JsonReadable: false, HasRateLimits: false, WindowKeys: Array.Empty<string>());
}

/// <summary>
/// Writes a tiny, sanitized record of the last status-line run so a capture
/// that found nothing can be told apart from a helper that never ran. No
/// payload values, paths, prompts or identifiers are stored.
/// </summary>
internal static class ClaudeStatusLineDiagnostics
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static void Write(
        string directory,
        DateTimeOffset ranAtUtc,
        int inputBytes,
        ClaudeStatusLineParseInfo info,
        bool captureSaved)
    {
        var record = new DiagnosticRecord
        {
            SchemaVersion = 1,
            RanAtUtc = ranAtUtc,
            InputBytes = inputBytes,
            JsonReadable = info.JsonReadable,
            HasRateLimits = info.HasRateLimits,
            WindowKeys = info.WindowKeys.ToArray(),
            CaptureSaved = captureSaved,
        };

        AtomicFile.WriteAllText(
            Path.Combine(directory, "claude-statusline.json"),
            JsonSerializer.Serialize(record, Options));
    }

    private sealed class DiagnosticRecord
    {
        public int SchemaVersion { get; set; }

        public DateTimeOffset RanAtUtc { get; set; }

        public int InputBytes { get; set; }

        public bool JsonReadable { get; set; }

        public bool HasRateLimits { get; set; }

        public IReadOnlyList<string> WindowKeys { get; set; } = Array.Empty<string>();

        public bool CaptureSaved { get; set; }
    }
}
