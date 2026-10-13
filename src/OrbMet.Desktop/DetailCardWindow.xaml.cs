using System.Windows;
using System.Windows.Threading;
using OrbMet.Desktop.Infrastructure;
using OrbMet.Desktop.ViewModels;

namespace OrbMet.Desktop;

/// <summary>
/// The provider detail card beside the strip. Like the strip it never takes
/// keyboard focus. It opens on the side with room, stays inside the work area
/// and scrolls when its content is taller than the screen allows.
/// </summary>
public partial class DetailCardWindow : Window
{
    private readonly Action _refresh;
    private readonly Action _openConnections;
    private readonly Action _close;
    private readonly DispatcherTimer _clock;
    private Rect _strip;
    private double _anchorTop;

    public DetailCardWindow(Action refresh, Action openConnections, Action close)
    {
        InitializeComponent();

        _refresh = refresh;
        _openConnections = openConnections;
        _close = close;

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
    /// bounds and <paramref name="anchorTop"/> the slot's top, both in DIPs.
    /// </summary>
    public void ShowFor(ProviderSlotViewModel slot, Rect strip, double anchorTop)
    {
        slot.UpdateDetails(DateTimeOffset.UtcNow, TimeZoneInfo.Local);
        DataContext = slot;
        _strip = strip;
        _anchorTop = anchorTop;

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

        // Left of the strip by default; the right side only when the left has no room.
        var left = _strip.Left - PanelMetrics.CardGap - ActualWidth;
        if (left < area.Left + PanelMetrics.WorkAreaMargin)
        {
            left = _strip.Right + PanelMetrics.CardGap;
        }

        var maxTop = area.Bottom - PanelMetrics.WorkAreaMargin - ActualHeight;
        var top = Math.Min(_anchorTop, maxTop);
        top = Math.Max(top, area.Top + PanelMetrics.WorkAreaMargin);

        Left = left;
        Top = top;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => _close();

    private void OnRefreshClick(object sender, RoutedEventArgs e) => _refresh();

    private void OnConnectionsClick(object sender, RoutedEventArgs e) => _openConnections();
}
