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
