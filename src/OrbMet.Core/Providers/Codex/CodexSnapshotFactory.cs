using OrbMet.Core.Models;

namespace OrbMet.Core.Providers.Codex;

/// <summary>Creates a failure snapshot without inventing a value for missing data.</summary>
internal static class CodexSnapshotFactory
{
    public static ProviderSnapshot Failure(DateTimeOffset at, ConnectionState state, string errorCode) => new()
    {
        ProviderId = ProviderId.Codex,
        SourceKind = SourceKind.CodexAppServer,
        AccountScopeKey = "codex",
        ReceivedAtUtc = at,
        LastAttemptAtUtc = at,
        ConnectionState = state,
        Freshness = Freshness.Missing,
        ErrorCode = errorCode,
    };
}
