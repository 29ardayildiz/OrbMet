namespace OrbMet.Core.Models;

/// <summary>
/// Connection state of a provider, separate from data freshness. A ready
/// connection can still be carrying stale data.
/// </summary>
public enum ConnectionState
{
    Disabled,
    MissingTool,
    AwaitingData,
    Ready,
    SignInRequired,
    Unsupported,
    Error,
}
