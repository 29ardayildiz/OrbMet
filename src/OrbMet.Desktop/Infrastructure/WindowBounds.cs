using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace OrbMet.Desktop.Infrastructure;

/// <summary>
/// Moves and sizes a window in one step. Setting Left, Top, Width and Height
/// one by one lets WPF apply them over several frames, so a window whose
/// drawing changes with its box shows a few frames of the drawing in the
/// wrong place.
/// </summary>
internal static class WindowBounds
{
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;

    /// <summary>Puts the window at <paramref name="bounds"/>, in DIPs.</summary>
    public static void Set(Window window, Rect bounds)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle != IntPtr.Zero && PresentationSource.FromVisual(window)?.CompositionTarget is { } target)
        {
            var device = target.TransformToDevice;
            var topLeft = device.Transform(bounds.TopLeft);
            var bottomRight = device.Transform(bounds.BottomRight);
            var x = (int)Math.Round(topLeft.X);
            var y = (int)Math.Round(topLeft.Y);
            SetWindowPos(
                handle,
                IntPtr.Zero,
                x,
                y,
                (int)Math.Round(bottomRight.X) - x,
                (int)Math.Round(bottomRight.Y) - y,
                SWP_NOZORDER | SWP_NOACTIVATE);
        }

        // WPF's own values follow, so its next layout leaves the window as it is.
        window.Width = bounds.Width;
        window.Height = bounds.Height;
        window.Left = bounds.X;
        window.Top = bounds.Y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
}
