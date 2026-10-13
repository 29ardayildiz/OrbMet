using System.Drawing;
using System.Windows.Forms;

namespace OrbMet.Desktop.Infrastructure;

/// <summary>
/// The system tray presence. The panel hiding is not the app exiting, so the
/// menu always offers a way back to the panel and an explicit exit.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly Icon _icon;
    private bool _disposed;

    public TrayIcon(
        Action togglePanel,
        Action openDetails,
        Action openConnections,
        Action refresh,
        Action exit)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Paneli göster / gizle", null, (_, _) => togglePanel());
        menu.Items.Add("Ayrıntıları aç", null, (_, _) => openDetails());
        menu.Items.Add("Yenile", null, (_, _) => refresh());
        menu.Items.Add("Bağlantılar", null, (_, _) => openConnections());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Çıkış", null, (_, _) => exit());

        _icon = LoadIcon();
        _notifyIcon = new NotifyIcon
        {
            Icon = _icon,
            Text = "OrbMet",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _notifyIcon.DoubleClick += (_, _) => togglePanel();
    }

    /// <summary>
    /// The OrbMet mark at the tray's small-icon size, so a scaled display
    /// gets its own frame instead of a stretched 16 px one.
    /// </summary>
    private static Icon LoadIcon()
    {
        var resource = System.Windows.Application.GetResourceStream(
            new Uri("pack://application:,,,/OrbMet.Desktop;component/Assets/OrbMet.ico"));
        if (resource is null)
        {
            return (Icon)SystemIcons.Application.Clone();
        }

        using var stream = resource.Stream;
        return new Icon(stream, SystemInformation.SmallIconSize);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _icon.Dispose();
    }
}
