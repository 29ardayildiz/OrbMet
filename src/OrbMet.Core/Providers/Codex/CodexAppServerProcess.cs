using System.Diagnostics;

namespace OrbMet.Core.Providers.Codex;

/// <summary>
/// Owns a single Codex app-server child process started by OrbMet. Only this
/// process is ever terminated; the user's other Codex sessions are untouched.
/// stderr is drained and discarded so a full pipe cannot deadlock the helper.
/// </summary>
public sealed class CodexAppServerProcess : IAsyncDisposable
{
    private readonly Process _process;
    private readonly CancellationTokenSource _stderrSource = new();
    private readonly Task _stderrDrain;

    private CodexAppServerProcess(Process process)
    {
        _process = process;
        Connection = JsonRpcConnection.ForStreams(process.StandardOutput.BaseStream, process.StandardInput.BaseStream);
        _stderrDrain = DrainAsync(process.StandardError, _stderrSource.Token);
    }

    public JsonRpcConnection Connection { get; }

    public static CodexAppServerProcess Start(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        var process = new Process { StartInfo = BuildStartInfo(executablePath) };
        if (!process.Start())
        {
            process.Dispose();
            throw new InvalidOperationException("Could not start the Codex app-server process.");
        }

        return new CodexAppServerProcess(process);
    }

    private static ProcessStartInfo BuildStartInfo(string executablePath)
    {
        var info = new ProcessStartInfo
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        var extension = Path.GetExtension(executablePath);
        if (extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".bat", StringComparison.OrdinalIgnoreCase))
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

        info.ArgumentList.Add("app-server");
        return info;
    }

    private static async Task DrainAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        try
        {
            var buffer = new char[4096];
            while (await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false) > 0)
            {
                // Discarded on purpose. stderr may carry sensitive diagnostics and
                // is never persisted, logged, or shown.
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
    }

    public async ValueTask DisposeAsync()
    {
        try { _process.StandardInput.Close(); } catch (Exception) { }

        if (!_process.HasExited)
        {
            try { _process.Kill(entireProcessTree: true); } catch (Exception) { }
        }

        try
        {
            using var waitSource = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await _process.WaitForExitAsync(waitSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (InvalidOperationException) { }

        _stderrSource.Cancel();
        try { await _stderrDrain.ConfigureAwait(false); } catch (Exception) { }

        await Connection.DisposeAsync().ConfigureAwait(false);

        _stderrSource.Dispose();
        _process.Dispose();
    }
}
