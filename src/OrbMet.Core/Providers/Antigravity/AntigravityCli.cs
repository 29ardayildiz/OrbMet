using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace OrbMet.Core.Providers.Antigravity;

/// <summary>What one <c>agy -p /usage</c> run printed; both streams are capped.</summary>
public sealed record AntigravityCliOutput(string StandardOutput, string StandardError);

/// <summary>
/// Antigravity's command-line tool, <c>agy</c>, which answers its own
/// <c>/usage</c> command in print mode with the quota groups the language
/// server gives (design.md 4.4), so its limits can be read while neither
/// Antigravity nor Antigravity IDE is open. Measured 2026-11-17: about 3 s,
/// no model request (0 tokens, 0 turns, no conversation id) and nothing
/// written to its conversations, history or projects. It runs hidden from a
/// folder of OrbMet's own and never updates itself during a read; only the
/// process OrbMet started, with whatever it started, is ever ended.
/// </summary>
public static class AntigravityCli
{
    private const int MaxOutputChars = 1024 * 1024;
    private const int MaxErrorChars = 64 * 1024;

    /// <summary>agy.exe where its installer puts it, else on PATH; null when it is not installed.</summary>
    public static string? Locate()
    {
        var installed = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "agy", "bin", "agy.exe");
        return File.Exists(installed) ? installed : CommandSearch.Find("agy");
    }

    /// <summary>
    /// Runs <c>/usage</c> once and returns what it printed, whatever its exit
    /// code; null when it cannot be started. Cancelling ends the run.
    /// </summary>
    public static Task<AntigravityCliOutput?> RunUsageAsync(string executablePath, TimeSpan printTimeout, CancellationToken cancellationToken) =>
        RunUsageAsync(executablePath, printTimeout, WorkingDirectory(), cancellationToken);

    internal static async Task<AntigravityCliOutput?> RunUsageAsync(
        string executablePath, TimeSpan printTimeout, string workingDirectory, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        using var process = new Process { StartInfo = BuildStartInfo(executablePath, printTimeout, workingDirectory) };
        try
        {
            if (!process.Start())
            {
                return null;
            }
        }
        catch (Win32Exception)
        {
            return null;
        }

        process.StandardInput.Close();
        var output = ReadCappedAsync(process.StandardOutput, MaxOutputChars);
        var errors = ReadCappedAsync(process.StandardError, MaxErrorChars);
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        try
        {
            // Something it started may still hold the pipes open; what came is kept.
            await Task.WhenAll(output, errors).WaitAsync(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
        }

        return new AntigravityCliOutput(
            output.IsCompletedSuccessfully ? output.Result : "",
            errors.IsCompletedSuccessfully ? errors.Result : "");
    }

    private static ProcessStartInfo BuildStartInfo(string executablePath, TimeSpan printTimeout, string workingDirectory)
    {
        var info = new ProcessStartInfo
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = workingDirectory,
        };

        var extension = Path.GetExtension(executablePath);
        if (extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".bat", StringComparison.OrdinalIgnoreCase))
        {
            // A batch wrapper cannot be executed directly without a shell.
            info.FileName = "cmd.exe";
            info.ArgumentList.Add("/c");
            info.ArgumentList.Add(executablePath);
        }
        else
        {
            info.FileName = executablePath;
        }

        info.ArgumentList.Add("-p");
        info.ArgumentList.Add("/usage");
        info.ArgumentList.Add("--output-format");
        info.ArgumentList.Add("json");
        info.ArgumentList.Add("--print-timeout");
        info.ArgumentList.Add($"{Math.Max(1, (int)printTimeout.TotalSeconds)}s");

        // A read must not update the user's CLI behind their back.
        info.Environment["AGY_CLI_DISABLE_AUTO_UPDATE"] = "true";
        return info;
    }

    /// <summary>An empty folder of OrbMet's own, so no project of the user's is opened.</summary>
    private static string WorkingDirectory()
    {
        var folder = Path.Combine(Storage.OrbMetPaths.FromEnvironment().DataRoot, "agy");
        try
        {
            Directory.CreateDirectory(folder);
            return folder;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Path.GetTempPath();
        }
    }

    /// <summary>The stream's text up to <paramref name="cap"/> characters; the rest is read and dropped.</summary>
    private static async Task<string> ReadCappedAsync(StreamReader reader, int cap)
    {
        var text = new StringBuilder();
        var buffer = new char[8192];
        try
        {
            int read;
            while ((read = await reader.ReadAsync(buffer.AsMemory(), CancellationToken.None).ConfigureAwait(false)) > 0)
            {
                var room = cap - text.Length;
                if (room > 0)
                {
                    text.Append(buffer, 0, Math.Min(read, room));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
        }

        return text.ToString();
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (Win32Exception)
        {
        }
    }
}
