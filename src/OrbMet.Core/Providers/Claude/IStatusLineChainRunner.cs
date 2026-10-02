namespace OrbMet.Core.Providers.Claude;

/// <summary>
/// Runs a status-line command that was already installed before OrbMet, feeding
/// it the same stdin, so installing OrbMet does not throw away the user's own
/// status line.
/// </summary>
public interface IStatusLineChainRunner
{
    /// <summary>
    /// Runs the command with a bounded wait. Returns null when the command
    /// could not run or exceeded its budget; the caller then prints a default.
    /// </summary>
    Task<string?> RunAsync(
        string command,
        byte[] input,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}
