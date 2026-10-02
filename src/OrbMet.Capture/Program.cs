using OrbMet.Core.Providers.Claude;
using OrbMet.Core.Storage;

namespace OrbMet.Capture;

internal static class Program
{
    private const int MaxInputBytes = 256 * 1024;

    private static async Task<int> Main()
    {
        // This helper runs after every Claude response, inside the user's
        // terminal. Anything unexpected is swallowed: a status line that errors
        // out or hangs is far worse than one that says nothing.
        try
        {
            var input = await ReadInputAsync(MaxInputBytes).ConfigureAwait(false);

            var paths = OrbMetPaths.FromEnvironment();
            var runner = new ClaudeStatusLineRunner(
                new ClaudeCaptureStore(paths.ClaudeCapturesDirectory),
                new ClaudeIntegrationRecordStore(paths.ClaudeIntegrationFile));

            using var stdout = Console.OpenStandardOutput();
            await runner.RunAsync(input, stdout, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception)
        {
        }

        return 0;
    }

    private static async Task<byte[]> ReadInputAsync(int maxBytes)
    {
        using var stdin = Console.OpenStandardInput();
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];

        int read;
        while (buffer.Length < maxBytes &&
               (read = await stdin.ReadAsync(chunk).ConfigureAwait(false)) > 0)
        {
            var take = (int)Math.Min(read, maxBytes - buffer.Length);
            buffer.Write(chunk, 0, take);
        }

        return buffer.ToArray();
    }
}
