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
/// card (both off), and each provider's ring colour (green).
/// </summary>
public sealed record DisplaySettings
{
    public bool WindowClock { get; init; } = true;

    public bool ShowRemaining { get; init; }

    public bool Forecast { get; init; }

    public RingColour ClaudeColour { get; init; } = RingColour.Green;

    public RingColour CodexColour { get; init; } = RingColour.Green;

    public RingColour ColourFor(ProviderId providerId) =>
        providerId == ProviderId.Codex ? CodexColour : ClaudeColour;

    public DisplaySettings WithColour(ProviderId providerId, RingColour colour) =>
        providerId == ProviderId.Codex ? this with { CodexColour = colour } : this with { ClaudeColour = colour };
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
