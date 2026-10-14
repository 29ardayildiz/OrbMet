using Microsoft.Win32;
using OrbMet.Core.Startup;

namespace OrbMet.Desktop.Infrastructure;

/// <summary>
/// The current user's Run key, and the StartupApproved value Task Manager
/// writes beside it when the user turns a startup app off or on. Per user, so
/// no administrator rights are needed.
/// </summary>
internal sealed class WindowsStartupList : IStartupList
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    public string? Get(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(name) as string;
    }

    public void Set(string name, string command)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        key.SetValue(name, command, RegistryValueKind.String);
    }

    public void Delete(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }

    // Task Manager stores 12 bytes; the first is even when on, odd when off.
    public bool TurnedOffInWindows(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(ApprovedKey);
        return key?.GetValue(name) is byte[] { Length: > 0 } state && (state[0] & 1) == 1;
    }

    public void ClearWindowsChoice(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(ApprovedKey, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }
}
