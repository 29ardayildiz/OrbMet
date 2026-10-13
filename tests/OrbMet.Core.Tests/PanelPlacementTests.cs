using OrbMet.Core.Placement;

namespace OrbMet.Core.Tests;

public class PanelPlacementTests
{
    // A 1920x1032 work area, the taskbar below it.
    private static readonly Box Area = new(0, 0, 1920, 1032);
    private const double Width = 64;
    private const double Height = 190;

    [Fact]
    public void DefaultIsDockedRightAndCentred()
    {
        var (x, y) = PanelLayout.RailOrigin(PanelPosition.Default, Area, Width, Height);

        Assert.Equal(1920 - 64 - 8, x);
        Assert.Equal((1032 - 190) / 2.0, y);
    }

    [Fact]
    public void DockedSidesStandTheInsetOffEveryEdge()
    {
        var left = new PanelPosition(PanelDock.Left, 0.7, 0, null);
        var right = new PanelPosition(PanelDock.Right, 0.2, 1, null);

        Assert.Equal((8, 8), PanelLayout.RailOrigin(left, Area, Width, Height));
        Assert.Equal((1920 - 64 - 8, 1032 - 190 - 8), PanelLayout.RailOrigin(right, Area, Width, Height));
    }

    [Fact]
    public void TopAndBottomSlideAlongTheEdge()
    {
        const double length = 202;
        var top = new PanelPosition(PanelDock.Top, 0.5, 0.9, null);
        var bottom = new PanelPosition(PanelDock.Bottom, 0, 0.1, null);

        Assert.Equal((8 + (1920 - length - 16) / 2, 8), PanelLayout.RailOrigin(top, Area, length, Width));
        Assert.Equal((8, 1032 - 64 - 8), PanelLayout.RailOrigin(bottom, Area, length, Width));
    }

    [Theory]
    [InlineData(PanelDock.TopLeft, 8, 8)]
    [InlineData(PanelDock.TopRight, 1920 - 164 - 8, 8)]
    [InlineData(PanelDock.BottomLeft, 8, 1032 - 152 - 8)]
    [InlineData(PanelDock.BottomRight, 1920 - 164 - 8, 1032 - 152 - 8)]
    public void CornersSitTheInsetOffBothEdgesWhateverTheRatios(PanelDock corner, double x, double y)
    {
        var position = new PanelPosition(corner, 0.3, 0.6, null);

        Assert.Equal((x, y), PanelLayout.RailOrigin(position, Area, 164, 152));
    }

    [Fact]
    public void DockedInsetHoldsOnANegativeDisplay()
    {
        var area = new Box(-1920, 0, 1920, 1032);

        Assert.Equal(-1920 + 8, PanelLayout.DockedX(PanelDock.Left, area, Width));
        Assert.Equal(-64 - 8, PanelLayout.DockedX(PanelDock.Right, area, Width));
        Assert.Equal(-64 - 8, PanelLayout.DockedX(PanelDock.BottomRight, area, Width));
    }

    [Fact]
    public void FloatingIsPulledOffBothSides()
    {
        var atLeft = new PanelPosition(PanelDock.Floating, 0, 0.5, null);
        var atRight = new PanelPosition(PanelDock.Floating, 1, 0.5, null);

        Assert.Equal(PanelLayout.DockDistance, PanelLayout.RailOrigin(atLeft, Area, Width, Height).X);
        Assert.Equal(1920 - Width - PanelLayout.DockDistance, PanelLayout.RailOrigin(atRight, Area, Width, Height).X);
    }

    [Theory]
    [InlineData(960, 500, PanelDock.Floating)]
    [InlineData(96, 500, PanelDock.Left)]
    [InlineData(97, 500, PanelDock.Floating)]
    [InlineData(1920 - 96, 500, PanelDock.Right)]
    [InlineData(1920 - 97, 500, PanelDock.Floating)]
    [InlineData(960, 96, PanelDock.Top)]
    [InlineData(960, 1032 - 96, PanelDock.Bottom)]
    [InlineData(40, 40, PanelDock.TopLeft)]
    [InlineData(1900, 20, PanelDock.TopRight)]
    [InlineData(10, 1020, PanelDock.BottomLeft)]
    [InlineData(1880, 1000, PanelDock.BottomRight)]
    public void FusesToTheEdgeOrCornerThePointerReaches(double x, double y, PanelDock expected)
    {
        Assert.Equal(expected, PanelLayout.DockFor(x, y, Area));
    }

    [Fact]
    public void ReachIsMeasuredOnTheDisplayUnderThePointer()
    {
        var left = new Box(-1920, 0, 1920, 1032);

        Assert.Equal(PanelDock.Right, PanelLayout.DockFor(-40, 500, left));
        Assert.Equal(PanelDock.BottomLeft, PanelLayout.DockFor(-1900, 1000, left));
    }

    [Theory]
    [InlineData(PanelDock.Floating)]
    [InlineData(PanelDock.Left)]
    [InlineData(PanelDock.Top)]
    public void RatiosRoundTripThroughThePlacement(PanelDock dock)
    {
        var area = new Box(-1280, 40, 1280, 984);
        var (w, h) = dock == PanelDock.Top ? (202.0, 64.0) : (Width, Height);
        var (x, y) = PanelLayout.DragOrigin(dock, -900, 300, area, w, h);
        var (horizontal, vertical) = PanelLayout.Ratios(dock, x, y, area, w, h);

        var restored = PanelLayout.RailOrigin(new PanelPosition(dock, horizontal, vertical, "DISPLAY2"), area, w, h);

        Assert.Equal(x, restored.X, 6);
        Assert.Equal(y, restored.Y, 6);
    }

    [Fact]
    public void DraggingAlongASideKeepsItsInsetAtTheEnds()
    {
        Assert.Equal((1920 - 64 - 8, 8), PanelLayout.DragOrigin(PanelDock.Right, 1500, -300, Area, Width, Height));
        Assert.Equal((8, 1032 - 64 - 8), PanelLayout.DragOrigin(PanelDock.Bottom, -50, 900, Area, 202, 64));
    }

    [Fact]
    public void ARatioKeepsItsPlaceOnALargerDisplay()
    {
        var bottom = new PanelPosition(PanelDock.Left, 0, 1, null);
        var large = new Box(0, 0, 2560, 1400);

        Assert.Equal(1400 - Height - 8, PanelLayout.RailOrigin(bottom, large, Width, Height).Y);
    }

    [Fact]
    public void BrokenRatiosFallBackToTheMiddle()
    {
        var position = new PanelPosition(PanelDock.Right, double.NaN, double.PositiveInfinity, null);

        Assert.Equal((1032 - 190) / 2.0, PanelLayout.RailOrigin(position, Area, Width, Height).Y);
    }

    [Fact]
    public void ClampKeepsTheStripOnTheWorkArea()
    {
        Assert.Equal((0, 0), PanelLayout.Clamp(-50, -50, Area, Width, Height));
        Assert.Equal((1856, 1032 - 190), PanelLayout.Clamp(5000, 5000, Area, Width, Height));
    }

    [Fact]
    public void ShapesFollowTheDock()
    {
        Assert.Equal(PanelShape.Vertical, PanelLayout.ShapeOf(PanelDock.Floating));
        Assert.Equal(PanelShape.Vertical, PanelLayout.ShapeOf(PanelDock.Left));
        Assert.Equal(PanelShape.Horizontal, PanelLayout.ShapeOf(PanelDock.Bottom));
        Assert.Equal(PanelShape.Corner, PanelLayout.ShapeOf(PanelDock.TopRight));
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
    public void StoreKeepsTheRingOrder()
    {
        using var temp = new TempDirectory();
        var store = new PanelPositionStore(Path.Combine(temp.Path, "panel.json"));
        var position = new PanelPosition(PanelDock.Bottom, 0.4, 1, null, Reversed: true);

        Assert.True(store.Save(position));
        Assert.Equal(position, store.Load());
    }

    [Fact]
    public void AFileFromBeforeTheRingOrderReadsInOrder()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "panel.json");
        File.WriteAllText(path, """{ "Dock": "TopRight", "HorizontalRatio": 1, "VerticalRatio": 0, "DisplayId": null }""");

        Assert.Equal(new PanelPosition(PanelDock.TopRight, 1, 0, null), new PanelPositionStore(path).Load());
    }

    [Fact]
    public void RingsTakeTheArrangementTheyReachWithoutCrossing()
    {
        // A horizontal capsule along the bottom, Claude left of Codex, moving
        // into the bottom-left L: Claude is nearer the vertical arm's slot,
        // so Codex takes the horizontal arm.
        var now = new List<(double X, double Y)> { (40, 1040), (138, 1040) };
        var claudeAcross = new List<(double X, double Y)> { (110, 1040), (40, 950) };
        var codexAcross = new List<(double X, double Y)> { (40, 950), (110, 1040) };

        Assert.Equal(1, PanelLayout.NearestArrangement(now, new[] { claudeAcross, codexAcross }));
        Assert.Equal(0, PanelLayout.NearestArrangement(now, new[] { codexAcross, claudeAcross }));
    }

    [Fact]
    public void RingsCarryOnTheWayTheyWereGoingOutOfACorner()
    {
        // The bottom-right L, Claude up the vertical arm and Codex along the
        // bottom, slid left onto the bottom edge: Codex stays in front, so
        // the capsule runs Codex then Claude rather than swapping them.
        var now = new List<(double X, double Y)> { (1880, 950), (1790, 1040) };
        var inOrder = new List<(double X, double Y)> { (1740, 1040), (1838, 1040) };
        var reversed = new List<(double X, double Y)> { (1838, 1040), (1740, 1040) };

        Assert.Equal(1, PanelLayout.NearestArrangement(now, new[] { inOrder, reversed }));
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

        File.WriteAllText(path, """{ "Dock": 42, "HorizontalRatio": 0, "VerticalRatio": 0 }""");
        Assert.Equal(PanelPosition.Default, store.Load());
    }
}
