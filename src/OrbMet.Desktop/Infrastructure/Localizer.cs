using System.ComponentModel;
using System.Globalization;
using OrbMet.Core.Localization;
using OrbMet.Core.Storage;
using OrbMet.Core.Usage;

namespace OrbMet.Desktop.Infrastructure;

/// <summary>
/// The language OrbMet is showing, switched live from the menus. XAML binds
/// to the indexer (<c>{Binding [Refresh], Source={x:Static infra:Localizer.Instance}}</c>),
/// which refreshes on a change; view models listen to
/// <see cref="LanguageChanged"/> and rebuild their text.
/// </summary>
public sealed class Localizer : INotifyPropertyChanged
{
    private readonly LanguageStore? _store;
    private UiLanguage _language;

    public static Localizer Instance { get; } = Create();

    private Localizer(LanguageStore? store, UiLanguage language)
    {
        _store = store;
        _language = language;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? LanguageChanged;

    public UiLanguage Language => _language;

    public Texts Text => Texts.For(_language);

    public UsageText Usage => UsageText.For(_language);

    public string this[string key] => Text[key];

    /// <summary>Shows and remembers <paramref name="language"/>.</summary>
    public void Use(UiLanguage language)
    {
        _store?.Save(language);
        if (language == _language)
        {
            return;
        }

        _language = language;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    // The picked language, else the Windows one. A data root that cannot be
    // resolved still gets a language; it just is not remembered.
    private static Localizer Create()
    {
        LanguageStore? store = null;
        try
        {
            store = new LanguageStore(OrbMetPaths.FromEnvironment().SettingsFile);
        }
        catch (ArgumentException)
        {
        }

        var language = store?.Load() ?? UiLanguages.ForCulture(CultureInfo.CurrentUICulture);
        return new Localizer(store, language);
    }
}
