using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OrbMet.Core.Providers.Claude;

/// <summary>
/// The work behind the capture helper: read one status-line payload, bank its
/// usage part, and print the line Claude Code should show. It never throws for
/// a bad payload, never stores anything but usage figures, and never chains to
/// itself.
/// </summary>
public sealed class ClaudeStatusLineRunner
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly ClaudeCaptureStore _captureStore;
    private readonly ClaudeIntegrationRecordStore _integrationStore;
    private readonly IStatusLineChainRunner _chainRunner;
    private readonly Func<DateTimeOffset> _clock;
    private readonly TimeSpan _chainTimeout;

    public ClaudeStatusLineRunner(
        ClaudeCaptureStore captureStore,
        ClaudeIntegrationRecordStore integrationStore,
        IStatusLineChainRunner? chainRunner = null,
        Func<DateTimeOffset>? clock = null,
        TimeSpan? chainTimeout = null)
    {
        _captureStore = captureStore ?? throw new ArgumentNullException(nameof(captureStore));
        _integrationStore = integrationStore ?? throw new ArgumentNullException(nameof(integrationStore));
        _chainRunner = chainRunner ?? new WindowsStatusLineChainRunner();
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _chainTimeout = chainTimeout ?? TimeSpan.FromSeconds(2);
    }

    public async Task RunAsync(ReadOnlyMemory<byte> input, Stream output, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(output);

        var bytes = input.ToArray();
        var text = Decode(bytes);

        var record = ClaudeStatusLineParser.Parse(text, _clock());
        if (record is not null)
        {
            TrySave(record);
        }

        var previous = ReadPreviousCommand();
        if (!string.IsNullOrWhiteSpace(previous) && !ClaudeStatusLineIntegration.IsOrbMetCommand(previous))
        {
            var chained = await _chainRunner
                .RunAsync(previous!, bytes, _chainTimeout, cancellationToken)
                .ConfigureAwait(false);

            if (chained is not null)
            {
                await WriteAsync(output, chained, cancellationToken).ConfigureAwait(false);
                return;
            }
        }

        await WriteAsync(output, DefaultStatusLine.Build(record), cancellationToken).ConfigureAwait(false);
    }

    private void TrySave(ClaudeCaptureRecord record)
    {
        try
        {
            _captureStore.Save(record);
        }
        catch (IOException)
        {
            // A failed capture write must not break the user's terminal.
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (JsonException)
        {
        }
    }

    private string? ReadPreviousCommand()
    {
        try
        {
            var record = _integrationStore.Read();
            if (record?.PreviousStatusLine is JsonObject previous &&
                previous["command"] is JsonValue command &&
                command.TryGetValue<string>(out var text))
            {
                return text;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException)
        {
        }

        return null;
    }

    private static string Decode(byte[] bytes)
    {
        try
        {
            return Utf8NoBom.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return string.Empty;
        }
    }

    private static async Task WriteAsync(Stream output, string text, CancellationToken cancellationToken)
    {
        var bytes = Utf8NoBom.GetBytes(text);
        await output.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
