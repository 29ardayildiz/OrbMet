namespace OrbMet.Core.Providers.Codex;

/// <summary>Wire shape of the account/rateLimits/read result. Only the fields OrbMet reads are mapped.</summary>
internal sealed class GetAccountRateLimitsResponseDto
{
    public RateLimitSnapshotDto? RateLimits { get; set; }

    public Dictionary<string, RateLimitSnapshotDto>? RateLimitsByLimitId { get; set; }

    public string? AccountId { get; set; }

    public bool? OrdinaryUsageAllowed { get; set; }
}

internal sealed class RateLimitSnapshotDto
{
    public string? LimitId { get; set; }

    public string? LimitName { get; set; }

    public string? PlanType { get; set; }

    public string? NormalModelSlug { get; set; }

    public RateLimitWindowDto? Primary { get; set; }

    public RateLimitWindowDto? Secondary { get; set; }

    public string? RateLimitReachedType { get; set; }

    public bool? SpendControlReached { get; set; }
}

internal sealed class RateLimitWindowDto
{
    public int? UsedPercent { get; set; }

    public long? WindowDurationMins { get; set; }

    public long? ResetsAt { get; set; }
}
