using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using OrbMet.Desktop.Infrastructure;

namespace OrbMet.Desktop.Controls;

/// <summary>One provider's ring and percentage on the strip (design.md 6).</summary>
public partial class SlotView : UserControl
{
    private double _beside;

    public SlotView()
    {
        InitializeComponent();
        SizeChanged += (_, _) => Lay();
        Percent.SizeChanged += (_, _) => Lay();
    }

    /// <summary>The ring, whose circle opens the card on hover and refreshes on click.</summary>
    public UsageRing RingElement => Ring;

    /// <summary>
    /// Where the percentage is: 0 under the ring (vertical strip), 1 beside
    /// it (horizontal strip), and in between while the strip changes shape.
    /// </summary>
    public double Beside
    {
        get => _beside;
        set
        {
            _beside = Math.Clamp(value, 0, 1);
            Lay();
        }
    }

    // Under: ring centred at the top, percentage centred below it.
    // Beside: ring at the left, percentage after it, both centred vertically.
    private void Lay()
    {
        var t = _beside;
        var ring = PanelMetrics.RingDiameter;
        var step = ring + PanelMetrics.RingToText;
        var width = double.IsNaN(Width) ? ActualWidth : Width;
        var height = double.IsNaN(Height) ? ActualHeight : Height;

        Canvas.SetLeft(Ring, Mix((width - ring) / 2, 0, t));
        Canvas.SetTop(Ring, Mix(0, (height - ring) / 2, t));
        Canvas.SetLeft(Percent, Mix((width - Percent.ActualWidth) / 2, step, t));
        Canvas.SetTop(Percent, Mix(step, (height - PanelMetrics.PercentHeight) / 2, t));
    }

    private static double Mix(double under, double beside, double t) => under + (beside - under) * t;

    // A UserControl is left out of UI Automation's control view by default;
    // the slot is what a screen reader should land on, with its spoken name.
    protected override AutomationPeer OnCreateAutomationPeer() => new FrameworkElementAutomationPeer(this);
}
