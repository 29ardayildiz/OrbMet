using System.Text.Json;
using OrbMet.Core.Models;
using OrbMet.Core.Providers.Codex;
using OrbMet.Core.Usage;

namespace OrbMet.Core.Tests;

public class CodexUsageNormalizerTests
{
    private static readonly DateTimeOffset ReceivedAt = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    private static ProviderSnapshot Normalize(string resultJson)
    {
        using var document = JsonDocument.Parse(resultJson);
        return CodexUsageNormalizer.Normalize(document.RootElement.Clone(), ReceivedAt);
    }

    [Fact]
    public void MapsASingleBucketIntoPrimaryAndSecondaryWindows()
    {
        var snapshot = Normalize(
            "{\"rateLimits\":{\"limitId\":\"codex\",\"planType\":\"plus\"," +
            "\"primary\":{\"usedPercent\":42,\"windowDurationMins\":300,\"resetsAt\":1750000000}," +
            "\"secondary\":{\"usedPercent\":61,\"windowDurationMins\":10080,\"resetsAt\":1750500000}}}");

        Assert.Equal(ConnectionState.Ready, snapshot.ConnectionState);
        Assert.Equal(Freshness.Fresh, snapshot.Freshness);
        Assert.Equal("plus", snapshot.PlanName);

        var primary = snapshot.Windows.Single(window => window.Id == "codex.primary");
        Assert.Equal(42, primary.UsedPercent);
        Assert.Equal(300 * 60, primary.DurationSeconds);
        Assert.Equal(DurationOrigin.Reported, primary.DurationOrigin);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1750000000), primary.ResetsAtUtc);

        var secondary = snapshot.Windows.Single(window => window.Id == "codex.secondary");
        Assert.Equal(61, secondary.UsedPercent);
    }

    [Fact]
    public void KeepsOnlyTheReportedWindowWhenSecondaryIsMissing()
    {
        var snapshot = Normalize(
            "{\"rateLimits\":{\"primary\":{\"usedPercent\":10}}}");

        Assert.Single(snapshot.Windows);
        Assert.Equal("default.primary", snapshot.Windows[0].Id);
        Assert.Equal(10, snapshot.Windows[0].UsedPercent);
    }

    [Fact]
    public void LeavesAMissingPercentageAsNullInsteadOfZero()
    {
        var snapshot = Normalize("{\"rateLimits\":{\"primary\":{\"windowDurationMins\":300}}}");

        Assert.Single(snapshot.Windows);
        Assert.Null(snapshot.Windows[0].UsedPercent);
    }

    [Fact]
    public void DoesNotClampValuesAboveOneHundred()
    {
        var snapshot = Normalize("{\"rateLimits\":{\"primary\":{\"usedPercent\":130}}}");

        Assert.Equal(130, snapshot.Windows[0].UsedPercent);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public void PreservesBoundaryPercentages(int usedPercent)
    {
        var snapshot = Normalize($"{{\"rateLimits\":{{\"primary\":{{\"usedPercent\":{usedPercent}}}}}}}");

        Assert.Equal(usedPercent, snapshot.Windows[0].UsedPercent);
    }

    [Fact]
    public void MarksDurationAsUnknownWhenTheProviderOmitsIt()
    {
        var snapshot = Normalize("{\"rateLimits\":{\"primary\":{\"usedPercent\":10}}}");

        Assert.Null(snapshot.Windows[0].DurationSeconds);
        Assert.Equal(DurationOrigin.Unknown, snapshot.Windows[0].DurationOrigin);
        Assert.Null(snapshot.Windows[0].ResetsAtUtc);
    }

    [Fact]
    public void MapsMultipleBucketsFromTheByLimitIdView()
    {
        var snapshot = Normalize(
            "{\"rateLimitsByLimitId\":{" +
            "\"codex\":{\"limitId\":\"codex\",\"limitName\":\"Codex\",\"primary\":{\"usedPercent\":20}}," +
            "\"codex-mini\":{\"limitId\":\"codex-mini\",\"limitName\":\"Codex mini\",\"primary\":{\"usedPercent\":80}}}}");

        Assert.Equal("codex", snapshot.Windows.Single(window => window.Id == "codex.primary").GroupId);
        Assert.Equal("Codex", snapshot.Windows.Single(window => window.Id == "codex.primary").ScopeLabel);
        Assert.Equal("codex-mini", snapshot.Windows.Single(window => window.Id == "codex-mini.primary").GroupId);
        Assert.Equal(2, snapshot.Windows.Count);
    }

    [Fact]
    public void PrefersTheMultiBucketViewOverTheSingleBucketView()
    {
        var snapshot = Normalize(
            "{\"rateLimits\":{\"limitId\":\"codex\",\"primary\":{\"usedPercent\":1}}," +
            "\"rateLimitsByLimitId\":{\"codex\":{\"limitId\":\"codex\",\"primary\":{\"usedPercent\":55}}}}");

        Assert.Single(snapshot.Windows);
        Assert.Equal(55, snapshot.Windows[0].UsedPercent);
    }

    [Fact]
    public void UsesAccountIdAsScopeWhenTheProviderSuppliesOne()
    {
        var snapshot = Normalize(
            "{\"accountId\":\"acct-123\",\"rateLimits\":{\"primary\":{\"usedPercent\":5}}}");

        Assert.Equal("acct-123", snapshot.AccountScopeKey);
    }

    [Fact]
    public void FallsBackToTheSourceScopeWhenNoAccountIdIsGiven()
    {
        var snapshot = Normalize("{\"rateLimits\":{\"primary\":{\"usedPercent\":5}}}");

        Assert.Equal("codex", snapshot.AccountScopeKey);
    }

    [Fact]
    public void ReportsExhaustionWhenOrdinaryUsageIsNotAllowed()
    {
        var snapshot = Normalize(
            "{\"ordinaryUsageAllowed\":false,\"rateLimits\":{\"primary\":{\"usedPercent\":100}}}");

        Assert.Equal(ExhaustionEvidence.Exhausted, snapshot.Windows[0].ExhaustionEvidence);
    }

    [Fact]
    public void ReportsExhaustionFromTheReachedType()
    {
        var snapshot = Normalize(
            "{\"rateLimits\":{\"primary\":{\"usedPercent\":100},\"rateLimitReachedType\":\"rate_limit_reached\"}}");

        Assert.Equal(ExhaustionEvidence.Exhausted, snapshot.Windows[0].ExhaustionEvidence);
    }

    [Fact]
    public void ReportsNotExhaustedOnlyWhenTheProviderSaysUsageIsAllowed()
    {
        var snapshot = Normalize(
            "{\"ordinaryUsageAllowed\":true,\"rateLimits\":{\"primary\":{\"usedPercent\":99}}}");

        Assert.Equal(ExhaustionEvidence.NotExhausted, snapshot.Windows[0].ExhaustionEvidence);
    }

    [Fact]
    public void LeavesExhaustionUnknownWithoutEvidence()
    {
        var snapshot = Normalize("{\"rateLimits\":{\"primary\":{\"usedPercent\":99}}}");

        Assert.Equal(ExhaustionEvidence.Unknown, snapshot.Windows[0].ExhaustionEvidence);
    }

    [Fact]
    public void ProducesNoWindowsForAnEmptyResult()
    {
        var snapshot = Normalize("{}");

        Assert.Empty(snapshot.Windows);
        Assert.Equal(ConnectionState.Ready, snapshot.ConnectionState);
    }

    [Fact]
    public void SelectionPicksTheHighestAccountWideWindow()
    {
        var snapshot = Normalize(
            "{\"rateLimits\":{\"limitId\":\"codex\"," +
            "\"primary\":{\"usedPercent\":30},\"secondary\":{\"usedPercent\":70}}}");

        var selected = UsageSelection.SelectRingWindow(snapshot);

        Assert.NotNull(selected);
        Assert.Equal("codex.secondary", selected!.Id);
    }
}
