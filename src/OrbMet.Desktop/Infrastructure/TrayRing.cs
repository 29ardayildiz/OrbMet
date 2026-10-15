using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OrbMet.Desktop.Controls;
using WpfBrush = System.Windows.Media.Brush;
using WpfPen = System.Windows.Media.Pen;
using WpfPoint = System.Windows.Point;
using WpfSize = System.Windows.Size;

namespace OrbMet.Desktop.Infrastructure;

/// <summary>
/// The tray icon as a live ring (design.md 3.1): the app mark's geometry, a
/// black disc, a white 18% track and an arc clockwise from 12 o'clock, filled
/// to the reading and coloured by the strip's thresholds.
/// </summary>
internal static class TrayRing
{
    /// <summary>An icon of <paramref name="size"/> pixels; the caller disposes it.</summary>
    public static Icon Render(int size, double percent, bool isExhausted, WpfBrush? good = null)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var c = size / 2.0;
            var radius = size * 0.33;
            var thickness = size * 0.12;
            var centre = new WpfPoint(c, c);

            dc.DrawEllipse(UsagePalette.Surface, null, centre, c, c);
            dc.DrawEllipse(null, new WpfPen(UsagePalette.RingTrack, thickness), centre, radius, radius);

            var spent = UsagePalette.IsSpent(percent, isExhausted);
            var fraction = spent ? 1.0 : Math.Clamp(percent, 0, 100) / 100.0;
            var brush = UsagePalette.For(percent, isExhausted, good);
            if (fraction >= 1.0)
            {
                dc.DrawEllipse(null, new WpfPen(brush, thickness), centre, radius, radius);
            }
            else if (fraction > 0)
            {
                dc.DrawGeometry(null, RoundPen(brush, thickness), Arc(centre, radius, fraction));
            }
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        return ToIcon(bitmap, size);
    }

    private static Geometry Arc(WpfPoint centre, double radius, double fraction)
    {
        WpfPoint At(double degrees) => new(
            centre.X + radius * Math.Cos(degrees * Math.PI / 180),
            centre.Y + radius * Math.Sin(degrees * Math.PI / 180));

        var figure = new PathFigure { StartPoint = At(-90), IsFilled = false };
        figure.Segments.Add(new ArcSegment(
            At(-90 + 360 * fraction), new WpfSize(radius, radius), 0, fraction > 0.5, SweepDirection.Clockwise, true));
        var arc = new PathGeometry();
        arc.Figures.Add(figure);
        return arc;
    }

    private static WpfPen RoundPen(WpfBrush brush, double thickness) =>
        new(brush, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };

    private static Icon ToIcon(BitmapSource source, int size)
    {
        using var bitmap = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        var data = bitmap.LockBits(new Rectangle(0, 0, size, size), ImageLockMode.WriteOnly, bitmap.PixelFormat);
        try
        {
            source.CopyPixels(Int32Rect.Empty, data.Scan0, data.Stride * size, data.Stride);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        var handle = bitmap.GetHicon();
        try
        {
            return (Icon)Icon.FromHandle(handle).Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);
}
