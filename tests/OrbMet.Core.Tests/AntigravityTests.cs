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

    // What agy -p /usage --output-format json printed on 2026-11-17, cut to
    // the fields read and a line of its text table; it carries no account details.
    private const string CliUsage = """
        {"conversation_id":"","status":"SUCCESS","response":"Gemini Models\tWeekly Limit Remaining\t98%\t2026-11-24T12:44:53Z\n","duration_seconds":0,"num_turns":0,"usage":{"input_tokens":0,"output_tokens":0,"thinking_tokens":0,"cache_read_tokens":0,"total_tokens":0},"command":{"name":"usage","data":{"groups":[{"name":"Gemini Models","buckets":[{"id":"gemini-weekly","window":"weekly","remaining_fraction":0.9811149835586548,"reset_time":"2026-11-24T12:44:53Z"},{"id":"gemini-5h","window":"5h","remaining_fraction":0.9726622700691223,"reset_time":"2026-11-17T17:44:53Z"}]},{"name":"Claude and GPT models","buckets":[{"id":"3p-weekly","window":"weekly","remaining_fraction":1,"reset_time":"2026-11-24T12:57:32Z"},{"id":"3p-5h","window":"5h","remaining_fraction":1,"reset_time":"2026-11-17T17:57:32Z"}]},{"name":"Gemini 4 Argon","buckets":[{"id":"gemini-argon-wtu-weekly","window":"weekly","remaining_fraction":1,"reset_time":"2026-11-24T12:57:32Z"}]}]}}}
        """;

    // What agy printed without a Google sign-in, as reported for the CLI of 2026-11.
    private const string CliSignedOut = """{"conversation_id":"","status":"ERROR","response":"","error":"authentication failed or timed out"}""";

    private static readonly DateTimeOffset Now = new(2026, 10, 15, 20, 0, 0, TimeSpan.Zero);

    private static readonly Func<string?> NoCli = () => null;

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
        var provider = new AntigravityUsageProvider(find: () => Array.Empty<AntigravityServer>(), clock: () => Now,
            locateCli: NoCli);

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
            clock: () => Now,
            locateCli: NoCli);

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
            clock: () => Now,
            locateCli: NoCli).ReadAsync(CancellationToken.None);
        var silent = await new AntigravityUsageProvider(
            find: () => servers,
            ask: (_, _, _) => Task.FromResult<string?>(null),
            clock: () => Now,
            locateCli: NoCli).ReadAsync(CancellationToken.None);

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
            timeout: TimeSpan.FromMilliseconds(100),
            locateCli: NoCli);

        var snapshot = await provider.ReadAsync(CancellationToken.None);

        Assert.Equal(ConnectionState.Error, snapshot.ConnectionState);
        Assert.Equal("timeout", snapshot.ErrorCode);
    }

    [Fact]
    public void TheCliUsageIsReadLikeTheServersReply()
    {
        var windows = AntigravityQuotaParser.ParseCliOutput(CliUsage)!;

        Assert.Equal(
            new[] { "gemini-weekly", "gemini-5h", "3p-weekly", "3p-5h", "gemini-argon-wtu-weekly" },
            windows.Select(window => window.Id));
        var gemini = windows[1];
        Assert.Equal("gemini", gemini.GroupId);
        Assert.Equal("Gemini", gemini.ScopeLabel);
        Assert.Equal(2.73, gemini.UsedPercent!.Value, 2);
        Assert.Equal(UsageSelection.FiveHourSeconds, gemini.DurationSeconds);
        Assert.Equal(new DateTimeOffset(2026, 11, 17, 17, 44, 53, TimeSpan.Zero), gemini.ResetsAtUtc);

        Assert.Equal("Claude and GPT", windows[3].ScopeLabel);
        Assert.True(windows[3].NotStarted);
        Assert.Null(windows[3].ResetsAtUtc);

        var argon = windows[4];
        Assert.Equal("gemini-argon-wtu", argon.GroupId);
        Assert.Equal("Gemini 4 Argon", argon.ScopeLabel);
        Assert.Equal(UsageSelection.WeeklySeconds, argon.DurationSeconds);
    }

    [Fact]
    public void OnlyTheCliSummaryLineCountsAndAFailedRunHasNone()
    {
        Assert.Equal(5, AntigravityQuotaParser.ParseCliOutput("Fetching usage...\r\n" + CliUsage + "\r\nbye\r\n")!.Count);
        Assert.Null(AntigravityQuotaParser.ParseCliOutput(CliSignedOut));
        Assert.Null(AntigravityQuotaParser.ParseCliOutput(""));
        Assert.Null(AntigravityQuotaParser.ParseCliOutput("""{"status":"SUCCESS"}"""));
        Assert.Empty(AntigravityQuotaParser.ParseCliOutput("""{"status":"SUCCESS","command":{"name":"usage","data":{"groups":[]}}}""")!);
    }

    [Fact]
    public async Task WithNothingOpenTheCliIsAsked()
    {
        string? ran = null;
        var provider = new AntigravityUsageProvider(
            find: () => Array.Empty<AntigravityServer>(),
            clock: () => Now,
            locateCli: () => @"C:\agy\bin\agy.exe",
            runCli: (path, _) =>
            {
                ran = path;
                return Task.FromResult<AntigravityCliOutput?>(new(CliUsage, ""));
            });

        var snapshot = await provider.ReadAsync(CancellationToken.None);

        Assert.Equal(@"C:\agy\bin\agy.exe", ran);
        Assert.Equal(ConnectionState.Ready, snapshot.ConnectionState);
        Assert.Equal(SourceKind.AntigravityCli, snapshot.SourceKind);
        Assert.Equal(Freshness.Fresh, snapshot.Freshness);
        Assert.Equal(Now, snapshot.ObservedAtUtc);
        Assert.Equal(5, snapshot.Windows.Count);
    }

    [Fact]
    public async Task AnOpenAntigravityThatAnswersKeepsTheCliFromStarting()
    {
        var ran = false;
        var servers = new[] { new AntigravityServer(10, false, new[] { 100 }, "code") };

        var snapshot = await new AntigravityUsageProvider(
            find: () => servers,
            ask: (_, _, _) => Task.FromResult<string?>(InUse),
            clock: () => Now,
            locateCli: () => "agy.exe",
            runCli: (_, _) =>
            {
                ran = true;
                return Task.FromResult<AntigravityCliOutput?>(null);
            }).ReadAsync(CancellationToken.None);

        Assert.False(ran);
        Assert.Equal(SourceKind.AntigravityLanguageServer, snapshot.SourceKind);
        Assert.Equal(4, snapshot.Windows.Count);
    }

    [Fact]
    public async Task AnOpenAntigravityThatDoesNotAnswerFallsBackToTheCli()
    {
        var servers = new[] { new AntigravityServer(10, false, new[] { 100 }, "code") };

        var snapshot = await new AntigravityUsageProvider(
            find: () => servers,
            ask: (_, _, _) => Task.FromResult<string?>(null),
            clock: () => Now,
            locateCli: () => "agy.exe",
            runCli: (_, _) => Task.FromResult<AntigravityCliOutput?>(new(CliUsage, ""))).ReadAsync(CancellationToken.None);

        Assert.Equal(ConnectionState.Ready, snapshot.ConnectionState);
        Assert.Equal(SourceKind.AntigravityCli, snapshot.SourceKind);
        Assert.Equal(5, snapshot.Windows.Count);
    }

    [Fact]
    public async Task OnlyACliThatSaysSoIsSignedOut()
    {
        Task<ProviderSnapshot> Read(AntigravityCliOutput? output) => new AntigravityUsageProvider(
            find: () => Array.Empty<AntigravityServer>(),
            clock: () => Now,
            locateCli: () => "agy.exe",
            runCli: (_, _) => Task.FromResult(output)).ReadAsync(CancellationToken.None);

        var signedOut = await Read(new(CliSignedOut, "Error: authentication required. Run 'agy' to log in, then retry.\n"));
        var timedOut = await Read(new(CliSignedOut, "error: authentication failed or timed out\n"));
        var notStarted = await Read(null);
        var noLimits = await Read(new("""{"status":"SUCCESS","command":{"data":{"groups":[]}}}""", ""));

        Assert.Equal(ConnectionState.SignInRequired, signedOut.ConnectionState);
        Assert.Equal("antigravity-cli-signed-out", signedOut.ErrorCode);
        Assert.Empty(signedOut.Windows);
        Assert.Equal(ConnectionState.Error, timedOut.ConnectionState);
        Assert.Equal("antigravity-cli-failed", timedOut.ErrorCode);
        Assert.Equal("antigravity-cli-failed", notStarted.ErrorCode);
        Assert.Equal(ConnectionState.Ready, noLimits.ConnectionState);
        Assert.Equal("antigravity-no-limits", noLimits.ErrorCode);
    }

    [Fact]
    public async Task ASlowCliIsCancelledAndEndsInATimeout()
    {
        var cancelled = false;
        var provider = new AntigravityUsageProvider(
            find: () => Array.Empty<AntigravityServer>(),
            clock: () => Now,
            locateCli: () => "agy.exe",
            runCli: async (_, token) =>
            {
                try
                {
                    await Task.Delay(Timeout.Infinite, token);
                }
                catch (OperationCanceledException)
                {
                    cancelled = true;
                    throw;
                }

                return null;
            },
            cliTimeout: TimeSpan.FromMilliseconds(100));

        var snapshot = await provider.ReadAsync(CancellationToken.None);

        Assert.True(cancelled);
        Assert.Equal(ConnectionState.Error, snapshot.ConnectionState);
        Assert.Equal("timeout", snapshot.ErrorCode);
        Assert.Equal(SourceKind.AntigravityCli, snapshot.SourceKind);
    }

    [Fact]
    public async Task TheCliIsAskedForUsageInJsonWithoutUpdatingItself()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var temp = new TempDirectory();
        var agy = temp.Combine("agy.cmd");
        File.WriteAllText(agy, "@echo off\r\necho args=%*\r\necho update=%AGY_CLI_DISABLE_AUTO_UPDATE%\r\necho cwd=%CD%\r\necho oops 1>&2\r\n");

        var output = await AntigravityCli.RunUsageAsync(agy, TimeSpan.FromSeconds(15), temp.Path, CancellationToken.None);

        Assert.NotNull(output);
        Assert.Contains("args=-p /usage --output-format json --print-timeout 15s", output.StandardOutput);
        Assert.Contains("update=true", output.StandardOutput);
        Assert.Contains("cwd=" + temp.Path, output.StandardOutput);
        Assert.Contains("oops", output.StandardError);
    }

    [Fact]
    public async Task ACancelledRunEndsWhatItStarted()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var temp = new TempDirectory();
        var folder = temp.Combine("run");
        Directory.CreateDirectory(folder);
        var agy = temp.Combine("agy.cmd");
        File.WriteAllText(agy, "@echo off\r\nping -n 60 127.0.0.1 >nul\r\n");
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var watch = System.Diagnostics.Stopwatch.StartNew();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => AntigravityCli.RunUsageAsync(agy, TimeSpan.FromSeconds(15), folder, cancel.Token));

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10));

        // cmd and ping both ran in the folder; Windows will not delete it
        // while either is still alive.
        var deleted = false;
        for (var attempt = 0; attempt < 50 && !deleted; attempt++)
        {
            try
            {
                Directory.Delete(folder);
                deleted = true;
            }
            catch (IOException)
            {
                await Task.Delay(100);
            }
        }

        Assert.True(deleted);
    }
}
