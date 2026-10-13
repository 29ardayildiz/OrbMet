using System.Windows;
using System.Windows.Media;
using OrbMet.Core.Placement;
using Screen = System.Windows.Forms.Screen;

namespace OrbMet.Desktop.Infrastructure;

/// <summary>
/// Work areas of the attached displays, in the DIP space of a given visual.
/// The work area already leaves out the taskbar. Coordinates left of or above
/// the primary display are negative, and that is valid.
/// </summary>
internal static class Displays
{
    /// <summary>The display under a point, so a strip can be carried to another monitor.</summary>
    public static (Box WorkArea, string Id) At(Point dipPoint, Visual visual)
    {
        var device = ToDevice(visual).Transform(dipPoint);
        var screen = Screen.FromPoint(new System.Drawing.Point((int)Math.Round(device.X), (int)Math.Round(device.Y)));
        return (WorkAreaOf(screen, visual), screen.DeviceName);
    }

    /// <summary>The remembered display, or the primary when it is no longer attached.</summary>
    public static (Box WorkArea, string Id) For(string? id, Visual visual)
    {
        var screen = Screen.AllScreens.FirstOrDefault(candidate => candidate.DeviceName == id)
            ?? Screen.PrimaryScreen
            ?? Screen.AllScreens[0];
        return (WorkAreaOf(screen, visual), screen.DeviceName);
    }

    private static Box WorkAreaOf(Screen screen, Visual visual)
    {
        var area = screen.WorkingArea;
        var fromDevice = FromDevice(visual);
        var topLeft = fromDevice.Transform(new Point(area.Left, area.Top));
        var bottomRight = fromDevice.Transform(new Point(area.Right, area.Bottom));
        return new Box(topLeft.X, topLeft.Y, bottomRight.X - topLeft.X, bottomRight.Y - topLeft.Y);
    }

    private static Matrix ToDevice(Visual visual) =>
        PresentationSource.FromVisual(visual)?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;

    private static Matrix FromDevice(Visual visual) =>
        PresentationSource.FromVisual(visual)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
}
