using OrbMet.Core.Localization;
using OrbMet.Core.Models;
using OrbMet.Core.Usage;

namespace OrbMet.Core.Tests;

public class UsagePaceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    // A 5-hour window with the given share of it passed.
    private static UsageWindow FiveHour(double? percent, double elapsed, ExhaustionEvidence evidence = ExhaustionEvidence.Unknown) => new()
    {
        Id = "five_hour",
        UsedPercent = percent,
        DurationSeconds = UsageSelection.FiveHourSeconds,
        ResetsAtUtc = Now.AddSeconds(UsageSelection.FiveHourSeconds * (1 - elapsed)),
        ExhaustionEvidence = evidence,
    };

    [Fact]
    public void TheClockIsTheShareOfTheWindowGone()
    {
        Assert.Equal(0.8, UsagePace.Elapsed(FiveHour(10, 0.8), Now)!.Value, 6);
    }

    [Fact]
    public void NoClockWithoutALengthOrAResetOrOnceTheResetHasPassed()
    {
        Assert.Null(UsagePace.Elapsed(FiveHour(10, 0.5) with { DurationSeconds = null }, Now));
        Assert.Null(UsagePace.Elapsed(FiveHour(10, 0.5) with { ResetsAtUtc = null }, Now));
        Assert.Null(UsagePace.Elapsed(FiveHour(10, 0.5) with { ResetsAtUtc = Now.AddMinutes(-1) }, Now));
    }

    [Fact]
    public void AResetFurtherOutThanTheLengthIsClampedToZero()
    {
        Assert.Equal(0, UsagePace.Elapsed(FiveHour(10, 0) with { ResetsAtUtc = Now.AddHours(7) }, Now));
    }

    [Fact]
    public void BehindTheClockItLasts()
    {
        var forecast = UsagePace.Forecast(FiveHour(40, 0.8), Now);

        Assert.Equal(new UsageForecast(false, null), forecast);
    }

    [Fact]
    public void AheadOfTheClockItRunsOutAndSaysWhenWithinTwoHours()
    {
        // 60% used in the first 2 hours: 40% more takes about 1h20m, before the 3h left.
        var forecast = UsagePace.Forecast(FiveHour(60, 0.4), Now);

        Assert.True(forecast!.RunsOutBeforeReset);
        Assert.Equal(80, forecast.TimeToEmpty!.Value.TotalMinutes, 3);
    }

    [Fact]
    public void FurtherOutThanTwoHoursOnlyTheVerdictIsGiven()
    {
        // 20% in the first 30 minutes: 80% more takes 2 hours, before the 4.5 hours left.
        var forecast = UsagePace.Forecast(FiveHour(20, 0.1), Now);
        Assert.True(forecast!.RunsOutBeforeReset);
        Assert.Equal(120, forecast.TimeToEmpty!.Value.TotalMinutes, 3);

        // 15% in 30 minutes: about 2h50m, still before the reset but past the horizon.
        var slower = UsagePace.Forecast(FiveHour(15, 0.1), Now);
        Assert.Equal(new UsageForecast(true, null), slower);
    }

    [Fact]
    public void NothingIsForecastForAWindowBarelyOpenOrWithoutAFigure()
    {
        Assert.Null(UsagePace.Forecast(FiveHour(30, 0.02), Now));
        Assert.Null(UsagePace.Forecast(FiveHour(null, 0.5), Now));
    }

    [Fact]
    public void ASpentLimitHasNoForecastLine()
    {
        var text = UsageText.For(UiLanguage.English);

        Assert.Null(text.Forecast(UsagePace.Forecast(FiveHour(100, 0.5), Now)));
        Assert.Null(text.Forecast(UsagePace.Forecast(FiveHour(40, 0.5, ExhaustionEvidence.Exhausted), Now)));
    }

    [Fact]
    public void NothingUsedLasts()
    {
        Assert.Equal(new UsageForecast(false, null), UsagePace.Forecast(FiveHour(0, 0.5), Now));
    }

    [Theory]
    [InlineData(UiLanguage.English, 10, "Forecast: runs out in under 15 minutes")]
    [InlineData(UiLanguage.English, 44, "Forecast: runs out in about 45 minutes")]
    [InlineData(UiLanguage.English, 63, "Forecast: runs out in about an hour")]
    [InlineData(UiLanguage.English, 95, "Forecast: runs out in about 1.5 hours")]
    [InlineData(UiLanguage.Turkish, 95, "Tahmin: yaklaşık 1,5 saat içinde dolar")]
    [InlineData(UiLanguage.Turkish, 44, "Tahmin: yaklaşık 45 dakika içinde dolar")]
    public void TheTimeIsRounded(UiLanguage language, int minutes, string expected)
    {
        var text = UsageText.For(language).Forecast(new UsageForecast(true, TimeSpan.FromMinutes(minutes)));

        Assert.Equal(expected, text);
    }

    [Theory]
    [InlineData(UiLanguage.English, false, "Forecast: runs out before the reset")]
    [InlineData(UiLanguage.Turkish, true, "Tahmin: sıfırlanmaya kadar yeter")]
    public void TheVerdictIsMarkedAsAForecast(UiLanguage language, bool lasts, string expected)
    {
        var forecast = new UsageForecast(!lasts, null);

        Assert.Equal(expected, UsageText.For(language).Forecast(forecast));
    }

    [Theory]
    [InlineData(75.0, false, 25.0)]
    [InlineData(130.0, false, 0.0)]
    [InlineData(40.0, true, 0.0)]
    public void WhatIsLeftComesFromTheProvidersFigure(double used, bool exhausted, double left)
    {
        Assert.Equal(left, UsageText.Remaining(used, exhausted));
    }

    [Fact]
    public void MissingStaysMissingWhenShowingWhatIsLeft()
    {
        var text = UsageText.For(UiLanguage.Turkish);

        Assert.Null(UsageText.Remaining(null, false));
        Assert.Equal("—", text.Figure(null, false, showRemaining: true));
        Assert.Equal("%25 kaldı", text.UsedOrLeft(75, false, showRemaining: true));
        Assert.Equal("%75 kullanıldı", text.UsedOrLeft(75, false, showRemaining: false));
        Assert.Equal("%25", text.Figure(75, false, showRemaining: true));
    }

    [Fact]
    public void ANotificationCanSayWhatIsLeft()
    {
        var alert = new UsageAlert(ProviderId.Codex, AlertKind.Approaching, FiveHour(82, 0.5));

        var (title, _) = UsageText.For(UiLanguage.Turkish).Alert(alert, "Codex", Now, TimeZoneInfo.Utc, showRemaining: true);

        Assert.Equal("Codex: %18 kaldı", title);
    }

    [Fact]
    public void DisplaySettingsDefaultToTheClockOnAndAreKept()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "settings.json");

        var defaults = new DisplayStore(path).Load();
        Assert.True(defaults.WindowClock);
        Assert.False(defaults.ShowRemaining);
        Assert.False(defaults.Forecast);
        Assert.Equal(RingColour.Green, defaults.ColourFor(ProviderId.ClaudeCode));

        new WelcomeStoreKeeper(path).Mark();
        var changed = defaults.WithColour(ProviderId.Codex, RingColour.Purple) with { WindowClock = false, Forecast = true };
        Assert.True(new DisplayStore(path).Save(changed));

        var again = new DisplayStore(path).Load();
        Assert.Equal(changed, again);
        Assert.Equal(RingColour.Green, again.ClaudeColour);
        Assert.True(new WelcomeStoreKeeper(path).Marked);
    }

    [Fact]
    public void BrokenDisplaySettingsFallBackToTheDefaults()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "settings.json");
        File.WriteAllText(path, """{"Display": {"WindowClock": "yes", "ClaudeColour": "Gold"}}""");

        Assert.Equal(new DisplaySettings(), new DisplayStore(path).Load());
    }

    private sealed class WelcomeStoreKeeper(string path)
    {
        private readonly OrbMet.Core.Storage.WelcomeStore _store = new(path);

        public void Mark() => _store.MarkConnectionsOffered();

        public bool Marked => _store.ConnectionsOffered;
    }
}
