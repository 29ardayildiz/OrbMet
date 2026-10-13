using System.Windows;
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
    private readonly DispatcherTimer _clock;
    private Rect _strip;
    private double _ringCentreY;
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
    /// bounds and <paramref name="ringCentreY"/> the ring's centre, both in DIPs.
    /// </summary>
    public void ShowFor(ProviderSlotViewModel slot, Rect strip, double ringCentreY)
    {
        slot.UpdateDetails(DateTimeOffset.UtcNow, TimeZoneInfo.Local);
        DataContext = slot;
        _strip = strip;
        _ringCentreY = ringCentreY;

        var area = SystemParameters.WorkArea;
        MaxHeight = Math.Max(0, area.Height - 2 * PanelMetrics.WorkAreaMargin);

        if (!IsVisible)
        {
            Show();
        }

        Place();
    }

    private void Place()
    {
        var area = SystemParameters.WorkArea;

        // Left of the strip with the tail pointing right; the other side only
        // when the left has no room.
        var left = _strip.Left - PanelMetrics.CardGap - ActualWidth;
        var tailOnRight = left >= area.Left;
        if (!tailOnRight)
        {
            left = _strip.Right + PanelMetrics.CardGap;
        }

        var top = _ringCentreY - ActualHeight / 2;
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

        Bubble.Data = DetailBubble.Build(ActualWidth, ActualHeight, _ringCentreY - top, _tailOnRight);
    }
}
