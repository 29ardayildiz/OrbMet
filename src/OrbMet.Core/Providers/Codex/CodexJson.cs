using System.Text.Json;

namespace OrbMet.Core.Providers.Codex;

/// <summary>JSON settings for the Codex app-server protocol (camelCase, case-insensitive).</summary>
internal static class CodexJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
