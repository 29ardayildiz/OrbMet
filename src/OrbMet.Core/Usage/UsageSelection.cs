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

    public const long WeeklySeconds = 7 * 24 * 60 * 60;

    /// <summary>
    /// Picks the window shown in the usage ring. The candidates are the
    /// account-wide windows with a value, or the grouped ones when there are
    /// none. Among them the chosen window wins even if another is fuller (the
    /// card lists every window): the 5-hour one by default, the weekly one, or
    /// for <see cref="RingChoice.ClosestToLimit"/> the highest used percentage.
    /// A weekly choice with no weekly window falls back to the 5-hour one, and
    /// a missing 5-hour window to the highest. Returns null when nothing is known.
    /// </summary>
    public static UsageWindow? SelectRingWindow(ProviderSnapshot snapshot, RingChoice choice = RingChoice.FiveHour)
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

        // Codex reports every window under a limit group, so the chosen
        // window is looked for among the same candidates, not only account-wide.
        var durations = choice switch
        {
            RingChoice.Weekly => new[] { WeeklySeconds, FiveHourSeconds },
            RingChoice.FiveHour => new[] { FiveHourSeconds },
            _ => Array.Empty<long>(),
        };

        foreach (var duration in durations)
        {
            var matching = candidates.Where(window => window.DurationSeconds == duration).ToList();
            if (matching.Count > 0)
            {
                return Fullest(matching);
            }
        }

        return Fullest(candidates);
    }

    private static UsageWindow Fullest(IEnumerable<UsageWindow> windows) =>
        windows
            .OrderByDescending(window => window.UsedPercent)
            .ThenBy(window => window.Id, StringComparer.Ordinal)
            .First();
}
