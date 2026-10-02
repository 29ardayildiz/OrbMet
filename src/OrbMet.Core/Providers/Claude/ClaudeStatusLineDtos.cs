using System.Text.Json.Serialization;

namespace OrbMet.Core.Providers.Claude;

/// <summary>
/// Wire shape of the Claude Code status-line blob. Only the fields OrbMet keeps
/// are mapped; prompts, transcripts and working directories are never read.
/// Field names are snake_case, as the documented payload supplies them.
/// </summary>
internal sealed class ClaudeStatusLineDto
{
    public ClaudeModelDto? Model { get; set; }

    [JsonPropertyName("session_id")]
    public string? SessionId { get; set; }

    [JsonPropertyName("rate_limits")]
    public ClaudeRateLimitsDto? RateLimits { get; set; }
}

internal sealed class ClaudeModelDto
{
    public string? Id { get; set; }

    [JsonPropertyName("display_name")]
    public string? DisplayName { get; set; }
}

internal sealed class ClaudeRateLimitsDto
{
    [JsonPropertyName("five_hour")]
    public ClaudeRateLimitWindowDto? FiveHour { get; set; }

    [JsonPropertyName("seven_day")]
    public ClaudeRateLimitWindowDto? SevenDay { get; set; }

    [JsonPropertyName("spend_limit")]
    public ClaudeRateLimitWindowDto? SpendLimit { get; set; }
}

internal sealed class ClaudeRateLimitWindowDto
{
    [JsonPropertyName("used_percentage")]
    public double? UsedPercentage { get; set; }

    /// <summary>Unix epoch seconds when the window resets.</summary>
    [JsonPropertyName("resets_at")]
    public double? ResetsAt { get; set; }
}
