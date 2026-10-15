using OrbMet.Core.Models;
using OrbMet.Core.Providers;

namespace OrbMet.Core.Usage;

/// <summary>A provider's latest snapshot together with the window the ring shows.</summary>
public sealed record ProviderUsage(ProviderSnapshot Snapshot, UsageWindow? RingWindow);

/// <summary>
/// Reads every enabled provider and pairs each snapshot with its ring window.
/// One provider failing never hides another's data, and a provider that throws
/// is turned into an error snapshot rather than taking the whole refresh down.
/// </summary>
public sealed class UsageCoordinator
{
    private readonly IReadOnlyList<IUsageProvider> _providers;

    public UsageCoordinator(IEnumerable<IUsageProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        _providers = providers.ToArray();
    }

    public IReadOnlyList<IUsageProvider> Providers => _providers;

    /// <summary>Which limit each reading's ring window is picked by; read on every read.</summary>
    public RingChoice RingChoice { get; set; }

    public async Task<IReadOnlyList<ProviderUsage>> ReadAllAsync(CancellationToken cancellationToken)
    {
        var tasks = _providers.Select(provider => ReadOneAsync(provider, cancellationToken));
        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads one provider, for a click on its ring. Null when no enabled
    /// provider has that id, so a disabled one is never started.
    /// </summary>
    public async Task<ProviderUsage?> ReadAsync(ProviderId providerId, CancellationToken cancellationToken)
    {
        var provider = _providers.FirstOrDefault(candidate => candidate.ProviderId == providerId);
        return provider is null ? null : await ReadOneAsync(provider, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ProviderUsage> ReadOneAsync(IUsageProvider provider, CancellationToken cancellationToken)
    {
        ProviderSnapshot snapshot;
        try
        {
            snapshot = await provider.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            snapshot = new ProviderSnapshot
            {
                ProviderId = provider.ProviderId,
                SourceKind = provider.SourceKind,
                AccountScopeKey = "source",
                ReceivedAtUtc = DateTimeOffset.UtcNow,
                LastAttemptAtUtc = DateTimeOffset.UtcNow,
                ConnectionState = ConnectionState.Error,
                Freshness = Freshness.Missing,
                ErrorCode = "read-failed",
            };
        }

        return new ProviderUsage(snapshot, UsageSelection.SelectRingWindow(snapshot, RingChoice));
    }
}
