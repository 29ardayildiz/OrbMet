using System.Net;
using System.Net.Http.Headers;
using System.Text;
using OrbMet.Core.Models;

namespace OrbMet.Core.Providers.Antigravity;

/// <summary>
/// Reads Antigravity's quotas from its own language server, which is the only
/// thing that knows them, so they exist only while Antigravity is open. Every
/// server and every port it listens on is asked over plain HTTP on 127.0.0.1
/// (the HTTPS port answers HTTP with 400), and the first reply holding limits
/// wins. The per-launch code goes to that address alone and is never kept.
/// </summary>
public sealed class AntigravityUsageProvider : IUsageProvider
{
    private const string QuotaPath = "/exa.language_server_pb.LanguageServerService/RetrieveUserQuotaSummary";
    private const int MaxReplyBytes = 1024 * 1024;

    private static readonly HttpClient SharedClient = new(new SocketsHttpHandler
    {
        UseProxy = false,
        AllowAutoRedirect = false,
        UseCookies = false,
        ConnectTimeout = TimeSpan.FromSeconds(2),
    })
    {
        Timeout = Timeout.InfiniteTimeSpan,
    };

    private readonly Func<IReadOnlyList<AntigravityServer>> _find;
    private readonly Func<int, string, CancellationToken, Task<string?>> _ask;
    private readonly Func<DateTimeOffset> _clock;
    private readonly TimeSpan _timeout;

    /// <param name="ask">Sends the quota question to a port with the code; returns the reply body or null.</param>
    public AntigravityUsageProvider(
        Func<IReadOnlyList<AntigravityServer>>? find = null,
        Func<int, string, CancellationToken, Task<string?>>? ask = null,
        Func<DateTimeOffset>? clock = null,
        TimeSpan? timeout = null)
    {
        _find = find ?? AntigravityServers.Find;
        _ask = ask ?? AskAsync;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _timeout = timeout ?? TimeSpan.FromSeconds(10);
    }

    public ProviderId ProviderId => ProviderId.Antigravity;

    public SourceKind SourceKind => SourceKind.AntigravityLanguageServer;

    public async Task<ProviderSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        var now = _clock();
        var servers = _find();
        if (servers.Count == 0)
        {
            return Failure(now, ConnectionState.AwaitingData, "antigravity-not-running");
        }

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(_timeout);

        // A reply with no limits is what a server that is not the right one
        // says too, so it is kept only in case nothing better turns up.
        var answeredEmpty = false;
        try
        {
            foreach (var server in servers)
            {
                foreach (var port in server.Ports)
                {
                    var reply = await _ask(port, server.CsrfToken, limit.Token).ConfigureAwait(false);
                    if (reply is null || AntigravityQuotaParser.Parse(reply) is not { } windows)
                    {
                        continue;
                    }

                    if (windows.Count == 0)
                    {
                        answeredEmpty = true;
                        continue;
                    }

                    return new ProviderSnapshot
                    {
                        ProviderId = ProviderId.Antigravity,
                        SourceKind = SourceKind.AntigravityLanguageServer,
                        AccountScopeKey = "antigravity",
                        ObservedAtUtc = now,
                        ReceivedAtUtc = now,
                        LastAttemptAtUtc = now,
                        ConnectionState = ConnectionState.Ready,
                        Freshness = Freshness.Fresh,
                        Windows = windows,
                    };
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failure(now, ConnectionState.Error, "timeout");
        }

        return answeredEmpty
            ? Failure(now, ConnectionState.Ready, "antigravity-no-limits")
            : Failure(now, ConnectionState.Error, "antigravity-not-answering");
    }

    private static ProviderSnapshot Failure(DateTimeOffset at, ConnectionState state, string errorCode) => new()
    {
        ProviderId = ProviderId.Antigravity,
        SourceKind = SourceKind.AntigravityLanguageServer,
        AccountScopeKey = "antigravity",
        ReceivedAtUtc = at,
        LastAttemptAtUtc = at,
        ConnectionState = state,
        Freshness = Freshness.Missing,
        ErrorCode = errorCode,
    };

    /// <summary>One port's answer, or null for anything but a 200 holding text.</summary>
    private static async Task<string?> AskAsync(int port, string csrfToken, CancellationToken cancellationToken)
    {
        using var perPort = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        perPort.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{port}{QuotaPath}")
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
            };
            request.Headers.Add("x-codeium-csrf-token", csrfToken);
            request.Headers.Add("Connect-Protocol-Version", "1");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await SharedClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, perPort.Token)
                .ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK || response.Content.Headers.ContentLength > MaxReplyBytes)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(perPort.Token).ConfigureAwait(false);
            using var memory = new MemoryStream();
            var buffer = new byte[16 * 1024];
            int read;
            while ((read = await stream.ReadAsync(buffer, perPort.Token).ConfigureAwait(false)) > 0)
            {
                if (memory.Length + read > MaxReplyBytes)
                {
                    return null;
                }

                memory.Write(buffer, 0, read);
            }

            return Encoding.UTF8.GetString(memory.GetBuffer(), 0, (int)memory.Length);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // This port was slow; the next may answer.
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            return null;
        }
    }
}
