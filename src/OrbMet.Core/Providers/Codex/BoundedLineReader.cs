using System.Text;

namespace OrbMet.Core.Providers.Codex;

/// <summary>
/// Reads newline-delimited UTF-8 lines from a stream with a hard byte bound, so
/// an unexpectedly large or malformed line cannot exhaust memory.
/// </summary>
internal sealed class BoundedLineReader
{
    private readonly Stream _stream;
    private readonly int _maxBytes;
    private readonly byte[] _buffer = new byte[8192];
    private int _start;
    private int _end;

    public BoundedLineReader(Stream stream, int maxBytes)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _maxBytes = maxBytes > 0 ? maxBytes : throw new ArgumentOutOfRangeException(nameof(maxBytes));
    }

    /// <summary>Returns the next line without its terminator, or null at end of stream.</summary>
    public async Task<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        var line = new List<byte>(256);

        while (true)
        {
            for (var i = _start; i < _end; i++)
            {
                if (_buffer[i] == (byte)'\n')
                {
                    line.AddRange(new ArraySegment<byte>(_buffer, _start, i - _start));
                    _start = i + 1;
                    return Decode(line);
                }
            }

            if (_end > _start)
            {
                line.AddRange(new ArraySegment<byte>(_buffer, _start, _end - _start));
                _start = _end;
            }

            if (line.Count > _maxBytes)
            {
                throw new InvalidDataException($"App-server line exceeded the {_maxBytes} byte bound.");
            }

            _start = 0;
            _end = await _stream.ReadAsync(_buffer.AsMemory(0, _buffer.Length), cancellationToken).ConfigureAwait(false);
            if (_end == 0)
            {
                return line.Count == 0 ? null : Decode(line);
            }
        }
    }

    private static string Decode(List<byte> bytes)
    {
        var array = bytes.ToArray();
        var length = array.Length;
        if (length > 0 && array[length - 1] == (byte)'\r')
        {
            length--;
        }

        return Encoding.UTF8.GetString(array, 0, length);
    }
}
