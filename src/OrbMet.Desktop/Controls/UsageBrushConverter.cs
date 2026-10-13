using System.Globalization;
using System.Windows.Data;

namespace OrbMet.Desktop.Controls;

/// <summary>
/// The usage colour for a reading, for text bound in XAML such as the card's
/// headline percentage (design.md 7.2). Bound values: the ring percentage
/// (double?) and whether the provider reports the limit exhausted (bool).
/// No reading is the footnote grey, never a green that would read as 0%.
/// </summary>
public sealed class UsageBrushConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var exhausted = values.Length > 1 && values[1] is true;
        return values.Length > 0 && values[0] is double percent
            ? UsagePalette.For(percent, exhausted)
            : UsagePalette.TextFootnote;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
