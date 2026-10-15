namespace OrbMet.Core.Models;

/// <summary>
/// One provider-reported quota window, for example a five hour or weekly limit.
/// </summary>
public sealed record UsageWindow
{
    /// <summary>Stable identity of the window within its provider and group.</summary>
    public required string Id { get; init; }

    /// <summary>Model or independent quota group, when the provider reports one.</summary>
    public string? ScopeLabel { get; init; }

    /// <summary>Group key used to tell account-wide windows apart from grouped ones.</summary>
    public string? GroupId { get; init; }

    /// <summary>
    /// Provider-reported used percentage. Null means the value is missing, not
    /// zero. Values are not clamped here, only at the drawing layer.
    /// </summary>
    public double? UsedPercent { get; init; }

    /// <summary>Window length in seconds when known.</summary>
    public long? DurationSeconds { get; init; }

    public DurationOrigin DurationOrigin { get; init; } = DurationOrigin.Unknown;

    /// <summary>Reset instant in UTC when the provider reports one.</summary>
    public DateTimeOffset? ResetsAtUtc { get; init; }

    public ExhaustionEvidence ExhaustionEvidence { get; init; } = ExhaustionEvidence.Unknown;

    /// <summary>
    /// Nothing has been used in this window yet, so it has not started and
    /// has no real reset time; one the provider sends then is left out.
    /// </summary>
    public bool NotStarted { get; init; }
}
