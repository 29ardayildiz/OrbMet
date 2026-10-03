using System.Text.Json;
using System.Text.Json.Nodes;
using OrbMet.Core.Storage;

namespace OrbMet.Core.Providers.Claude;

public enum ClaudeIntegrationStatus
{
    Connected,
    Disconnected,
    NotConnected,
    Conflict,
    Unreadable,
}

/// <summary>Outcome of a connect or disconnect attempt, with a safe error code.</summary>
public sealed record ClaudeIntegrationResult(
    ClaudeIntegrationStatus Status,
    string? ErrorCode = null,
    string? Command = null);

/// <summary>Current view of the connection, for the settings surface to show.</summary>
public sealed record ClaudeIntegrationState(
    bool SettingsReadable,
    bool Connected,
    string? CurrentCommand,
    string? ErrorCode);

/// <summary>
/// Installs and removes OrbMet's Claude Code status line. It preserves every
/// other settings field, keeps the previous statusLine object for an exact
/// restore, refuses to touch a settings file it cannot parse, and re-reads the
/// file before writing so a concurrent user edit is never overwritten.
///
/// This is a visible user action, never a build or startup side effect.
/// </summary>
public sealed class ClaudeStatusLineIntegration
{
    public const string CaptureArgument = "--statusline";

    private static readonly JsonSerializerOptions WriteOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private static readonly JsonDocumentOptions StrictDocument = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
    };

    private readonly string _settingsPath;
    private readonly string _backupsDirectory;
    private readonly string _captureExecutablePath;
    private readonly string _captureArguments;
    private readonly ClaudeIntegrationRecordStore _recordStore;
    private readonly CrossProcessFileLockFactory _lockFactory;
    private readonly Func<DateTimeOffset> _clock;

    public ClaudeStatusLineIntegration(
        string settingsPath,
        string integrationRecordPath,
        string lockPath,
        string backupsDirectory,
        string captureExecutablePath,
        string captureArguments = CaptureArgument,
        Func<DateTimeOffset>? clock = null,
        TimeSpan? lockTimeout = null)
    {
        _settingsPath = string.IsNullOrWhiteSpace(settingsPath)
            ? throw new ArgumentException("A settings path is required.", nameof(settingsPath))
            : settingsPath;
        _backupsDirectory = string.IsNullOrWhiteSpace(backupsDirectory)
            ? throw new ArgumentException("A backups directory is required.", nameof(backupsDirectory))
            : backupsDirectory;
        _captureExecutablePath = string.IsNullOrWhiteSpace(captureExecutablePath)
            ? throw new ArgumentException("A capture executable path is required.", nameof(captureExecutablePath))
            : captureExecutablePath;
        _captureArguments = captureArguments;
        _recordStore = new ClaudeIntegrationRecordStore(integrationRecordPath);
        _lockFactory = new CrossProcessFileLockFactory(lockPath, lockTimeout ?? TimeSpan.FromSeconds(5));
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public static ClaudeStatusLineIntegration FromPaths(
        OrbMetPaths paths,
        string captureExecutablePath,
        Func<DateTimeOffset>? clock = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return new ClaudeStatusLineIntegration(
            paths.ClaudeSettingsFile
                ?? throw new InvalidOperationException("The Claude settings path could not be resolved."),
            paths.ClaudeIntegrationFile,
            paths.ClaudeLockFile,
            paths.BackupsDirectory,
            captureExecutablePath,
            clock: clock);
    }

    public string SettingsPath => _settingsPath;

    public ClaudeIntegrationState GetState()
    {
        var bytes = ReadSettingsBytes();
        if (bytes is null)
        {
            return new ClaudeIntegrationState(SettingsReadable: true, Connected: false, CurrentCommand: null, ErrorCode: null);
        }

        if (!TryParseSettings(bytes, out var settings))
        {
            return new ClaudeIntegrationState(SettingsReadable: false, Connected: false, CurrentCommand: null, ErrorCode: "settings-unreadable");
        }

        var record = _recordStore.Read();
        var command = ReadCommand(settings);
        var connected = record is not null &&
                        command is not null &&
                        (string.Equals(command, record.InstalledCommand, StringComparison.Ordinal) ||
                         IsOrbMetCommand(command));

        return new ClaudeIntegrationState(SettingsReadable: true, Connected: connected, CurrentCommand: command, ErrorCode: null);
    }

    public ClaudeIntegrationResult Connect()
    {
        using var fileLock = _lockFactory.Acquire();

        var existingBytes = ReadSettingsBytes();

        JsonObject settings;
        if (existingBytes is not null)
        {
            if (!TryParseSettings(existingBytes, out settings))
            {
                return new ClaudeIntegrationResult(ClaudeIntegrationStatus.Unreadable, "settings-unreadable");
            }
        }
        else
        {
            settings = new JsonObject();
        }

        var record = _recordStore.Read();
        if (record is null && existingBytes is not null)
        {
            WriteFirstBackup(existingBytes);
        }

        var previous = record is not null
            ? record.PreviousStatusLine?.DeepClone()
            : settings["statusLine"]?.DeepClone();
        var hadPrevious = record?.HadPreviousStatusLine ?? settings.ContainsKey("statusLine");

        var command = BuildCommand();
        // A second clone: the instance kept in the ownership record must stay
        // byte-for-byte the user's original, not a copy we then edit.
        var statusLine = previous is JsonObject previousObject
            ? (JsonObject)previousObject.DeepClone()
            : new JsonObject();
        statusLine["type"] = "command";
        statusLine["command"] = command;
        settings["statusLine"] = statusLine;

        var newBytes = SerializeSettings(settings);
        if (!UnchangedSince(existingBytes))
        {
            return new ClaudeIntegrationResult(ClaudeIntegrationStatus.Conflict, "settings-changed");
        }

        AtomicFile.WriteAllBytes(_settingsPath, newBytes);
        _recordStore.Write(new ClaudeIntegrationRecord
        {
            InstalledCommand = command,
            HadPreviousStatusLine = hadPrevious,
            PreviousStatusLine = previous,
            SettingsPath = _settingsPath,
            InstalledAtUtc = _clock(),
        });

        return new ClaudeIntegrationResult(ClaudeIntegrationStatus.Connected, Command: command);
    }

    public ClaudeIntegrationResult Disconnect()
    {
        using var fileLock = _lockFactory.Acquire();

        var record = _recordStore.Read();
        if (record is null)
        {
            return new ClaudeIntegrationResult(ClaudeIntegrationStatus.NotConnected);
        }

        var bytes = ReadSettingsBytes();
        if (bytes is null)
        {
            return new ClaudeIntegrationResult(ClaudeIntegrationStatus.Unreadable, "settings-missing");
        }

        if (!TryParseSettings(bytes, out var settings))
        {
            return new ClaudeIntegrationResult(ClaudeIntegrationStatus.Unreadable, "settings-unreadable");
        }

        var currentCommand = ReadCommand(settings);
        if (!string.Equals(currentCommand, record.InstalledCommand, StringComparison.Ordinal) &&
            !IsOrbMetCommand(currentCommand))
        {
            return new ClaudeIntegrationResult(ClaudeIntegrationStatus.Conflict, "status-line-changed");
        }

        if (record.HadPreviousStatusLine && record.PreviousStatusLine is not null)
        {
            settings["statusLine"] = record.PreviousStatusLine.DeepClone();
        }
        else
        {
            settings.Remove("statusLine");
        }

        var newBytes = SerializeSettings(settings);
        if (!UnchangedSince(bytes))
        {
            return new ClaudeIntegrationResult(ClaudeIntegrationStatus.Conflict, "settings-changed");
        }

        AtomicFile.WriteAllBytes(_settingsPath, newBytes);
        _recordStore.Delete();
        return new ClaudeIntegrationResult(ClaudeIntegrationStatus.Disconnected);
    }

    /// <summary>True when a command string belongs to OrbMet's capture helper.</summary>
    public static bool IsOrbMetCommand(string? command) =>
        command is not null && command.Contains(CaptureArgument, StringComparison.Ordinal);

    private string BuildCommand()
    {
        // Claude Code runs status line commands through Git Bash when it is
        // installed, and Git Bash eats unquoted backslashes in a Windows path.
        // The documented fix is forward slashes, which also work in PowerShell.
        var executable = _captureExecutablePath.Replace('\\', '/');
        return string.IsNullOrWhiteSpace(_captureArguments)
            ? $"\"{executable}\""
            : $"\"{executable}\" {_captureArguments}";
    }

    private bool UnchangedSince(byte[]? original)
    {
        var current = ReadSettingsBytes();
        if (original is null)
        {
            return current is null;
        }

        return current is not null && current.AsSpan().SequenceEqual(original);
    }

    private byte[]? ReadSettingsBytes()
    {
        try
        {
            return File.Exists(_settingsPath) ? File.ReadAllBytes(_settingsPath) : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void WriteFirstBackup(byte[] original)
    {
        var path = Path.Combine(_backupsDirectory, "claude-settings.json.orbmet-backup");
        if (File.Exists(path))
        {
            return;
        }

        Directory.CreateDirectory(_backupsDirectory);
        AtomicFile.WriteAllBytes(path, original);
    }

    private static bool TryParseSettings(byte[] bytes, out JsonObject settings)
    {
        settings = new JsonObject();
        if (bytes.Length == 0)
        {
            return true;
        }

        try
        {
            var node = JsonNode.Parse(bytes, nodeOptions: null, documentOptions: StrictDocument);
            if (node is JsonObject obj)
            {
                settings = obj;
                return true;
            }

            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? ReadCommand(JsonObject settings)
    {
        if (settings["statusLine"] is not JsonObject statusLine)
        {
            return null;
        }

        return statusLine["command"] is JsonValue value && value.TryGetValue<string>(out var text)
            ? text
            : null;
    }

    private static byte[] SerializeSettings(JsonObject settings) =>
        new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
            .GetBytes(settings.ToJsonString(WriteOptions));

    /// <summary>Creates the lock on demand, so a failed acquire surfaces as an IOException.</summary>
    private sealed class CrossProcessFileLockFactory
    {
        private readonly string _path;
        private readonly TimeSpan _timeout;

        public CrossProcessFileLockFactory(string path, TimeSpan timeout)
        {
            _path = string.IsNullOrWhiteSpace(path)
                ? throw new ArgumentException("A lock path is required.", nameof(path))
                : path;
            _timeout = timeout;
        }

        public IDisposable Acquire() => CrossProcessFileLock.Acquire(_path, _timeout);
    }
}
