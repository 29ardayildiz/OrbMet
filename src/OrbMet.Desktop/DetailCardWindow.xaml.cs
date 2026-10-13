using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using OrbMet.Desktop.Controls;
using OrbMet.Desktop.Infrastructure;
using OrbMet.Desktop.ViewModels;

namespace OrbMet.Desktop;

/// <summary>
/// The provider detail card beside the strip (design.md 7). Like the strip it
/// never takes keyboard focus. It is centred on the ring it belongs to, kept
/// inside the work area, and its tail always points at that ring's centre.
/// </summary>
public partial class DetailCardWindow : Window
{
    /// <summary>
    /// The ring centre the card is laid out against. Animated when the card
    /// moves to another ring, so the card and its tail slide together.
    /// </summary>
    private static readonly DependencyProperty RingCentreYProperty = DependencyProperty.Register(
        "RingCentreY",
        typeof(double),
        typeof(DetailCardWindow),
        new PropertyMetadata(0.0, (d, _) => ((DetailCardWindow)d).Place()));

    private readonly DispatcherTimer _clock;
    private Rect _strip;
    private Rect _workArea;
    private bool _opensRight;
    private bool _tailOnRight = true;

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
    /// Shows the card for a slot. <paramref name="strip"/> is the strip's
    /// bounds, <paramref name="ringCentreY"/> the ring's centre and
    /// <paramref name="workArea"/> the strip's display, all in DIPs.
    /// <paramref name="opensRight"/> is the side it prefers when both fit.
    /// </summary>
    public void ShowFor(ProviderSlotViewModel slot, Rect strip, double ringCentreY, Rect workArea, bool opensRight)
    {
        slot.UpdateDetails(DateTimeOffset.UtcNow, TimeZoneInfo.Local);

        // Moving from one ring to another is the only change that animates;
        // opening the card puts it in place at once (design.md 8.1).
        var switching = IsVisible && !ReferenceEquals(DataContext, slot) && Motion.Enabled;

        DataContext = slot;
        _strip = strip;
        _workArea = workArea;
        _opensRight = opensRight;

        MaxHeight = Math.Max(0, workArea.Height - 2 * PanelMetrics.WorkAreaMargin);

        if (switching)
        {
            Body.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Motion.CardContent) { EasingFunction = Motion.EaseOut });
            BeginAnimation(RingCentreYProperty, Motion.To(ringCentreY, Motion.CardMove));
        }
        else
        {
            Body.BeginAnimation(OpacityProperty, null);
            BeginAnimation(RingCentreYProperty, null);
            SetValue(RingCentreYProperty, ringCentreY);
        }

        if (!IsVisible)
        {
            Show();
        }

        Place();
    }

    private double RingCentreY => (double)GetValue(RingCentreYProperty);

    private void Place()
    {
        if (!IsVisible)
        {
            return;
        }

        var area = _workArea;

        // The preferred side, or the other one when the preferred has no room.
        var leftOfStrip = _strip.Left - PanelMetrics.CardGap - ActualWidth;
        var rightOfStrip = _strip.Right + PanelMetrics.CardGap;
        var fitsLeft = leftOfStrip >= area.Left;
        var fitsRight = rightOfStrip + ActualWidth <= area.Right;
        var opensRight = _opensRight ? fitsRight || !fitsLeft : !fitsLeft && fitsRight;

        // Opening to the right, the strip is on the card's left, so the tail is too.
        var tailOnRight = !opensRight;
        var left = opensRight ? rightOfStrip : leftOfStrip;

        var top = RingCentreY - ActualHeight / 2;
        top = Math.Min(top, area.Bottom - PanelMetrics.WorkAreaMargin - ActualHeight);
        top = Math.Max(top, area.Top + PanelMetrics.WorkAreaMargin);

        Left = left;
        Top = top;

        if (tailOnRight != _tailOnRight || Scroller.Margin == default)
        {
            _tailOnRight = tailOnRight;
            Scroller.Margin = tailOnRight
                ? new Thickness(0, 0, PanelMetrics.CardTailWidth, 0)
                : new Thickness(PanelMetrics.CardTailWidth, 0, 0, 0);
        }

        Bubble.Data = DetailBubble.Build(ActualWidth, ActualHeight, RingCentreY - top, _tailOnRight);
    }
}
