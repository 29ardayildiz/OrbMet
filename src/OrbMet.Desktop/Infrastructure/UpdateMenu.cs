using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows.Controls;
using System.Windows.Threading;
using OrbMet.Core.Storage;
using OrbMet.Core.Updates;

namespace OrbMet.Desktop.Infrastructure;

/// <summary>
/// Updates (design.md 10, Güncellemeler): at start and then once a day OrbMet
/// asks GitHub for the latest release, and only that. A newer one is
/// announced once and offered at the top of both menus; a click on either
/// downloads the installer, checks its SHA-256, runs it quietly and exits,
/// and the installer starts the new version. A copy from the zip has no
/// installer to run, so it opens the release page instead. "Check for
/// updates" turns the asking off.
/// </summary>
internal static class UpdateMenu
{
    // Segoe Fluent Icons: Download, Sync.
    private const string UpdateGlyph = "";
    private const string CheckGlyph = "";

    /// <summary>
    /// For testing only: a folder URL serving latest.json and the files it
    /// names, used instead of GitHub.
    /// </summary>
    private const string FeedVariable = "ORBMET_UPDATE_FEED";

    private static readonly TimeSpan CheckEvery = TimeSpan.FromDays(1);
    private static readonly HttpClient Http = CreateClient();

    private static UpdateStore? _store;
    private static TrayIcon? _tray;
    private static Action? _exit;
    private static DispatcherTimer? _timer;
    private static AvailableUpdate? _available;
    private static bool _installing;

    private static event Action? Changed;

    private static UpdateStore Store => _store ??= new UpdateStore(OrbMetPaths.FromEnvironment().SettingsFile);

    /// <summary>The running version; null for a development build, which never updates.</summary>
    private static Version? Running
    {
        get
        {
            var informational = Assembly.GetEntryAssembly()?
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            var version = ReleaseCheck.ParseVersion(informational);
            return version is null || version == new Version(1, 0, 0) ? null : version;
        }
    }

    private static string? Feed =>
        Environment.GetEnvironmentVariable(FeedVariable) is { Length: > 0 } feed ? feed.TrimEnd('/') + "/" : null;

    private static string DownloadFolder => Path.Combine(Path.GetTempPath(), "OrbMet-update");

    private static bool IsInstalled => File.Exists(Path.Combine(AppContext.BaseDirectory, "unins000.exe"));

    /// <summary>"Update to 0.4.0", at the top of a menu; hidden until there is one.</summary>
    public static MenuItem CreateUpdateItem()
    {
        var item = new MenuItem { Icon = UpdateGlyph };
        item.Click += (_, _) => _ = InstallAsync();

        void Show()
        {
            var text = Localizer.Instance.Text;
            item.Visibility = _available is null ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
            item.IsEnabled = !_installing;
            item.Header = _installing
                ? text["UpdateDownloading"]
                : _available is { } update ? text.Format("UpdateToFormat", update.Version.ToString(3)) : string.Empty;
        }

        Show();
        Changed += Show;
        Localizer.Instance.LanguageChanged += (_, _) => Show();
        return item;
    }

    /// <summary>"Check for updates", with a check on the right while on.</summary>
    public static MenuItem CreateCheckItem()
    {
        var item = new MenuItem { Icon = CheckGlyph };
        item.Click += (_, _) =>
        {
            var on = !Store.Enabled;
            Store.SetEnabled(on);
            if (!on)
            {
                _available = null;
            }

            Changed?.Invoke();
            if (on)
            {
                _ = CheckAsync();
            }
        };

        void Show()
        {
            item.Header = Localizer.Instance["CheckForUpdates"];
            item.IsChecked = Store.Enabled;
        }

        Show();
        Changed += Show;
        Localizer.Instance.LanguageChanged += (_, _) => Show();
        return item;
    }

    /// <summary>Starts asking: once now, then once a day while OrbMet runs.</summary>
    public static void Start(TrayIcon tray, Action exit)
    {
        // The installer a past update ran is no longer needed.
        try
        {
            if (Directory.Exists(DownloadFolder))
            {
                Directory.Delete(DownloadFolder, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        _tray = tray;
        _exit = exit;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromHours(1) };
        _timer.Tick += (_, _) =>
        {
            if (Store.CheckedAtUtc is not { } last || DateTimeOffset.UtcNow - last >= CheckEvery)
            {
                _ = CheckAsync();
            }
        };
        _timer.Start();
        _ = CheckAsync();
    }

    public static void Stop() => _timer?.Stop();

    private static async Task CheckAsync()
    {
        if (!Store.Enabled || Running is not { } running || _installing)
        {
            return;
        }

        AvailableUpdate? update;
        try
        {
            var feed = Feed;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var json = await Http.GetStringAsync(feed is null ? ReleaseCheck.LatestReleaseUrl : feed + "latest.json", timeout.Token);
            update = ReleaseCheck.Read(json, running, feed);
            Store.MarkChecked(DateTimeOffset.UtcNow);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            // Offline or GitHub unreachable: quietly try again later.
            return;
        }

        _available = update;
        Changed?.Invoke();

        if (update is not null && (Store.Announced is not { } announced || announced < update.Version))
        {
            Store.MarkAnnounced(update.Version);
            var text = Localizer.Instance.Text;
            _tray?.ShowNotification(
                text.Format("UpdateAvailableFormat", update.Version.ToString(3)),
                text[CanInstall(update) ? "UpdateAvailableBody" : "UpdateOpenPageBody"],
                () => _ = InstallAsync());
        }
    }

    private static bool CanInstall(AvailableUpdate update) =>
        IsInstalled && update.SetupUrl is not null && update.ChecksumUrl is not null;

    private static async Task InstallAsync()
    {
        if (_available is not { } update || _installing)
        {
            return;
        }

        if (!CanInstall(update))
        {
            OpenPage(update.PageUrl);
            return;
        }

        _installing = true;
        Changed?.Invoke();
        try
        {
            var expected = ReleaseCheck.ParseChecksum(await Http.GetStringAsync(update.ChecksumUrl))
                ?? throw new InvalidDataException("The checksum file has no SHA-256.");

            Directory.CreateDirectory(DownloadFolder);
            var setup = Path.Combine(DownloadFolder,$"OrbMet-{update.Version.ToString(3)}-setup.exe");
            await using (var download = await Http.GetStreamAsync(update.SetupUrl))
            await using (var file = File.Create(setup))
            {
                await download.CopyToAsync(file);
            }

            string actual;
            await using (var file = File.OpenRead(setup))
            {
                actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(file));
            }

            if (actual != expected)
            {
                File.Delete(setup);
                throw new InvalidDataException("The installer does not match its checksum.");
            }

            // The installer waits for OrbMet's single-instance lock to be
            // free, so it is let go first; the installer starts the new
            // version when it is done (/relaunch=1, OrbMet.iss).
            App.ReleaseInstance();
            Process.Start(new ProcessStartInfo(setup, "/SILENT /SUPPRESSMSGBOXES /NORESTART /relaunch=1")
            {
                UseShellExecute = false,
            });
            _exit?.Invoke();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException
            or InvalidDataException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            _installing = false;
            Changed?.Invoke();
            var text = Localizer.Instance.Text;
            _tray?.ShowNotification(text["UpdateFailed"], text["UpdateOpenPageBody"], () => OpenPage(update.PageUrl));
        }
    }

    private static void OpenPage(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("OrbMet-update-check");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }
}
