using OrbMet.Core.Models;
using OrbMet.Core.Usage;

namespace OrbMet.Core.Tests;

public class UsageTextTests
{
    // 13:00 local in the +03:00 test zone.
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
        Assert.Equal("Kullanım bildirilmedi", UsageText.Used(null));
    }

    [Fact]
    public void UsedNamesTheFigure()
    {
        Assert.Equal("%6 kullanıldı", UsageText.Used(6));
        Assert.Equal("%0 kullanıldı", UsageText.Used(0));
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
        Assert.Equal($"{expected} limit", UsageText.LimitTitle(window, "Codex"));
    }

    [Fact]
    public void UnknownOrUnevenDurationFallsBackToANeutralName()
    {
        Assert.Equal("Kullanım limiti", UsageText.WindowLabel(new UsageWindow { Id = "w" }, "Codex"));
        Assert.Equal("Kullanım limiti", UsageText.LimitTitle(new UsageWindow { Id = "w", DurationSeconds = 5400 }, "Codex"));
    }

    [Fact]
    public void ModelScopeIsAppendedButProviderNameAndDefaultAreNot()
    {
        var scoped = new UsageWindow { Id = "w", DurationSeconds = 7 * 86400, ScopeLabel = "gpt-5" };
        var provider = new UsageWindow { Id = "w", DurationSeconds = 5 * 3600, ScopeLabel = "codex" };
        var unknownLength = new UsageWindow { Id = "w", ScopeLabel = "gpt-5" };

        Assert.Equal("Haftalık limit · gpt-5", UsageText.LimitTitle(scoped, "Codex"));
        Assert.Equal("5 saatlik limit", UsageText.LimitTitle(provider, "Codex"));
        Assert.Equal("Kullanım limiti · gpt-5", UsageText.LimitTitle(unknownLength, "Codex"));
        Assert.Equal("gpt-5", UsageText.WindowLabel(unknownLength, "Codex"));
    }

    [Fact]
    public void UnknownResetIsLeftEmptyNotInvented()
    {
        Assert.Equal(string.Empty, UsageText.ResetAt(null, Now, Plus3));
    }

    [Fact]
    public void PassedResetWaitsForNewData()
    {
        Assert.Equal("Yeni veri bekleniyor", UsageText.ResetAt(Now, Now, Plus3));
        Assert.Equal("Yeni veri bekleniyor", UsageText.ResetAt(Now.AddHours(-2), Now, Plus3));
        Assert.True(UsageText.IsPast(Now.AddSeconds(-1), Now));
        Assert.False(UsageText.IsPast(Now.AddSeconds(1), Now));
        Assert.False(UsageText.IsPast(null, Now));
    }

    [Fact]
    public void ResetTodayShowsOnlyTheLocalTime()
    {
        Assert.Equal("Sıfırlanma 15:14", UsageText.ResetAt(Now.AddMinutes(134), Now, Plus3));
    }

    [Fact]
    public void ResetOnAnotherDayShowsTheLocalDate()
    {
        var resetsAt = new DateTimeOffset(2026, 10, 15, 11, 30, 0, TimeSpan.Zero);

        Assert.Equal("Sıfırlanma 15 Eki 14:30", UsageText.ResetAt(resetsAt, Now, Plus3));
    }

    [Fact]
    public void ResetJustPastLocalMidnightIsAnotherDay()
    {
        // 21:30 UTC is 00:30 the next day at +03:00.
        var resetsAt = new DateTimeOffset(2026, 10, 13, 21, 30, 0, TimeSpan.Zero);

        Assert.Equal("Sıfırlanma 14 Eki 00:30", UsageText.ResetAt(resetsAt, Now, Plus3));
    }

    [Fact]
    public void StaleFootnoteDescribesTheProviderObservation()
    {
        Assert.Equal("Veri güncel olmayabilir", UsageText.StaleFootnote(null, Now, Plus3));
        Assert.Equal("Az önce itibarıyla", UsageText.StaleFootnote(Now.AddSeconds(-20), Now, Plus3));
        Assert.Equal("12 dk önce itibarıyla", UsageText.StaleFootnote(Now.AddMinutes(-12), Now, Plus3));
        Assert.Equal("3 sa önce itibarıyla", UsageText.StaleFootnote(Now.AddHours(-3), Now, Plus3));
        Assert.Equal("11 Eki 13:00 itibarıyla", UsageText.StaleFootnote(Now.AddDays(-2), Now, Plus3));
    }

    [Theory]
    [InlineData("plus", "Plan: Plus")]
    [InlineData(" pro ", "Plan: Pro")]
    [InlineData("individual", "Plan: Individual")]
    [InlineData("Max", "Plan: Max")]
    public void PlanIsTheReportedNameCapitalised(string reported, string expected)
    {
        Assert.Equal(expected, UsageText.Plan(reported));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void UnreportedPlanShowsNothing(string? reported)
    {
        Assert.Null(UsageText.Plan(reported));
    }
}
