using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using OrbMet.Desktop.Infrastructure;

namespace OrbMet.Desktop.Controls;

/// <summary>
/// The usage ring from design.md section 6: a white 18% track, a round-capped
/// arc clockwise from 12 o'clock in the usage colour with a white satellite
/// dot riding its tip, and the provider's logo in the middle. A missing value is the bare track, never a full ring. The
/// arc caps at a full circle while the text beside it keeps the raw value; a
/// spent limit fills the ring whatever the number.
/// </summary>
public sealed class UsageRing : FrameworkElement
{
    private const double SelectedScale = 1.06;
    private const double HaloRadius = 10;
    private const double HaloOpacity = 0.7;

    // WPF's blur spreads a thin stroke much thinner than SwiftUI's shadow
    // does, so Pulse's numbers (0.42, the arc itself) do not carry over.
    // Measured just outside the arc: 4 DIP at 0.42 gave 2/255 of green and
    // 10 DIP at 0.42 gave 9/255, both effectively invisible.
    private const double HaloStroke = 12;
    private const double RefreshingArcOpacity = 0.3;
    private const double SatelliteDiameter = 5.2;
    private const double RefreshSweep = 0.16;
    private static readonly TimeSpan RefreshPeriod = TimeSpan.FromSeconds(0.85);

    public static readonly DependencyProperty PercentProperty = DependencyProperty.Register(
        nameof(Percent),
        typeof(double?),
        typeof(UsageRing),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnReadingChanged));

    public static readonly DependencyProperty IsExhaustedProperty = DependencyProperty.Register(
        nameof(IsExhausted),
        typeof(bool),
        typeof(UsageRing),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender, OnReadingChanged));

    /// <summary>The share of the circle the arc covers on screen, eased towards the reading.</summary>
    private static readonly DependencyProperty ShownFractionProperty = DependencyProperty.Register(
        "ShownFraction",
        typeof(double),
        typeof(UsageRing),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MarkProperty = DependencyProperty.Register(
        nameof(Mark),
        typeof(string),
        typeof(UsageRing),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsSelectedProperty = DependencyProperty.Register(
        nameof(IsSelected),
        typeof(bool),
        typeof(UsageRing),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender, OnIsSelectedChanged));

    public static readonly DependencyProperty IsRefreshingProperty = DependencyProperty.Register(
        nameof(IsRefreshing),
        typeof(bool),
        typeof(UsageRing),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender, OnIsRefreshingChanged));

    private readonly Stopwatch _refreshClock = new();
    private bool _animating;

    // Two layers: a blurred copy of the arc underneath, the ring itself on
    // top. The clip sits outside the blur so the blurred edge is cut too.
    private readonly ContainerVisual _haloClip = new();
    private readonly DrawingVisual _halo = new();
    private readonly DrawingVisual _ring = new();

    public UsageRing()
    {
        RenderTransformOrigin = new Point(0.5, 0.5);
        Unloaded += (_, _) => StopAnimating();

        var blurred = new ContainerVisual
        {
            Opacity = HaloOpacity,
            Effect = new BlurEffect { Radius = HaloRadius, KernelType = KernelType.Gaussian },
        };
        blurred.Children.Add(_halo);
        _haloClip.Children.Add(blurred);

        AddVisualChild(_haloClip);
        AddVisualChild(_ring);
    }

    protected override int VisualChildrenCount => 2;

    protected override Visual GetVisualChild(int index) => index switch
    {
        0 => _haloClip,
        1 => _ring,
        _ => throw new ArgumentOutOfRangeException(nameof(index)),
    };

    /// <summary>Provider-reported used percentage, or null when it is missing.</summary>
    public double? Percent
    {
        get => (double?)GetValue(PercentProperty);
        set => SetValue(PercentProperty, value);
    }

    /// <summary>True only when the provider explicitly reports being blocked.</summary>
    public bool IsExhausted
    {
        get => (bool)GetValue(IsExhaustedProperty);
        set => SetValue(IsExhaustedProperty, value);
    }

    /// <summary>Asset name of the provider logo, such as "claude".</summary>
    public string Mark
    {
        get => (string)GetValue(MarkProperty);
        set => SetValue(MarkProperty, value);
    }

    /// <summary>The card is open for this ring: it grows slightly and its arc casts a halo.</summary>
    public bool IsSelected
    {
        get => (bool)GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    /// <summary>A refresh is in flight: the arc dims in place and a short segment travels round.</summary>
    public bool IsRefreshing
    {
        get => (bool)GetValue(IsRefreshingProperty);
        set => SetValue(IsRefreshingProperty, value);
    }

    /// <summary>The whole disc is the ring's target, not only its painted stroke.</summary>
    protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters)
    {
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var radius = Math.Min(ActualWidth, ActualHeight) / 2;
        return (hitTestParameters.HitPoint - center).Length <= radius
            ? new PointHitTestResult(this, hitTestParameters.HitPoint)
            : null;
    }

    // Called whenever a property that affects rendering changes; the drawing
    // goes into the two child layers rather than the element's own content.
    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        using var halo = _halo.RenderOpen();
        using var ring = _ring.RenderOpen();

        var size = Math.Min(ActualWidth, ActualHeight);
        var thickness = PanelMetrics.RingThickness;
        var radius = (size - thickness) / 2;
        if (radius <= 0)
        {
            return;
        }

        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        ring.DrawEllipse(null, new Pen(UsagePalette.RingTrack, thickness), center, radius, radius);

        var colour = UsagePalette.For(Percent ?? 0, IsExhausted);
        var arc = BuildUsageArc(center, radius);

        if (arc is not null && IsSelected)
        {
            // The glow spreads both ways; only its outward half is kept, so
            // it never washes behind the logo.
            _haloClip.Clip = OutsideCircle(center, radius - thickness / 2, size);
            halo.DrawGeometry(null, RoundPen(colour, HaloStroke), arc);
        }

        if (arc is not null)
        {
            if (IsRefreshing)
            {
                ring.PushOpacity(RefreshingArcOpacity);
            }

            ring.DrawGeometry(null, RoundPen(colour, thickness), arc);

            // The satellite: OrbMet's own mark on the gauge, at the arc's
            // tip, so it travels with the arc as it eases.
            var tip = PointOnCircle(center, radius, ShownFractionTipDegrees() - 90);
            ring.DrawEllipse(UsagePalette.Text, null, tip, SatelliteDiameter / 2, SatelliteDiameter / 2);

            if (IsRefreshing)
            {
                ring.Pop();
            }
        }

        if (IsRefreshing)
        {
            // Never rotate the gauge itself; a separate segment says "working".
            var turns = SystemParameters.ClientAreaAnimation
                ? _refreshClock.Elapsed.TotalSeconds / RefreshPeriod.TotalSeconds
                : 0;
            var start = (turns - Math.Floor(turns)) * 360;
            ring.DrawGeometry(null, RoundPen(colour, thickness), Arc(center, radius, start, RefreshSweep));
        }

        DrawMark(ring, center, size, thickness);
    }

    /// <summary>Where the arc ends, clockwise from 12 o'clock; a full ring ends back at 12.</summary>
    private double ShownFractionTipDegrees()
    {
        var fraction = (double)GetValue(ShownFractionProperty);
        return fraction >= 0.999 ? 0 : 360.0 * fraction;
    }

    /// <summary>The usage arc, a full circle when spent, or null when there is nothing to draw.</summary>
    private Geometry? BuildUsageArc(Point center, double radius)
    {
        if (Percent is not { } percent)
        {
            return null;
        }

        var fraction = (double)GetValue(ShownFractionProperty);
        if (fraction <= 0)
        {
            return null;
        }

        if (fraction >= 0.999)
        {
            var circle = new EllipseGeometry(center, radius, radius);
            circle.Freeze();
            return circle;
        }

        return Arc(center, radius, 0, fraction);
    }

    /// <summary>Everything around the ring except its inside, with room for the blur.</summary>
    private static Geometry OutsideCircle(Point center, double innerRadius, double size)
    {
        var margin = HaloRadius * 3;
        var outside = new CombinedGeometry(
            GeometryCombineMode.Exclude,
            new RectangleGeometry(new Rect(-margin, -margin, size + margin * 2, size + margin * 2)),
            new EllipseGeometry(center, innerRadius, innerRadius));
        outside.Freeze();
        return outside;
    }

    private void DrawMark(DrawingContext drawingContext, Point center, double size, double thickness)
    {
        if (ProviderMarks.Get(Mark) is not { } mark)
        {
            return;
        }

        // The logo sits in the disc left inside the ring: the ring's inner
        // edge, less a small gap, scaled down so its margin stays steady.
        var disc = size - 2 * thickness - 2 * PanelMetrics.RingCentreGap;
        var markSize = Math.Max(0, disc * PanelMetrics.MarkScale);
        var scale = markSize / ProviderMarks.ViewBoxSize;

        drawingContext.PushTransform(new TranslateTransform(center.X - markSize / 2, center.Y - markSize / 2));
        drawingContext.PushTransform(new ScaleTransform(scale, scale));
        drawingContext.DrawGeometry(Percent is null ? UsagePalette.IconDim : UsagePalette.Text, null, mark);
        drawingContext.Pop();
        drawingContext.Pop();
    }

    private static Pen RoundPen(Brush brush, double thickness)
    {
        var pen = new Pen(brush, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        pen.Freeze();
        return pen;
    }

    /// <summary>An arc starting <paramref name="startDegrees"/> clockwise from 12 o'clock.</summary>
    private static Geometry Arc(Point center, double radius, double startDegrees, double fraction)
    {
        var from = startDegrees - 90;
        var sweep = 360.0 * fraction;

        var figure = new PathFigure
        {
            StartPoint = PointOnCircle(center, radius, from),
            IsClosed = false,
            IsFilled = false,
        };
        figure.Segments.Add(new ArcSegment(
            PointOnCircle(center, radius, from + sweep),
            new Size(radius, radius),
            0,
            isLargeArc: sweep > 180,
            SweepDirection.Clockwise,
            isStroked: true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        geometry.Freeze();
        return geometry;
    }

    private static Point PointOnCircle(Point center, double radius, double degrees)
    {
        var radians = degrees * Math.PI / 180.0;
        return new Point(center.X + radius * Math.Cos(radians), center.Y + radius * Math.Sin(radians));
    }

    /// <summary>How much of the circle a reading fills: all of it when spent.</summary>
    private double TargetFraction() => Percent is not { } percent
        ? 0
        : UsagePalette.IsSpent(percent, IsExhausted) ? 1.0 : Math.Clamp(percent, 0, 100) / 100.0;

    /// <summary>
    /// A new reading moves the arc from where it is to where it belongs
    /// (design.md 6.1). Losing the reading removes the arc at once rather
    /// than draining it, since an empty ring would read as 0%.
    /// </summary>
    private static void OnReadingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ring = (UsageRing)d;
        var target = ring.TargetFraction();

        if (ring.Percent is null || !Motion.Enabled)
        {
            ring.BeginAnimation(ShownFractionProperty, null);
            ring.SetValue(ShownFractionProperty, target);
            return;
        }

        ring.BeginAnimation(ShownFractionProperty, Motion.To(target, Motion.Arc));
    }

    private static void OnIsSelectedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ring = (UsageRing)d;
        var scale = (bool)e.NewValue ? SelectedScale : 1.0;
        ring.RenderTransform = new ScaleTransform(scale, scale);
    }

    private static void OnIsRefreshingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ring = (UsageRing)d;
        if ((bool)e.NewValue)
        {
            ring.StartAnimating();
        }
        else
        {
            ring.StopAnimating();
        }
    }

    // Frames are only requested while a refresh is in flight; an idle ring costs nothing.
    private void StartAnimating()
    {
        _refreshClock.Restart();
        if (!_animating)
        {
            _animating = true;
            CompositionTarget.Rendering += OnRendering;
        }
    }

    private void StopAnimating()
    {
        _refreshClock.Reset();
        if (_animating)
        {
            _animating = false;
            CompositionTarget.Rendering -= OnRendering;
        }
    }

    private void OnRendering(object? sender, EventArgs e) => InvalidateVisual();
}
