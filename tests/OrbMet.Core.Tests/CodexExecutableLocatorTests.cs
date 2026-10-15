using OrbMet.Core.Providers.Codex;

namespace OrbMet.Core.Tests;

public class CodexExecutableLocatorTests
{
    private static Func<string, bool> Exists(params string[] files) =>
        path => files.Contains(path, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void FindsCodexOnThisProcesssPath()
    {
        var found = CodexExecutableLocator.Locate(
            null,
            new[] { @"C:\Tools;C:\Codex\bin" },
            Exists(@"C:\Codex\bin\codex.exe"));

        Assert.Equal(@"C:\Codex\bin\codex.exe", found);
    }

    [Fact]
    public void FallsBackToTheSavedPathWhenThisProcesssPathIsOld()
    {
        // Started by an installer or at sign-in: the process PATH predates Codex.
        var found = CodexExecutableLocator.Locate(
            null,
            new[] { @"C:\Windows\System32", @"C:\Users\me\AppData\Local\Programs\OpenAI\Codex\bin", null },
            Exists(@"C:\Users\me\AppData\Local\Programs\OpenAI\Codex\bin\codex.exe"));

        Assert.Equal(@"C:\Users\me\AppData\Local\Programs\OpenAI\Codex\bin\codex.exe", found);
    }

    [Fact]
    public void ThisProcesssPathComesFirst()
    {
        var found = CodexExecutableLocator.Locate(
            null,
            new[] { @"C:\First", @"C:\Second" },
            Exists(@"C:\First\codex.cmd", @"C:\Second\codex.exe"));

        Assert.Equal(@"C:\First\codex.cmd", found);
    }

    [Fact]
    public void ExpandsVariablesInASavedPath()
    {
        var root = Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";

        var found = CodexExecutableLocator.Locate(
            null,
            new[] { "%SystemRoot%\\codexdir" },
            Exists(Path.Combine(root, "codexdir", "codex.exe")));

        Assert.Equal(Path.Combine(root, "codexdir", "codex.exe"), found);
    }

    [Fact]
    public void AConfiguredPathWins()
    {
        var found = CodexExecutableLocator.Locate(
            @"D:\custom\codex.exe",
            new[] { @"C:\Codex" },
            Exists(@"D:\custom\codex.exe", @"C:\Codex\codex.exe"));

        Assert.Equal(@"D:\custom\codex.exe", found);
    }

    [Fact]
    public void ReturnsNullWhenCodexIsNowhere()
    {
        Assert.Null(CodexExecutableLocator.Locate(null, new[] { @"C:\A", null, "" }, Exists()));
    }
}
