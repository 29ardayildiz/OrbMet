using OrbMet.Core.Localization;
using OrbMet.Core.Storage;

namespace OrbMet.Core.Tests;

public class WelcomeStoreTests
{
    [Fact]
    public void NotOfferedWhenThereIsNoSettingsFile()
    {
        using var temp = new TempDirectory();

        Assert.False(new WelcomeStore(Path.Combine(temp.Path, "settings.json")).ConnectionsOffered);
    }

    [Fact]
    public void RemembersThatConnectionsWasOffered()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "settings.json");

        Assert.True(new WelcomeStore(path).MarkConnectionsOffered());

        Assert.True(new WelcomeStore(path).ConnectionsOffered);
    }

    [Fact]
    public void KeepsTheLanguageAndTheLanguageKeepsIt()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "settings.json");

        new LanguageStore(path).Save(UiLanguage.Turkish);
        new WelcomeStore(path).MarkConnectionsOffered();
        new LanguageStore(path).Save(UiLanguage.English);

        Assert.Equal(UiLanguage.English, new LanguageStore(path).Load());
        Assert.True(new WelcomeStore(path).ConnectionsOffered);
    }

    [Fact]
    public void AnUnreadableFileIsNotOfferedAndIsReplacedOnWrite()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(temp.Path);
        var path = Path.Combine(temp.Path, "settings.json");
        File.WriteAllText(path, "{ half");
        var store = new WelcomeStore(path);

        Assert.False(store.ConnectionsOffered);
        Assert.True(store.MarkConnectionsOffered());
        Assert.True(store.ConnectionsOffered);
    }

    [Fact]
    public void AValueThatIsNotTrueIsNotOffered()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(temp.Path);
        var path = Path.Combine(temp.Path, "settings.json");
        File.WriteAllText(path, "{\"ConnectionsOffered\": \"yes\"}");

        Assert.False(new WelcomeStore(path).ConnectionsOffered);
    }
}
