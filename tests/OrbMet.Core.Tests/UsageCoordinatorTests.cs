using OrbMet.Core.Models;
using OrbMet.Core.Providers;
using OrbMet.Core.Usage;

namespace OrbMet.Core.Tests;

public class UsageCoordinatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task PairsEachSnapshotWithItsRingWindow()
    {
        var coordinator = new UsageCoordinator(new IUsageProvider[]
        {
            new FakeProvider(ProviderId.Codex, SourceKind.CodexAppServer, Window("codex.secondary", 70)),
            new FakeProvider(ProviderId.ClaudeCode, SourceKind.ClaudeStatusLine, Window("claudeCode.seven_day", 85)),
        });

        var results = await coordinator.ReadAllAsync(CancellationToken.None);

        Assert.Equal(2, results.Count);
        Assert.Equal("codex.secondary", results[0].RingWindow!.Id);
        Assert.Equal("claudeCode.seven_day", results[1].RingWindow!.Id);
    }

    [Fact]
    public async Task TurnsAThrowingProviderIntoAnErrorSnapshot()
    {
        var coordinator = new UsageCoordinator(new IUsageProvider[]
        {
            new FakeProvider(ProviderId.Codex, SourceKind.CodexAppServer, null, throwOnRead: true),
            new FakeProvider(ProviderId.ClaudeCode, SourceKind.ClaudeStatusLine, Window("claudeCode.five_hour", 10)),
        });

        var results = await coordinator.ReadAllAsync(CancellationToken.None);

        Assert.Equal(ConnectionState.Error, results[0].Snapshot.ConnectionState);
        Assert.Equal("read-failed", results[0].Snapshot.ErrorCode);
        Assert.Null(results[0].RingWindow);
        Assert.Equal(10, results[1].RingWindow!.UsedPercent);
    }

    [Fact]
    public async Task PropagatesCancellation()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        var coordinator = new UsageCoordinator(new IUsageProvider[]
        {
            new FakeProvider(ProviderId.Codex, SourceKind.CodexAppServer, null, honorCancellation: true),
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => coordinator.ReadAllAsync(source.Token));
    }

    [Fact]
    public async Task ReadsOnlyTheRequestedProvider()
    {
        var codex = new FakeProvider(ProviderId.Codex, SourceKind.CodexAppServer, Window("codex.primary", 40));
        var claude = new FakeProvider(ProviderId.ClaudeCode, SourceKind.ClaudeStatusLine, Window("claudeCode.five_hour", 10));
        var coordinator = new UsageCoordinator(new IUsageProvider[] { codex, claude });

        var result = await coordinator.ReadAsync(ProviderId.ClaudeCode, CancellationToken.None);

        Assert.Equal("claudeCode.five_hour", result!.RingWindow!.Id);
        Assert.Equal(0, codex.Reads);
        Assert.Equal(1, claude.Reads);
    }

    [Fact]
    public async Task ReadingAProviderThatIsNotEnabledStartsNothing()
    {
        var codex = new FakeProvider(ProviderId.Codex, SourceKind.CodexAppServer, Window("codex.primary", 40));
        var coordinator = new UsageCoordinator(new IUsageProvider[] { codex });

        var result = await coordinator.ReadAsync(ProviderId.ClaudeCode, CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(0, codex.Reads);
    }

    private static UsageWindow Window(string id, double percent) => new()
    {
        Id = id,
        UsedPercent = percent,
    };

    private sealed class FakeProvider : IUsageProvider
    {
        private readonly UsageWindow? _window;
        private readonly bool _throwOnRead;
        private readonly bool _honorCancellation;

        public FakeProvider(
            ProviderId providerId,
            SourceKind sourceKind,
            UsageWindow? window,
            bool throwOnRead = false,
            bool honorCancellation = false)
        {
            ProviderId = providerId;
            SourceKind = sourceKind;
            _window = window;
            _throwOnRead = throwOnRead;
            _honorCancellation = honorCancellation;
        }

        public ProviderId ProviderId { get; }

        public SourceKind SourceKind { get; }

        public int Reads { get; private set; }

        public Task<ProviderSnapshot> ReadAsync(CancellationToken cancellationToken)
        {
            Reads++;

            if (_honorCancellation)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (_throwOnRead)
            {
                throw new InvalidOperationException("provider blew up");
            }

            return Task.FromResult(new ProviderSnapshot
            {
                ProviderId = ProviderId,
                SourceKind = SourceKind,
                ReceivedAtUtc = Now,
                LastAttemptAtUtc = Now,
                ConnectionState = ConnectionState.Ready,
                Freshness = Freshness.Fresh,
                Windows = _window is null ? Array.Empty<UsageWindow>() : new[] { _window },
            });
        }
    }
}
