namespace OrbMet.Core.Models;

/// <summary>
/// Normalised last observation for a single provider. Carries observation time
/// and receipt time separately, because re-reading a file does not refresh the
/// underlying provider data.
/// </summary>
public sealed record ProviderSnapshot
{
    public required ProviderId ProviderId { get; init; }

    public required SourceKind SourceKind { get; init; }

    /// <summary>
    /// Local scope key. It reflects a real account identity only when the
    /// provider supplies one; otherwise it stays at the source scope.
    /// </summary>
    public string AccountScopeKey { get; init; } = "source";

    /// <summary>When the provider actually produced the observation.</summary>
    public DateTimeOffset? ObservedAtUtc { get; init; }

    /// <summary>When OrbMet received the record. This never replaces observation age.</summary>
    public DateTimeOffset ReceivedAtUtc { get; init; }

    /// <summary>Last connection or read attempt, successful or not.</summary>
    public DateTimeOffset? LastAttemptAtUtc { get; init; }

    public ConnectionState ConnectionState { get; init; } = ConnectionState.AwaitingData;

    public Freshness Freshness { get; init; } = Freshness.Missing;

    /// <summary>Plan name only when the source reports it. It is never guessed.</summary>
    public string? PlanName { get; init; }

    /// <summary>Safe classification that the UI can localise into text.</summary>
    public string? ErrorCode { get; init; }

    public IReadOnlyList<UsageWindow> Windows { get; init; } = Array.Empty<UsageWindow>();
}
