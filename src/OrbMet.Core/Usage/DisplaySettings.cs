using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using OrbMet.Core.Models;
using OrbMet.Core.Storage;

namespace OrbMet.Core.Usage;

/// <summary>A ring colour the user may pick; warning colours stay the provider's thresholds.</summary>
public enum RingColour
{
    Green,
    Teal,
    Blue,
    Purple,
    Pink,
    White,
}

/// <summary>
/// The "Appearance" choices (design.md 10, Görünüm): the window clock arc (on
/// by default), figures as remaining rather than used, the forecast on the
/// card, each provider's ring colour (green), and for the strip: folding to a
/// sliver when idle, following the pointer's display and showing when an
/// agent is working (all off).
/// </summary>
public sealed record DisplaySettings
{
    public bool WindowClock { get; init; } = true;

    public bool ShowRemaining { get; init; }

    public bool Forecast { get; init; }

    public RingColour ClaudeColour { get; init; } = RingColour.Green;

    public RingColour CodexColour { get; init; } = RingColour.Green;

    public RingColour AntigravityColour { get; init; } = RingColour.Green;

    /// <summary>A docked strip folds to a sliver at the screen edge while the pointer is elsewhere.</summary>
    public bool CollapseWhenIdle { get; init; }

    /// <summary>The waits the menu offers before folding, in seconds; 30 unless the user picked another.</summary>
    public static readonly IReadOnlyList<int> CollapseDelays = new[] { 10, 30, 120 };

    /// <summary>How long the pointer must be away before the strip folds; one of <see cref="CollapseDelays"/>.</summary>
    public int CollapseAfterSeconds { get; init; } = 30;

    /// <summary>The wait in force: an unknown stored value is the default 30 seconds.</summary>
    [JsonIgnore]
    public TimeSpan CollapseDelay =>
        TimeSpan.FromSeconds(CollapseDelays.Contains(CollapseAfterSeconds) ? CollapseAfterSeconds : 30);

    /// <summary>The user hid this provider from the strip; it is then not read at all.</summary>
    public bool HideClaude { get; init; }

    public bool HideCodex { get; init; }

    public bool HideAntigravity { get; init; }

    [JsonIgnore]
    public IReadOnlySet<ProviderId> Hidden => new HashSet<ProviderId>(
        new[] { (ProviderId.ClaudeCode, HideClaude), (ProviderId.Codex, HideCodex), (ProviderId.Antigravity, HideAntigravity) }
            .Where(entry => entry.Item2)
            .Select(entry => entry.Item1));

    public DisplaySettings WithHidden(ProviderId providerId, bool hide) => providerId switch
    {
        ProviderId.Codex => this with { HideCodex = hide },
        ProviderId.Antigravity => this with { HideAntigravity = hide },
        _ => this with { HideClaude = hide },
    };

    /// <summary>The strip moves to whichever display the pointer settles on.</summary>
    public bool FollowPointer { get; init; }

    /// <summary>A dot circles a ring while that agent is mid-turn, read from the tail of its session files.</summary>
    public bool ShowActivity { get; init; }

    public RingColour ColourFor(ProviderId providerId) => providerId switch
    {
        ProviderId.Codex => CodexColour,
        ProviderId.Antigravity => AntigravityColour,
        _ => ClaudeColour,
    };

    public DisplaySettings WithColour(ProviderId providerId, RingColour colour) => providerId switch
    {
        ProviderId.Codex => this with { CodexColour = colour },
        ProviderId.Antigravity => this with { AntigravityColour = colour },
        _ => this with { ClaudeColour = colour },
    };
}

/// <summary>
/// <see cref="DisplaySettings"/> in settings.json under <c>"Display"</c>.
/// Missing or unreadable values keep their defaults; other keys are kept.
/// </summary>
public sealed class DisplayStore
{
    private const string Key = "Display";

    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly SettingsFile _file;

    public DisplayStore(string path)
    {
        _file = new SettingsFile(path);
    }

    public DisplaySettings Load()
    {
        try
        {
            return _file.Read(Key)?.Deserialize<DisplaySettings>(Options) ?? new DisplaySettings();
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return new DisplaySettings();
        }
    }

    public bool Save(DisplaySettings settings) =>
        _file.Write(Key, JsonSerializer.SerializeToNode(settings, Options) ?? new JsonObject());
}
