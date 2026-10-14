using System.ComponentModel;
using OrbMet.Core.Models;
using OrbMet.Core.Providers.Codex;

namespace OrbMet.Core.Tests;

/// <summary>
/// What the panel is told when Codex is missing, will not start, closes early
/// or never answers: a diagnosed state with no usage, and the helper process
/// always shut down.
/// </summary>
public class CodexUsageProviderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 9, 0, 0, TimeSpan.Zero);

    private const string InitializeResult =
        "{\"codexHome\":\"x\",\"platformFamily\":\"windows\",\"platformOs\":\"windows\",\"userAgent\":\"codex_cli\"}";

    private const string RateLimitsResult =
        "{\"rateLimits\":{\"limitId\":\"codex\",\"planType\":\"plus\"," +
        "\"primary\":{\"usedPercent\":42,\"windowDurationMins\":300,\"resetsAt\":1790000000}}}";

    private static CodexUsageProvider Provider(Func<string, TimeSpan, CodexAppServerSession> sessionFactory) => new(
        timeout: TimeSpan.FromMilliseconds(300),
        clock: () => Now,
        locator: _ => @"C:\tools\codex.exe",
        sessionFactory: sessionFactory);

    private static Func<string, TimeSpan, CodexAppServerSession> Serving(FakeAppServer server, Transport transport) =>
        (_, timeout) => new CodexAppServerSession(server.CreateConnection(), timeout, transport);

    [Fact]
    public async Task MissingCodexIsReportedWithoutUsage()
    {
        var started = false;
        var provider = new CodexUsageProvider(
            clock: () => Now,
            locator: _ => null,
            sessionFactory: (_, _) => { started = true; throw new InvalidOperationException(); });

        var snapshot = await provider.ReadAsync(CancellationToken.None);

        Assert.Equal(ConnectionState.MissingTool, snapshot.ConnectionState);
        Assert.Empty(snapshot.Windows);
        Assert.False(started);
    }

    [Fact]
    public async Task CodexThatCannotStartIsMissingTool()
    {
        var provider = Provider((_, _) => throw new Win32Exception(2));

        var snapshot = await provider.ReadAsync(CancellationToken.None);

        Assert.Equal(ConnectionState.MissingTool, snapshot.ConnectionState);
        Assert.Empty(snapshot.Windows);
    }

    [Fact]
    public async Task ReadsUsageAndShutsTheHelperDown()
    {
        var transport = new Transport();
        var server = new FakeAppServer((method, id) => method switch
        {
            "initialize" => FakeAppServer.Success(id, InitializeResult),
            "account/rateLimits/read" => FakeAppServer.Success(id, RateLimitsResult),
            _ => null,
        });

        var snapshot = await Provider(Serving(server, transport)).ReadAsync(CancellationToken.None);

        Assert.Equal(ConnectionState.Ready, snapshot.ConnectionState);
        Assert.Equal(42, Assert.Single(snapshot.Windows).UsedPercent);
        Assert.True(transport.Disposed);
    }

    [Fact]
    public async Task HelperThatClosesBeforeAnsweringIsAnErrorAndShutDown()
    {
        var transport = new Transport();
        var server = new FakeAppServer((_, _) => null);

        var snapshot = await Provider(Serving(server, transport)).ReadAsync(CancellationToken.None);

        Assert.Equal(ConnectionState.Error, snapshot.ConnectionState);
        Assert.Empty(snapshot.Windows);
        Assert.True(transport.Disposed);
    }

    [Fact]
    public async Task HelperThatNeverAnswersTimesOutAndIsShutDown()
    {
        var transport = new Transport();
        var silent = new JsonRpcConnection(
            async cancellationToken =>
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
                return null;
            },
            (_, _) => Task.CompletedTask);
        var provider = Provider((_, timeout) => new CodexAppServerSession(silent, timeout, transport));

        var started = DateTimeOffset.UtcNow;
        var snapshot = await provider.ReadAsync(CancellationToken.None);

        Assert.Equal(ConnectionState.Error, snapshot.ConnectionState);
        Assert.Empty(snapshot.Windows);
        Assert.True(transport.Disposed);
        Assert.True(DateTimeOffset.UtcNow - started < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task VersionWithoutTheUsageReadIsUnsupported()
    {
        var server = new FakeAppServer((method, id) => method switch
        {
            "initialize" => FakeAppServer.Success(id, InitializeResult),
            _ => FakeAppServer.Error(id, -32601, "Method not found"),
        });

        var snapshot = await Provider(Serving(server, new Transport())).ReadAsync(CancellationToken.None);

        Assert.Equal(ConnectionState.Unsupported, snapshot.ConnectionState);
        Assert.Empty(snapshot.Windows);
    }

    [Fact]
    public async Task SignedOutCodexAsksForSignInRatherThanShowingZero()
    {
        var server = new FakeAppServer((method, id) => method switch
        {
            "initialize" => FakeAppServer.Success(id, InitializeResult),
            _ => FakeAppServer.Error(id, -32000, "Not logged in"),
        });

        var snapshot = await Provider(Serving(server, new Transport())).ReadAsync(CancellationToken.None);

        Assert.Equal(ConnectionState.SignInRequired, snapshot.ConnectionState);
        Assert.Empty(snapshot.Windows);
    }

    /// <summary>Stands in for the app-server process and records that it was shut down.</summary>
    private sealed class Transport : IAsyncDisposable
    {
        public bool Disposed { get; private set; }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
