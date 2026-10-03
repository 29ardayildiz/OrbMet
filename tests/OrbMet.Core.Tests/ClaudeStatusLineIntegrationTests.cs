using System.Text.Json.Nodes;
using OrbMet.Core.Providers.Claude;

namespace OrbMet.Core.Tests;

public class ClaudeStatusLineIntegrationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    private static ClaudeStatusLineIntegration Create(TempDirectory temp) => new(
        settingsPath: temp.Combine("settings.json"),
        integrationRecordPath: temp.Combine("data", "integrations", "claude.json"),
        lockPath: temp.Combine("data", "integrations", "claude.lock"),
        backupsDirectory: temp.Combine("data", "backups"),
        captureExecutablePath: @"C:\OrbMet\OrbMet.Capture.exe",
        clock: () => Now);

    private static JsonObject Settings(TempDirectory temp) =>
        (JsonObject)JsonNode.Parse(File.ReadAllText(temp.Combine("settings.json")))!;

    private static void WriteSettings(TempDirectory temp, string json) =>
        File.WriteAllText(temp.Combine("settings.json"), json);

    [Fact]
    public void ConnectCreatesASettingsFileAndAnOwnershipRecord()
    {
        using var temp = new TempDirectory();
        var integration = Create(temp);

        var result = integration.Connect();

        Assert.Equal(ClaudeIntegrationStatus.Connected, result.Status);
        var statusLine = (JsonObject)Settings(temp)["statusLine"]!;
        Assert.Equal("command", statusLine["type"]!.GetValue<string>());
        Assert.Equal(
            "\"C:/OrbMet/OrbMet.Capture.exe\" --statusline",
            statusLine["command"]!.GetValue<string>());
        Assert.True(File.Exists(temp.Combine("data", "integrations", "claude.json")));
        Assert.True(integration.GetState().Connected);
    }

    [Fact]
    public void ConnectPreservesEveryOtherSetting()
    {
        using var temp = new TempDirectory();
        WriteSettings(temp, "{\"model\":\"opus\",\"permissions\":{\"allow\":[\"Bash\"]}}");

        Create(temp).Connect();

        var settings = Settings(temp);
        Assert.Equal("opus", settings["model"]!.GetValue<string>());
        Assert.Equal("Bash", settings["permissions"]!["allow"]![0]!.GetValue<string>());
    }

    [Fact]
    public void ConnectKeepsAnExistingStatusLineFieldsAndRestoresThemOnDisconnect()
    {
        using var temp = new TempDirectory();
        WriteSettings(temp, "{\"statusLine\":{\"type\":\"command\",\"command\":\"myline.sh\",\"padding\":2}}");
        var integration = Create(temp);

        integration.Connect();

        var connected = (JsonObject)Settings(temp)["statusLine"]!;
        Assert.Equal(2, connected["padding"]!.GetValue<int>());
        Assert.Contains("--statusline", connected["command"]!.GetValue<string>(), StringComparison.Ordinal);

        var result = integration.Disconnect();

        Assert.Equal(ClaudeIntegrationStatus.Disconnected, result.Status);
        var restored = (JsonObject)Settings(temp)["statusLine"]!;
        Assert.Equal("myline.sh", restored["command"]!.GetValue<string>());
        Assert.Equal(2, restored["padding"]!.GetValue<int>());
    }

    [Fact]
    public void ConnectRefusesToTouchASettingsFileItCannotParse()
    {
        using var temp = new TempDirectory();
        WriteSettings(temp, "{ potentially a comment or a half write");
        var before = File.ReadAllBytes(temp.Combine("settings.json"));

        var result = Create(temp).Connect();

        Assert.Equal(ClaudeIntegrationStatus.Unreadable, result.Status);
        Assert.Equal("settings-unreadable", result.ErrorCode);
        Assert.Equal(before, File.ReadAllBytes(temp.Combine("settings.json")));
    }

    [Fact]
    public void ConnectBacksUpTheOriginalFileOnce()
    {
        using var temp = new TempDirectory();
        WriteSettings(temp, "{\"model\":\"opus\"}");
        var original = File.ReadAllBytes(temp.Combine("settings.json"));
        var integration = Create(temp);

        integration.Connect();
        integration.Connect();

        var backup = temp.Combine("data", "backups", "claude-settings.json.orbmet-backup");
        Assert.True(File.Exists(backup));
        Assert.Equal(original, File.ReadAllBytes(backup));
    }

    [Fact]
    public void DisconnectRemovesOnlyTheStatusLineWhenThereWasNoPreviousOne()
    {
        using var temp = new TempDirectory();
        WriteSettings(temp, "{\"model\":\"opus\"}");
        var integration = Create(temp);
        integration.Connect();

        var result = integration.Disconnect();

        Assert.Equal(ClaudeIntegrationStatus.Disconnected, result.Status);
        var settings = Settings(temp);
        Assert.Null(settings["statusLine"]);
        Assert.Equal("opus", settings["model"]!.GetValue<string>());
        Assert.False(File.Exists(temp.Combine("data", "integrations", "claude.json")));
    }

    [Fact]
    public void DisconnectIsIdempotent()
    {
        using var temp = new TempDirectory();
        var integration = Create(temp);
        integration.Connect();
        integration.Disconnect();

        Assert.Equal(ClaudeIntegrationStatus.NotConnected, integration.Disconnect().Status);
    }

    [Fact]
    public void DisconnectLeavesAUserReplacedStatusLineAlone()
    {
        using var temp = new TempDirectory();
        WriteSettings(temp, "{\"statusLine\":{\"type\":\"command\",\"command\":\"myline.sh\"}}");
        var integration = Create(temp);
        integration.Connect();
        WriteSettings(temp, "{\"statusLine\":{\"type\":\"command\",\"command\":\"other.exe\"}}");
        var before = File.ReadAllBytes(temp.Combine("settings.json"));

        var result = integration.Disconnect();

        Assert.Equal(ClaudeIntegrationStatus.Conflict, result.Status);
        Assert.Equal("status-line-changed", result.ErrorCode);
        Assert.Equal(before, File.ReadAllBytes(temp.Combine("settings.json")));
    }

    [Fact]
    public void ReconnectingDoesNotLoseTheOriginalPreviousStatusLine()
    {
        using var temp = new TempDirectory();
        WriteSettings(temp, "{\"statusLine\":{\"type\":\"command\",\"command\":\"orig.sh\"}}");
        var integration = Create(temp);

        integration.Connect();
        integration.Connect();
        integration.Disconnect();

        var restored = (JsonObject)Settings(temp)["statusLine"]!;
        Assert.Equal("orig.sh", restored["command"]!.GetValue<string>());
    }

    [Fact]
    public void StateIsDisconnectedBeforeConnect()
    {
        using var temp = new TempDirectory();

        var state = Create(temp).GetState();

        Assert.False(state.Connected);
        Assert.True(state.SettingsReadable);
        Assert.Null(state.CurrentCommand);
    }
}
