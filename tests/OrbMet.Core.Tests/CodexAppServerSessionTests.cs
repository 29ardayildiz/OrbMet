using System.Text.Json;
using OrbMet.Core.Models;
using OrbMet.Core.Providers.Codex;

namespace OrbMet.Core.Tests;

public class CodexAppServerSessionTests
{
    private const string InitializeResult =
        "{\"codexHome\":\"C:\\\\Users\\\\x\\\\.codex\",\"platformFamily\":\"windows\",\"platformOs\":\"windows\",\"userAgent\":\"codex_cli\"}";

    private const string RateLimitsResult =
        "{\"rateLimits\":{\"limitId\":\"codex\",\"planType\":\"plus\"," +
        "\"primary\":{\"usedPercent\":42,\"windowDurationMins\":300,\"resetsAt\":1750000000}," +
        "\"secondary\":{\"usedPercent\":61,\"windowDurationMins\":10080,\"resetsAt\":1750500000}}}";

    private static FakeAppServer CreateServer() => new((method, id) => method switch
    {
        "initialize" => FakeAppServer.Success(id, InitializeResult),
        "account/rateLimits/read" => FakeAppServer.Success(id, RateLimitsResult),
        _ => FakeAppServer.Error(id, -32601, "unsupported"),
    });

    [Fact]
    public async Task PerformsTheHandshakeAndReadsUsageInOrder()
    {
        var server = CreateServer();
        await using var session = new CodexAppServerSession(server.CreateConnection(), TimeSpan.FromSeconds(5));

        await session.InitializeAsync(CancellationToken.None);
        var result = await session.ReadAccountRateLimitsAsync(CancellationToken.None);

        var methods = server.RawRequests
            .Select(line => JsonDocument.Parse(line).RootElement.GetProperty("method").GetString())
            .ToList();

        Assert.Equal(new[] { "initialize", "initialized", "account/rateLimits/read" }, methods);

        var snapshot = CodexUsageNormalizer.Normalize(result, DateTimeOffset.UnixEpoch);
        Assert.Equal(ConnectionState.Ready, snapshot.ConnectionState);
        Assert.Equal(2, snapshot.Windows.Count);
    }

    [Fact]
    public async Task SendsInitializeWithClientInfo()
    {
        var server = CreateServer();
        await using var session = new CodexAppServerSession(server.CreateConnection(), TimeSpan.FromSeconds(5));

        await session.InitializeAsync(CancellationToken.None);

        var initialize = server.RawRequests
            .Select(line => JsonDocument.Parse(line).RootElement)
            .First(root => root.GetProperty("method").GetString() == "initialize");

        var clientInfo = initialize.GetProperty("params").GetProperty("clientInfo");
        Assert.Equal("orbmet", clientInfo.GetProperty("name").GetString());
        Assert.False(initialize.GetProperty("params").GetProperty("capabilities").GetProperty("experimentalApi").GetBoolean());
    }

    [Fact]
    public async Task PropagatesARpcErrorFromTheRead()
    {
        var server = new FakeAppServer((method, id) => method switch
        {
            "initialize" => FakeAppServer.Success(id, InitializeResult),
            _ => FakeAppServer.Error(id, -32601, "no such method"),
        });

        await using var session = new CodexAppServerSession(server.CreateConnection(), TimeSpan.FromSeconds(5));
        await session.InitializeAsync(CancellationToken.None);

        var exception = await Assert.ThrowsAsync<JsonRpcException>(
            () => session.ReadAccountRateLimitsAsync(CancellationToken.None));

        Assert.True(exception.IsMethodNotFound);
    }
}
