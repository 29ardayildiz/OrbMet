using OrbMet.Core.Updates;

namespace OrbMet.Core.Tests;

public class ReleaseCheckTests
{
    private const string Download = "https://github.com/29ardayildiz/OrbMet/releases/download/v0.4.0/";

    private static string Release(string tag, bool draft = false, bool prerelease = false, string assetsBase = Download) => $$"""
        {
          "tag_name": "{{tag}}",
          "html_url": "https://github.com/29ardayildiz/OrbMet/releases/tag/{{tag}}",
          "draft": {{(draft ? "true" : "false")}},
          "prerelease": {{(prerelease ? "true" : "false")}},
          "assets": [
            { "name": "OrbMet-0.4.0-win-x64.zip", "browser_download_url": "{{assetsBase}}OrbMet-0.4.0-win-x64.zip" },
            { "name": "OrbMet-0.4.0-setup.exe", "browser_download_url": "{{assetsBase}}OrbMet-0.4.0-setup.exe" },
            { "name": "OrbMet-0.4.0-setup.exe.sha256", "browser_download_url": "{{assetsBase}}OrbMet-0.4.0-setup.exe.sha256" }
          ]
        }
        """;

    [Fact]
    public void ANewerReleaseIsOfferedWithItsInstallerAndChecksum()
    {
        var update = ReleaseCheck.Read(Release("v0.4.0"), new Version(0, 3, 0));

        Assert.NotNull(update);
        Assert.Equal(new Version(0, 4, 0), update!.Version);
        Assert.Equal(Download + "OrbMet-0.4.0-setup.exe", update.SetupUrl);
        Assert.Equal(Download + "OrbMet-0.4.0-setup.exe.sha256", update.ChecksumUrl);
        Assert.Equal("https://github.com/29ardayildiz/OrbMet/releases/tag/v0.4.0", update.PageUrl);
    }

    [Theory]
    [InlineData("0.4.0")]
    [InlineData("0.5.0")]
    public void TheSameOrAnOlderReleaseIsNotOffered(string running)
    {
        Assert.Null(ReleaseCheck.Read(Release("v0.4.0"), Version.Parse(running)));
    }

    [Fact]
    public void ADraftOrPrereleaseIsNotOffered()
    {
        Assert.Null(ReleaseCheck.Read(Release("v0.4.0", draft: true), new Version(0, 3, 0)));
        Assert.Null(ReleaseCheck.Read(Release("v0.4.0", prerelease: true), new Version(0, 3, 0)));
    }

    [Fact]
    public void AssetsOnAnotherHostAreIgnored()
    {
        var update = ReleaseCheck.Read(Release("v0.4.0", assetsBase: "https://example.com/"), new Version(0, 3, 0));

        Assert.NotNull(update);
        Assert.Null(update!.SetupUrl);
        Assert.Null(update.ChecksumUrl);
    }

    [Fact]
    public void ATestFeedMaySupplyItsOwnDownloads()
    {
        var update = ReleaseCheck.Read(Release("v0.4.0", assetsBase: "http://127.0.0.1:8765/"), new Version(0, 3, 0), "http://127.0.0.1:8765/");

        Assert.Equal("http://127.0.0.1:8765/OrbMet-0.4.0-setup.exe", update!.SetupUrl);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"tag_name": "latest"}""")]
    public void AnUnreadableReplyOffersNothing(string json)
    {
        Assert.Null(ReleaseCheck.Read(json, new Version(0, 3, 0)));
    }

    [Theory]
    [InlineData("0.3.0+9061d974b63ba83f8ab11db2e8b33efc72fa9b1b", "0.3.0")]
    [InlineData("v0.4.0", "0.4.0")]
    [InlineData("0.4", "0.4.0")]
    [InlineData("1.2.3-beta", "1.2.3")]
    public void VersionsAreReadWithoutTheirSuffix(string text, string expected)
    {
        Assert.Equal(Version.Parse(expected), ReleaseCheck.ParseVersion(text));
    }

    [Fact]
    public void AChecksumFileGivesItsHash()
    {
        var hash = new string('A', 64);

        Assert.Equal(new string('a', 64), ReleaseCheck.ParseChecksum($"{hash}  OrbMet-0.4.0-setup.exe\n"));
        Assert.Null(ReleaseCheck.ParseChecksum("not a hash"));
        Assert.Null(ReleaseCheck.ParseChecksum(""));
    }

    [Fact]
    public void ChecksAreOnUntilTurnedOffAndTheAnnouncedVersionIsKept()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "settings.json");
        var store = new UpdateStore(path);

        Assert.True(store.Enabled);
        Assert.Null(store.Announced);
        Assert.Null(store.CheckedAtUtc);

        store.SetEnabled(false);
        store.MarkAnnounced(new Version(0, 4, 0));
        var now = new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);
        store.MarkChecked(now);

        var again = new UpdateStore(path);
        Assert.False(again.Enabled);
        Assert.Equal(new Version(0, 4, 0), again.Announced);
        Assert.Equal(now, again.CheckedAtUtc);
    }
}
