using System.ComponentModel;
using OrbMet.Core.Models;

namespace OrbMet.Core.Providers.Codex;

/// <summary>
/// Reads Codex usage through a single app-server helper process. The CLI path
/// is discovered at runtime; failures come back as diagnosed connection states
/// rather than as fabricated values.
/// </summary>
public sealed class CodexUsageProvider : IUsageProvider
{
    private readonly string? _configuredExecutablePath;
    private readonly TimeSpan _timeout;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Func<string?, string?> _locator;
    private readonly Func<string, TimeSpan, CodexAppServerSession> _sessionFactory;

    public CodexUsageProvider(
        string? configuredExecutablePath = null,
        TimeSpan? timeout = null,
        Func<DateTimeOffset>? clock = null,
        Func<string?, string?>? locator = null,
        Func<string, TimeSpan, CodexAppServerSession>? sessionFactory = null)
    {
        _configuredExecutablePath = configuredExecutablePath;
        _timeout = timeout ?? TimeSpan.FromSeconds(20);
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _locator = locator ?? CodexExecutableLocator.Locate;
        _sessionFactory = sessionFactory ?? CodexAppServerSession.Start;
    }

    public ProviderId ProviderId => ProviderId.Codex;

    public SourceKind SourceKind => SourceKind.CodexAppServer;

    public async Task<ProviderSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        var now = _clock();

        string? executablePath;
        try
        {
            executablePath = _locator(_configuredExecutablePath);
        }
        catch (Exception)
        {
            executablePath = null;
        }

        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return CodexSnapshotFactory.Failure(now, ConnectionState.MissingTool, "codex-not-found");
        }

        try
        {
            await using var session = _sessionFactory(executablePath, _timeout);
            await session.InitializeAsync(cancellationToken).ConfigureAwait(false);
            var result = await session.ReadAccountRateLimitsAsync(cancellationToken).ConfigureAwait(false);
            return CodexUsageNormalizer.Normalize(result, now);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return CodexSnapshotFactory.Failure(now, ConnectionState.Error, "timeout");
        }
        catch (JsonRpcException ex) when (ex.IsMethodNotFound)
        {
            return CodexSnapshotFactory.Failure(now, ConnectionState.Unsupported, "rate-limits-unsupported");
        }
        catch (JsonRpcException ex)
        {
            return CodexErrorMapping.FromRpcError(now, ex);
        }
        catch (CodexReadException ex)
        {
            return CodexErrorMapping.FromReadError(now, ex);
        }
        catch (Win32Exception)
        {
            return CodexSnapshotFactory.Failure(now, ConnectionState.MissingTool, "codex-not-found");
        }
        catch (IOException)
        {
            return CodexSnapshotFactory.Failure(now, ConnectionState.Error, "connection-closed");
        }
        catch (Exception)
        {
            return CodexSnapshotFactory.Failure(now, ConnectionState.Error, "codex-read-failed");
        }
    }
}
