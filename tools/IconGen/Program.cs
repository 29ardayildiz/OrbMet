using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

// The app icon (design.md 3): the strip's ring on a black disc, a white 18%
// track and a Good-green arc clockwise from 12 o'clock. It is a fixed mark,
// not a live reading.
if (args.Length != 1)
{
    Console.Error.WriteLine("usage: IconGen <output.ico>");
    return 1;
}

int[] sizes = [16, 20, 24, 32, 40, 48, 64, 256];
var frames = sizes.Select(size => (size, pixels: Render(size))).ToList();

using var output = File.Create(args[0]);
using var writer = new BinaryWriter(output);

// ICONDIR, then one ICONDIRENTRY per size, then the images. Small sizes are
// classic 32-bit DIBs, which every Windows icon reader accepts; 256 is PNG.
writer.Write((ushort)0);
writer.Write((ushort)1);
writer.Write((ushort)frames.Count);

var images = frames.Select(frame => frame.size == 256 ? Png(frame.size, frame.pixels) : Dib(frame.size, frame.pixels)).ToList();
var offset = 6 + 16 * frames.Count;
for (var i = 0; i < frames.Count; i++)
{
    var size = frames[i].size;
    writer.Write((byte)(size == 256 ? 0 : size));
    writer.Write((byte)(size == 256 ? 0 : size));
    writer.Write((byte)0);
    writer.Write((byte)0);
    writer.Write((ushort)1);
    writer.Write((ushort)32);
    writer.Write(images[i].Length);
    writer.Write(offset);
    offset += images[i].Length;
}

foreach (var image in images)
{
    writer.Write(image);
}

Console.WriteLine($"wrote {args[0]} ({string.Join(", ", sizes)})");
return 0;

static byte[] Render(int size)
{
    var visual = new DrawingVisual();
    using (var dc = visual.RenderOpen())
    {
        var c = size / 2.0;
        var radius = size * 0.33;
        var thickness = size * 0.12;
        var centre = new Point(c, c);

        dc.DrawEllipse(Brushes.Black, null, centre, c, c);
        dc.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(0x2E, 255, 255, 255)), thickness), centre, radius, radius);

        const double fraction = 0.62;
        Point At(double degrees) => new(c + radius * Math.Cos(degrees * Math.PI / 180), c + radius * Math.Sin(degrees * Math.PI / 180));
        var figure = new PathFigure { StartPoint = At(-90), IsFilled = false };
        figure.Segments.Add(new ArcSegment(At(-90 + 360 * fraction), new Size(radius, radius), 0, true, SweepDirection.Clockwise, true));
        var arc = new PathGeometry();
        arc.Figures.Add(figure);
        var green = new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x8C));
        dc.DrawGeometry(null, new Pen(green, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, arc);
    }

    var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
    bitmap.Render(visual);
    var pixels = new byte[size * size * 4];
    bitmap.CopyPixels(pixels, size * 4, 0);
    Unpremultiply(pixels);
    return pixels;
}

// Icons store straight alpha; WPF renders premultiplied.
static void Unpremultiply(byte[] pixels)
{
    for (var i = 0; i < pixels.Length; i += 4)
    {
        var alpha = pixels[i + 3];
        if (alpha is 0 or 255)
        {
            continue;
        }

        for (var channel = 0; channel < 3; channel++)
        {
            pixels[i + channel] = (byte)Math.Min(255, pixels[i + channel] * 255 / alpha);
        }
    }
}

static byte[] Dib(int size, byte[] pixels)
{
    using var stream = new MemoryStream();
    using var writer = new BinaryWriter(stream);

    // BITMAPINFOHEADER; the height counts the colour rows and the mask rows.
    writer.Write(40);
    writer.Write(size);
    writer.Write(size * 2);
    writer.Write((ushort)1);
    writer.Write((ushort)32);
    writer.Write(0);
    writer.Write(0);
    writer.Write(0);
    writer.Write(0);
    writer.Write(0);
    writer.Write(0);

    // Rows bottom-up, BGRA.
    for (var y = size - 1; y >= 0; y--)
    {
        writer.Write(pixels, y * size * 4, size * 4);
    }

    // An all-clear AND mask; the alpha channel carries transparency.
    var maskStride = (size + 31) / 32 * 4;
    writer.Write(new byte[maskStride * size]);
    return stream.ToArray();
}

static byte[] Png(int size, byte[] pixels)
{
    var source = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);
    var encoder = new PngBitmapEncoder();
    encoder.Frames.Add(BitmapFrame.Create(source));
    using var stream = new MemoryStream();
    encoder.Save(stream);
    return stream.ToArray();
}
