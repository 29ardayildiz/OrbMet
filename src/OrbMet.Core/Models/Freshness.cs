namespace OrbMet.Core.Models;

/// <summary>
/// Age of the observation, independent of the connection state. A file that
/// exists is not proof of fresh data.
/// </summary>
public enum Freshness
{
    Fresh,
    Stale,
    Missing,
}
