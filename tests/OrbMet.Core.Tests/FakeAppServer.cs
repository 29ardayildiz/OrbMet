using System.Text.Json;
using OrbMet.Core.Providers.Codex;

namespace OrbMet.Core.Tests;

/// <summary>
/// In-memory stand-in for the Codex app-server. It answers client requests from
/// a supplied responder and records every line the client sent, so tests can
/// assert both framing and handshake order without spawning a process.
/// </summary>
internal sealed class FakeAppServer
{
    private readonly Queue<string> _toClient = new();
    private readonly Func<string, JsonElement, string?> _responder;

    public FakeAppServer(Func<string, JsonElement, string?> responder) => _responder = responder;

    public List<string> RawRequests { get; } = new();

    public JsonRpcConnection CreateConnection() => new(
        _ => Task.FromResult(_toClient.Count > 0 ? _toClient.Dequeue() : null),
        (line, _) =>
        {
            Handle(line);
            return Task.CompletedTask;
        });

    public void Push(string line) => _toClient.Enqueue(line);

    private void Handle(string line)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(line);
        }
        catch (JsonException)
        {
            return;
        }

        using (document)
        {
            var root = document.RootElement;
            RawRequests.Add(line);

            if (!root.TryGetProperty("id", out var id))
            {
                return; // Client notification; nothing to answer.
            }

            var method = root.TryGetProperty("method", out var methodElement)
                ? methodElement.GetString() ?? string.Empty
                : string.Empty;

            var response = _responder(method, id);
            if (response is not null)
            {
                _toClient.Enqueue(response);
            }
        }
    }

    public static string Success(JsonElement id, string resultJson) =>
        $"{{\"id\":{id.GetRawText()},\"result\":{resultJson}}}";

    public static string Error(JsonElement id, long code, string message) =>
        $"{{\"id\":{id.GetRawText()},\"error\":{{\"code\":{code},\"message\":\"{message}\"}}}}";
}

/// <summary>Returns the underlying stream in fixed-size chunks to exercise partial reads.</summary>
internal sealed class ChunkedStream : Stream
{
    private readonly byte[] _data;
    private readonly int _chunkSize;
    private int _position;

    public ChunkedStream(byte[] data, int chunkSize)
    {
        _data = data;
        _chunkSize = chunkSize;
    }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => _data.Length;

    public override long Position
    {
        get => _position;
        set => throw new NotSupportedException();
    }

    public override void Flush() { }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var remaining = _data.Length - _position;
        if (remaining <= 0)
        {
            return 0;
        }

        var toCopy = Math.Min(Math.Min(count, _chunkSize), remaining);
        Array.Copy(_data, _position, buffer, offset, toCopy);
        _position += toCopy;
        return toCopy;
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
