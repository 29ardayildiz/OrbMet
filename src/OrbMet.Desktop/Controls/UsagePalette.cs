using System.Windows.Media;

namespace OrbMet.Desktop.Controls;

/// <summary>
/// Design tokens for usage colours, shared by the ring and the bar so both
/// read the same threshold the same way. Colour never carries meaning alone;
/// every use sits beside a percentage or a status text.
/// </summary>
internal static class UsagePalette
{
    public static readonly Brush Track = Freeze(0x41, 0x49, 0x57);
    public static readonly Brush Normal = Freeze(0x55, 0xD6, 0xA0);
    public static readonly Brush Caution = Freeze(0xF3, 0xC5, 0x6B);
    public static readonly Brush High = Freeze(0xF5, 0x82, 0x82);
    public static readonly Brush Blocked = Freeze(0xE8, 0x5B, 0x6B);
    public static readonly Brush TextPrimary = Freeze(0xF4, 0xF6, 0xFA);

    /// <summary>Threshold colour from design.md: below 50, below 75, from 75; blocked wins.</summary>
    public static Brush For(double percent, bool isExhausted)
    {
        if (isExhausted)
        {
            return Blocked;
        }

        if (percent >= 75)
        {
            return High;
        }

        return percent >= 50 ? Caution : Normal;
    }

    private static Brush Freeze(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
