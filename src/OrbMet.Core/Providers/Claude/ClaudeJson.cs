using System.Text.Json;

namespace OrbMet.Core.Providers.Claude;

/// <summary>JSON settings for the Claude Code status-line payload.</summary>
internal static class ClaudeJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
