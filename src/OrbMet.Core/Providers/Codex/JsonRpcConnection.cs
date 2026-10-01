using System.Text;
using System.Text.Json;

namespace OrbMet.Core.Providers.Codex;

/// <summary>
/// Minimal line-delimited JSON-RPC 2.0 connection over stdio. Requests are
/// serialised so only one call is outstanding at a time, which keeps response
/// correlation simple. Notifications without an id are ignored; a server
/// request is answered with method-not-found so the peer never blocks on
/// OrbMet.
/// </summary>
public sealed class JsonRpcConnection : IAsyncDisposable
{
    private readonly Func<CancellationToken, Task<string?>> _readLine;
    private readonly Func<string, CancellationToken, Task> _writeLine;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IDisposable? _ownedWriter;
    private long _nextId;

    public JsonRpcConnection(
        Func<CancellationToken, Task<string?>> readLine,
        Func<string, CancellationToken, Task> writeLine,
        IDisposable? ownedWriter = null)
    {
        _readLine = readLine ?? throw new ArgumentNullException(nameof(readLine));
        _writeLine = writeLine ?? throw new ArgumentNullException(nameof(writeLine));
        _ownedWriter = ownedWriter;
    }

    public static JsonRpcConnection ForStreams(Stream input, Stream output, int maxLineBytes = 4 * 1024 * 1024)
    {
        var reader = new BoundedLineReader(input, maxLineBytes);
        var writer = new StreamWriter(output, new UTF8Encoding(false)) { AutoFlush = false, NewLine = "\n" };

        return new JsonRpcConnection(
            reader.ReadLineAsync,
            async (line, cancellationToken) =>
            {
                await writer.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
                await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            },
            writer);
    }

    public async Task<JsonElement> SendRequestAsync(
        string method,
        object? parameters,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var id = Interlocked.Increment(ref _nextId);
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);
            var token = timeoutSource.Token;

            await _writeLine(BuildMessage(id, method, parameters), token).ConfigureAwait(false);
            return await AwaitResponseAsync(id, token).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SendNotificationAsync(string method, object? parameters, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _writeLine(BuildMessage(null, method, parameters), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<JsonElement> AwaitResponseAsync(long id, CancellationToken cancellationToken)
    {
        while (true)
        {
            var line = await _readLine(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                throw new JsonRpcException(null, "The app-server connection closed before a response arrived.");
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(line);
            }
            catch (JsonException ex)
            {
                throw new JsonRpcException(null, "Malformed JSON from the app-server.", ex);
            }

            using (document)
            {
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (!root.TryGetProperty("id", out var idElement))
                {
                    continue; // Notification for another concern; ignore it.
                }

                if (root.TryGetProperty("method", out _))
                {
                    await AnswerUnsupportedAsync(root, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (!TryMatchId(idElement, id))
                {
                    continue; // Response for a different request; ignore it.
                }

                if (root.TryGetProperty("error", out var errorElement))
                {
                    var code = errorElement.TryGetProperty("code", out var codeElement) && codeElement.TryGetInt64(out var parsed)
                        ? parsed
                        : (long?)null;
                    var message = errorElement.TryGetProperty("message", out var messageElement)
                        ? messageElement.GetString() ?? "Unknown app-server error."
                        : "Unknown app-server error.";
                    throw new JsonRpcException(code, message);
                }

                if (root.TryGetProperty("result", out var resultElement))
                {
                    return resultElement.Clone();
                }

                throw new JsonRpcException(null, "App-server response had neither result nor error.");
            }
        }
    }

    private async Task AnswerUnsupportedAsync(JsonElement request, CancellationToken cancellationToken)
    {
        var method = request.TryGetProperty("method", out var methodElement) ? methodElement.GetString() : "unknown";

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("id");
            request.GetProperty("id").WriteTo(writer);
            writer.WritePropertyName("error");
            writer.WriteStartObject();
            writer.WriteNumber("code", -32601);
            writer.WriteString("message", "Method not supported by OrbMet: " + method);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        await _writeLine(Encoding.UTF8.GetString(stream.ToArray()), cancellationToken).ConfigureAwait(false);
    }

    private static bool TryMatchId(JsonElement idElement, long expected) =>
        idElement.ValueKind == JsonValueKind.Number
        && idElement.TryGetInt64(out var value)
        && value == expected;

    private static string BuildMessage(long? id, string method, object? parameters)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            if (id is not null)
            {
                writer.WriteNumber("id", id.Value);
            }

            writer.WriteString("method", method);
            if (parameters is not null)
            {
                writer.WritePropertyName("params");
                JsonSerializer.Serialize(writer, parameters, parameters.GetType(), CodexJson.Options);
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public ValueTask DisposeAsync()
    {
        if (_ownedWriter is not null)
        {
            try { _ownedWriter.Dispose(); } catch (Exception) { }
        }

        _gate.Dispose();
        return ValueTask.CompletedTask;
    }
}
