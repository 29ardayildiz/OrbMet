using OrbMet.Core.Localization;
using OrbMet.Core.Models;
using OrbMet.Core.Usage;

namespace OrbMet.Core.Tests;

public class UsageTextEnglishTests
{
    private static readonly UsageText Text = UsageText.For(UiLanguage.English);

    // 13:00 local in the +03:00 test zone.
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 10, 0, 0, TimeSpan.Zero);

    private static readonly TimeZoneInfo Plus3 =
        TimeZoneInfo.CreateCustomTimeZone("test+3", TimeSpan.FromHours(3), "test+3", "test+3");

    [Fact]
    public void PercentPutsTheSignAfterAndMissingIsStillADash()
    {
        Assert.Equal("19%", Text.Percent(19));
        Assert.Equal("130%", Text.Percent(130));
        Assert.Equal("—", Text.Percent(null));
        Assert.Equal("6% used", Text.Used(6));
        Assert.Equal("Usage not reported", Text.Used(null));
    }

    [Theory]
    [InlineData(5 * 3600L, "5-hour limit")]
    [InlineData(7 * 86400L, "Weekly limit")]
    [InlineData(86400L, "Daily limit")]
    [InlineData(30 * 86400L, "30-day limit")]
    public void NamesKnownDurations(long seconds, string expected)
    {
        Assert.Equal(expected, Text.LimitTitle(new UsageWindow { Id = "w", DurationSeconds = seconds }, "Codex"));
    }

    [Fact]
    public void UnknownLengthAndScopeReadInEnglish()
    {
        var scoped = new UsageWindow { Id = "w", DurationSeconds = 7 * 86400, ScopeLabel = "gpt-5" };

        Assert.Equal("Weekly limit · gpt-5", Text.LimitTitle(scoped, "Codex"));
        Assert.Equal("Usage limit", Text.LimitTitle(new UsageWindow { Id = "w" }, "Codex"));
    }

    [Fact]
    public void ResetsUseEnglishMonthsAndTheLocalZone()
    {
        Assert.Equal("Resets 15:14", Text.ResetAt(Now.AddMinutes(134), Now, Plus3));
        Assert.Equal("Resets Oct 15, 14:30", Text.ResetAt(new DateTimeOffset(2026, 10, 15, 11, 30, 0, TimeSpan.Zero), Now, Plus3));
        Assert.Equal("Waiting for new data", Text.ResetAt(Now.AddHours(-2), Now, Plus3));
        Assert.Equal(string.Empty, Text.ResetAt(null, Now, Plus3));
    }

    [Fact]
    public void StaleFootnoteReadsInEnglish()
    {
        Assert.Equal("Data may be out of date", Text.StaleFootnote(null, Now, Plus3));
        Assert.Equal("As of just now", Text.StaleFootnote(Now.AddSeconds(-20), Now, Plus3));
        Assert.Equal("As of 12 min ago", Text.StaleFootnote(Now.AddMinutes(-12), Now, Plus3));
        Assert.Equal("As of 3 h ago", Text.StaleFootnote(Now.AddHours(-3), Now, Plus3));
        Assert.Equal("As of Oct 11, 13:00", Text.StaleFootnote(Now.AddDays(-2), Now, Plus3));
    }

    [Fact]
    public void PlanReadsTheSame()
    {
        Assert.Equal("Plan: Plus", Text.Plan("plus"));
    }
}
