using System.Windows.Controls;
using OrbMet.Core.Storage;
using OrbMet.Core.Usage;

namespace OrbMet.Desktop.Infrastructure;

/// <summary>
/// "Notifications", shared by the strip and tray menus (design.md 10): off
/// (default) or a threshold, 75% to 95%. The one on shows a check; the choice
/// is kept in settings.json.
/// </summary>
internal static class AlertsMenu
{
    // Segoe Fluent Icons: Ringer, CheckMark.
    private const string Glyph = "";
    private const string Check = "";

    private static AlertStore? _store;
    private static int? _current;

    /// <summary>Raised after either menu picks a threshold.</summary>
    public static event Action<int>? Changed;

    public static AlertStore Store => _store ??= new AlertStore(OrbMetPaths.FromEnvironment().SettingsFile);

    /// <summary>The threshold in percent; 0 is off.</summary>
    public static int Current => _current ??= Store.LoadThreshold();

    public static MenuItem Create()
    {
        var menu = new MenuItem { Icon = Glyph };
        foreach (var threshold in UsageAlerts.Thresholds)
        {
            var item = new MenuItem { Tag = threshold };
            item.Click += (_, _) => Use(threshold);
            menu.Items.Add(item);
        }

        void Show()
        {
            var text = Localizer.Instance.Text;
            menu.Header = text["Notifications"];
            foreach (MenuItem item in menu.Items)
            {
                var threshold = (int)item.Tag;
                item.Header = threshold == 0
                    ? text["NotificationsOff"]
                    : text.Format("NotificationsAtFormat", Localizer.Instance.Usage.Percent(threshold));
                item.Icon = threshold == Current ? Check : null;
            }
        }

        Show();
        Changed += _ => Show();
        Localizer.Instance.LanguageChanged += (_, _) => Show();
        return menu;
    }

    private static void Use(int threshold)
    {
        if (threshold == Current)
        {
            return;
        }

        _current = threshold;
        Store.SaveThreshold(threshold);
        Changed?.Invoke(threshold);
    }
}
