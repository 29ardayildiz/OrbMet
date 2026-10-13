using System.Windows.Controls;
using OrbMet.Core.Localization;

namespace OrbMet.Desktop.Infrastructure;

/// <summary>
/// The "Dil / Language" submenu shared by the strip and tray menus
/// (design.md 10). Its name is in both languages so someone who picked the
/// wrong one can find the way back; each language is in its own words, and
/// the one on shows a check.
/// </summary>
internal static class LanguageMenu
{
    private const string Globe = "\uE774";
    private const string Check = "\uE73E";

    public static MenuItem Create()
    {
        var menu = new MenuItem { Header = "Dil / Language", Icon = Globe };
        foreach (var language in UiLanguages.All)
        {
            var item = new MenuItem { Header = language.NativeName(), Tag = language };
            item.Click += (_, _) => Localizer.Instance.Use(language);
            menu.Items.Add(item);
        }

        void MarkCurrent()
        {
            foreach (MenuItem item in menu.Items)
            {
                item.Icon = (UiLanguage)item.Tag == Localizer.Instance.Language ? Check : null;
            }
        }

        MarkCurrent();
        Localizer.Instance.LanguageChanged += (_, _) => MarkCurrent();
        return menu;
    }
}
