using System.Text.Json.Nodes;
using OrbMet.Core.Storage;

namespace OrbMet.Core.Usage;

/// <summary>Which limit the ring shows (design.md 6.2); the card always lists every one.</summary>
public enum RingChoice
{
    FiveHour,
    Weekly,
    ClosestToLimit,
}

/// <summary>
/// The ring choice the user picked, in OrbMet's settings.json as
/// <c>{"Ring": "weekly"}</c>. Nothing is written until the user picks one,
/// so until then the ring shows the 5-hour limit. Other keys are kept.
/// </summary>
public sealed class RingChoiceStore
{
    private const string Key = "Ring";

    private readonly SettingsFile _file;

    public RingChoiceStore(string path)
    {
        _file = new SettingsFile(path);
    }

    /// <summary>The picked choice, or <see cref="RingChoice.FiveHour"/> when none was picked or it is unreadable.</summary>
    public RingChoice Load() =>
        _file.Read(Key) is JsonValue value && value.TryGetValue<string>(out var code)
            ? code switch
            {
                "weekly" => RingChoice.Weekly,
                "closest" => RingChoice.ClosestToLimit,
                _ => RingChoice.FiveHour,
            }
            : RingChoice.FiveHour;

    /// <summary>Returns false when the file could not be written.</summary>
    public bool Save(RingChoice choice) => _file.Write(Key, JsonValue.Create(choice switch
    {
        RingChoice.Weekly => "weekly",
        RingChoice.ClosestToLimit => "closest",
        _ => "five-hour",
    }));
}
