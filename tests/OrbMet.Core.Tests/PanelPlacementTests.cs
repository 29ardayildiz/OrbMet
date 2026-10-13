using OrbMet.Core.Placement;

namespace OrbMet.Core.Tests;

public class PanelPlacementTests
{
    // A 1920x1032 work area, the taskbar below it.
    private static readonly Box Area = new(0, 0, 1920, 1032);
    private const double Width = 64;
    private const double Docked = 238;
    private const double Floating = 190;

    [Fact]
    public void DefaultIsDockedRightAndCentred()
    {
        var (x, y) = PanelLayout.RailOrigin(PanelPosition.Default, Area, Width, Docked);

        Assert.Equal(1856, x);
        Assert.Equal((1032 - 238) / 2.0, y);
    }

    [Fact]
    public void DockedSidesSitFlushAgainstTheWorkArea()
    {
        var left = new PanelPosition(PanelDock.Left, 0.7, 0, null);
        var right = new PanelPosition(PanelDock.Right, 0.2, 1, null);

        Assert.Equal((0, 0), PanelLayout.RailOrigin(left, Area, Width, Docked));
        Assert.Equal((1856, 1032 - 238), PanelLayout.RailOrigin(right, Area, Width, Docked));
    }

    [Fact]
    public void FloatingIsPulledOffBothSides()
    {
        var atLeft = new PanelPosition(PanelDock.Floating, 0, 0.5, null);
        var atRight = new PanelPosition(PanelDock.Floating, 1, 0.5, null);

        Assert.Equal(PanelLayout.DockDistance, PanelLayout.RailOrigin(atLeft, Area, Width, Floating).X);
        Assert.Equal(1920 - Width - PanelLayout.DockDistance, PanelLayout.RailOrigin(atRight, Area, Width, Floating).X);
    }

    [Theory]
    [InlineData(0, PanelDock.Left)]
    [InlineData(32, PanelDock.Left)]
    [InlineData(33, PanelDock.Floating)]
    [InlineData(900, PanelDock.Floating)]
    [InlineData(1920 - 64 - 33, PanelDock.Floating)]
    [InlineData(1920 - 64 - 32, PanelDock.Right)]
    [InlineData(1856, PanelDock.Right)]
    public void FusesToASideWithinTheDockDistance(double wantedX, PanelDock expected)
    {
        Assert.Equal(expected, PanelLayout.DockFor(wantedX, Width, Area));
    }

    [Fact]
    public void RatiosRoundTripThroughThePlacement()
    {
        var area = new Box(-1280, 40, 1280, 984);
        var (h, v) = PanelLayout.Ratios(-900, 300, area, Width, Floating);
        var position = new PanelPosition(PanelDock.Floating, h, v, "DISPLAY2");

        var (x, y) = PanelLayout.RailOrigin(position, area, Width, Floating);

        Assert.Equal(-900, x, 6);
        Assert.Equal(300, y, 6);
    }

    [Fact]
    public void ARatioKeepsItsPlaceOnALargerDisplay()
    {
        var small = new Box(0, 0, 1280, 680);
        var large = new Box(0, 0, 2560, 1400);
        var (_, v) = PanelLayout.Ratios(0, 0, small, Width, Docked);

        var bottom = new PanelPosition(PanelDock.Left, 0, 1, null);

        Assert.Equal(0, v);
        Assert.Equal(1400 - Docked, PanelLayout.RailOrigin(bottom, large, Width, Docked).Y);
    }

    [Fact]
    public void BrokenRatiosFallBackToTheMiddle()
    {
        var position = new PanelPosition(PanelDock.Right, double.NaN, double.PositiveInfinity, null);

        Assert.Equal((1032 - 238) / 2.0, PanelLayout.RailOrigin(position, Area, Width, Docked).Y);
    }

    [Fact]
    public void ClampKeepsTheStripOnTheWorkArea()
    {
        Assert.Equal((0, 0), PanelLayout.Clamp(-50, -50, Area, Width, Docked));
        Assert.Equal((1856, 1032 - 238), PanelLayout.Clamp(5000, 5000, Area, Width, Docked));
    }

    [Fact]
    public void StoreRoundTripsAPosition()
    {
        using var temp = new TempDirectory();
        var store = new PanelPositionStore(Path.Combine(temp.Path, "panel.json"));
        var position = new PanelPosition(PanelDock.Floating, 0.25, 0.75, @"\\.\DISPLAY2");

        Assert.True(store.Save(position));
        Assert.Equal(position, store.Load());
    }

    [Fact]
    public void MissingOrBrokenFileMeansTheDefaultPlace()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "panel.json");
        var store = new PanelPositionStore(path);

        Assert.Equal(PanelPosition.Default, store.Load());

        File.WriteAllText(path, "{ not json");
        Assert.Equal(PanelPosition.Default, store.Load());

        File.WriteAllText(path, """{ "Dock": 7, "HorizontalRatio": 0, "VerticalRatio": 0 }""");
        Assert.Equal(PanelPosition.Default, store.Load());
    }
}
