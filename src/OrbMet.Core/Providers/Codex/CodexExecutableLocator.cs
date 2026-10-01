namespace OrbMet.Core.Providers.Codex;

/// <summary>
/// Finds the Codex CLI at runtime. A user-configured path wins; otherwise PATH
/// is searched. Machine-specific paths are never hardcoded.
/// </summary>
public static class CodexExecutableLocator
{
    private static readonly string[] CandidateNames = { "codex.exe", "codex.cmd", "codex.bat" };

    public static string? Locate(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
        {
            return configuredPath;
        }

        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var name in CandidateNames)
            {
                string candidate;
                try
                {
                    candidate = Path.Combine(directory.Trim(), name);
                }
                catch (ArgumentException)
                {
                    continue;
                }

                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }
}
