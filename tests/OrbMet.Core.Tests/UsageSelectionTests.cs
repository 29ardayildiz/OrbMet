using OrbMet.Core.Models;
using OrbMet.Core.Usage;

namespace OrbMet.Core.Tests;

public class UsageSelectionTests
{
    private static ProviderSnapshot Snapshot(params UsageWindow[] windows) => new()
    {
        ProviderId = ProviderId.Codex,
        SourceKind = SourceKind.CodexAppServer,
        Windows = windows,
    };

    [Fact]
    public void SelectsHighestAccountWideWindow()
    {
        var snapshot = Snapshot(
            new UsageWindow { Id = "five_hour", UsedPercent = 42 },
            new UsageWindow { Id = "seven_day", UsedPercent = 61 });

        var selected = UsageSelection.SelectRingWindow(snapshot);

        Assert.NotNull(selected);
        Assert.Equal("seven_day", selected!.Id);
    }

    [Fact]
    public void ShowsTheFiveHourWindowEvenWhenTheWeeklyOneIsFuller()
    {
        var snapshot = Snapshot(
            new UsageWindow { Id = "seven_day", UsedPercent = 30, DurationSeconds = 7 * 24 * 60 * 60 },
            new UsageWindow { Id = "five_hour", UsedPercent = 15, DurationSeconds = 5 * 60 * 60 });

        Assert.Equal("five_hour", UsageSelection.SelectRingWindow(snapshot)!.Id);
    }

    [Fact]
    public void ShowsTheFiveHourWindowEvenWhenTheWeeklyOneIsSpent()
    {
        var snapshot = Snapshot(
            new UsageWindow { Id = "seven_day", UsedPercent = 100, DurationSeconds = 7 * 24 * 60 * 60 },
            new UsageWindow { Id = "five_hour", UsedPercent = 10, DurationSeconds = 5 * 60 * 60 });

        Assert.Equal("five_hour", UsageSelection.SelectRingWindow(snapshot)!.Id);
    }

    [Fact]
    public void FallsBackToTheHighestWhenTheFiveHourWindowHasNoValue()
    {
        var snapshot = Snapshot(
            new UsageWindow { Id = "five_hour", UsedPercent = null, DurationSeconds = 5 * 60 * 60 },
            new UsageWindow { Id = "seven_day", UsedPercent = 30, DurationSeconds = 7 * 24 * 60 * 60 },
            new UsageWindow { Id = "monthly", UsedPercent = 12 });

        Assert.Equal("seven_day", UsageSelection.SelectRingWindow(snapshot)!.Id);
    }

    [Fact]
    public void AGroupedFiveHourWindowDoesNotTakeTheRing()
    {
        var snapshot = Snapshot(
            new UsageWindow { Id = "weekly", UsedPercent = 20, DurationSeconds = 7 * 24 * 60 * 60 },
            new UsageWindow { Id = "opus", GroupId = "opus", ScopeLabel = "Opus", UsedPercent = 5, DurationSeconds = 5 * 60 * 60 });

        Assert.Equal("weekly", UsageSelection.SelectRingWindow(snapshot)!.Id);
    }

    [Fact]
    public void IgnoresWindowsWithoutAReportedPercentage()
    {
        var snapshot = Snapshot(
            new UsageWindow { Id = "five_hour", UsedPercent = null },
            new UsageWindow { Id = "seven_day", UsedPercent = 10 });

        var selected = UsageSelection.SelectRingWindow(snapshot);

        Assert.NotNull(selected);
        Assert.Equal("seven_day", selected!.Id);
    }

    [Fact]
    public void ReturnsNullWhenNothingIsKnown()
    {
        var snapshot = Snapshot(
            new UsageWindow { Id = "five_hour", UsedPercent = null });

        Assert.Null(UsageSelection.SelectRingWindow(snapshot));
    }

    [Fact]
    public void ReturnsNullForAnEmptySnapshot()
    {
        Assert.Null(UsageSelection.SelectRingWindow(Snapshot()));
    }

    [Fact]
    public void BreaksTiesByStableIdOrder()
    {
        var snapshot = Snapshot(
            new UsageWindow { Id = "b_window", UsedPercent = 50 },
            new UsageWindow { Id = "a_window", UsedPercent = 50 });

        var selected = UsageSelection.SelectRingWindow(snapshot);

        Assert.NotNull(selected);
        Assert.Equal("a_window", selected!.Id);
    }

    [Fact]
    public void FallsBackToGroupedWindowsWhenNoAccountWideValueExists()
    {
        var snapshot = Snapshot(
            new UsageWindow { Id = "opus", GroupId = "opus", ScopeLabel = "Opus", UsedPercent = 30 },
            new UsageWindow { Id = "sonnet", GroupId = "sonnet", ScopeLabel = "Sonnet", UsedPercent = 77 });

        var selected = UsageSelection.SelectRingWindow(snapshot);

        Assert.NotNull(selected);
        Assert.Equal("sonnet", selected!.Id);
        Assert.NotNull(selected.ScopeLabel);
    }

    [Fact]
    public void PrefersAccountWideEvenWhenAGroupValueIsHigher()
    {
        var snapshot = Snapshot(
            new UsageWindow { Id = "weekly", UsedPercent = 20 },
            new UsageWindow { Id = "opus", GroupId = "opus", ScopeLabel = "Opus", UsedPercent = 95 });

        var selected = UsageSelection.SelectRingWindow(snapshot);

        Assert.NotNull(selected);
        Assert.Equal("weekly", selected!.Id);
    }

    [Fact]
    public void DoesNotClampValuesAboveOneHundred()
    {
        var snapshot = Snapshot(
            new UsageWindow { Id = "weekly", UsedPercent = 130 });

        var selected = UsageSelection.SelectRingWindow(snapshot);

        Assert.NotNull(selected);
        Assert.Equal(130, selected!.UsedPercent);
    }

    private static ProviderSnapshot FiveHourAndWeekly(double fiveHour, double weekly) => Snapshot(
        new UsageWindow { Id = "five_hour", UsedPercent = fiveHour, DurationSeconds = UsageSelection.FiveHourSeconds },
        new UsageWindow { Id = "seven_day", UsedPercent = weekly, DurationSeconds = UsageSelection.WeeklySeconds });

    [Fact]
    public void TheWeeklyChoiceShowsTheWeeklyWindowEvenWhenTheFiveHourOneIsFuller()
    {
        Assert.Equal("seven_day", UsageSelection.SelectRingWindow(FiveHourAndWeekly(80, 20), RingChoice.Weekly)!.Id);
    }

    [Fact]
    public void TheWeeklyChoiceFallsBackToTheFiveHourWindow()
    {
        var snapshot = Snapshot(
            new UsageWindow { Id = "five_hour", UsedPercent = 10, DurationSeconds = UsageSelection.FiveHourSeconds },
            new UsageWindow { Id = "monthly", UsedPercent = 50, DurationSeconds = 30 * 24 * 60 * 60 });

        Assert.Equal("five_hour", UsageSelection.SelectRingWindow(snapshot, RingChoice.Weekly)!.Id);
    }

    [Fact]
    public void TheWeeklyChoiceFallsBackToTheFullestWithNeitherWindow()
    {
        var snapshot = Snapshot(
            new UsageWindow { Id = "daily", UsedPercent = 10, DurationSeconds = 24 * 60 * 60 },
            new UsageWindow { Id = "monthly", UsedPercent = 50, DurationSeconds = 30 * 24 * 60 * 60 });

        Assert.Equal("monthly", UsageSelection.SelectRingWindow(snapshot, RingChoice.Weekly)!.Id);
    }

    [Theory]
    [InlineData(80, 20, "five_hour")]
    [InlineData(20, 80, "seven_day")]
    public void TheClosestChoiceShowsTheFullestWindow(double fiveHour, double weekly, string expected)
    {
        Assert.Equal(expected, UsageSelection.SelectRingWindow(FiveHourAndWeekly(fiveHour, weekly), RingChoice.ClosestToLimit)!.Id);
    }

    [Fact]
    public void TheWeeklyChoiceFindsCodexsGroupedWeeklyWindow()
    {
        var snapshot = Snapshot(
            new UsageWindow { Id = "primary", GroupId = "codex", UsedPercent = 40, DurationSeconds = UsageSelection.FiveHourSeconds },
            new UsageWindow { Id = "secondary", GroupId = "codex", UsedPercent = 5, DurationSeconds = UsageSelection.WeeklySeconds });

        Assert.Equal("secondary", UsageSelection.SelectRingWindow(snapshot, RingChoice.Weekly)!.Id);
    }

    [Fact]
    public void TheChoiceIsRememberedAndOtherSettingsKept()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "settings.json");

        Assert.Equal(RingChoice.FiveHour, new RingChoiceStore(path).Load());

        new OrbMet.Core.Storage.WelcomeStore(path).MarkConnectionsOffered();
        Assert.True(new RingChoiceStore(path).Save(RingChoice.Weekly));
        Assert.Equal(RingChoice.Weekly, new RingChoiceStore(path).Load());

        Assert.True(new RingChoiceStore(path).Save(RingChoice.ClosestToLimit));
        Assert.Equal(RingChoice.ClosestToLimit, new RingChoiceStore(path).Load());
        Assert.True(new OrbMet.Core.Storage.WelcomeStore(path).ConnectionsOffered);
    }

    [Fact]
    public void AnUnknownStoredChoiceMeansTheFiveHourLimit()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "settings.json");
        File.WriteAllText(path, """{"Ring": "monthly"}""");

        Assert.Equal(RingChoice.FiveHour, new RingChoiceStore(path).Load());
    }
}
