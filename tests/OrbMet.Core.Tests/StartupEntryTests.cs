using OrbMet.Core.Startup;

namespace OrbMet.Core.Tests;

public class StartupEntryTests
{
    private const string Exe = @"C:\Apps\OrbMet\OrbMet.exe";

    [Fact]
    public void IsOffUntilTurnedOn()
    {
        var list = new FakeList();
        var entry = new StartupEntry(list, Exe);

        Assert.False(entry.IsOn);

        entry.TurnOn();

        Assert.True(entry.IsOn);
        Assert.Equal("\"C:\\Apps\\OrbMet\\OrbMet.exe\"", list.Values[StartupEntry.Name]);
    }

    [Fact]
    public void TurningOffRemovesTheEntry()
    {
        var list = new FakeList();
        var entry = new StartupEntry(list, Exe);
        entry.TurnOn();

        entry.TurnOff();

        Assert.False(entry.IsOn);
        Assert.Empty(list.Values);
    }

    [Fact]
    public void IsOffWhenTaskManagerTurnedItOff()
    {
        var list = new FakeList();
        var entry = new StartupEntry(list, Exe);
        entry.TurnOn();
        list.OffInWindows.Add(StartupEntry.Name);

        Assert.False(entry.IsOn);
    }

    [Fact]
    public void TurningOnAgainForgetsTheTaskManagerChoice()
    {
        var list = new FakeList();
        var entry = new StartupEntry(list, Exe);
        entry.TurnOn();
        list.OffInWindows.Add(StartupEntry.Name);

        entry.TurnOn();

        Assert.True(entry.IsOn);
    }

    [Fact]
    public void FollowsAMovedFolder()
    {
        var list = new FakeList();
        list.Values[StartupEntry.Name] = "\"D:\\Old\\OrbMet.exe\"";
        var entry = new StartupEntry(list, Exe, fileExists: _ => false);

        Assert.True(entry.FollowMove());

        Assert.Equal(entry.Command, list.Values[StartupEntry.Name]);
    }

    [Fact]
    public void LeavesAnotherCopyThatStillExists()
    {
        var list = new FakeList();
        list.Values[StartupEntry.Name] = "\"D:\\Other\\OrbMet.exe\"";
        var entry = new StartupEntry(list, Exe, fileExists: path => path == @"D:\Other\OrbMet.exe");

        Assert.False(entry.FollowMove());

        Assert.Equal("\"D:\\Other\\OrbMet.exe\"", list.Values[StartupEntry.Name]);
    }

    [Fact]
    public void FollowingDoesNothingWhenOff()
    {
        var list = new FakeList();
        var entry = new StartupEntry(list, Exe, fileExists: _ => false);

        Assert.False(entry.FollowMove());

        Assert.Empty(list.Values);
    }

    [Theory]
    [InlineData("\"C:\\A B\\OrbMet.exe\"", @"C:\A B\OrbMet.exe")]
    [InlineData("\"C:\\A B\\OrbMet.exe\" --flag", @"C:\A B\OrbMet.exe")]
    [InlineData(@"C:\Apps\OrbMet.exe", @"C:\Apps\OrbMet.exe")]
    [InlineData("\"", null)]
    [InlineData("  ", null)]
    public void ReadsTheProgramFromACommand(string command, string? expected)
    {
        Assert.Equal(expected, StartupEntry.ExePath(command));
    }

    private sealed class FakeList : IStartupList
    {
        public Dictionary<string, string> Values { get; } = new();

        public HashSet<string> OffInWindows { get; } = new();

        public string? Get(string name) => Values.GetValueOrDefault(name);

        public void Set(string name, string command) => Values[name] = command;

        public void Delete(string name) => Values.Remove(name);

        public bool TurnedOffInWindows(string name) => OffInWindows.Contains(name);

        public void ClearWindowsChoice(string name) => OffInWindows.Remove(name);
    }
}
