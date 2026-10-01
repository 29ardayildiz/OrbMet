using System.Text;
using OrbMet.Core.Providers.Codex;

namespace OrbMet.Core.Tests;

public class BoundedLineReaderTests
{
    [Fact]
    public async Task ReadsAFragmentedLineAcrossChunkBoundaries()
    {
        var reader = new BoundedLineReader(new ChunkedStream(Encoding.UTF8.GetBytes("hello world\nnext\n"), 3), 1024);

        Assert.Equal("hello world", await reader.ReadLineAsync(CancellationToken.None));
        Assert.Equal("next", await reader.ReadLineAsync(CancellationToken.None));
        Assert.Null(await reader.ReadLineAsync(CancellationToken.None));
    }

    [Fact]
    public async Task StripsACarriageReturn()
    {
        var reader = new BoundedLineReader(new ChunkedStream(Encoding.UTF8.GetBytes("line\r\n"), 64), 1024);

        Assert.Equal("line", await reader.ReadLineAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ReturnsTheFinalLineWithoutATerminatorAtEndOfStream()
    {
        var reader = new BoundedLineReader(new ChunkedStream(Encoding.UTF8.GetBytes("tail"), 64), 1024);

        Assert.Equal("tail", await reader.ReadLineAsync(CancellationToken.None));
        Assert.Null(await reader.ReadLineAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ThrowsWhenALineExceedsTheBound()
    {
        var reader = new BoundedLineReader(new ChunkedStream(Encoding.UTF8.GetBytes(new string('x', 4096) + "\n"), 128), 256);

        await Assert.ThrowsAsync<InvalidDataException>(() => reader.ReadLineAsync(CancellationToken.None));
    }

    [Fact]
    public async Task DecodesUtf8Text()
    {
        var reader = new BoundedLineReader(new ChunkedStream(Encoding.UTF8.GetBytes("ölçüm\n"), 2), 1024);

        Assert.Equal("ölçüm", await reader.ReadLineAsync(CancellationToken.None));
    }
}
