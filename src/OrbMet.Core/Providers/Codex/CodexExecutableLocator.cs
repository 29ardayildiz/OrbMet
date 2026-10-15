namespace OrbMet.Core.Providers.Codex;

/// <summary>
/// Finds the Codex CLI at runtime. A user-configured path wins; otherwise PATH
/// is searched: this process's first, then the user and machine PATH Windows
/// has saved, since a process started by an installer or at sign-in can carry
/// an older PATH than a fresh terminal. Machine-specific paths are never
/// hardcoded.
/// </summary>
public static class CodexExecutableLocator
{
    private static readonly string[] CandidateNames = { "codex.exe", "codex.cmd", "codex.bat" };

    public static string? Locate(string? configuredPath) =>
        Locate(configuredPath, SearchPaths(), File.Exists);

    internal static string? Locate(string? configuredPath, IEnumerable<string?> searchPaths, Func<string, bool> fileExists)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath) && fileExists(configuredPath))
        {
            return configuredPath;
        }

        foreach (var path in searchPaths)
        {
            if (string.IsNullOrEmpty(path))
            {
                continue;
            }

            foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                foreach (var name in CandidateNames)
                {
                    string candidate;
                    try
                    {
                        candidate = Path.Combine(Environment.ExpandEnvironmentVariables(directory.Trim()), name);
                    }
                    catch (ArgumentException)
                    {
                        continue;
                    }

                    if (fileExists(candidate))
                    {
                        return candidate;
                    }
                }
            }
        }

        return null;
    }

    private static IEnumerable<string?> SearchPaths()
    {
        yield return Environment.GetEnvironmentVariable("PATH");

        if (OperatingSystem.IsWindows())
        {
            yield return Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User);
            yield return Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine);
        }
    }
}
