using System.Text;
using System.Text.Json.Nodes;
using OrbMet.Core.Providers.Claude;

namespace OrbMet.Core.Tests;

public class ClaudeStatusLineRunnerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    private const string Payload =
        "{\"session_id\":\"raw-session-secret\",\"cwd\":\"C:\\\\Users\\\\user\\\\secret\"," +
        "\"transcript_path\":\"C:\\\\Users\\\\user\\\\.claude\\\\transcript.jsonl\"," +
        "\"rate_limits\":{\"five_hour\":{\"used_percentage\":23,\"resets_at\":1738425600}," +
        "\"seven_day\":{\"used_percentage\":41,\"resets_at\":1738857600}}}";

    private static ClaudeStatusLineRunner BuildRunner(
        TempDirectory temp,
        IStatusLineChainRunner chain,
        out ClaudeIntegrationRecordStore integrationStore)
    {
        integrationStore = new ClaudeIntegrationRecordStore(
            temp.Combine("data", "integrations", "claude.json"));
        return new ClaudeStatusLineRunner(
            new ClaudeCaptureStore(temp.Combine("captures", "claude")),
            integrationStore,
            chain,
            () => Now,
            TimeSpan.FromSeconds(2));
    }

    private static async Task<string> RunAsync(ClaudeStatusLineRunner runner, string input)
    {
        using var output = new MemoryStream();
        await runner.RunAsync(Encoding.UTF8.GetBytes(input), output, CancellationToken.None);
        return Encoding.UTF8.GetString(output.ToArray());
    }

    private static void SetPreviousCommand(ClaudeIntegrationRecordStore store, string command) =>
        store.Write(new ClaudeIntegrationRecord
        {
            InstalledCommand = "\"C:\\OrbMet\\OrbMet.Capture.exe\" --statusline",
            HadPreviousStatusLine = true,
            PreviousStatusLine = JsonNode.Parse($"{{\"command\":{JsonValue.Create(command)!.ToJsonString()}}}"),
            InstalledAtUtc = Now,
        });

    [Fact]
    public async Task BanksTheUsageAndPrintsTheChainedOutput()
    {
        using var temp = new TempDirectory();
        var chain = new FakeChainRunner { Result = "my own status line" };
        var runner = BuildRunner(temp, chain, out var store);
        SetPreviousCommand(store, "myline.sh");

        var output = await RunAsync(runner, Payload);

        Assert.Equal("my own status line", output);
        Assert.Equal(1, chain.Calls);
        Assert.Equal("myline.sh", chain.LastCommand);
        Assert.Equal(Payload, Encoding.UTF8.GetString(chain.LastInput!));

        var capture = new ClaudeCaptureStore(temp.Combine("captures", "claude")).ReadNewest(Now);
        Assert.NotNull(capture);
        Assert.Equal(23, capture!.Windows.Single(window => window.Key == "five_hour").UsedPercent);
    }

    [Fact]
    public async Task PrintsADefaultLineWhenThereIsNoPreviousCommand()
    {
        using var temp = new TempDirectory();
        var chain = new FakeChainRunner();
        var runner = BuildRunner(temp, chain, out _);

        var output = await RunAsync(runner, Payload);

        Assert.Equal("5h 23%  ·  7d 41%", output);
        Assert.Equal(0, chain.Calls);
    }

    [Fact]
    public async Task NeverChainsToItself()
    {
        using var temp = new TempDirectory();
        var chain = new FakeChainRunner { Result = "should not run" };
        var runner = BuildRunner(temp, chain, out var store);
        SetPreviousCommand(store, "\"C:\\OrbMet\\OrbMet.Capture.exe\" --statusline");

        var output = await RunAsync(runner, Payload);

        Assert.Equal(0, chain.Calls);
        Assert.Equal("5h 23%  ·  7d 41%", output);
    }

    [Fact]
    public async Task FallsBackToTheDefaultLineWhenTheChainFails()
    {
        using var temp = new TempDirectory();
        var chain = new FakeChainRunner { Result = null };
        var runner = BuildRunner(temp, chain, out var store);
        SetPreviousCommand(store, "slow.sh");

        var output = await RunAsync(runner, Payload);

        Assert.Equal(1, chain.Calls);
        Assert.Equal("5h 23%  ·  7d 41%", output);
    }

    [Fact]
    public async Task StaysQuietAndSafeForMalformedInput()
    {
        using var temp = new TempDirectory();
        var chain = new FakeChainRunner();
        var runner = BuildRunner(temp, chain, out _);

        var output = await RunAsync(runner, "{ not json");

        Assert.Equal("OrbMet", output);
        Assert.Empty(new ClaudeCaptureStore(temp.Combine("captures", "claude")).ReadAll());
    }

    [Fact]
    public async Task StoresOnlyUsageAndNeverTheRawPayload()
    {
        using var temp = new TempDirectory();
        var runner = BuildRunner(temp, new FakeChainRunner(), out _);

        await RunAsync(runner, Payload);

        var directory = temp.Combine("captures", "claude");
        var text = string.Concat(Directory.EnumerateFiles(directory).Select(File.ReadAllText));
        Assert.Contains("five_hour", text, StringComparison.Ordinal);
        Assert.DoesNotContain("raw-session-secret", text, StringComparison.Ordinal);
        Assert.DoesNotContain("transcript", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Users", text, StringComparison.Ordinal);
    }

    private sealed class FakeChainRunner : IStatusLineChainRunner
    {
        public string? Result { get; init; } = "chained";

        public int Calls { get; private set; }

        public string? LastCommand { get; private set; }

        public byte[]? LastInput { get; private set; }

        public Task<string?> RunAsync(
            string command,
            byte[] input,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            Calls++;
            LastCommand = command;
            LastInput = input;
            return Task.FromResult(Result);
        }
    }
}
