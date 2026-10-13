using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using OrbMet.Desktop.Infrastructure;
using OrbMet.Desktop.ViewModels;

namespace OrbMet.Desktop;

/// <summary>Which way the card opens from the strip's arm.</summary>
public enum CardSide
{
    Left,
    Right,
    Below,
    Above,
}

/// <summary>
/// The provider detail card beside the strip (design.md 7). Like the strip it
/// never takes keyboard focus. Beside a vertical arm it is centred on the
/// ring's height; below or above a horizontal arm, on the ring's x. It is
/// always kept inside its bounds.
/// </summary>
public partial class DetailCardWindow : Window
{
    /// <summary>
    /// The ring centre the card is laid out against. Animated when the card
    /// moves to another ring, so it slides there.
    /// </summary>
    private static readonly DependencyProperty RingCentreXProperty = DependencyProperty.Register(
        "RingCentreX",
        typeof(double),
        typeof(DetailCardWindow),
        new PropertyMetadata(0.0, (d, _) => ((DetailCardWindow)d).Place()));

    private static readonly DependencyProperty RingCentreYProperty = DependencyProperty.Register(
        "RingCentreY",
        typeof(double),
        typeof(DetailCardWindow),
        new PropertyMetadata(0.0, (d, _) => ((DetailCardWindow)d).Place()));

    private readonly DispatcherTimer _clock;
    private Rect _anchor;
    private Rect _bounds;
    private CardSide _side;

    public DetailCardWindow()
    {
        InitializeComponent();

        Body.Margin = new Thickness(PanelMetrics.CardPadding);

        // Relative times ("12 dk önce") are rebuilt only while the card is up.
        _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _clock.Tick += (_, _) => Slot?.UpdateDetails(DateTimeOffset.UtcNow, TimeZoneInfo.Local);
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                _clock.Start();
            }
            else
            {
                _clock.Stop();
            }
        };

        SourceInitialized += (_, _) => NoActivateWindow.Apply(this);
        SizeChanged += (_, _) => Place();
    }

    public ProviderSlotViewModel? Slot => DataContext as ProviderSlotViewModel;

    /// <summary>
    /// Shows the card for a slot, all in DIPs: <paramref name="ringCentre"/>
    /// is the ring's centre, <paramref name="anchor"/> the strip's arm the
    /// ring is on, <paramref name="bounds"/> where the card may go, and
    /// <paramref name="side"/> the way it prefers to open when there is room.
    /// </summary>
    public void ShowFor(ProviderSlotViewModel slot, Point ringCentre, Rect anchor, Rect bounds, CardSide side)
    {
        slot.UpdateDetails(DateTimeOffset.UtcNow, TimeZoneInfo.Local);

        // Moving from one ring to another on the same side is the only change
        // that animates; opening the card puts it in place at once (design.md 8.1).
        var switching = IsVisible && !ReferenceEquals(DataContext, slot) && side == _side && Motion.Enabled;

        DataContext = slot;
        _anchor = anchor;
        _bounds = bounds;
        _side = side;

        MaxHeight = Math.Max(0, bounds.Height - 2 * PanelMetrics.WorkAreaMargin);

        if (switching)
        {
            Body.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Motion.CardContent) { EasingFunction = Motion.EaseOut });
            BeginAnimation(RingCentreXProperty, Motion.To(ringCentre.X, Motion.CardMove));
            BeginAnimation(RingCentreYProperty, Motion.To(ringCentre.Y, Motion.CardMove));
        }
        else
        {
            Body.BeginAnimation(OpacityProperty, null);
            BeginAnimation(RingCentreXProperty, null);
            BeginAnimation(RingCentreYProperty, null);
            SetValue(RingCentreXProperty, ringCentre.X);
            SetValue(RingCentreYProperty, ringCentre.Y);
        }

        if (!IsVisible)
        {
            Show();
        }

        Place();
    }

    private double RingCentreX => (double)GetValue(RingCentreXProperty);

    private double RingCentreY => (double)GetValue(RingCentreYProperty);

    private void Place()
    {
        if (!IsVisible)
        {
            return;
        }

        var margin = PanelMetrics.WorkAreaMargin;
        var gap = PanelMetrics.CardGap;
        var bounds = _bounds;
        double left;
        double top;

        if (_side is CardSide.Left or CardSide.Right)
        {
            // The preferred side, or the other one when the preferred has no room.
            var leftOfArm = _anchor.Left - gap - ActualWidth;
            var rightOfArm = _anchor.Right + gap;
            var fitsLeft = leftOfArm >= bounds.Left;
            var fitsRight = rightOfArm + ActualWidth <= bounds.Right;
            var opensRight = _side == CardSide.Right ? fitsRight || !fitsLeft : !fitsLeft && fitsRight;

            left = opensRight ? rightOfArm : leftOfArm;
            top = Within(RingCentreY - ActualHeight / 2, bounds.Top + margin, bounds.Bottom - margin - ActualHeight);
        }
        else
        {
            var belowArm = _anchor.Bottom + gap;
            var aboveArm = _anchor.Top - gap - ActualHeight;
            var fitsBelow = belowArm + ActualHeight <= bounds.Bottom;
            var fitsAbove = aboveArm >= bounds.Top;
            var opensBelow = _side == CardSide.Below ? fitsBelow || !fitsAbove : !fitsAbove && fitsBelow;

            top = opensBelow ? belowArm : aboveArm;
            left = Within(RingCentreX - ActualWidth / 2, bounds.Left + margin, bounds.Right - margin - ActualWidth);
        }

        Left = left;
        Top = top;
    }

    // Clamps to a range, preferring its start when the card is larger than it.
    private static double Within(double value, double min, double max) => Math.Max(Math.Min(value, max), min);
}
