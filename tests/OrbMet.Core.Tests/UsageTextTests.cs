using OrbMet.Core.Models;
using OrbMet.Core.Usage;

namespace OrbMet.Core.Tests;

public class UsageTextTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 10, 0, 0, TimeSpan.Zero);

    // A fixed +03:00 zone so the tests do not depend on the machine's zone.
    private static readonly TimeZoneInfo Plus3 =
        TimeZoneInfo.CreateCustomTimeZone("test+3", TimeSpan.FromHours(3), "test+3", "test+3");

    [Theory]
    [InlineData(19.0, "%19")]
    [InlineData(0.0, "%0")]
    [InlineData(130.0, "%130")]
    public void PercentPutsTheSignFirstAndKeepsTheRawValue(double value, string expected)
    {
        Assert.Equal(expected, UsageText.Percent(value));
    }

    [Fact]
    public void MissingPercentIsADashNotZero()
    {
        Assert.Equal("—", UsageText.Percent(null));
    }

    [Theory]
    [InlineData(5 * 3600L, "5 saatlik")]
    [InlineData(7 * 86400L, "Haftalık")]
    [InlineData(86400L, "Günlük")]
    [InlineData(30 * 86400L, "30 günlük")]
    public void NamesKnownDurations(long seconds, string expected)
    {
        var window = new UsageWindow { Id = "w", DurationSeconds = seconds };

        Assert.Equal(expected, UsageText.WindowLabel(window, "Codex"));
    }

    [Fact]
    public void UnknownOrUnevenDurationFallsBackToANeutralName()
    {
        Assert.Equal("Kullanım limiti", UsageText.WindowLabel(new UsageWindow { Id = "w" }, "Codex"));
        Assert.Equal("Kullanım limiti", UsageText.WindowLabel(new UsageWindow { Id = "w", DurationSeconds = 5400 }, "Codex"));
    }

    [Fact]
    public void ModelScopeIsAppendedButProviderNameAndDefaultAreNot()
    {
        var scoped = new UsageWindow { Id = "w", DurationSeconds = 5 * 3600, ScopeLabel = "gpt-5" };
        var provider = new UsageWindow { Id = "w", DurationSeconds = 5 * 3600, ScopeLabel = "codex" };
        var fallback = new UsageWindow { Id = "w", ScopeLabel = "gpt-5" };

        Assert.Equal("5 saatlik · gpt-5", UsageText.WindowLabel(scoped, "Codex"));
        Assert.Equal("5 saatlik", UsageText.WindowLabel(provider, "Codex"));
        Assert.Equal("gpt-5", UsageText.WindowLabel(fallback, "Codex"));
    }

    [Fact]
    public void UnknownResetIsNotInvented()
    {
        Assert.Equal("Sıfırlanma zamanı bildirilmedi", UsageText.Reset(null, Now, Plus3));
    }

    [Fact]
    public void PassedResetWaitsForNewDataInsteadOfCountingNegative()
    {
        Assert.Equal("Yeni veri bekleniyor", UsageText.Reset(Now, Now, Plus3));
        Assert.Equal("Yeni veri bekleniyor", UsageText.Reset(Now.AddHours(-2), Now, Plus3));
        Assert.True(UsageText.IsPast(Now.AddSeconds(-1), Now));
        Assert.False(UsageText.IsPast(Now.AddSeconds(1), Now));
        Assert.False(UsageText.IsPast(null, Now));
    }

    [Fact]
    public void NearResetsCountDown()
    {
        Assert.Equal("Sıfırlanmaya 1 dk'dan az", UsageText.Reset(Now.AddSeconds(30), Now, Plus3));
        Assert.Equal("Sıfırlanmaya 14 dk", UsageText.Reset(Now.AddMinutes(14), Now, Plus3));
        Assert.Equal("Sıfırlanmaya 2 sa 14 dk", UsageText.Reset(Now.AddMinutes(134), Now, Plus3));
    }

    [Fact]
    public void DistantResetsShowTheLocalDate()
    {
        var resetsAt = new DateTimeOffset(2026, 10, 15, 11, 30, 0, TimeSpan.Zero);

        Assert.Equal("Sıfırlanma: 15 Ekim 14:30", UsageText.Reset(resetsAt, Now, Plus3));
    }

    [Fact]
    public void ObservedAgeDescribesTheProviderObservation()
    {
        Assert.Equal("Son veri zamanı bilinmiyor", UsageText.ObservedAge(null, Now, Plus3));
        Assert.Equal("Son veri: az önce", UsageText.ObservedAge(Now.AddSeconds(-20), Now, Plus3));
        Assert.Equal("Son veri: 12 dk önce", UsageText.ObservedAge(Now.AddMinutes(-12), Now, Plus3));
        Assert.Equal("Son veri: 3 sa önce", UsageText.ObservedAge(Now.AddHours(-3), Now, Plus3));
        Assert.Equal("Son veri: 11 Ekim 13:00", UsageText.ObservedAge(Now.AddDays(-2), Now, Plus3));
    }
}
