using OrbMet.Core.Models;

namespace OrbMet.Core.Providers.Claude;

/// <summary>
/// Reads the last Claude capture from disk. This is a push source: the button
/// re-reads the file, it does not ask Claude for a new measurement, and a
/// missing file means no data rather than zero usage.
/// </summary>
public sealed class ClaudeCaptureProvider : IUsageProvider
{
    private readonly ClaudeCaptureStore _store;
    private readonly Func<DateTimeOffset> _clock;
    private readonly TimeSpan _freshFor;
    private bool _sweptLeftovers;

    public ClaudeCaptureProvider(
        string captureDirectory,
        Func<DateTimeOffset>? clock = null,
        TimeSpan? freshFor = null)
    {
        _store = new ClaudeCaptureStore(captureDirectory);
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _freshFor = freshFor ?? ClaudeCaptureNormalizer.DefaultFreshFor;
    }

    public ProviderId ProviderId => ProviderId.ClaudeCode;

    public SourceKind SourceKind => SourceKind.ClaudeStatusLine;

    public Task<ProviderSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        var now = _clock();

        ClaudeCaptureRecord? record;
        try
        {
            if (!_sweptLeftovers)
            {
                _sweptLeftovers = true;
                _store.RemoveLeftovers(now, TimeSpan.FromHours(1));
            }

            record = _store.ReadNewest(now);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(new ProviderSnapshot
            {
                ProviderId = ProviderId.ClaudeCode,
                SourceKind = SourceKind.ClaudeStatusLine,
                AccountScopeKey = "claude-code",
                ReceivedAtUtc = now,
                LastAttemptAtUtc = now,
                ConnectionState = ConnectionState.Error,
                Freshness = Freshness.Missing,
                ErrorCode = "claude-capture-read-failed",
            });
        }

        return Task.FromResult(ClaudeCaptureNormalizer.Normalize(record, now, _freshFor));
    }
}
