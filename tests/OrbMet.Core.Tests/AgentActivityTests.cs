using OrbMet.Core.Models;
using OrbMet.Core.Usage;
using static OrbMet.Core.Usage.AgentActivity;

namespace OrbMet.Core.Tests;

public class AgentActivityTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    private static string Stamp(TimeSpan ago) => (Now - ago).ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

    private static string ClaudeAssistant(string? stop, TimeSpan ago) => stop is null
        ? $$$"""{"type":"assistant","timestamp":"{{{Stamp(ago)}}}","message":{"content":[]}}"""
        : $$$"""{"type":"assistant","timestamp":"{{{Stamp(ago)}}}","message":{"stop_reason":"{{{stop}}}","content":[]}}""";

    private static string ClaudeUser(string text, TimeSpan ago) =>
        $$$"""{"type":"user","timestamp":"{{{Stamp(ago)}}}","message":{"content":[{"type":"text","text":"{{{text}}}"}]}}""";

    private static string Codex(string type, TimeSpan ago) =>
        $$$"""{"timestamp":"{{{Stamp(ago)}}}","type":"event_msg","payload":{"type":"{{{type}}}"}}""";

    private const string Bookkeeping = """{"type":"summary","summary":"x"}""";

    [Fact]
    public void AClaudeTurnHandedToAToolIsWorkingOnTheToolsGrace()
    {
        var verdict = VerdictFor(ProviderId.ClaudeCode, new[] { ClaudeAssistant("tool_use", TimeSpan.FromSeconds(5)), Bookkeeping });

        Assert.Equal(Wait.Tool, Assert.IsType<Verdict.Working>(verdict).Wait);
    }

    [Fact]
    public void AClaudeTurnThatEndedIsFinished()
    {
        Assert.IsType<Verdict.Finished>(VerdictFor(ProviderId.ClaudeCode, new[] { ClaudeAssistant("end_turn", TimeSpan.Zero) }));
    }

    [Fact]
    public void APromptOrAToolResultIsTheModelsMove()
    {
        var verdict = VerdictFor(ProviderId.ClaudeCode, new[] { ClaudeAssistant("end_turn", TimeSpan.FromMinutes(1)), ClaudeUser("a prompt", TimeSpan.Zero) });

        Assert.Equal(Wait.Model, Assert.IsType<Verdict.Working>(verdict).Wait);
    }

    [Fact]
    public void AnInterruptedClaudeTurnIsFinished()
    {
        var verdict = VerdictFor(ProviderId.ClaudeCode, new[] { ClaudeUser("[Request interrupted by user]", TimeSpan.Zero) });

        Assert.IsType<Verdict.Finished>(verdict);
    }

    [Theory]
    [InlineData("task_started", typeof(Verdict.Working))]
    [InlineData("function_call", typeof(Verdict.Working))]
    [InlineData("function_call_output", typeof(Verdict.Working))]
    [InlineData("task_complete", typeof(Verdict.Finished))]
    [InlineData("turn_aborted", typeof(Verdict.Finished))]
    public void CodexTurnsAreReadFromTheirEvents(string type, Type expected)
    {
        Assert.IsType(expected, VerdictFor(ProviderId.Codex, new[] { Codex(type, TimeSpan.Zero) }));
    }

    [Fact]
    public void UnrecognisedOrBrokenLinesSayNothing()
    {
        Assert.IsType<Verdict.Unknown>(VerdictFor(ProviderId.ClaudeCode, new[] { "not json", Bookkeeping, "[]" }));
    }

    [Fact]
    public void AModelWaitPastItsGraceIsADeadSession()
    {
        using var temp = new TempDirectory();
        var file = Path.Combine(temp.Path, "session.jsonl");
        File.WriteAllLines(file, new[] { ClaudeUser("a prompt", TimeSpan.FromMinutes(2)) });
        File.SetLastWriteTimeUtc(file, Now.UtcDateTime);

        Assert.False(IsWorking(ProviderId.ClaudeCode, temp.Path, Now));
    }

    [Fact]
    public void AToolMayRunForMinutesWithoutWriting()
    {
        using var temp = new TempDirectory();
        var file = Path.Combine(temp.Path, "project", "session.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllLines(file, new[] { ClaudeAssistant("tool_use", TimeSpan.FromMinutes(4)) });
        File.SetLastWriteTimeUtc(file, (Now - TimeSpan.FromMinutes(4)).UtcDateTime);

        Assert.True(IsWorking(ProviderId.ClaudeCode, temp.Path, Now));
    }

    [Fact]
    public void AnyOfSeveralSessionsCounts()
    {
        using var temp = new TempDirectory();
        var done = Path.Combine(temp.Path, "a.jsonl");
        var busy = Path.Combine(temp.Path, "b.jsonl");
        File.WriteAllLines(done, new[] { Codex("task_complete", TimeSpan.Zero) });
        File.WriteAllLines(busy, new[] { Codex("task_started", TimeSpan.FromSeconds(10)) });
        File.SetLastWriteTimeUtc(done, Now.UtcDateTime);
        File.SetLastWriteTimeUtc(busy, (Now - TimeSpan.FromSeconds(10)).UtcDateTime);

        Assert.True(IsWorking(ProviderId.Codex, temp.Path, Now));
    }

    [Fact]
    public void OldFilesAndAMissingFolderAreNotWork()
    {
        using var temp = new TempDirectory();
        var file = Path.Combine(temp.Path, "old.jsonl");
        File.WriteAllLines(file, new[] { Codex("task_started", TimeSpan.FromMinutes(10)) });
        File.SetLastWriteTimeUtc(file, (Now - TimeSpan.FromMinutes(10)).UtcDateTime);

        Assert.False(IsWorking(ProviderId.Codex, temp.Path, Now));
        Assert.False(IsWorking(ProviderId.Codex, Path.Combine(temp.Path, "missing"), Now));
    }

    [Fact]
    public void OnlyTheTailOfALargeFileIsRead()
    {
        using var temp = new TempDirectory();
        var file = Path.Combine(temp.Path, "big.jsonl");
        var filler = Enumerable.Repeat(Bookkeeping, 8000);
        File.WriteAllLines(file, filler.Prepend(Codex("task_started", TimeSpan.Zero)).Append(Codex("task_complete", TimeSpan.Zero)));
        File.SetLastWriteTimeUtc(file, Now.UtcDateTime);

        Assert.True(new FileInfo(file).Length > 128 * 1024);
        Assert.False(IsWorking(ProviderId.Codex, temp.Path, Now));
    }
}
