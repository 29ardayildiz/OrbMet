using System.Text.Json;
using OrbMet.Core.Models;

namespace OrbMet.Core.Providers.Codex;

/// <summary>
/// Maps the account/rateLimits/read result onto the shared usage model. The
/// provider decides which windows exist; this type never invents a period,
/// sums windows, or clamps a reported percentage.
/// </summary>
public static class CodexUsageNormalizer
{
    public static ProviderSnapshot Normalize(JsonElement result, DateTimeOffset receivedAtUtc)
    {
        GetAccountRateLimitsResponseDto dto;
        try
        {
            dto = result.Deserialize<GetAccountRateLimitsResponseDto>(CodexJson.Options)
                  ?? throw new JsonException("Rate limits result was null.");
        }
        catch (JsonException ex)
        {
            throw new CodexReadException(CodexFailureKind.ProtocolError, "protocol-error", ex);
        }

        var windows = new List<UsageWindow>();
        if (dto.RateLimitsByLimitId is { Count: > 0 })
        {
            foreach (var pair in dto.RateLimitsByLimitId.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                AddSnapshotWindows(windows, pair.Value, pair.Key, dto.OrdinaryUsageAllowed);
            }
        }
        else if (dto.RateLimits is not null)
        {
            AddSnapshotWindows(windows, dto.RateLimits, dto.RateLimits.LimitId, dto.OrdinaryUsageAllowed);
        }

        return new ProviderSnapshot
        {
            ProviderId = ProviderId.Codex,
            SourceKind = SourceKind.CodexAppServer,
            AccountScopeKey = string.IsNullOrWhiteSpace(dto.AccountId) ? "codex" : dto.AccountId!,
            ObservedAtUtc = receivedAtUtc,
            ReceivedAtUtc = receivedAtUtc,
            LastAttemptAtUtc = receivedAtUtc,
            ConnectionState = ConnectionState.Ready,
            Freshness = Freshness.Fresh,
            PlanName = ResolvePlanName(dto),
            Windows = windows,
        };
    }

    private static void AddSnapshotWindows(
        List<UsageWindow> windows,
        RateLimitSnapshotDto snapshot,
        string? limitId,
        bool? ordinaryUsageAllowed)
    {
        var groupId = string.IsNullOrWhiteSpace(limitId) ? null : limitId;
        var scopeLabel = groupId is null
            ? null
            : FirstNonEmpty(snapshot.LimitName, snapshot.NormalModelSlug, groupId);
        var exhaustion = ResolveExhaustion(snapshot, ordinaryUsageAllowed);

        AddWindow(windows, snapshot.Primary, groupId, scopeLabel, "primary", exhaustion);
        AddWindow(windows, snapshot.Secondary, groupId, scopeLabel, "secondary", exhaustion);
    }

    private static void AddWindow(
        List<UsageWindow> windows,
        RateLimitWindowDto? window,
        string? groupId,
        string? scopeLabel,
        string role,
        ExhaustionEvidence exhaustion)
    {
        if (window is null)
        {
            return;
        }

        windows.Add(new UsageWindow
        {
            Id = $"{(groupId ?? "default")}.{role}",
            ScopeLabel = scopeLabel,
            GroupId = groupId,
            UsedPercent = window.UsedPercent,
            DurationSeconds = window.WindowDurationMins is { } minutes ? minutes * 60 : null,
            DurationOrigin = window.WindowDurationMins is null ? DurationOrigin.Unknown : DurationOrigin.Reported,
            ResetsAtUtc = window.ResetsAt is { } seconds ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null,
            ExhaustionEvidence = exhaustion,
        });
    }

    private static ExhaustionEvidence ResolveExhaustion(RateLimitSnapshotDto snapshot, bool? ordinaryUsageAllowed)
    {
        if (!string.IsNullOrWhiteSpace(snapshot.RateLimitReachedType))
        {
            return ExhaustionEvidence.Exhausted;
        }

        if (snapshot.SpendControlReached == true)
        {
            return ExhaustionEvidence.Exhausted;
        }

        if (ordinaryUsageAllowed == false)
        {
            return ExhaustionEvidence.Exhausted;
        }

        if (ordinaryUsageAllowed == true)
        {
            return ExhaustionEvidence.NotExhausted;
        }

        return ExhaustionEvidence.Unknown;
    }

    private static string? ResolvePlanName(GetAccountRateLimitsResponseDto dto)
    {
        if (!string.IsNullOrWhiteSpace(dto.RateLimits?.PlanType))
        {
            return dto.RateLimits!.PlanType;
        }

        if (dto.RateLimitsByLimitId is { Count: > 0 })
        {
            foreach (var snapshot in dto.RateLimitsByLimitId.Values)
            {
                if (!string.IsNullOrWhiteSpace(snapshot.PlanType))
                {
                    return snapshot.PlanType;
                }
            }
        }

        return null;
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}
