using OrbMet.Core.Models;

namespace OrbMet.Core.Providers;

/// <summary>
/// Read side of a usage source. Each implementation owns its transport,
/// timeout, cancellation, and process lifecycle.
/// </summary>
public interface IUsageProvider
{
    ProviderId ProviderId { get; }

    SourceKind SourceKind { get; }

    /// <summary>
    /// Reads the current usage observation. A diagnosed failure is returned as
    /// a snapshot with an error state, so the caller never has to invent a
    /// value for missing data.
    /// </summary>
    Task<ProviderSnapshot> ReadAsync(CancellationToken cancellationToken);
}
