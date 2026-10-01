namespace OrbMet.Capture;

internal static class Program
{
    private const int MaxInputBytes = 256 * 1024;

    private static int Main()
    {
        // Stage 0 skeleton only. Consumes stdin within a fixed bound and writes
        // nothing to stdout, so a chained status line still owns the terminal.
        // Parsing and atomic usage capture arrive in stage 2.
        try
        {
            using var stdin = Console.OpenStandardInput();
            var buffer = new byte[8192];
            var total = 0;

            int read;
            while ((read = stdin.Read(buffer, 0, buffer.Length)) > 0)
            {
                total += read;
                if (total >= MaxInputBytes)
                {
                    break;
                }
            }
        }
        catch (IOException)
        {
            // EOF and closed pipes are normal termination paths, not crashes.
        }

        return 0;
    }
}
