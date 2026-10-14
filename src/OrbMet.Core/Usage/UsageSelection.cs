using OrbMet.Core.Models;

namespace OrbMet.Core.Usage;

/// <summary>
/// Shared ring selection rule from master.md. The UI must not invent an
/// alternative ordering, and must never sum or average windows.
/// </summary>
public static class UsageSelection
{
    /// <summary>The window people pace their work by, shown first when there is one.</summary>
    public const long FiveHourSeconds = 5 * 60 * 60;

    /// <summary>
    /// Picks the window shown in the usage ring. The candidates are the
    /// account-wide windows with a value, or the grouped ones when there are
    /// none. Among them a 5-hour window wins even if a longer one is fuller
    /// (the card lists every window); otherwise the highest used percentage.
    /// Returns null when nothing is known.
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

        // Codex reports every window under a limit group, so the 5-hour
        // window is looked for among the same candidates, not only account-wide.
        var fiveHour = candidates
            .Where(window => window.DurationSeconds == FiveHourSeconds)
            .ToList();

        return (fiveHour.Count > 0 ? fiveHour : candidates)
            .OrderByDescending(window => window.UsedPercent)
            .ThenBy(window => window.Id, StringComparer.Ordinal)
            .First();
    }
}
