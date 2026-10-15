using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using OrbMet.Core.Models;
using OrbMet.Core.Storage;
using OrbMet.Core.Usage;
using OrbMet.Desktop.Controls;

namespace OrbMet.Desktop.Infrastructure;

/// <summary>
/// "Appearance", shared by the strip and tray menus (design.md 10, Görünüm):
/// which limit the rings show, the window clock, used or left, the forecast
/// on the card, and each provider's ring colour. Kept in settings.json; every
/// change shows at once.
/// </summary>
internal static class AppearanceMenu
{
    // Segoe Fluent Icons: Color, CheckMark.
    private const string Glyph = "";
    private const string Check = "";

    private static readonly (RingColour Colour, string TextKey)[] Colours =
    {
        (RingColour.Green, "ColourGreen"),
        (RingColour.Teal, "ColourTeal"),
        (RingColour.Blue, "ColourBlue"),
        (RingColour.Purple, "ColourPurple"),
        (RingColour.Pink, "ColourPink"),
        (RingColour.White, "ColourWhite"),
    };

    private static DisplayStore? _store;
    private static DisplaySettings? _current;

    /// <summary>Raised after either menu changes a choice.</summary>
    public static event Action? Changed;

    private static DisplayStore Store => _store ??= new DisplayStore(OrbMetPaths.FromEnvironment().SettingsFile);

    public static DisplaySettings Current => _current ??= Store.Load();

    /// <summary>The colour that stands for "fine" in this provider's ring, bars and figures.</summary>
    public static Brush GoodFor(ProviderId providerId) => UsagePalette.Ring(Current.ColourFor(providerId));

    private static readonly (ProviderId Id, string Name)[] Providers =
    {
        (ProviderId.ClaudeCode, "Claude Code"),
        (ProviderId.Codex, "Codex"),
    };

    public static MenuItem Create()
    {
        var menu = new MenuItem { Icon = Glyph };
        menu.Items.Add(RingChoiceMenu.Create());
        var clock = Toggle("WindowClock", settings => settings.WindowClock, settings => settings with { WindowClock = !settings.WindowClock });
        var remaining = Toggle("ShowRemaining", settings => settings.ShowRemaining, settings => settings with { ShowRemaining = !settings.ShowRemaining });
        var forecast = Toggle("ShowForecast", settings => settings.Forecast, settings => settings with { Forecast = !settings.Forecast });
        menu.Items.Add(clock);
        menu.Items.Add(remaining);
        menu.Items.Add(forecast);
        menu.Items.Add(new Separator());
        foreach (var (id, name) in Providers)
        {
            menu.Items.Add(ColourMenu(id, name));
        }

        menu.Items.Add(new Separator());
        foreach (var (id, name) in Providers)
        {
            menu.Items.Add(ShowProvider(id, name));
        }

        menu.Items.Add(new Separator());
        menu.Items.Add(CollapseMenu());
        menu.Items.Add(Toggle("FollowPointer", settings => settings.FollowPointer, settings => settings with { FollowPointer = !settings.FollowPointer }));
        menu.Items.Add(Toggle("ShowActivity", settings => settings.ShowActivity, settings => settings with { ShowActivity = !settings.ShowActivity }));

        void Show() => menu.Header = Localizer.Instance["Appearance"];
        Show();
        Localizer.Instance.LanguageChanged += (_, _) => Show();
        return menu;
    }

    private static MenuItem Toggle(string textKey, Func<DisplaySettings, bool> isOn, Func<DisplaySettings, DisplaySettings> flip)
    {
        var item = new MenuItem();
        item.Click += (_, _) => Use(flip(Current));

        void Show()
        {
            item.Header = Localizer.Instance[textKey];
            item.IsChecked = isOn(Current);
        }

        Show();
        Changed += Show;
        Localizer.Instance.LanguageChanged += (_, _) => Show();
        return item;
    }

    /// <summary>
    /// "Claude Code on the strip" and "Codex on the strip" (design.md 4.4):
    /// unticked hides that provider and stops reading it. The last one shown
    /// cannot be hidden, so the strip is never empty.
    /// </summary>
    private static MenuItem ShowProvider(ProviderId providerId, string providerName)
    {
        var item = new MenuItem();
        item.Click += (_, _) =>
        {
            var hide = !Current.Hidden.Contains(providerId);
            var next = providerId == ProviderId.Codex ? Current with { HideCodex = hide } : Current with { HideClaude = hide };
            if (next.Hidden.Count < Providers.Length)
            {
                Use(next);
            }
        };

        void Show()
        {
            item.Header = Localizer.Instance.Text.Format("OnStripFormat", providerName);
            item.IsChecked = !Current.Hidden.Contains(providerId);
            item.IsEnabled = Current.Hidden.Contains(providerId) || Current.Hidden.Count < Providers.Length - 1;
        }

        Show();
        Changed += Show;
        Localizer.Instance.LanguageChanged += (_, _) => Show();
        return item;
    }

    /// <summary>"Fold when idle": off, or after 10 seconds, 30 seconds or 2 minutes away (design.md 4.3).</summary>
    private static MenuItem CollapseMenu()
    {
        var menu = new MenuItem();
        var off = new MenuItem();
        off.Click += (_, _) => Use(Current with { CollapseWhenIdle = false });
        menu.Items.Add(off);
        foreach (var seconds in DisplaySettings.CollapseDelays)
        {
            var item = new MenuItem { Tag = seconds };
            item.Click += (_, _) => Use(Current with { CollapseWhenIdle = true, CollapseAfterSeconds = seconds });
            menu.Items.Add(item);
        }

        void Show()
        {
            var text = Localizer.Instance.Text;
            menu.Header = text["CollapseWhenIdle"];
            off.Header = text["NotificationsOff"];
            off.IsChecked = !Current.CollapseWhenIdle;
            foreach (var item in menu.Items.OfType<MenuItem>().Where(item => item.Tag is int))
            {
                var seconds = (int)item.Tag;
                item.Header = seconds < 60
                    ? text.Format("AfterSecondsFormat", seconds)
                    : text.Format("AfterMinutesFormat", seconds / 60);
                item.IsChecked = Current.CollapseWhenIdle && Current.CollapseDelay == TimeSpan.FromSeconds(seconds);
            }
        }

        Show();
        Changed += Show;
        Localizer.Instance.LanguageChanged += (_, _) => Show();
        return menu;
    }

    private static MenuItem ColourMenu(ProviderId providerId, string providerName)
    {
        var menu = new MenuItem();
        foreach (var (colour, _) in Colours)
        {
            var item = new MenuItem { Tag = colour };
            item.Click += (_, _) => Use(Current.WithColour(providerId, colour));
            menu.Items.Add(item);
        }

        void Show()
        {
            var text = Localizer.Instance.Text;
            menu.Header = text.Format("RingColourFormat", providerName);
            menu.Icon = Swatch(GoodFor(providerId));
            foreach (MenuItem item in menu.Items)
            {
                var colour = (RingColour)item.Tag;
                item.Header = text[Colours.First(entry => entry.Colour == colour).TextKey];
                item.Icon = Swatch(UsagePalette.Ring(colour));
                item.IsChecked = colour == Current.ColourFor(providerId);
            }
        }

        Show();
        Changed += Show;
        Localizer.Instance.LanguageChanged += (_, _) => Show();
        return menu;
    }

    // A dot of the colour where the menu draws an icon.
    private static Ellipse Swatch(Brush brush) => new() { Width = 10, Height = 10, Fill = brush };

    private static void Use(DisplaySettings settings)
    {
        _current = settings;
        Store.Save(settings);
        Changed?.Invoke();
    }
}
