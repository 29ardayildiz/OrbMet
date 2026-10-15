using System.Text.Json;
using System.Text.Json.Nodes;
using OrbMet.Core.Storage;

namespace OrbMet.Core.Updates;

/// <summary>A newer release than the running one, as GitHub lists it.</summary>
/// <param name="SetupUrl">The installer's download, or null when the release has none.</param>
/// <param name="ChecksumUrl">The installer's SHA-256 file; an installer without one is never run.</param>
public sealed record AvailableUpdate(Version Version, string PageUrl, string? SetupUrl, string? ChecksumUrl);

/// <summary>
/// Reads GitHub's "latest release" reply (design.md 10, Güncellemeler). Only
/// the tag, the page and the installer's two assets are looked at.
/// </summary>
public static class ReleaseCheck
{
    public const string Owner = "29ardayildiz";
    public const string Repository = "OrbMet";

    public static string LatestReleaseUrl => $"https://api.github.com/repos/{Owner}/{Repository}/releases/latest";

    public static string ReleasesPage => $"https://github.com/{Owner}/{Repository}/releases";

    /// <summary>"0.3.0+9061d97" or "0.3.0" to a version; null for anything else.</summary>
    public static Version? ParseVersion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var core = text.Trim().TrimStart('v', 'V').Split('+', '-')[0];
        return Version.TryParse(core, out var version) ? Normalise(version) : null;
    }

    /// <summary>
    /// The update the reply offers over <paramref name="running"/>, or null
    /// when it is not newer, is a draft or prerelease, or cannot be read.
    /// Assets are taken only from under <paramref name="downloadPrefix"/>,
    /// this repository's release downloads unless a test feed says otherwise.
    /// </summary>
    public static AvailableUpdate? Read(string json, Version running, string? downloadPrefix = null)
    {
        downloadPrefix ??= DownloadPrefix;
        ArgumentNullException.ThrowIfNull(running);

        JsonObject? release;
        try
        {
            release = JsonNode.Parse(json) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }

        if (release is null
            || Flag(release, "draft")
            || Flag(release, "prerelease")
            || ParseVersion(Text(release, "tag_name")) is not { } version
            || version <= Normalise(running))
        {
            return null;
        }

        var setupName = $"OrbMet-{version.ToString(3)}-setup.exe";
        string? setupUrl = null;
        string? checksumUrl = null;
        if (release["assets"] is JsonArray assets)
        {
            foreach (var asset in assets.OfType<JsonObject>())
            {
                var name = Text(asset, "name");
                var url = Text(asset, "browser_download_url");
                if (url is null || !url.StartsWith(downloadPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (string.Equals(name, setupName, StringComparison.OrdinalIgnoreCase))
                {
                    setupUrl = url;
                }
                else if (string.Equals(name, setupName + ".sha256", StringComparison.OrdinalIgnoreCase))
                {
                    checksumUrl = url;
                }
            }
        }

        var page = Text(release, "html_url") is { } html && html.StartsWith($"https://github.com/{Owner}/{Repository}/", StringComparison.OrdinalIgnoreCase)
            ? html
            : ReleasesPage;
        return new AvailableUpdate(version, page, setupUrl, checksumUrl);
    }

    /// <summary>The hash from a "&lt;hex&gt;  name" checksum file, lower case; null when there is none.</summary>
    public static string? ParseChecksum(string text)
    {
        var first = text?.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return first is { Length: 64 } && first.All(Uri.IsHexDigit) ? first.ToLowerInvariant() : null;
    }

    /// <summary>Downloads come from this repository's releases only, never another host.</summary>
    public static string DownloadPrefix => $"https://github.com/{Owner}/{Repository}/releases/download/";

    private static Version Normalise(Version version) =>
        new(version.Major, version.Minor, Math.Max(version.Build, 0));

    private static string? Text(JsonObject node, string key) =>
        node[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static bool Flag(JsonObject node, string key) =>
        node[key] is JsonValue value && value.TryGetValue<bool>(out var flag) && flag;
}

/// <summary>
/// The update settings in settings.json: <c>"CheckForUpdates"</c> (on unless
/// turned off), the last check, and the version already announced so it is
/// announced once.
/// </summary>
public sealed class UpdateStore
{
    private const string EnabledKey = "CheckForUpdates";
    private const string CheckedKey = "UpdateCheckedAtUtc";
    private const string AnnouncedKey = "UpdateAnnounced";

    private readonly SettingsFile _file;

    public UpdateStore(string path)
    {
        _file = new SettingsFile(path);
    }

    public bool Enabled =>
        _file.Read(EnabledKey) is not JsonValue value || !value.TryGetValue<bool>(out var enabled) || enabled;

    public bool SetEnabled(bool enabled) => _file.Write(EnabledKey, JsonValue.Create(enabled));

    public DateTimeOffset? CheckedAtUtc =>
        _file.Read(CheckedKey) is JsonValue value && value.TryGetValue<string>(out var text)
            && DateTimeOffset.TryParse(text, null, System.Globalization.DateTimeStyles.RoundtripKind, out var at)
            ? at
            : null;

    public bool MarkChecked(DateTimeOffset nowUtc) => _file.Write(CheckedKey, JsonValue.Create(nowUtc.ToString("O")));

    public Version? Announced =>
        _file.Read(AnnouncedKey) is JsonValue value && value.TryGetValue<string>(out var text) ? ReleaseCheck.ParseVersion(text) : null;

    public bool MarkAnnounced(Version version) => _file.Write(AnnouncedKey, JsonValue.Create(version.ToString(3)));
}
