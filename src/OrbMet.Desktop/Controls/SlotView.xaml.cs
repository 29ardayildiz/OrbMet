using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using OrbMet.Desktop.Infrastructure;

namespace OrbMet.Desktop.Controls;

/// <summary>One provider's ring and percentage on the strip (design.md 6).</summary>
public partial class SlotView : UserControl
{
    public SlotView()
    {
        InitializeComponent();
        PercentBeside = false;
    }

    /// <summary>The ring, whose circle opens the card on hover and refreshes on click.</summary>
    public UsageRing RingElement => Ring;

    /// <summary>Beside the ring on a horizontal strip, under it on a vertical one.</summary>
    public bool PercentBeside
    {
        get => Percent.HorizontalAlignment == HorizontalAlignment.Left;
        set
        {
            Ring.HorizontalAlignment = value ? HorizontalAlignment.Left : HorizontalAlignment.Center;
            Ring.VerticalAlignment = value ? VerticalAlignment.Center : VerticalAlignment.Top;
            Percent.HorizontalAlignment = value ? HorizontalAlignment.Left : HorizontalAlignment.Center;
            Percent.VerticalAlignment = value ? VerticalAlignment.Center : VerticalAlignment.Top;
            Percent.Margin = value
                ? new Thickness(PanelMetrics.RingDiameter + PanelMetrics.RingToText, 0, 0, 0)
                : new Thickness(0, PanelMetrics.RingDiameter + PanelMetrics.RingToText, 0, 0);
        }
    }

    // A UserControl is left out of UI Automation's control view by default;
    // the slot is what a screen reader should land on, with its spoken name.
    protected override AutomationPeer OnCreateAutomationPeer() => new FrameworkElementAutomationPeer(this);
}
