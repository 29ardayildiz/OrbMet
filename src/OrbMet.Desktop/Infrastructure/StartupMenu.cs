using System.Security;
using System.Windows.Controls;
using OrbMet.Core.Startup;

namespace OrbMet.Desktop.Infrastructure;

/// <summary>
/// "Start with Windows", shared by the strip and tray menus (design.md 10).
/// Off by default; a click turns it on or off and the check on the right
/// shows what Windows will actually do, Task Manager's choice included.
/// </summary>
internal static class StartupMenu
{
    // Segoe Fluent Icons: Play.
    private const string Glyph = "";

    private static StartupEntry? _entry;

    // Either menu's item changes the entry; both show the result.
    private static event Action? Changed;

    /// <summary>The entry for this copy, or null when OrbMet's own path is unknown.</summary>
    public static StartupEntry? Entry =>
        _entry ??= Environment.ProcessPath is { } exe ? new StartupEntry(new WindowsStartupList(), exe) : null;

    public static MenuItem Create()
    {
        var item = new MenuItem { Header = Localizer.Instance["StartWithWindows"], Icon = Glyph };

        void Show()
        {
            item.IsEnabled = Entry is not null;
            item.IsChecked = Read(entry => entry.IsOn);
        }

        item.Click += (_, _) =>
        {
            var on = Read(entry => entry.IsOn);
            Write(entry =>
            {
                if (on)
                {
                    entry.TurnOff();
                }
                else
                {
                    entry.TurnOn();
                }
            });
            Changed?.Invoke();
        };

        // Task Manager can change it while OrbMet runs; the menu reads it again each time it opens.
        item.Loaded += (_, _) => Show();
        Changed += Show;
        Localizer.Instance.LanguageChanged += (_, _) => item.Header = Localizer.Instance["StartWithWindows"];
        Show();
        return item;
    }

    /// <summary>Follows a moved folder at start; quiet when the registry cannot be reached.</summary>
    public static void FollowMove() => Write(entry => entry.FollowMove());

    private static bool Read(Func<StartupEntry, bool> read)
    {
        try
        {
            return Entry is { } entry && read(entry);
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or System.IO.IOException)
        {
            return false;
        }
    }

    private static void Write(Action<StartupEntry> write)
    {
        try
        {
            if (Entry is { } entry)
            {
                write(entry);
            }
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or System.IO.IOException)
        {
            // The check shows what is actually set, so a failed write is visible there.
        }
    }
}
