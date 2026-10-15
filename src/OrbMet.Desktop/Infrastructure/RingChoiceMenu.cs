using System.Windows.Controls;
using OrbMet.Core.Storage;
using OrbMet.Core.Usage;

namespace OrbMet.Desktop.Infrastructure;

/// <summary>
/// "Ring shows", shared by the strip and tray menus (design.md 10): the
/// 5-hour limit (default), the weekly one, or whichever is closest to its
/// limit. The one on shows a check; the choice is kept in settings.json.
/// </summary>
internal static class RingChoiceMenu
{
    // Segoe Fluent Icons: Stopwatch, CheckMark.
    private const string Glyph = "";
    private const string Check = "";

    private static readonly (RingChoice Choice, string TextKey)[] Choices =
    {
        (RingChoice.FiveHour, "RingFiveHour"),
        (RingChoice.Weekly, "RingWeekly"),
        (RingChoice.ClosestToLimit, "RingClosest"),
    };

    private static RingChoiceStore? _store;
    private static RingChoice? _current;

    /// <summary>Raised after either menu picks a choice.</summary>
    public static event Action<RingChoice>? Changed;

    private static RingChoiceStore Store => _store ??= new RingChoiceStore(OrbMetPaths.FromEnvironment().SettingsFile);

    public static RingChoice Current => _current ??= Store.Load();

    public static MenuItem Create()
    {
        var menu = new MenuItem { Header = Localizer.Instance["RingShows"], Icon = Glyph };
        foreach (var (choice, _) in Choices)
        {
            var item = new MenuItem { Tag = choice };
            item.Click += (_, _) => Use(choice);
            menu.Items.Add(item);
        }

        void Show()
        {
            menu.Header = Localizer.Instance["RingShows"];
            foreach (MenuItem item in menu.Items)
            {
                var choice = (RingChoice)item.Tag;
                item.Header = Localizer.Instance[Choices.First(entry => entry.Choice == choice).TextKey];
                item.Icon = choice == Current ? Check : null;
            }
        }

        Show();
        Changed += _ => Show();
        Localizer.Instance.LanguageChanged += (_, _) => Show();
        return menu;
    }

    private static void Use(RingChoice choice)
    {
        if (choice == Current)
        {
            return;
        }

        _current = choice;
        Store.Save(choice);
        Changed?.Invoke(choice);
    }
}
