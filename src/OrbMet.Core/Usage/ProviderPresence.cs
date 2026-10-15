using OrbMet.Core.Models;
using OrbMet.Core.Providers;
using OrbMet.Core.Providers.Antigravity;
using OrbMet.Core.Providers.Codex;

namespace OrbMet.Core.Usage;

/// <summary>
/// Which providers this machine has and which the strip shows (design.md
/// 4.4). Codex is here when its CLI is on PATH; Claude Code when its CLI is,
/// or its settings folder exists; Antigravity when Antigravity or Antigravity
/// IDE is installed for this user. Only file and folder existence is checked.
/// </summary>
public static class ProviderPresence
{
    public static readonly IReadOnlyList<ProviderId> All = new[] { ProviderId.ClaudeCode, ProviderId.Codex, ProviderId.Antigravity };

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

        if (AntigravityServers.Installed())
        {
            found.Add(ProviderId.Antigravity);
        }

        return found;
    }

    /// <summary>
    /// What the strip stands for when nothing is found: Claude Code and Codex.
    /// Antigravity is shown only when it is installed.
    /// </summary>
    public static readonly IReadOnlyList<ProviderId> Main = new[] { ProviderId.ClaudeCode, ProviderId.Codex };

    /// <summary>
    /// The providers to show, in order: those found and not hidden by the
    /// user; when that leaves none, Claude Code and Codex unless hidden, so
    /// the strip says what is missing rather than standing empty; and never none.
    /// </summary>
    public static IReadOnlyList<ProviderId> Visible(IReadOnlySet<ProviderId> installed, IReadOnlySet<ProviderId> hidden)
    {
        ArgumentNullException.ThrowIfNull(installed);
        ArgumentNullException.ThrowIfNull(hidden);

        var shown = All.Where(id => !hidden.Contains(id) && installed.Contains(id)).ToList();
        if (shown.Count > 0)
        {
            return shown;
        }

        var missing = Main.Where(id => !hidden.Contains(id)).ToList();
        return missing.Count > 0 ? missing : Main;
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
