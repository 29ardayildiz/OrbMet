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
    private bool _disposed;

    public TrayIcon(
        Action togglePanel,
        Action openConnections,
        Action refresh,
        Action exit)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Paneli göster / gizle", null, (_, _) => togglePanel());
        menu.Items.Add("Bağlantılar", null, (_, _) => openConnections());
        menu.Items.Add("Yenile", null, (_, _) => refresh());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Çıkış", null, (_, _) => exit());

        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "OrbMet",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _notifyIcon.DoubleClick += (_, _) => togglePanel();
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
    }
}
