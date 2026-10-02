using OrbMet.Core.Models;

namespace OrbMet.Core.Providers.Claude;

/// <summary>
/// Turns a banked capture into the shared usage model. A window whose reset
/// time has passed is dropped rather than aged: this is a push source, so an
/// old blob still holds a window that no longer exists. Percentages are neither
/// clamped nor summed, and a missing one stays null.
/// </summary>
public static class ClaudeCaptureNormalizer
{
    public static readonly TimeSpan DefaultFreshFor = TimeSpan.FromMinutes(10);

    public static ProviderSnapshot Normalize(
        ClaudeCaptureRecord? record,
        DateTimeOffset now,
        TimeSpan freshFor)
    {
        if (record is null)
        {
            return new ProviderSnapshot
            {
                ProviderId = ProviderId.ClaudeCode,
                SourceKind = SourceKind.ClaudeStatusLine,
                AccountScopeKey = "claude-code",
                ReceivedAtUtc = now,
                LastAttemptAtUtc = now,
                ConnectionState = ConnectionState.AwaitingData,
                Freshness = Freshness.Missing,
                ErrorCode = "claude-no-capture",
            };
        }

        var windows = record.Windows
            .Where(window => window.ResetsAtUtc is null || window.ResetsAtUtc > now)
            .Select(window => new UsageWindow
            {
                Id = $"claudeCode.{window.Key}",
                ScopeLabel = null,
                GroupId = null,
                UsedPercent = window.UsedPercent,
                DurationSeconds = DurationFor(window.Key),
                DurationOrigin = window.Key == "spend_limit"
                    ? DurationOrigin.Unknown
                    : DurationOrigin.KnownSemantic,
                ResetsAtUtc = window.ResetsAtUtc,
                ExhaustionEvidence = ExhaustionEvidence.Unknown,
            })
            .ToList();

        var age = now - record.CapturedAtUtc;
        var freshness = age <= freshFor && age >= TimeSpan.Zero ? Freshness.Fresh : Freshness.Stale;

        return new ProviderSnapshot
        {
            ProviderId = ProviderId.ClaudeCode,
            SourceKind = SourceKind.ClaudeStatusLine,
            AccountScopeKey = "claude-code",
            ObservedAtUtc = record.CapturedAtUtc,
            ReceivedAtUtc = now,
            LastAttemptAtUtc = now,
            ConnectionState = windows.Count == 0 ? ConnectionState.AwaitingData : ConnectionState.Ready,
            Freshness = freshness,
            PlanName = null,
            ErrorCode = windows.Count == 0 ? "claude-no-current-window" : null,
            Windows = windows,
        };
    }

    private static long? DurationFor(string key) => key switch
    {
        "five_hour" => 5 * 60 * 60,
        "seven_day" => 7 * 24 * 60 * 60,
        _ => null,
    };
}
