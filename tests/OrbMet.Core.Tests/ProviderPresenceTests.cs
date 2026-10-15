using OrbMet.Core.Models;
using OrbMet.Core.Providers;
using OrbMet.Core.Usage;

namespace OrbMet.Core.Tests;

public class ProviderPresenceTests
{
    private static IReadOnlySet<ProviderId> Set(params ProviderId[] ids) => new HashSet<ProviderId>(ids);

    [Fact]
    public void OnlyTheInstalledProviderIsShown()
    {
        Assert.Equal(new[] { ProviderId.ClaudeCode }, ProviderPresence.Visible(Set(ProviderId.ClaudeCode), Set()));
        Assert.Equal(new[] { ProviderId.Codex }, ProviderPresence.Visible(Set(ProviderId.Codex), Set()));
    }

    [Fact]
    public void BothInstalledAreShownInOrder()
    {
        Assert.Equal(ProviderPresence.All, ProviderPresence.Visible(Set(ProviderId.Codex, ProviderId.ClaudeCode), Set()));
    }

    [Fact]
    public void WithNeitherInstalledBothAreShownSoTheStripSaysWhatIsMissing()
    {
        Assert.Equal(ProviderPresence.All, ProviderPresence.Visible(Set(), Set()));
    }

    [Fact]
    public void AHiddenProviderIsNotShownEvenWhenInstalled()
    {
        Assert.Equal(new[] { ProviderId.Codex }, ProviderPresence.Visible(ProviderPresence.All.ToHashSet(), Set(ProviderId.ClaudeCode)));
    }

    [Fact]
    public void HidingTheOnlyInstalledOneShowsTheOtherSaysMissing()
    {
        // Claude installed but hidden, Codex not installed: Codex shows as not found.
        Assert.Equal(new[] { ProviderId.Codex }, ProviderPresence.Visible(Set(ProviderId.ClaudeCode), Set(ProviderId.ClaudeCode)));
    }

    [Fact]
    public void TheStripIsNeverEmpty()
    {
        Assert.Equal(ProviderPresence.All, ProviderPresence.Visible(Set(), Set(ProviderId.ClaudeCode, ProviderId.Codex)));
    }

    [Fact]
    public void HiddenProvidersComeFromTheDisplaySettings()
    {
        var settings = new DisplaySettings { HideCodex = true };

        Assert.Equal(Set(ProviderId.Codex), settings.Hidden);
        Assert.Empty(new DisplaySettings().Hidden);
    }

    [Fact]
    public void ACommandIsFoundUnderAnyOfItsWindowsNames()
    {
        var found = CommandSearch.Find("claude", new[] { @"C:\A;C:\B" }, path => path == @"C:\B\claude.cmd");

        Assert.Equal(@"C:\B\claude.cmd", found);
        Assert.Null(CommandSearch.Find("claude", new[] { @"C:\A" }, _ => false));
    }

    [Fact]
    public async Task ASkippedProviderIsNeverRead()
    {
        var read = new List<ProviderId>();
        var coordinator = new UsageCoordinator(new[] { new FakeProvider(ProviderId.ClaudeCode, read), new FakeProvider(ProviderId.Codex, read) })
        {
            IsEnabled = id => id != ProviderId.Codex,
        };

        var results = await coordinator.ReadAllAsync(CancellationToken.None);

        Assert.Equal(new[] { ProviderId.ClaudeCode }, read);
        Assert.Single(results);
        Assert.Null(await coordinator.ReadAsync(ProviderId.Codex, CancellationToken.None));
    }

    private sealed class FakeProvider(ProviderId id, List<ProviderId> read) : IUsageProvider
    {
        public ProviderId ProviderId => id;

        public SourceKind SourceKind => SourceKind.CodexAppServer;

        public Task<ProviderSnapshot> ReadAsync(CancellationToken cancellationToken)
        {
            read.Add(id);
            return Task.FromResult(new ProviderSnapshot { ProviderId = id, SourceKind = SourceKind });
        }
    }
}
