using System.Globalization;
using System.Text;
using System.Text.Json;
using OrbMet.Core.Models;

namespace OrbMet.Core.Usage;

/// <summary>
/// Whether Claude Code or Codex is mid-turn (design.md 6.7), read from the
/// tail of the session files their CLIs write as they go. Off unless the user
/// turns it on. Only the last 128 KB of recent files is opened, and of each
/// record only its type, stop reason and time are looked at; for Claude a
/// user record is also checked for Claude Code's own interruption marker.
/// Nothing is kept, logged or sent. Pulse's rules.
/// </summary>
public static class AgentActivity
{
    /// <summary>What a turn in flight is waiting for, which decides how long its claim may outlive the record.</summary>
    public enum Wait
    {
        /// <summary>A tool: shells and builds write nothing while they run, so up to five minutes.</summary>
        Tool,

        /// <summary>The model: 99% answer inside 70 seconds, so 90 seconds.</summary>
        Model,
    }

    public abstract record Verdict
    {
        private Verdict()
        {
        }

        public sealed record Working(Wait Wait, DateTimeOffset? At) : Verdict;

        public sealed record Finished : Verdict;

        public sealed record Unknown : Verdict;
    }

    public static readonly TimeSpan ToolGrace = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan ModelGrace = TimeSpan.FromSeconds(90);

    /// <summary>A tail that says nothing recognisable falls back to "written to just now".</summary>
    public static readonly TimeSpan UnknownFormatWindow = TimeSpan.FromSeconds(30);

    private const int TailBytes = 128 * 1024;
    private const string InterruptionMarker = "[Request interrupted by user";

    /// <summary>The folder a provider's CLI writes its sessions to, or null when it is unknown.</summary>
    public static string? SessionsRoot(ProviderId providerId) => providerId switch
    {
        ProviderId.ClaudeCode => Path.Combine(
            Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") is { Length: > 0 } claude
                ? claude
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude"),
            "projects"),
        ProviderId.Codex => Path.Combine(
            Environment.GetEnvironmentVariable("CODEX_HOME") is { Length: > 0 } codex
                ? codex
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex"),
            "sessions"),
        _ => null,
    };

    /// <summary>
    /// Whether any of the provider's recent sessions is mid-turn. Two
    /// terminals can run at once, so every file touched within the longest
    /// grace counts, newest first.
    /// </summary>
    public static bool IsWorking(ProviderId providerId, string root, DateTimeOffset nowUtc)
    {
        if (!Directory.Exists(root))
        {
            return false;
        }

        IEnumerable<FileInfo> recent;
        try
        {
            recent = new DirectoryInfo(root)
                .EnumerateFiles("*.jsonl", SearchOption.AllDirectories)
                .Where(file => file.Length > 0 && nowUtc - file.LastWriteTimeUtc <= ToolGrace)
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        foreach (var file in recent)
        {
            var modified = new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero);
            var inFlight = VerdictFor(providerId, Tail(file.FullName)) switch
            {
                // Timed from the record's own stamp: Claude goes on touching a
                // transcript after the turn it belongs to has ended.
                Verdict.Working working => nowUtc - (working.At ?? modified) <= Grace(working.Wait),
                Verdict.Unknown => nowUtc - modified <= UnknownFormatWindow,
                _ => false,
            };

            if (inFlight)
            {
                return true;
            }
        }

        return false;
    }

    public static TimeSpan Grace(Wait wait) => wait == Wait.Tool ? ToolGrace : ModelGrace;

    /// <summary>Reads backwards for the first record that settles whether a turn is in flight.</summary>
    public static Verdict VerdictFor(ProviderId providerId, IReadOnlyList<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        for (var i = lines.Count - 1; i >= 0; i--)
        {
            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(lines[i]);
            }
            catch (JsonException)
            {
                continue;
            }

            using (document)
            {
                var record = document.RootElement;
                if (record.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var verdict = providerId == ProviderId.Codex ? CodexVerdict(record) : ClaudeVerdict(record);
                if (verdict is not null)
                {
                    return verdict;
                }
            }
        }

        return new Verdict.Unknown();
    }

    private static Verdict? ClaudeVerdict(JsonElement record)
    {
        switch (Text(record, "type"))
        {
            case "assistant":
                // No stop reason at all is a subagent's record or a turn still
                // streaming; "tool_use" hands off to a tool and comes back.
                var stop = record.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.Object
                    ? Text(message, "stop_reason")
                    : null;
                return stop switch
                {
                    null => new Verdict.Working(Wait.Model, Stamp(record)),
                    "tool_use" => new Verdict.Working(Wait.Tool, Stamp(record)),
                    _ => new Verdict.Finished(),
                };
            case "user":
                // Escape ends the turn and says so in the record it leaves; any
                // other user record (a prompt, a tool's result) is the model's move.
                return IsInterruption(record)
                    ? new Verdict.Finished()
                    : new Verdict.Working(Wait.Model, Stamp(record));
            default:
                // Bookkeeping records say nothing about the turn.
                return null;
        }
    }

    private static Verdict? CodexVerdict(JsonElement record)
    {
        var type = record.TryGetProperty("payload", out var payload) && payload.ValueKind == JsonValueKind.Object
            ? Text(payload, "type")
            : null;
        return type switch
        {
            "task_complete" or "turn_aborted" => new Verdict.Finished(),
            "task_started" => new Verdict.Working(Wait.Model, Stamp(record)),
            "function_call" or "custom_tool_call" or "local_shell_call" or "web_search_call" or "tool_search_call"
                => new Verdict.Working(Wait.Tool, Stamp(record)),
            "function_call_output" or "custom_tool_call_output" or "tool_search_output"
                => new Verdict.Working(Wait.Model, Stamp(record)),
            _ => null,
        };
    }

    // Only Claude Code's own marker is looked for; the text is not kept.
    private static bool IsInterruption(JsonElement record)
    {
        if (!record.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object
            || !message.TryGetProperty("content", out var content))
        {
            return false;
        }

        if (content.ValueKind == JsonValueKind.String)
        {
            return content.GetString()!.Contains(InterruptionMarker, StringComparison.Ordinal);
        }

        return content.ValueKind == JsonValueKind.Array && content.EnumerateArray().Any(block =>
            block.ValueKind == JsonValueKind.Object
            && Text(block, "text") is { } text
            && text.Contains(InterruptionMarker, StringComparison.Ordinal));
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static DateTimeOffset? Stamp(JsonElement record) =>
        Text(record, "timestamp") is { } text
        && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at)
            ? at
            : null;

    /// <summary>The last stretch of a file as whole lines; the first is dropped unless the read began at the top.</summary>
    private static IReadOnlyList<string> Tail(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var start = Math.Max(stream.Length - TailBytes, 0);
            stream.Seek(start, SeekOrigin.Begin);
            var buffer = new byte[stream.Length - start];
            stream.ReadExactly(buffer);
            var lines = Encoding.UTF8.GetString(buffer).Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
            if (start > 0 && lines.Count > 0)
            {
                lines.RemoveAt(0);
            }

            return lines;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }
}
