using System.Text.Json;
using OrbMet.Core.Providers.Codex;

namespace OrbMet.Core.Tests;

public class JsonRpcConnectionTests
{
    private static (JsonRpcConnection Connection, List<string> Written) Create(params string[] incoming)
    {
        var queue = new Queue<string>(incoming);
        var written = new List<string>();
        var connection = new JsonRpcConnection(
            _ => Task.FromResult(queue.Count > 0 ? queue.Dequeue() : null),
            (line, _) =>
            {
                written.Add(line);
                return Task.CompletedTask;
            });
        return (connection, written);
    }

    [Fact]
    public async Task MatchesTheResponseIdAndIgnoresNotifications()
    {
        var (connection, _) = Create(
            "{\"method\":\"account/rateLimits/updated\",\"params\":{}}",
            "{\"id\":1,\"result\":{\"ok\":true}}");

        var result = await connection.SendRequestAsync("account/rateLimits/read", null, TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.True(result.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task IgnoresAResponseForAnotherId()
    {
        var (connection, _) = Create(
            "{\"id\":99,\"result\":{\"stale\":true}}",
            "{\"id\":1,\"result\":{\"value\":2}}");

        var result = await connection.SendRequestAsync("x", null, TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.False(result.TryGetProperty("stale", out _));
        Assert.Equal(2, result.GetProperty("value").GetInt32());
    }

    [Fact]
    public async Task SurfacesAnErrorResponse()
    {
        var (connection, _) = Create("{\"id\":1,\"error\":{\"code\":-32601,\"message\":\"nope\"}}");

        var exception = await Assert.ThrowsAsync<JsonRpcException>(
            () => connection.SendRequestAsync("x", null, TimeSpan.FromSeconds(5), CancellationToken.None));

        Assert.True(exception.IsMethodNotFound);
        Assert.Equal(-32601, exception.Code);
    }

    [Fact]
    public async Task ThrowsWhenTheConnectionClosesBeforeAResponse()
    {
        var (connection, _) = Create();

        var exception = await Assert.ThrowsAsync<JsonRpcException>(
            () => connection.SendRequestAsync("x", null, TimeSpan.FromSeconds(5), CancellationToken.None));

        Assert.Contains("closed", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnswersAnUnknownServerRequestWithMethodNotFound()
    {
        var (connection, written) = Create(
            "{\"id\":7,\"method\":\"server/unknown\"}",
            "{\"id\":1,\"result\":{}}");

        await connection.SendRequestAsync("x", null, TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.Contains(written, line => line.Contains("\"id\":7", StringComparison.Ordinal) && line.Contains("-32601", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TimesOutWhenNoResponseArrives()
    {
        var connection = new JsonRpcConnection(
            async token =>
            {
                await Task.Delay(Timeout.Infinite, token);
                return null;
            },
            (_, _) => Task.CompletedTask);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => connection.SendRequestAsync("x", null, TimeSpan.FromMilliseconds(50), CancellationToken.None));
    }

    [Fact]
    public async Task WritesTheMethodAndParametersAsOneLine()
    {
        var (connection, written) = Create("{\"id\":1,\"result\":{}}");

        await connection.SendRequestAsync("account/rateLimits/read", new { excludeResetCreditDetails = true }, TimeSpan.FromSeconds(5), CancellationToken.None);

        var request = JsonDocument.Parse(Assert.Single(written)).RootElement;
        Assert.Equal("account/rateLimits/read", request.GetProperty("method").GetString());
        Assert.True(request.GetProperty("params").GetProperty("excludeResetCreditDetails").GetBoolean());
    }

    [Fact]
    public async Task SendsNotificationsWithoutAnId()
    {
        var written = new List<string>();
        var connection = new JsonRpcConnection(
            _ => Task.FromResult<string?>(null),
            (line, _) =>
            {
                written.Add(line);
                return Task.CompletedTask;
            });

        await connection.SendNotificationAsync("initialized", null, CancellationToken.None);

        var notification = JsonDocument.Parse(Assert.Single(written)).RootElement;
        Assert.Equal("initialized", notification.GetProperty("method").GetString());
        Assert.False(notification.TryGetProperty("id", out _));
    }
}
