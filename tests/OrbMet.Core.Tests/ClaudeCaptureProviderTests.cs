using OrbMet.Core.Models;
using OrbMet.Core.Providers.Claude;

namespace OrbMet.Core.Tests;

public class ClaudeCaptureProviderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    private static ClaudeCaptureRecord Record(
        DateTimeOffset capturedAt,
        DateTimeOffset resetsAt,
        double? percent = 42) => new()
    {
        CapturedAtUtc = capturedAt,
        SessionHash = "abc",
        Windows = new[]
        {
            new ClaudeCaptureWindow { Key = "five_hour", UsedPercent = percent, ResetsAtUtc = resetsAt },
        },
    };

    private static async Task<ProviderSnapshot> ReadAsync(TempDirectory temp, DateTimeOffset now)
    {
        var provider = new ClaudeCaptureProvider(temp.Path, () => now);
        return await provider.ReadAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ReportsNoCaptureWithoutInventingZeroUsage()
    {
        using var temp = new TempDirectory();
        var store = new ClaudeCaptureStore(temp.Path);

        var snapshot = await ReadAsync(temp, Now);

        Assert.Equal(ConnectionState.AwaitingData, snapshot.ConnectionState);
        Assert.Equal(Freshness.Missing, snapshot.Freshness);
        Assert.Equal("claude-no-capture", snapshot.ErrorCode);
        Assert.Empty(snapshot.Windows);
        Assert.Equal("claude-code", snapshot.AccountScopeKey);
        Assert.Null(snapshot.PlanName);
    }

    [Fact]
    public async Task MapsAFreshCaptureToReadyWindows()
    {
        using var temp = new TempDirectory();
        var store = new ClaudeCaptureStore(temp.Path);
        store.Save(Record(Now.AddMinutes(-1), Now.AddHours(5), percent: 23.5));

        var snapshot = await ReadAsync(temp, Now);

        Assert.Equal(ConnectionState.Ready, snapshot.ConnectionState);
        Assert.Equal(Freshness.Fresh, snapshot.Freshness);
        Assert.Null(snapshot.ErrorCode);
        Assert.Equal(Now.AddMinutes(-1), snapshot.ObservedAtUtc);

        var window = snapshot.Windows.Single();
        Assert.Equal("claudeCode.five_hour", window.Id);
        Assert.Equal(23.5, window.UsedPercent);
        Assert.Equal(5 * 60 * 60, window.DurationSeconds);
        Assert.Equal(DurationOrigin.KnownSemantic, window.DurationOrigin);
        Assert.Equal(ExhaustionEvidence.Unknown, window.ExhaustionEvidence);
    }

    [Fact]
    public async Task MarksAnOldCaptureStaleWithoutHidingIt()
    {
        using var temp = new TempDirectory();
        var store = new ClaudeCaptureStore(temp.Path);
        store.Save(Record(Now.AddMinutes(-30), Now.AddHours(5), percent: 61));

        var snapshot = await ReadAsync(temp, Now);

        Assert.Equal(Freshness.Stale, snapshot.Freshness);
        Assert.Equal(ConnectionState.Ready, snapshot.ConnectionState);
        Assert.Equal(61, snapshot.Windows.Single().UsedPercent);
    }

    [Fact]
    public async Task DropsAWindowWhoseResetHasPassed()
    {
        using var temp = new TempDirectory();
        var store = new ClaudeCaptureStore(temp.Path);
        store.Save(Record(Now.AddMinutes(-1), Now.AddMinutes(-30), percent: 90));

        var snapshot = await ReadAsync(temp, Now);

        Assert.Empty(snapshot.Windows);
        Assert.Equal(ConnectionState.AwaitingData, snapshot.ConnectionState);
        Assert.Equal("claude-no-current-window", snapshot.ErrorCode);
        Assert.Equal(Now.AddMinutes(-1), snapshot.ObservedAtUtc);
    }

    [Fact]
    public async Task UsesUnknownDurationForSpendLimit()
    {
        using var temp = new TempDirectory();
        var store = new ClaudeCaptureStore(temp.Path);
        store.Save(new ClaudeCaptureRecord
        {
            CapturedAtUtc = Now,
            SessionHash = "abc",
            Windows = new[]
            {
                new ClaudeCaptureWindow { Key = "spend_limit", UsedPercent = 120, ResetsAtUtc = Now.AddDays(1) },
            },
        });

        var snapshot = await ReadAsync(temp, Now);

        var window = snapshot.Windows.Single();
        Assert.Null(window.DurationSeconds);
        Assert.Equal(DurationOrigin.Unknown, window.DurationOrigin);
        Assert.Equal(120, window.UsedPercent);
    }
}
