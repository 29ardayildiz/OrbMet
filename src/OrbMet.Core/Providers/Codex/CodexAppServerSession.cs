using System.Text.Json;

namespace OrbMet.Core.Providers.Codex;

/// <summary>
/// One Codex app-server conversation: initialize, initialized, then a single
/// usage read. The transport (a child process or a test double) is owned by the
/// session and disposed with it.
/// </summary>
public sealed class CodexAppServerSession : IAsyncDisposable
{
    private readonly JsonRpcConnection _connection;
    private readonly IAsyncDisposable? _transport;
    private readonly TimeSpan _timeout;

    public CodexAppServerSession(JsonRpcConnection connection, TimeSpan timeout, IAsyncDisposable? transport = null)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _timeout = timeout;
        _transport = transport;
    }

    public static CodexAppServerSession Start(string executablePath, TimeSpan timeout)
    {
        var process = CodexAppServerProcess.Start(executablePath);
        return new CodexAppServerSession(process.Connection, timeout, process);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var clientInfo = new
        {
            name = "orbmet",
            title = "OrbMet",
            version = ClientVersion,
        };
        var capabilities = new { experimentalApi = false };

        await _connection
            .SendRequestAsync("initialize", new { clientInfo, capabilities }, _timeout, cancellationToken)
            .ConfigureAwait(false);

        await _connection
            .SendNotificationAsync("initialized", null, cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<JsonElement> ReadAccountRateLimitsAsync(CancellationToken cancellationToken) =>
        _connection.SendRequestAsync("account/rateLimits/read", null, _timeout, cancellationToken);

    private static string ClientVersion =>
        typeof(CodexAppServerSession).Assembly.GetName().Version?.ToString() ?? "0.0.0";

    public ValueTask DisposeAsync() => _transport is null ? ValueTask.CompletedTask : _transport.DisposeAsync();
}
