using System.Globalization;
using OrbMet.Core.Localization;
using OrbMet.Core.Models;

namespace OrbMet.Core.Usage;

/// <summary>
/// User-facing wording for usage windows (design.md sections 6 and 7), in
/// the language it is built for. Kept free of WPF so the rules (no invented
/// reset times, no old value shown as the new period's) are unit-tested.
/// Times are shown in the zone passed in, never a fixed one.
/// </summary>
public sealed class UsageText
{
    private const long Hour = 60 * 60;
    private const long Day = 24 * Hour;

    private static readonly UsageText EnglishText = new(Texts.For(UiLanguage.English));
    private static readonly UsageText TurkishText = new(Texts.For(UiLanguage.Turkish));

    private readonly Texts _text;

    private UsageText(Texts text) => _text = text;

    public static UsageText For(UiLanguage language) => language == UiLanguage.Turkish ? TurkishText : EnglishText;

    /// <summary>"19%", or "%19" in Turkish. Missing is a dash, never 0.</summary>
    public string Percent(double? usedPercent) => usedPercent is { } value
        ? _text.Format("PercentFormat", value.ToString("0", CultureInfo.InvariantCulture))
        : "—";

    /// <summary>The card's figure under a bar: "6% used".</summary>
    public string Used(double? usedPercent) => usedPercent is null
        ? _text["UsageNotReported"]
        : _text.Format("UsedFormat", Percent(usedPercent));

    /// <summary>
    /// Short window name such as "5-hour" or "Weekly", used where space is
    /// tight. Falls back to the scope label, then to a neutral name.
    /// </summary>
    public string WindowLabel(UsageWindow window, string providerDisplayName)
    {
        ArgumentNullException.ThrowIfNull(window);

        var label = DurationLabel(window.DurationSeconds) ?? window.ScopeLabel ?? _text["UsageLimit"];
        return WithScope(label, window.ScopeLabel, providerDisplayName);
    }

    /// <summary>
    /// The card's row title: "5-hour limit", "Weekly limit · gpt-5", or
    /// "Usage limit" when the length is not reported.
    /// </summary>
    public string LimitTitle(UsageWindow window, string providerDisplayName)
    {
        ArgumentNullException.ThrowIfNull(window);

        var label = DurationLabel(window.DurationSeconds) is { } duration
            ? _text.Format("LimitFormat", duration)
            : _text["UsageLimit"];
        return WithScope(label, window.ScopeLabel, providerDisplayName);
    }

    private static string WithScope(string label, string? scope, string providerDisplayName)
    {
        if (scope is { Length: > 0 } &&
            !label.Contains(scope, StringComparison.Ordinal) &&
            !string.Equals(scope, providerDisplayName, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(scope, "default", StringComparison.OrdinalIgnoreCase))
        {
            return $"{label} · {scope}";
        }

        return label;
    }

    private string? DurationLabel(long? seconds) => seconds switch
    {
        null or <= 0 => null,
        7 * Day => _text["Weekly"],
        Day => _text["Daily"],
        { } s when s % Day == 0 => _text.Format("DaysFormat", s / Day),
        { } s when s % Hour == 0 => _text.Format("HoursFormat", s / Hour),
        _ => null,
    };

    /// <summary>
    /// When a window resets, as a clock time: "Resets 13:30" today,
    /// "Resets Oct 18, 09:00" on another day. An unknown reset is left empty
    /// rather than guessed; a reset already behind us is the old period.
    /// </summary>
    public string ResetAt(DateTimeOffset? resetsAtUtc, DateTimeOffset nowUtc, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);

        if (resetsAtUtc is not { } resetsAt)
        {
            return string.Empty;
        }

        if (IsPast(resetsAtUtc, nowUtc))
        {
            return _text["WaitingForNewData"];
        }

        var local = TimeZoneInfo.ConvertTime(resetsAt, zone);
        var today = TimeZoneInfo.ConvertTime(nowUtc, zone).Date;
        var format = local.Date == today ? _text["ResetTimeFormat"] : _text["DateTimeFormat"];
        return _text.Format("ResetsFormat", local.ToString(format, _text.Culture));
    }

    /// <summary>
    /// "Plan: Plus" for the Connections window, or null when the provider did
    /// not report a plan. The reported name is only capitalised, never mapped,
    /// and invariantly so the English "individual" does not become "İndividual".
    /// </summary>
    public string? Plan(string? planName)
    {
        if (string.IsNullOrWhiteSpace(planName))
        {
            return null;
        }

        var name = planName.Trim();
        return _text.Format("PlanFormat", $"{char.ToUpperInvariant(name[0])}{name[1..]}");
    }

    /// <summary>
    /// A notification's title and text: "Codex: 82% used" over "5-hour limit
    /// · Resets 17:00". The figure is the provider's own reading.
    /// </summary>
    public (string Title, string Body) Alert(UsageAlert alert, string providerDisplayName, DateTimeOffset nowUtc, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(alert);

        var limit = alert.Window is { } window ? LimitTitle(window, providerDisplayName) : string.Empty;
        var reset = alert.Window is { } resetting ? ResetAt(resetting.ResetsAtUtc, nowUtc, zone) : string.Empty;
        var limitAndReset = string.IsNullOrEmpty(reset) ? limit : $"{limit} · {reset}";

        return alert.Kind switch
        {
            AlertKind.Approaching => (_text.Format("AlertApproachingFormat", providerDisplayName, Percent(alert.Window?.UsedPercent)), limitAndReset),
            AlertKind.Spent => (_text.Format("AlertSpentFormat", providerDisplayName), limitAndReset),
            AlertKind.Reset => (_text.Format("AlertResetFormat", providerDisplayName), _text.Format("AlertResetBodyFormat", limit)),
            AlertKind.ClaudeLinkBroken => (_text["AlertClaudeLink"], _text["AlertClaudeLinkBody"]),
            _ => (_text["AlertCodexUnreadable"], _text["AlertCodexUnreadableBody"]),
        };
    }

    /// <summary>True once a reported reset moment has passed.</summary>
    public static bool IsPast(DateTimeOffset? resetsAtUtc, DateTimeOffset nowUtc) =>
        resetsAtUtc is { } resetsAt && resetsAt <= nowUtc;

    /// <summary>
    /// The card's footnote for a stale reading: "As of 12 min ago". This is
    /// when the provider last reported, not when OrbMet last read a file.
    /// </summary>
    public string StaleFootnote(DateTimeOffset? observedAtUtc, DateTimeOffset nowUtc, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);

        if (observedAtUtc is not { } observed)
        {
            return _text["MayBeOutOfDate"];
        }

        var age = nowUtc - observed;
        if (age < TimeSpan.FromMinutes(1))
        {
            return _text["AsOfJustNow"];
        }

        if (age < TimeSpan.FromHours(1))
        {
            return _text.Format("AsOfMinutesFormat", (int)age.TotalMinutes);
        }

        if (age < TimeSpan.FromHours(24))
        {
            return _text.Format("AsOfHoursFormat", (int)age.TotalHours);
        }

        var local = TimeZoneInfo.ConvertTime(observed, zone);
        return _text.Format("AsOfFormat", local.ToString(_text["DateTimeFormat"], _text.Culture));
    }
}
