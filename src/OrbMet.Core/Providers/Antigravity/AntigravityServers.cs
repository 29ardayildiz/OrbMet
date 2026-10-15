using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace OrbMet.Core.Providers.Antigravity;

/// <summary>
/// One running Antigravity language server: where it listens on loopback and
/// the per-launch code it expects back. The code is never printed.
/// </summary>
public sealed record AntigravityServer(int ProcessId, bool IsIde, IReadOnlyList<int> Ports, string CsrfToken)
{
    public override string ToString() => $"AntigravityServer {{ ProcessId = {ProcessId}, IsIde = {IsIde}, Ports = {Ports.Count} }}";
}

/// <summary>
/// Finds Antigravity's language servers (design.md 4.4, master.md). Antigravity
/// starts a <c>language_server</c> of its own with a <c>--csrf_token</c> on its
/// command line and takes free loopback ports, so none of the three can be
/// assumed: the process is matched by name and by an install path containing
/// "Antigravity" (other Codeium editors ship the same binary), its ports are
/// the ones Windows lists it listening on at 127.0.0.1, and the code is read
/// from its command line. Measured 2026-10-15: Antigravity 2.8.1 runs
/// <c>language_server.exe</c>, Antigravity IDE 2.5.5 two
/// <c>language_server_windows_x64.exe</c>, each with one HTTP and one HTTPS port.
/// </summary>
public static partial class AntigravityServers
{
    /// <summary>The servers running now, the app's before the IDE's; empty when none is.</summary>
    public static IReadOnlyList<AntigravityServer> Find()
    {
        if (!OperatingSystem.IsWindows())
        {
            return Array.Empty<AntigravityServer>();
        }

        Dictionary<int, List<int>> listening;
        try
        {
            listening = LoopbackListeners();
        }
        catch (Win32Exception)
        {
            return Array.Empty<AntigravityServer>();
        }

        var found = new List<AntigravityServer>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                if (!process.ProcessName.StartsWith("language_server", StringComparison.OrdinalIgnoreCase) ||
                    !listening.TryGetValue(process.Id, out var ports) ||
                    Inspect(process.Id) is not { } details ||
                    !details.Path.Contains("antigravity", StringComparison.OrdinalIgnoreCase) ||
                    ReadCsrfToken(details.CommandLine) is not { } token)
                {
                    continue;
                }

                var isIde = details.Path.Contains("Antigravity IDE", StringComparison.OrdinalIgnoreCase);
                found.Add(new AntigravityServer(process.Id, isIde, ports.Order().ToList(), token));
            }
        }

        return found.OrderBy(server => server.IsIde).ThenBy(server => server.ProcessId).ToList();
    }

    /// <summary>Whether Antigravity or Antigravity IDE is installed for this user; only files are checked.</summary>
    public static bool Installed()
    {
        var programs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs");
        return File.Exists(Path.Combine(programs, "Antigravity", "Antigravity.exe")) ||
               File.Exists(Path.Combine(programs, "Antigravity IDE", "Antigravity IDE.exe"));
    }

    /// <summary>The value of <c>--csrf_token</c>, not of <c>--extension_server_csrf_token</c>; null when absent.</summary>
    internal static string? ReadCsrfToken(string commandLine) =>
        CsrfToken().Match(commandLine) is { Success: true } match ? match.Groups[1].Value : null;

    [GeneratedRegex(@"(?:^|\s)--csrf_token(?:=|\s+)""?([^\s""]+)")]
    private static partial Regex CsrfToken();

    /// <summary>The process's image path and command line, or null when it cannot be opened.</summary>
    private static (string Path, string CommandLine)? Inspect(int processId)
    {
        var handle = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var path = new StringBuilder(1024);
            var length = path.Capacity;
            if (!QueryFullProcessImageName(handle, 0, path, ref length))
            {
                return null;
            }

            return CommandLine(handle) is { } commandLine ? (path.ToString(), commandLine) : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    /// <summary>
    /// The command line through ProcessCommandLineInformation, which needs only
    /// limited query rights: a UNICODE_STRING whose text follows it in the buffer.
    /// </summary>
    private static string? CommandLine(IntPtr handle)
    {
        var size = 4096;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                var status = NtQueryInformationProcess(handle, ProcessCommandLineInformation, buffer, size, out var needed);
                if (status == StatusInfoLengthMismatch || status == StatusBufferTooSmall)
                {
                    size = Math.Max(needed, size * 2);
                    continue;
                }

                if (status != 0)
                {
                    return null;
                }

                var bytes = (ushort)Marshal.ReadInt16(buffer);
                var text = Marshal.ReadIntPtr(buffer, IntPtr.Size);
                return bytes == 0 || text == IntPtr.Zero ? null : Marshal.PtrToStringUni(text, bytes / 2);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        return null;
    }

    /// <summary>Each process's TCP ports listening on 127.0.0.1, from the IPv4 owner table.</summary>
    private static Dictionary<int, List<int>> LoopbackListeners()
    {
        var size = 0;
        _ = GetExtendedTcpTable(IntPtr.Zero, ref size, false, AfInet, TcpTableOwnerPidListener, 0);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                var result = GetExtendedTcpTable(buffer, ref size, false, AfInet, TcpTableOwnerPidListener, 0);
                if (result == ErrorInsufficientBuffer)
                {
                    continue;
                }

                if (result != 0)
                {
                    throw new Win32Exception((int)result);
                }

                // DWORD count, then rows of six DWORDs: state, local address,
                // local port, remote address, remote port, owning process.
                var listening = new Dictionary<int, List<int>>();
                var count = Marshal.ReadInt32(buffer);
                for (var row = 0; row < count; row++)
                {
                    var offset = 4 + row * 24;
                    var address = (uint)Marshal.ReadInt32(buffer, offset + 4);
                    if (address != Loopback)
                    {
                        continue;
                    }

                    var rawPort = (uint)Marshal.ReadInt32(buffer, offset + 8);
                    var port = (int)(((rawPort & 0xFF) << 8) | ((rawPort >> 8) & 0xFF));
                    var owner = Marshal.ReadInt32(buffer, offset + 20);
                    if (!listening.TryGetValue(owner, out var ports))
                    {
                        listening[owner] = ports = new List<int>();
                    }

                    ports.Add(port);
                }

                return listening;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        throw new Win32Exception((int)ErrorInsufficientBuffer);
    }

    private const int ProcessQueryLimitedInformation = 0x1000;
    private const int ProcessCommandLineInformation = 60;
    private const int StatusInfoLengthMismatch = unchecked((int)0xC0000004);
    private const int StatusBufferTooSmall = unchecked((int)0xC0000023);
    private const int AfInet = 2;
    private const int TcpTableOwnerPidListener = 3;
    private const uint ErrorInsufficientBuffer = 122;

    // 127.0.0.1 as the table stores it, in network byte order.
    private const uint Loopback = 0x0100007F;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(int access, bool inheritHandle, int processId);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(IntPtr process, int informationClass, IntPtr information, int length, out int returnLength);

    [DllImport("iphlpapi.dll")]
    private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order, int addressFamily, int tableClass, int reserved);
}
