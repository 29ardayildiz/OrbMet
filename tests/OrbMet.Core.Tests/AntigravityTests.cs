using OrbMet.Core.Localization;
using OrbMet.Core.Models;
using OrbMet.Core.Providers.Antigravity;
using OrbMet.Core.Usage;

namespace OrbMet.Core.Tests;

public class AntigravityTests
{
    // The reply Antigravity 2.8.1 and Antigravity IDE 2.5.5 both gave on
    // 2026-10-15 with nothing used; it carries no account details.
    private const string Unused = """
        {"response":{"groups":[{"displayName":"Gemini Models","description":"Models within this group: Gemini Flash, Gemini Pro","buckets":[{"bucketId":"gemini-weekly","displayName":"Weekly Limit Remaining","window":"weekly","remainingFraction":1,"resetTime":"2026-10-22T19:36:35Z"},{"bucketId":"gemini-5h","displayName":"Five Hour Limit Remaining","window":"5h","remainingFraction":1,"resetTime":"2026-10-16T00:36:35Z"}]},{"displayName":"Claude and GPT models","description":"Models within this group: Claude Opus, Claude Sonnet, GPT-OSS","buckets":[{"bucketId":"3p-weekly","displayName":"Weekly Limit Remaining","window":"weekly","remainingFraction":1,"resetTime":"2026-10-22T19:36:35Z"},{"bucketId":"3p-5h","displayName":"Five Hour Limit Remaining","window":"5h","remainingFraction":1,"resetTime":"2026-10-16T00:36:35Z"}]}],"description":"Within each group, models share a weekly limit and a 5-hour limit."}}
        """;

    // The same shape with some of each limit used.
    private const string InUse = """
        {"response":{"groups":[{"displayName":"Gemini Models","buckets":[{"bucketId":"gemini-weekly","window":"weekly","remainingFraction":0.9,"resetTime":"2026-10-22T08:00:00Z"},{"bucketId":"gemini-5h","window":"5h","remainingFraction":0.75,"resetTime":"2026-10-16T02:00:00Z"}]},{"displayName":"Claude and GPT models","buckets":[{"bucketId":"3p-weekly","window":"weekly","remainingFraction":0.5,"resetTime":"2026-10-22T08:00:00Z"},{"bucketId":"3p-5h","window":"5h","remainingFraction":0.4,"resetTime":"2026-10-16T01:00:00Z"}]}]}}
        """;

    private static readonly DateTimeOffset Now = new(2026, 10, 15, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public void EachBucketIsAWindowWithItsGroupAndLength()
    {
        var windows = AntigravityQuotaParser.Parse(InUse)!;

        Assert.Equal(4, windows.Count);
        var gemini = windows.Single(window => window.Id == "gemini-5h");
        Assert.Equal("gemini", gemini.GroupId);
        Assert.Equal("Gemini", gemini.ScopeLabel);
        Assert.Equal(25, gemini.UsedPercent!.Value, 6);
        Assert.Equal(UsageSelection.FiveHourSeconds, gemini.DurationSeconds);
        Assert.Equal(DurationOrigin.Reported, gemini.DurationOrigin);
        Assert.Equal(new DateTimeOffset(2026, 10, 16, 2, 0, 0, TimeSpan.Zero), gemini.ResetsAtUtc);
        Assert.False(gemini.NotStarted);

        var other = windows.Single(window => window.Id == "3p-weekly");
        Assert.Equal("3p", other.GroupId);
        Assert.Equal("Claude and GPT", other.ScopeLabel);
        Assert.Equal(UsageSelection.WeeklySeconds, other.DurationSeconds);
        Assert.Equal(50, other.UsedPercent!.Value, 6);
    }

    [Fact]
    public void AnUnusedWindowHasNotStartedAndItsMovingResetIsLeftOut()
    {
        var windows = AntigravityQuotaParser.Parse(Unused)!;

        Assert.Equal(4, windows.Count);
        Assert.All(windows, window =>
        {
            Assert.Equal(0, window.UsedPercent);
            Assert.True(window.NotStarted);
            Assert.Null(window.ResetsAtUtc);
        });
    }

    [Fact]
    public void TheCardSaysANotStartedWindowHasNotStarted()
    {
        var window = AntigravityQuotaParser.Parse(Unused)![0];
        var zone = TimeZoneInfo.Utc;

        Assert.Equal("Henüz başlamadı", UsageText.For(UiLanguage.Turkish).ResetLine(window, Now, zone));
        Assert.Equal("Not started yet", UsageText.For(UiLanguage.English).ResetLine(window, Now, zone));
        Assert.Equal(
            "Sıfırlanma 16 Eki 02:00 · 6 sa sonra",
            UsageText.For(UiLanguage.Turkish).ResetLine(AntigravityQuotaParser.Parse(InUse)![1], Now, zone));
    }

    [Fact]
    public void TheRingShowsTheFullerGroupForTheChosenLimit()
    {
        var snapshot = new ProviderSnapshot
        {
            ProviderId = ProviderId.Antigravity,
            SourceKind = SourceKind.AntigravityLanguageServer,
            Windows = AntigravityQuotaParser.Parse(InUse)!,
        };

        Assert.Equal("3p-5h", UsageSelection.SelectRingWindow(snapshot, RingChoice.FiveHour)!.Id);
        Assert.Equal("3p-weekly", UsageSelection.SelectRingWindow(snapshot, RingChoice.Weekly)!.Id);
        Assert.Equal("3p-5h", UsageSelection.SelectRingWindow(snapshot, RingChoice.ClosestToLimit)!.Id);
    }

    [Fact]
    public void AMissingFigureStaysMissingAndAnUnknownWindowIsLeftOut()
    {
        const string json = """
            {"response":{"groups":[{"displayName":"Gemini Models","buckets":[
              {"bucketId":"gemini-5h","window":"5h","resetTime":"2026-10-16T02:00:00Z"},
              {"bucketId":"gemini-odd","window":"fortnightly","remainingFraction":0.5},
              {"bucketId":"gemini-12h","window":"12h","remainingFraction":0.5}]}]}}
            """;

        var windows = AntigravityQuotaParser.Parse(json)!;

        Assert.Equal(new[] { "gemini-5h", "gemini-12h" }, windows.Select(window => window.Id));
        Assert.Null(windows[0].UsedPercent);
        Assert.False(windows[0].NotStarted);
        Assert.Equal(12 * 3600, windows[1].DurationSeconds);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"error":"unauthenticated"}""")]
    public void AReplyThatIsNotAQuotaSummaryIsNull(string json)
    {
        Assert.Null(AntigravityQuotaParser.Parse(json));
    }

    [Fact]
    public void AReplyWithNoGroupsHasNoWindows()
    {
        Assert.Empty(AntigravityQuotaParser.Parse("""{"response":{}}""")!);
    }

    [Fact]
    public void TheCodeIsReadFromItsOwnSwitchOnly()
    {
        const string line = "language_server.exe --extension_server_csrf_token other --csrf_token abc-123 --extension_server_port 62308";

        Assert.Equal("abc-123", AntigravityServers.ReadCsrfToken(line));
        Assert.Equal("q", AntigravityServers.ReadCsrfToken("x --csrf_token=q"));
        Assert.Null(AntigravityServers.ReadCsrfToken("x --extension_server_csrf_token only"));
    }

    [Fact]
    public void AServerNeverPrintsItsCode()
    {
        var server = new AntigravityServer(1, false, new[] { 5000 }, "secret-code");

        Assert.DoesNotContain("secret-code", server.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithNoServerRunningItSaysSoWithoutAnError()
    {
        var provider = new AntigravityUsageProvider(find: () => Array.Empty<AntigravityServer>(), clock: () => Now);

        var snapshot = await provider.ReadAsync(CancellationToken.None);

        Assert.Equal(ConnectionState.AwaitingData, snapshot.ConnectionState);
        Assert.Equal("antigravity-not-running", snapshot.ErrorCode);
        Assert.Empty(snapshot.Windows);
    }

    [Fact]
    public async Task EveryPortIsTriedUntilOneAnswersWithLimits()
    {
        var asked = new List<(int Port, string Code)>();
        var servers = new[]
        {
            new AntigravityServer(10, false, new[] { 100, 101 }, "app-code"),
            new AntigravityServer(20, true, new[] { 200 }, "ide-code"),
        };
        var provider = new AntigravityUsageProvider(
            find: () => servers,
            ask: (port, code, _) =>
            {
                asked.Add((port, code));
                return Task.FromResult(port switch
                {
                    100 => null,
                    101 => """{"response":{}}""",
                    _ => InUse,
                });
            },
            clock: () => Now);

        var snapshot = await provider.ReadAsync(CancellationToken.None);

        Assert.Equal(new[] { (100, "app-code"), (101, "app-code"), (200, "ide-code") }, asked);
        Assert.Equal(ConnectionState.Ready, snapshot.ConnectionState);
        Assert.Equal(Freshness.Fresh, snapshot.Freshness);
        Assert.Equal(Now, snapshot.ObservedAtUtc);
        Assert.Equal(4, snapshot.Windows.Count);
    }

    [Fact]
    public async Task OnlyEmptyAnswersSayNoLimitsAndNoAnswersSayNotAnswering()
    {
        var servers = new[] { new AntigravityServer(10, false, new[] { 100 }, "code") };

        var empty = await new AntigravityUsageProvider(
            find: () => servers,
            ask: (_, _, _) => Task.FromResult<string?>("""{"response":{}}"""),
            clock: () => Now).ReadAsync(CancellationToken.None);
        var silent = await new AntigravityUsageProvider(
            find: () => servers,
            ask: (_, _, _) => Task.FromResult<string?>(null),
            clock: () => Now).ReadAsync(CancellationToken.None);

        Assert.Equal("antigravity-no-limits", empty.ErrorCode);
        Assert.Empty(empty.Windows);
        Assert.Equal(ConnectionState.Error, silent.ConnectionState);
        Assert.Equal("antigravity-not-answering", silent.ErrorCode);
    }

    [Fact]
    public async Task ASlowServerEndsInATimeoutNotAHang()
    {
        var servers = new[] { new AntigravityServer(10, false, new[] { 100 }, "code") };
        var provider = new AntigravityUsageProvider(
            find: () => servers,
            ask: async (_, _, token) =>
            {
                await Task.Delay(Timeout.Infinite, token);
                return null;
            },
            clock: () => Now,
            timeout: TimeSpan.FromMilliseconds(100));

        var snapshot = await provider.ReadAsync(CancellationToken.None);

        Assert.Equal(ConnectionState.Error, snapshot.ConnectionState);
        Assert.Equal("timeout", snapshot.ErrorCode);
    }
}
