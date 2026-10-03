using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OrbMet.Core.Providers.Claude;

/// <summary>
/// Reads the documented Claude Code status-line JSON and keeps only the usage
/// figures. Anything unexpected yields no record: the status line runs after
/// every response, so staying quiet is better than failing.
/// </summary>
public static class ClaudeStatusLineParser
{
    private static readonly (string Key, Func<ClaudeRateLimitsDto, ClaudeRateLimitWindowDto?> Read)[] KnownWindows =
    {
        ("five_hour", limits => limits.FiveHour),
        ("seven_day", limits => limits.SevenDay),
        ("spend_limit", limits => limits.SpendLimit),
    };

    /// <summary>
    /// Builds a capture record, or null when the payload has no usable
    /// rate_limits block. The reset time is kept even when a percentage is
    /// missing, and a missing percentage is kept null rather than zeroed.
    /// </summary>
    public static ClaudeCaptureRecord? Parse(string json, DateTimeOffset capturedAtUtc) =>
        Parse(json, capturedAtUtc, out _);

    /// <summary>
    /// Same as <see cref="Parse(string, DateTimeOffset)"/>, also reporting
    /// schema-level facts (readable JSON, rate_limits present, window names)
    /// with no values, so a capture that finds nothing can still be explained.
    /// </summary>
    public static ClaudeCaptureRecord? Parse(
        string json,
        DateTimeOffset capturedAtUtc,
        out ClaudeStatusLineParseInfo info)
    {
        info = ClaudeStatusLineParseInfo.Empty;

        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        ClaudeStatusLineDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<ClaudeStatusLineDto>(json, ClaudeJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }

        info = new ClaudeStatusLineParseInfo(JsonReadable: true, HasRateLimits: false, WindowKeys: Array.Empty<string>());

        if (dto?.RateLimits is null)
        {
            return null;
        }

        var windows = new List<ClaudeCaptureWindow>();
        var windowKeys = new List<string>();
        foreach (var (key, read) in KnownWindows)
        {
            var window = read(dto.RateLimits);
            if (window is null)
            {
                continue;
            }

            windowKeys.Add(key);
            windows.Add(new ClaudeCaptureWindow
            {
                Key = key,
                UsedPercent = SanitizePercent(window.UsedPercentage),
                ResetsAtUtc = ToUtc(window.ResetsAt),
            });
        }

        info = new ClaudeStatusLineParseInfo(JsonReadable: true, HasRateLimits: true, WindowKeys: windowKeys);

        if (windows.Count == 0)
        {
            return null;
        }

        return new ClaudeCaptureRecord
        {
            CapturedAtUtc = capturedAtUtc,
            SessionHash = HashSessionId(dto.SessionId),
            Windows = windows,
        };
    }

    /// <summary>
    /// Rejects negative, NaN and infinite percentages. Values above 100 are
    /// kept: the documented spend_limit can exceed 100, so clamping here would
    /// rewrite the provider's own number.
    /// </summary>
    private static double? SanitizePercent(double? value)
    {
        if (value is not { } percent)
        {
            return null;
        }

        if (double.IsNaN(percent) || double.IsInfinity(percent) || percent < 0)
        {
            return null;
        }

        return percent;
    }

    private static DateTimeOffset? ToUtc(double? epochSeconds)
    {
        if (epochSeconds is not { } value || double.IsNaN(value) || double.IsInfinity(value))
        {
            return null;
        }

        try
        {
            return DateTimeOffset.FromUnixTimeSeconds((long)value);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>
    /// A stable, non-reversible session separator. The raw session id is never
    /// stored, shown or used as an account identity.
    /// </summary>
    private static string? HashSessionId(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return null;
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sessionId));
        return Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
    }
}
