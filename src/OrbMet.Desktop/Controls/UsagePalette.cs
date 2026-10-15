using System.Windows.Media;

namespace OrbMet.Desktop.Controls;

/// <summary>
/// Colour tokens from design.md section 5. The surface is black, every
/// neutral is white at an opacity, and hue means one thing only: how close a
/// limit is to running out. Colour never carries meaning alone; every use sits
/// beside a percentage or a text.
/// </summary>
internal static class UsagePalette
{
    public static readonly Brush Surface = Freeze(Colors.Black);
    public static readonly Brush Text = White(1.0);
    public static readonly Brush TextValue = White(0.90);
    public static readonly Brush TextSecondary = White(0.55);
    public static readonly Brush TextReset = White(0.45);
    public static readonly Brush TextFootnote = White(0.40);
    public static readonly Brush RingTrack = White(0.18);
    public static readonly Brush BarTrack = White(0.17);
    public static readonly Brush IconDim = White(0.35);

    public static readonly Brush Good = Freeze(Color.FromRgb(0x00, 0xE6, 0x8C));
    public static readonly Brush Caution = Freeze(Color.FromRgb(0xFF, 0xC2, 0x26));
    public static readonly Brush Warning = Freeze(Color.FromRgb(0xFF, 0x4F, 0x42));
    public static readonly Brush Spent = Freeze(Color.FromRgb(0xD9, 0x17, 0x1F));

    /// <summary>
    /// Spent is the provider saying so, or a reported value at or past 100%.
    /// It is a colour, not a claim the account is blocked.
    /// </summary>
    public static bool IsSpent(double? percent, bool isExhausted) =>
        isExhausted || percent >= 100;

    /// <summary>
    /// Threshold colour: below 50, below 75, from 75; spent wins. A colour the
    /// user picked for the provider takes the place of green only, so the
    /// warnings keep their meaning (design.md 5.1).
    /// </summary>
    public static Brush For(double percent, bool isExhausted, Brush? good = null)
    {
        if (IsSpent(percent, isExhausted))
        {
            return Spent;
        }

        if (percent >= 75)
        {
            return Warning;
        }

        return percent >= 50 ? Caution : good ?? Good;
    }

    /// <summary>The ring colours on offer; none of them is a warning hue.</summary>
    public static Brush Ring(Core.Usage.RingColour colour) => colour switch
    {
        Core.Usage.RingColour.Teal => Teal,
        Core.Usage.RingColour.Blue => Blue,
        Core.Usage.RingColour.Purple => Purple,
        Core.Usage.RingColour.Pink => Pink,
        Core.Usage.RingColour.White => Text,
        _ => Good,
    };

    private static readonly Brush Teal = Freeze(Color.FromRgb(0x2E, 0xD3, 0xE6));
    private static readonly Brush Blue = Freeze(Color.FromRgb(0x4D, 0x9F, 0xFF));
    private static readonly Brush Purple = Freeze(Color.FromRgb(0xA7, 0x82, 0xFF));
    private static readonly Brush Pink = Freeze(Color.FromRgb(0xFF, 0x78, 0xC8));

    private static Brush White(double opacity) =>
        Freeze(Color.FromArgb((byte)Math.Round(opacity * 255), 0xFF, 0xFF, 0xFF));

    private static Brush Freeze(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
