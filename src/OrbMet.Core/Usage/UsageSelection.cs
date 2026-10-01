using OrbMet.Core.Models;

namespace OrbMet.Core.Usage;

/// <summary>
/// Shared ring selection rule from master.md. The UI must not invent an
/// alternative ordering, and must never sum or average windows.
/// </summary>
public static class UsageSelection
{
    /// <summary>
    /// Picks the window shown in the usage ring: the account-wide window with
    /// the highest used percentage, or the highest grouped window when no
    /// account-wide window carries a value. Returns null when nothing is known.
    /// </summary>
    public static UsageWindow? SelectRingWindow(ProviderSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var withValue = snapshot.Windows
            .Where(window => window.UsedPercent is not null)
            .ToList();

        if (withValue.Count == 0)
        {
            return null;
        }

        var accountWide = withValue
            .Where(window => window.GroupId is null)
            .ToList();

        var candidates = accountWide.Count > 0 ? accountWide : withValue;

        return candidates
            .OrderByDescending(window => window.UsedPercent)
            .ThenBy(window => window.Id, StringComparer.Ordinal)
            .First();
    }
}
