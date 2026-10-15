namespace OrbMet.Core.Providers;

/// <summary>
/// Finds a command-line tool on PATH: this process's first, then the user and
/// machine PATH Windows has saved, since a process started by an installer or
/// at sign-in can carry an older PATH than a fresh terminal. Machine-specific
/// paths are never hardcoded.
/// </summary>
public static class CommandSearch
{
    /// <summary>The first of "name.exe", "name.cmd", "name.bat" found, or null.</summary>
    public static string? Find(string command) => Find(command, SearchPaths(), File.Exists);

    internal static string? Find(string command, IEnumerable<string?> searchPaths, Func<string, bool> fileExists)
    {
        var names = new[] { command + ".exe", command + ".cmd", command + ".bat" };
        foreach (var path in searchPaths)
        {
            if (string.IsNullOrEmpty(path))
            {
                continue;
            }

            foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                foreach (var name in names)
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
