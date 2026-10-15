namespace OrbMet.Core.Startup;

/// <summary>
/// Where Windows keeps the programs a user starts at sign-in: the per-user
/// Run list, and the switch Task Manager's Startup apps tab keeps beside it.
/// </summary>
public interface IStartupList
{
    string? Get(string name);

    void Set(string name, string command);

    void Delete(string name);

    /// <summary>Whether the user turned the entry off in Task Manager.</summary>
    bool TurnedOffInWindows(string name);

    /// <summary>Forgets a Task Manager choice, so the entry runs again.</summary>
    void ClearWindowsChoice(string name);
}

/// <summary>
/// OrbMet's own "Start with Windows" entry. Off unless the user turns it on;
/// it starts this copy of OrbMet.exe. When the copy it names has been moved
/// away, the entry follows this one; a copy that still exists elsewhere is
/// left alone, as it may be the one the user wants.
/// </summary>
public sealed class StartupEntry
{
    public const string Name = "OrbMet";

    private readonly IStartupList _list;
    private readonly string _exePath;
    private readonly Func<string, bool> _fileExists;

    public StartupEntry(IStartupList list, string exePath, Func<string, bool>? fileExists = null)
    {
        _list = list ?? throw new ArgumentNullException(nameof(list));
        _exePath = string.IsNullOrWhiteSpace(exePath)
            ? throw new ArgumentException("The executable path is required.", nameof(exePath))
            : exePath;
        _fileExists = fileExists ?? File.Exists;
    }

    public string Command => Quote(_exePath);

    /// <summary>On when the entry exists and Task Manager has not turned it off.</summary>
    public bool IsOn => _list.Get(Name) is not null && !_list.TurnedOffInWindows(Name);

    public void TurnOn()
    {
        _list.Set(Name, Command);
        _list.ClearWindowsChoice(Name);
    }

    public void TurnOff()
    {
        _list.Delete(Name);
        _list.ClearWindowsChoice(Name);
    }

    /// <summary>
    /// Points the entry at this copy when the one it names is gone. Returns
    /// whether it changed anything.
    /// </summary>
    public bool FollowMove()
    {
        if (_list.Get(Name) is not { } command || string.Equals(command, Command, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (ExePath(command) is { } named && _fileExists(named))
        {
            return false;
        }

        _list.Set(Name, Command);
        return true;
    }

    /// <summary>
    /// Removes the entry when it starts this copy, or a copy that is gone;
    /// for an uninstall. Another copy that still exists keeps its entry.
    /// Returns whether it removed anything.
    /// </summary>
    public bool RemoveIfThisCopy()
    {
        if (_list.Get(Name) is not { } command)
        {
            return false;
        }

        var named = ExePath(command);
        var thisCopy = named is not null && string.Equals(
            Path.GetFullPath(named), Path.GetFullPath(_exePath), StringComparison.OrdinalIgnoreCase);
        if (!thisCopy && named is not null && _fileExists(named))
        {
            return false;
        }

        TurnOff();
        return true;
    }

    /// <summary>The program a Run command starts: the quoted path, or the whole command.</summary>
    public static string? ExePath(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        var text = command.Trim();
        if (text.StartsWith('"'))
        {
            var end = text.IndexOf('"', 1);
            return end > 1 ? text[1..end] : null;
        }

        return text;
    }

    private static string Quote(string path) => $"\"{path}\"";
}
