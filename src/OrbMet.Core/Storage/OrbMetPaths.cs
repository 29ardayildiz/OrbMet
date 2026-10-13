namespace OrbMet.Core.Storage;

/// <summary>
/// Resolves OrbMet's local data locations. The runtime data root lives under
/// %LOCALAPPDATA%\OrbMet and never inside the source repository. Tests inject
/// an isolated root instead of touching the real one.
/// </summary>
public sealed class OrbMetPaths
{
    public OrbMetPaths(string dataRoot, string? claudeSettingsFile = null)
    {
        DataRoot = string.IsNullOrWhiteSpace(dataRoot)
            ? throw new ArgumentException("A data root is required.", nameof(dataRoot))
            : dataRoot;
        ClaudeSettingsFile = claudeSettingsFile ?? ResolveClaudeSettingsFile();
    }

    public string DataRoot { get; }

    /// <summary>
    /// Claude's settings.json. Honours CLAUDE_CONFIG_DIR, otherwise ~/.claude.
    /// Null when the user profile cannot be resolved, which only blocks the
    /// connect action, not reading captures.
    /// </summary>
    public string? ClaudeSettingsFile { get; }

    public string ClaudeCapturesDirectory => Path.Combine(DataRoot, "captures", "claude");

    public string IntegrationsDirectory => Path.Combine(DataRoot, "integrations");

    public string ClaudeIntegrationFile => Path.Combine(IntegrationsDirectory, "claude.json");

    public string ClaudeLockFile => Path.Combine(IntegrationsDirectory, "claude.lock");

    public string BackupsDirectory => Path.Combine(DataRoot, "backups");

    public string LogsDirectory => Path.Combine(DataRoot, "logs");

    public string PanelPositionFile => Path.Combine(DataRoot, "panel.json");

    public static OrbMetPaths FromEnvironment()
    {
        var localAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        }

        return new OrbMetPaths(Path.Combine(localAppData, "OrbMet"));
    }

    /// <summary>
    /// The default user settings file. Project or managed settings can take
    /// precedence over this, which is not yet verified on a real machine.
    /// </summary>
    public static string? ResolveClaudeSettingsFile()
    {
        var configDir = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        if (!string.IsNullOrWhiteSpace(configDir))
        {
            return Path.Combine(configDir, "settings.json");
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home))
        {
            return null;
        }

        return Path.Combine(home, ".claude", "settings.json");
    }
}
