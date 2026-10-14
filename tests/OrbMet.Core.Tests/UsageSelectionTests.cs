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
}
