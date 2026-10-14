using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Forms;
using WpfContextMenu = System.Windows.Controls.ContextMenu;
using WpfMenuItem = System.Windows.Controls.MenuItem;
using WpfSeparator = System.Windows.Controls.Separator;

namespace OrbMet.Desktop.Infrastructure;

/// <summary>
/// The system tray presence. The panel hiding is not the app exiting, so the
/// menu always offers a way back to the panel and an explicit exit. The menu
/// is WPF, styled by Themes/Menus.xaml like the strip's, rather than
/// WinForms' ContextMenuStrip (design.md 10).
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly Icon _icon;
    private readonly WpfContextMenu _menu;
    private bool _disposed;

    public TrayIcon(
        Action togglePanel,
        Action openDetails,
        Action openConnections,
        Action refresh,
        Action exit)
    {
        // Segoe Fluent Icons: View, Info, Refresh, Link, PowerButton. Each
        // header is a text key, said again when the language changes.
        _menu = new WpfContextMenu
        {
            Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint,
        };
        _menu.Items.Add(Item("TogglePanel", "\uE7B3", togglePanel));
        _menu.Items.Add(Item("OpenDetails", "\uE946", openDetails));
        _menu.Items.Add(Item("Refresh", "\uE72C", refresh));
        _menu.Items.Add(Item("Connections", "\uE71B", openConnections));
        _menu.Items.Add(LanguageMenu.Create());
        _menu.Items.Add(StartupMenu.Create());
        _menu.Items.Add(new WpfSeparator());
        _menu.Items.Add(Item("Exit", "\uE7E8", exit));
        _menu.Opened += (_, _) => TakeForeground();

        _icon = LoadIcon();
        _notifyIcon = new NotifyIcon
        {
            Icon = _icon,
            Text = "OrbMet",
            Visible = true,
        };
        _notifyIcon.DoubleClick += (_, _) => togglePanel();

        // A right click, and Shift+F10 or the menu key on a selected icon: the
        // shell reports both as a right button release for this icon version.
        _notifyIcon.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Right)
            {
                _menu.IsOpen = true;
            }
        };
    }

    private static WpfMenuItem Item(string textKey, string glyph, Action action)
    {
        var item = new WpfMenuItem { Header = Localizer.Instance[textKey], Icon = glyph };
        item.Click += (_, _) => action();
        Localizer.Instance.LanguageChanged += (_, _) => item.Header = Localizer.Instance[textKey];
        return item;
    }

    /// <summary>
    /// A tray menu must own the foreground: otherwise a click elsewhere does
    /// not close it and the keyboard does not reach it. The shell lets the
    /// icon's process take the foreground right after the click.
    /// </summary>
    private void TakeForeground()
    {
        if (System.Windows.PresentationSource.FromVisual(_menu) is HwndSource source)
        {
            SetForegroundWindow(source.Handle);
        }

        _menu.Focus();
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    /// <summary>
    /// The OrbMet mark at the tray's small-icon size, so a scaled display
    /// gets its own frame instead of a stretched 16 px one.
    /// </summary>
    private static Icon LoadIcon()
    {
        var resource = System.Windows.Application.GetResourceStream(
            new Uri("pack://application:,,,/OrbMet;component/Assets/OrbMet.ico"));
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
        _menu.IsOpen = false;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _icon.Dispose();
    }
}
