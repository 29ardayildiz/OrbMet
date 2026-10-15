using OrbMet.Core.Localization;
using OrbMet.Core.Usage;

namespace OrbMet.Core.Tests;

public class CountdownTests
{
    private static readonly UsageText Turkish = UsageText.For(UiLanguage.Turkish);
    private static readonly UsageText English = UsageText.For(UiLanguage.English);

    // 13:00 local in the +03:00 test zone.
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 10, 0, 0, TimeSpan.Zero);

    private static readonly TimeZoneInfo Plus3 =
        TimeZoneInfo.CreateCustomTimeZone("test+3", TimeSpan.FromHours(3), "test+3", "test+3");

    [Theory]
    [InlineData(134, "2 sa 14 dk sonra", "in 2 h 14 min")]
    [InlineData(120, "2 sa sonra", "in 2 h")]
    [InlineData(14, "14 dk sonra", "in 14 min")]
    [InlineData(0.5, "1 dk içinde", "in under a minute")]
    [InlineData(39 * 60, "1 gün 15 sa sonra", "in 1 d 15 h")]
    [InlineData(48 * 60 + 20, "2 gün sonra", "in 2 d")]
    public void TheCountdownLeavesOutZeroPartsAndRoundsDown(double minutes, string turkish, string english)
    {
        var left = TimeSpan.FromMinutes(minutes);

        Assert.Equal(turkish, Turkish.Countdown(left));
        Assert.Equal(english, English.Countdown(left));
    }

    [Fact]
    public void TheResetLineCarriesTheCountdown()
    {
        Assert.Equal("Sıfırlanma 15:14 · 2 sa 14 dk sonra", Turkish.ResetWithCountdown(Now.AddMinutes(134), Now, Plus3));
        Assert.Equal("Resets 15:14 · in 2 h 14 min", English.ResetWithCountdown(Now.AddMinutes(134), Now, Plus3));
    }

    [Fact]
    public void AnotherDaysResetShowsItsDateAndDays()
    {
        Assert.Equal("Sıfırlanma 15 Eki 04:00 · 1 gün 15 sa sonra", Turkish.ResetWithCountdown(Now.AddHours(39), Now, Plus3));
    }

    [Fact]
    public void NoCountdownForAnUnknownOrPassedReset()
    {
        Assert.Equal(string.Empty, Turkish.ResetWithCountdown(null, Now, Plus3));
        Assert.Equal("Yeni veri bekleniyor", Turkish.ResetWithCountdown(Now.AddMinutes(-1), Now, Plus3));
    }
}
