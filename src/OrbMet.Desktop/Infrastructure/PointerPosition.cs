using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;

namespace OrbMet.Desktop.Infrastructure;

/// <summary>
/// The real pointer position, sampled rather than taken from enter/leave
/// events. Windows that appear or move under a still pointer fire spurious
/// leaves, which would close and reopen the card in a loop.
/// </summary>
internal static class PointerPosition
{
    /// <summary>The pointer in the DIP space of <paramref name="visual"/>'s screen, or null if unknown.</summary>
    public static Point? InDips(Visual visual)
    {
        if (!GetCursorPos(out var point) || PresentationSource.FromVisual(visual) is not { CompositionTarget: { } target })
        {
            return null;
        }

        return target.TransformFromDevice.Transform(new Point(point.X, point.Y));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);
}
