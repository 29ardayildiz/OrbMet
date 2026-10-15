using System.Globalization;
using System.Text.Json;
using OrbMet.Core.Models;

namespace OrbMet.Core.Providers.Antigravity;

/// <summary>
/// Maps the language server's RetrieveUserQuotaSummary reply onto the shared
/// usage model. The reply says what is left (<c>remainingFraction</c> 1 is
/// nothing used) in groups of buckets, each with its own window; a bucket
/// whose window or figure cannot be read keeps no invented value.
/// </summary>
public static class AntigravityQuotaParser
{
    /// <summary>
    /// The windows in the reply, or null when it is not a quota summary at
    /// all. An empty list is a reply that holds no limits.
    /// </summary>
    public static IReadOnlyList<UsageWindow>? Parse(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("response", out var response) ||
                response.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var windows = new List<UsageWindow>();
            if (!response.TryGetProperty("groups", out var groups) || groups.ValueKind != JsonValueKind.Array)
            {
                return windows;
            }

            foreach (var group in groups.EnumerateArray())
            {
                if (group.ValueKind != JsonValueKind.Object ||
                    !group.TryGetProperty("buckets", out var buckets) ||
                    buckets.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                var scope = Scope(Text(group, "displayName"));
                foreach (var bucket in buckets.EnumerateArray())
                {
                    if (Window(bucket, scope) is { } window)
                    {
                        windows.Add(window);
                    }
                }
            }

            return windows;
        }
    }

    private static UsageWindow? Window(JsonElement bucket, string? scope)
    {
        if (bucket.ValueKind != JsonValueKind.Object ||
            Text(bucket, "bucketId") is not { } id ||
            Length(Text(bucket, "window")) is not { } seconds)
        {
            return null;
        }

        // A missing figure stays missing: neither nothing used nor all of it.
        double? remaining = bucket.TryGetProperty("remainingFraction", out var fraction) && fraction.ValueKind == JsonValueKind.Number
            ? fraction.GetDouble()
            : null;

        // While nothing is used the server sends "now plus the window" as the
        // reset, moving on every read (measured 2026-10-15), so it is not one.
        var notStarted = remaining >= 1;
        DateTimeOffset? resetsAt = !notStarted &&
            Text(bucket, "resetTime") is { } reset &&
            DateTimeOffset.TryParse(reset, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
                ? parsed.ToUniversalTime()
                : null;

        return new UsageWindow
        {
            Id = id,
            GroupId = GroupOf(id),
            ScopeLabel = scope,
            UsedPercent = remaining is { } left ? (1 - left) * 100 : null,
            DurationSeconds = seconds,
            DurationOrigin = DurationOrigin.Reported,
            ResetsAtUtc = resetsAt,
            NotStarted = notStarted,
        };
    }

    /// <summary>"gemini-5h" belongs to "gemini": the buckets of one group share the prefix.</summary>
    private static string GroupOf(string bucketId)
    {
        var dash = bucketId.LastIndexOf('-');
        return dash > 0 ? bucketId[..dash] : bucketId;
    }

    /// <summary>"Gemini Models" is "Gemini" on the card; "Claude and GPT models" is "Claude and GPT".</summary>
    private static string? Scope(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return null;
        }

        var name = displayName.Trim();
        return name.EndsWith(" models", StringComparison.OrdinalIgnoreCase) ? name[..^" models".Length].TrimEnd() : name;
    }

    /// <summary>"5h" and "weekly" are what the server sends; "12h" or "3d" are read too, anything else is left out.</summary>
    private static long? Length(string? window)
    {
        switch (window?.Trim().ToLowerInvariant())
        {
            case null or "":
                return null;
            case "weekly":
                return 7 * 24 * 3600;
            case "daily":
                return 24 * 3600;
            case var text when text.Length > 1 && long.TryParse(text[..^1], NumberStyles.None, CultureInfo.InvariantCulture, out var count) && count > 0:
                return text[^1] switch
                {
                    'h' => count * 3600,
                    'd' => count * 24 * 3600,
                    _ => null,
                };
            default:
                return null;
        }
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text
            ? text
            : null;
}
