using OrbMet.Core.Providers.Claude;

namespace OrbMet.Core.Tests;

public class ClaudeStatusLineParserTests
{
    private static readonly DateTimeOffset Captured = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    private static ClaudeCaptureRecord? Parse(string json) =>
        ClaudeStatusLineParser.Parse(json, Captured);

    [Fact]
    public void ReadsTheFiveHourAndSevenDayWindows()
    {
        var record = Parse(
            "{\"rate_limits\":{" +
            "\"five_hour\":{\"used_percentage\":23.5,\"resets_at\":1738425600}," +
            "\"seven_day\":{\"used_percentage\":41.2,\"resets_at\":1738857600}}}");

        Assert.NotNull(record);
        Assert.Equal(Captured, record!.CapturedAtUtc);

        var fiveHour = record.Windows.Single(window => window.Key == "five_hour");
        Assert.Equal(23.5, fiveHour.UsedPercent);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1738425600), fiveHour.ResetsAtUtc);

        var sevenDay = record.Windows.Single(window => window.Key == "seven_day");
        Assert.Equal(41.2, sevenDay.UsedPercent);
    }

    [Fact]
    public void KeepsZeroAsARealMeasurement()
    {
        var record = Parse("{\"rate_limits\":{\"five_hour\":{\"used_percentage\":0}}}");

        Assert.Equal(0, record!.Windows.Single().UsedPercent);
    }

    [Fact]
    public void LeavesAMissingPercentageNullInsteadOfZero()
    {
        var record = Parse("{\"rate_limits\":{\"five_hour\":{\"resets_at\":1738425600}}}");

        var window = record!.Windows.Single();
        Assert.Null(window.UsedPercent);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1738425600), window.ResetsAtUtc);
    }

    [Fact]
    public void KeepsSpendLimitAboveOneHundred()
    {
        var record = Parse("{\"rate_limits\":{\"spend_limit\":{\"used_percentage\":130}}}");

        Assert.Equal(130, record!.Windows.Single().UsedPercent);
    }

    [Fact]
    public void RejectsNegativePercentage()
    {
        var record = Parse("{\"rate_limits\":{\"five_hour\":{\"used_percentage\":-5}}}");

        Assert.Null(record!.Windows.Single().UsedPercent);
    }

    [Fact]
    public void KeepsEachWindowIndependently()
    {
        var record = Parse("{\"rate_limits\":{\"seven_day\":{\"used_percentage\":10}}}");

        Assert.Single(record!.Windows);
        Assert.Equal("seven_day", record.Windows[0].Key);
    }

    [Fact]
    public void ReturnsNullWhenRateLimitsIsAbsent()
    {
        Assert.Null(Parse("{\"model\":{\"display_name\":\"Opus\"},\"session_id\":\"abc\"}"));
    }

    [Fact]
    public void ReturnsNullWhenNoWindowHasAnything()
    {
        Assert.Null(Parse("{\"rate_limits\":{}}"));
    }

    [Fact]
    public void ReturnsNullForMalformedJson()
    {
        Assert.Null(Parse("{not json"));
        Assert.Null(Parse(string.Empty));
    }

    [Fact]
    public void HashesTheSessionIdAndNeverKeepsTheRawValue()
    {
        var first = Parse("{\"session_id\":\"session-alpha\",\"rate_limits\":{\"five_hour\":{\"used_percentage\":1}}}");
        var again = Parse("{\"session_id\":\"session-alpha\",\"rate_limits\":{\"five_hour\":{\"used_percentage\":1}}}");
        var other = Parse("{\"session_id\":\"session-beta\",\"rate_limits\":{\"five_hour\":{\"used_percentage\":1}}}");

        Assert.NotNull(first!.SessionHash);
        Assert.Equal(first.SessionHash, again!.SessionHash);
        Assert.NotEqual(first.SessionHash, other!.SessionHash);
        Assert.DoesNotContain("session-alpha", first.SessionHash, StringComparison.Ordinal);
    }

    [Fact]
    public void LeavesSessionHashNullWhenSessionIdIsAbsent()
    {
        var record = Parse("{\"rate_limits\":{\"five_hour\":{\"used_percentage\":1}}}");

        Assert.Null(record!.SessionHash);
    }

    [Fact]
    public void ReportsSchemaFactsWithoutValues()
    {
        ClaudeStatusLineParser.Parse(
            "{\"rate_limits\":{\"five_hour\":{\"used_percentage\":5},\"seven_day\":{\"used_percentage\":9}}}",
            Captured,
            out var withLimits);
        Assert.True(withLimits.JsonReadable);
        Assert.True(withLimits.HasRateLimits);
        Assert.Equal(new[] { "five_hour", "seven_day" }, withLimits.WindowKeys);

        ClaudeStatusLineParser.Parse("{\"model\":{\"display_name\":\"Opus\"}}", Captured, out var noLimits);
        Assert.True(noLimits.JsonReadable);
        Assert.False(noLimits.HasRateLimits);
        Assert.Empty(noLimits.WindowKeys);

        ClaudeStatusLineParser.Parse("{not json", Captured, out var broken);
        Assert.False(broken.JsonReadable);
        Assert.False(broken.HasRateLimits);
    }
}
