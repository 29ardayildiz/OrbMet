namespace OrbMet.Core.Providers.Codex;

/// <summary>
/// Finds the Codex CLI at runtime. A user-configured path wins; otherwise
/// PATH is searched (<see cref="CommandSearch"/>).
/// </summary>
public static class CodexExecutableLocator
{
    public static string? Locate(string? configuredPath) =>
        !string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath)
            ? configuredPath
            : CommandSearch.Find("codex");

    internal static string? Locate(string? configuredPath, IEnumerable<string?> searchPaths, Func<string, bool> fileExists) =>
        !string.IsNullOrWhiteSpace(configuredPath) && fileExists(configuredPath)
            ? configuredPath
            : CommandSearch.Find("codex", searchPaths, fileExists);
}
