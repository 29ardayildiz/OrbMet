using OrbMet.Core.Localization;
using OrbMet.Core.Models;
using OrbMet.Core.Usage;

namespace OrbMet.Core.Tests;

public class UsageAlertsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    private static UsageWindow FiveHour(double percent, DateTimeOffset? resetsAt = null, ExhaustionEvidence evidence = ExhaustionEvidence.Unknown) => new()
    {
        Id = "five_hour",
        UsedPercent = percent,
        DurationSeconds = UsageSelection.FiveHourSeconds,
        ResetsAtUtc = resetsAt ?? Now.AddHours(2),
        ExhaustionEvidence = evidence,
    };

    private static AlertReading Claude(params UsageWindow[] windows) => new(new ProviderSnapshot
    {
        ProviderId = ProviderId.ClaudeCode,
        SourceKind = SourceKind.ClaudeStatusLine,
        ConnectionState = ConnectionState.Ready,
        Windows = windows,
    });

    private static AlertReading Codex(ConnectionState state) => new(new ProviderSnapshot
    {
        ProviderId = ProviderId.Codex,
        SourceKind = SourceKind.CodexAppServer,
        ConnectionState = state,
    });

    private static IReadOnlyList<UsageAlert> Evaluate(AlertMemory memory, int threshold, DateTimeOffset now, params AlertReading[] readings) =>
        UsageAlerts.Evaluate(memory, threshold, readings, now);

    [Fact]
    public void SaysNothingAndRemembersNothingWhenOff()
    {
        var memory = new AlertMemory();

        Assert.Empty(Evaluate(memory, 0, Now, Claude(FiveHour(99))));
        Assert.Empty(memory.Windows);
    }

    [Fact]
    public void SaysAThresholdOnceThenNotAgain()
    {
        var memory = new AlertMemory();

        Assert.Empty(Evaluate(memory, 80, Now, Claude(FiveHour(79))));

        var alert = Assert.Single(Evaluate(memory, 80, Now, Claude(FiveHour(81))));
        Assert.Equal(AlertKind.Approaching, alert.Kind);
        Assert.Equal(81, alert.Window!.UsedPercent);

        Assert.Empty(Evaluate(memory, 80, Now, Claude(FiveHour(85))));
    }

    [Fact]
    public void ARaisedThresholdIsSaidAgainALoweredOneIsNot()
    {
        var memory = new AlertMemory();
        Evaluate(memory, 75, Now, Claude(FiveHour(92)));

        Assert.Equal(AlertKind.Approaching, Assert.Single(Evaluate(memory, 90, Now, Claude(FiveHour(92)))).Kind);
        Assert.Empty(Evaluate(memory, 75, Now, Claude(FiveHour(92))));
    }

    [Fact]
    public void SpentIsSaidOnceEvenAfterTheThreshold()
    {
        var memory = new AlertMemory();
        Evaluate(memory, 90, Now, Claude(FiveHour(95)));

        Assert.Equal(AlertKind.Spent, Assert.Single(Evaluate(memory, 90, Now, Claude(FiveHour(100)))).Kind);
        Assert.Empty(Evaluate(memory, 90, Now, Claude(FiveHour(100))));
    }

    [Fact]
    public void TheProvidersOwnBlockCountsAsSpentBelowOneHundred()
    {
        var alert = Assert.Single(Evaluate(new AlertMemory(), 95, Now, Claude(FiveHour(40, evidence: ExhaustionEvidence.Exhausted))));

        Assert.Equal(AlertKind.Spent, alert.Kind);
    }

    [Fact]
    public void APeriodWhoseResetHasPassedSaysNothing()
    {
        Assert.Empty(Evaluate(new AlertMemory(), 75, Now, Claude(FiveHour(99, resetsAt: Now.AddMinutes(-1)))));
    }

    [Fact]
    public void AWarnedLimitIsSaidToResetWhenItsTimeComesOnce()
    {
        var memory = new AlertMemory();
        var resetsAt = Now.AddHours(1);
        Evaluate(memory, 80, Now, Claude(FiveHour(90, resetsAt)));

        // No new reading: Claude only reports while in use.
        var later = resetsAt.AddMinutes(2);
        var alert = Assert.Single(Evaluate(memory, 80, later));
        Assert.Equal(AlertKind.Reset, alert.Kind);
        Assert.Equal(UsageSelection.FiveHourSeconds, alert.Window!.DurationSeconds);

        Assert.Empty(Evaluate(memory, 80, later.AddMinutes(2)));
    }

    [Fact]
    public void ANewPeriodInTheDataIsAResetAndStartsAfresh()
    {
        var memory = new AlertMemory();
        Evaluate(memory, 80, Now, Claude(FiveHour(90, Now.AddHours(1))));

        var alerts = Evaluate(memory, 80, Now.AddMinutes(10), Claude(FiveHour(85, Now.AddHours(6))));

        Assert.Equal(new[] { AlertKind.Approaching, AlertKind.Reset }, alerts.Select(alert => alert.Kind));
    }

    [Fact]
    public void AnUnwarnedLimitResetsQuietly()
    {
        var memory = new AlertMemory();
        Evaluate(memory, 80, Now, Claude(FiveHour(20, Now.AddHours(1))));

        Assert.Empty(Evaluate(memory, 80, Now.AddHours(2), Claude(FiveHour(5, Now.AddHours(6)))));
    }

    [Fact]
    public void CodexIsCalledUnreadableAfterThreeFailedReadsOnce()
    {
        var memory = new AlertMemory();

        Assert.Empty(Evaluate(memory, 75, Now, Codex(ConnectionState.Error)));
        Assert.Empty(Evaluate(memory, 75, Now, Codex(ConnectionState.Error)));
        Assert.Equal(AlertKind.CodexUnreadable, Assert.Single(Evaluate(memory, 75, Now, Codex(ConnectionState.Error))).Kind);
        Assert.Empty(Evaluate(memory, 75, Now, Codex(ConnectionState.Error)));

        // A good read ends the outage; the next one is said again.
        Evaluate(memory, 75, Now, Codex(ConnectionState.Ready));
        Evaluate(memory, 75, Now, Codex(ConnectionState.Error));
        Evaluate(memory, 75, Now, Codex(ConnectionState.Error));
        Assert.Single(Evaluate(memory, 75, Now, Codex(ConnectionState.Error)));
    }

    [Fact]
    public void CodexNotInstalledIsNotAnOutage()
    {
        var memory = new AlertMemory();
        for (var i = 0; i < 5; i++)
        {
            Assert.Empty(Evaluate(memory, 75, Now, Codex(ConnectionState.MissingTool)));
        }
    }

    [Fact]
    public void LookingAgainWithoutAReadDoesNotCountAFailure()
    {
        var memory = new AlertMemory();
        Evaluate(memory, 75, Now, Codex(ConnectionState.Error));
        UsageAlerts.Evaluate(memory, 80, new[] { Codex(ConnectionState.Error) }, Now, afterRead: false);

        Assert.Equal(1, memory.CodexFailures);
    }

    [Fact]
    public void ABrokenClaudeLinkIsSaidOncePerBreak()
    {
        var memory = new AlertMemory();
        var broken = Claude() with { ClaudeLinkBroken = true };

        Assert.Equal(AlertKind.ClaudeLinkBroken, Assert.Single(Evaluate(memory, 75, Now, broken)).Kind);
        Assert.Empty(Evaluate(memory, 75, Now, broken));

        Evaluate(memory, 75, Now, Claude());
        Assert.Single(Evaluate(memory, 75, Now, broken));
    }

    [Fact]
    public void SpentComesFirst()
    {
        var weekly = new UsageWindow
        {
            Id = "seven_day",
            UsedPercent = 100,
            DurationSeconds = UsageSelection.WeeklySeconds,
            ResetsAtUtc = Now.AddDays(2),
        };

        var alerts = Evaluate(new AlertMemory(), 75, Now, Claude(FiveHour(80), weekly));

        Assert.Equal(new[] { AlertKind.Spent, AlertKind.Approaching }, alerts.Select(alert => alert.Kind));
    }

    [Fact]
    public void TheMemoryAndThresholdSurviveARestart()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "settings.json");
        var memory = new AlertMemory();
        Evaluate(memory, 80, Now, Claude(FiveHour(90)));

        var store = new AlertStore(path);
        Assert.Equal(0, store.LoadThreshold());
        Assert.True(store.SaveThreshold(80));
        Assert.True(store.SaveMemory(memory));

        var again = new AlertStore(path);
        Assert.Equal(80, again.LoadThreshold());
        Assert.Empty(Evaluate(again.LoadMemory(), 80, Now, Claude(FiveHour(92))));
    }

    [Fact]
    public void AnUnofferedThresholdMeansOff()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "settings.json");
        File.WriteAllText(path, """{"AlertThreshold": 33, "Alerts": "broken"}""");

        var store = new AlertStore(path);
        Assert.Equal(0, store.LoadThreshold());
        Assert.Empty(store.LoadMemory().Windows);
    }

    [Theory]
    [InlineData(UiLanguage.English, "Claude Code: 82% used", "5-hour limit · Resets 14:00")]
    [InlineData(UiLanguage.Turkish, "Claude Code: %82 kullanıldı", "5 saatlik limit · Sıfırlanma 14:00")]
    public void ANotificationNamesTheProviderTheFigureAndTheReset(UiLanguage language, string title, string body)
    {
        var alert = new UsageAlert(ProviderId.ClaudeCode, AlertKind.Approaching, FiveHour(82));

        var text = UsageText.For(language).Alert(alert, "Claude Code", Now, TimeZoneInfo.Utc);

        Assert.Equal((title, body), text);
    }
}
