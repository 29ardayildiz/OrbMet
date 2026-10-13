using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace OrbMet.Desktop.Infrastructure;

/// <summary>
/// Asks Windows for the dark caption so a black window does not wear a white
/// title bar. Older builds ignore the attribute and keep the default.
/// </summary>
internal static class DarkTitleBar
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    public static void Apply(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var enabled = 1;
        _ = DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref enabled, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
