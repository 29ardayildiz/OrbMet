using OrbMet.Core.Models;
using OrbMet.Core.Providers;
using OrbMet.Core.Providers.Codex;

namespace OrbMet.Core.Usage;

/// <summary>
/// Which providers this machine has and which the strip shows (design.md
/// 4.4). Codex is here when its CLI is on PATH; Claude Code when its CLI is,
/// or its settings folder exists. Only file and folder existence is checked.
/// </summary>
public static class ProviderPresence
{
    public static readonly IReadOnlyList<ProviderId> All = new[] { ProviderId.ClaudeCode, ProviderId.Codex };

    /// <summary>The providers found on this machine now.</summary>
    public static IReadOnlySet<ProviderId> Detect()
    {
        var found = new HashSet<ProviderId>();
        if (ClaudeInstalled())
        {
            found.Add(ProviderId.ClaudeCode);
        }

        if (CodexExecutableLocator.Locate(null) is not null)
        {
            found.Add(ProviderId.Codex);
        }

        return found;
    }

    /// <summary>
    /// The providers to show, in order: those found and not hidden by the
    /// user; when that leaves none, every one the user has not hidden, so the
    /// strip says what is missing rather than standing empty; and never none.
    /// </summary>
    public static IReadOnlyList<ProviderId> Visible(IReadOnlySet<ProviderId> installed, IReadOnlySet<ProviderId> hidden)
    {
        ArgumentNullException.ThrowIfNull(installed);
        ArgumentNullException.ThrowIfNull(hidden);

        var wanted = All.Where(id => !hidden.Contains(id)).ToList();
        if (wanted.Count == 0)
        {
            return All;
        }

        var shown = wanted.Where(installed.Contains).ToList();
        return shown.Count > 0 ? shown : wanted;
    }

    private static bool ClaudeInstalled()
    {
        if (CommandSearch.Find("claude") is not null)
        {
            return true;
        }

        var folder = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") is { Length: > 0 } configured
            ? configured
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
        return Directory.Exists(folder);
    }
}
