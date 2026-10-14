using OrbMet.Core.Providers.Claude;

namespace OrbMet.Core.Tests;

public class ClaudeCaptureStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    private static ClaudeCaptureRecord Record(
        string? session,
        DateTimeOffset capturedAt,
        double percent = 42,
        DateTimeOffset? resetsAt = null) => new()
    {
        CapturedAtUtc = capturedAt,
        SessionHash = session,
        Windows = new[]
        {
            new ClaudeCaptureWindow
            {
                Key = "five_hour",
                UsedPercent = percent,
                ResetsAtUtc = resetsAt ?? capturedAt.AddHours(5),
            },
        },
    };

    [Fact]
    public void RoundTripsACapture()
    {
        using var temp = new TempDirectory();
        var store = new ClaudeCaptureStore(temp.Path);

        store.Save(Record("aaa", Now, percent: 23.5));

        var read = store.ReadNewest(Now);
        Assert.NotNull(read);
        Assert.Equal("aaa", read!.SessionHash);
        Assert.Equal(23.5, read.Windows.Single().UsedPercent);
    }

    [Fact]
    public void ReadsAnEmptyDirectoryAsNoRecord()
    {
        using var temp = new TempDirectory();
        var store = new ClaudeCaptureStore(temp.Combine("missing"));

        Assert.Null(store.ReadNewest(Now));
        Assert.Empty(store.ReadAll());
    }

    [Fact]
    public void PrefersTheNewestSession()
    {
        using var temp = new TempDirectory();
        var store = new ClaudeCaptureStore(temp.Path);

        store.Save(Record("older", Now.AddMinutes(-5), percent: 10));
        store.Save(Record("newer", Now.AddMinutes(-1), percent: 20));

        Assert.Equal("newer", store.ReadNewest(Now)!.SessionHash);
    }

    [Fact]
    public void IgnoresACorruptFile()
    {
        using var temp = new TempDirectory();
        var store = new ClaudeCaptureStore(temp.Path);
        Directory.CreateDirectory(temp.Path);
        File.WriteAllText(Path.Combine(temp.Path, "claude-broken.json"), "{ half-written");

        store.Save(Record("good", Now, percent: 55));

        Assert.Equal(55, store.ReadNewest(Now)!.Windows.Single().UsedPercent);
        Assert.Single(store.ReadAll());
    }

    [Fact]
    public void PrefersAnOlderCurrentWindowOverANewerExpiredOne()
    {
        using var temp = new TempDirectory();
        var store = new ClaudeCaptureStore(temp.Path);

        // Older, but its window still resets in the future.
        store.Save(Record("still-valid", Now.AddMinutes(-20), percent: 30, resetsAt: Now.AddHours(1)));
        // Newer, but its window already reset.
        store.Save(Record("expired", Now.AddMinutes(-1), percent: 90, resetsAt: Now.AddMinutes(-1)));

        var read = store.ReadNewest(Now);
        Assert.Equal("still-valid", read!.SessionHash);
    }

    [Fact]
    public void FallsBackToTheNewestRecordWhenEverythingHasExpired()
    {
        using var temp = new TempDirectory();
        var store = new ClaudeCaptureStore(temp.Path);

        store.Save(Record("expired", Now.AddMinutes(-1), percent: 90, resetsAt: Now.AddMinutes(-10)));

        var read = store.ReadNewest(Now);
        Assert.Equal("expired", read!.SessionHash);
    }

    [Fact]
    public void SurvivesConcurrentWritesToTheSameAndDifferentSessions()
    {
        using var temp = new TempDirectory();
        var store = new ClaudeCaptureStore(temp.Path);

        Parallel.For(0, 60, index =>
        {
            var session = index % 3 == 0 ? "shared" : $"session-{index}";
            store.Save(Record(session, Now.AddSeconds(index), percent: index));
        });

        // Every remaining file must parse; no half-written file may survive.
        var all = store.ReadAll();
        Assert.NotEmpty(all);
        Assert.DoesNotContain(
            Directory.EnumerateFiles(temp.Path),
            file => Path.GetFileName(file).Contains(".tmp-", StringComparison.Ordinal));

        Assert.NotNull(store.ReadNewest(Now));
    }

    [Fact]
    public void RemovesOnlyOldLeftoverTemporaryFiles()
    {
        using var temp = new TempDirectory();
        var store = new ClaudeCaptureStore(temp.Path);
        store.Save(Record("good", Now, percent: 55));

        var old = Path.Combine(temp.Path, "claude-good.json.tmp-old");
        var inProgress = Path.Combine(temp.Path, "claude-good.json.tmp-new");
        File.WriteAllText(old, "{}");
        File.WriteAllText(inProgress, "{}");
        File.SetLastWriteTimeUtc(old, Now.AddHours(-2).UtcDateTime);
        File.SetLastWriteTimeUtc(inProgress, Now.AddMinutes(-1).UtcDateTime);

        Assert.Equal(1, store.RemoveLeftovers(Now, TimeSpan.FromHours(1)));
        Assert.False(File.Exists(old));
        Assert.True(File.Exists(inProgress));
        Assert.Equal(55, store.ReadNewest(Now)!.Windows.Single().UsedPercent);
    }

    [Fact]
    public void RemovingLeftoversFromAMissingDirectoryDoesNothing()
    {
        using var temp = new TempDirectory();
        var store = new ClaudeCaptureStore(Path.Combine(temp.Path, "absent"));

        Assert.Equal(0, store.RemoveLeftovers(Now, TimeSpan.FromHours(1)));
    }
}
