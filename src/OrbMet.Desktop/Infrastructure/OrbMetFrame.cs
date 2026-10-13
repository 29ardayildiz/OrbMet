using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Shell;

namespace OrbMet.Desktop.Infrastructure;

/// <summary>
/// OrbMet's own window frame (design.md 10): the <c>OrbMetWindow</c> style in
/// Themes/Windows.xaml draws a black caption with the app's mark, the
/// <see cref="CaptionProperty"/> text and a close button. This wires the close
/// command and asks Windows for round corners, the dark frame and a hairline
/// border to match the menus. Older builds ignore the attributes.
/// </summary>
public static class OrbMetFrame
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWCP_ROUND = 2;
    private const double CaptionHeight = 40;

    // White at 12% over black, as a COLORREF (0x00BBGGRR).
    private const int HairlineBorder = 0x001F1F1F;

    public static readonly DependencyProperty CaptionProperty = DependencyProperty.RegisterAttached(
        "Caption",
        typeof(string),
        typeof(OrbMetFrame),
        new PropertyMetadata(string.Empty));

    public static string GetCaption(DependencyObject element) => (string)element.GetValue(CaptionProperty);

    public static void SetCaption(DependencyObject element, string value) => element.SetValue(CaptionProperty, value);

    public static void Attach(Window window)
    {
        // Neither can be a style setter: the start location is a plain CLR
        // property, and the chrome is a freezable each window needs its own of.
        window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowChrome.SetWindowChrome(window, new WindowChrome
        {
            CaptionHeight = CaptionHeight,
            ResizeBorderThickness = new Thickness(0),
            GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false,
        });
        window.CommandBindings.Add(new CommandBinding(
            SystemCommands.CloseWindowCommand,
            (_, _) => window.Close()));
        window.SourceInitialized += (_, _) => ApplyFrame(window);
    }

    private static void ApplyFrame(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var dark = 1;
        _ = DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
        var round = DWMWCP_ROUND;
        _ = DwmSetWindowAttribute(handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
        var border = HairlineBorder;
        _ = DwmSetWindowAttribute(handle, DWMWA_BORDER_COLOR, ref border, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
