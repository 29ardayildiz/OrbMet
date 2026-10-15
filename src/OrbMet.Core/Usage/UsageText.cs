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
    /// What is left of a limit, from the provider's used figure: never below
    /// 0, and 0 once the provider says it is spent. Null stays null, never 100.
    /// </summary>
    public static double? Remaining(double? usedPercent, bool isExhausted) => usedPercent is { } used
        ? isExhausted ? 0 : Math.Max(0, 100 - used)
        : null;

    /// <summary>The strip's figure: "%21" used, or "%79" left when the user shows what remains.</summary>
    public string Figure(double? usedPercent, bool isExhausted, bool showRemaining) =>
        Percent(showRemaining ? Remaining(usedPercent, isExhausted) : usedPercent);

    /// <summary>"6% used", or "94% left" when the user shows what remains.</summary>
    public string UsedOrLeft(double? usedPercent, bool isExhausted, bool showRemaining) =>
        !showRemaining || usedPercent is null
            ? Used(usedPercent)
            : _text.Format("LeftFormat", Percent(Remaining(usedPercent, isExhausted)));

    /// <summary>
    /// The card's forecast line, always marked as one: "Forecast: lasts until
    /// the reset", or when it runs out first, roughly when. Null for a spent
    /// limit, which the row already says. Times are rounded, because the
    /// precision is not real.
    /// </summary>
    public string? Forecast(UsageForecast? forecast)
    {
        if (forecast is null || forecast.TimeToEmpty == TimeSpan.Zero)
        {
            return null;
        }

        if (!forecast.RunsOutBeforeReset)
        {
            return _text["ForecastLasts"];
        }

        return forecast.TimeToEmpty is { } until
            ? _text.Format("ForecastRunsOutInFormat", Approximately(until))
            : _text["ForecastRunsOut"];
    }

    /// <summary>"under 15 minutes", "about 45 minutes", "about an hour", "about 1.5 hours".</summary>
    private string Approximately(TimeSpan span)
    {
        var minutes = (int)Math.Round(span.TotalMinutes);
        if (minutes < 15)
        {
            return _text["UnderFifteenMinutes"];
        }

        if (minutes < 68)
        {
            var quarters = Math.Max((int)Math.Round(minutes / 15.0), 1);
            return quarters == 4 ? _text["AboutAnHour"] : _text.Format("AboutMinutesFormat", quarters * 15);
        }

        var hours = Math.Round(minutes / 60.0 * 2) / 2;
        return _text.Format("AboutHoursFormat", hours.ToString("0.#", _text.Culture));
    }

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
    /// The card's reset line with how long is left: "Resets 17:00 · in 2 h
    /// 14 min". Unknown, or already passed, reads as <see cref="ResetAt"/>.
    /// </summary>
    public string ResetWithCountdown(DateTimeOffset? resetsAtUtc, DateTimeOffset nowUtc, TimeZoneInfo zone)
    {
        var reset = ResetAt(resetsAtUtc, nowUtc, zone);
        return resetsAtUtc is { } resetsAt && !IsPast(resetsAtUtc, nowUtc)
            ? $"{reset} · {Countdown(resetsAt - nowUtc)}"
            : reset;
    }

    /// <summary>"in 1 d 15 h", "in 2 h 14 min", "in 14 min", "in under a minute"; a zero part is left out.</summary>
    public string Countdown(TimeSpan left)
    {
        var minutes = (long)Math.Floor(left.TotalMinutes);
        if (minutes < 1)
        {
            return _text["CountdownSoon"];
        }

        var days = minutes / (24 * 60);
        var hours = minutes / 60 % 24;
        var rest = minutes % 60;
        if (days > 0)
        {
            return hours > 0 ? _text.Format("CountdownDaysHoursFormat", days, hours) : _text.Format("CountdownDaysFormat", days);
        }

        if (hours > 0)
        {
            return rest > 0 ? _text.Format("CountdownHoursMinutesFormat", hours, rest) : _text.Format("CountdownHoursFormat", hours);
        }

        return _text.Format("CountdownMinutesFormat", rest);
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
    public (string Title, string Body) Alert(
        UsageAlert alert,
        string providerDisplayName,
        DateTimeOffset nowUtc,
        TimeZoneInfo zone,
        bool showRemaining = false)
    {
        ArgumentNullException.ThrowIfNull(alert);

        var limit = alert.Window is { } window ? LimitTitle(window, providerDisplayName) : string.Empty;
        var reset = alert.Window is { } resetting ? ResetAt(resetting.ResetsAtUtc, nowUtc, zone) : string.Empty;
        var limitAndReset = string.IsNullOrEmpty(reset) ? limit : $"{limit} · {reset}";
        var approaching = showRemaining
            ? _text.Format("AlertLeftFormat", providerDisplayName, Percent(Remaining(alert.Window?.UsedPercent, false)))
            : _text.Format("AlertApproachingFormat", providerDisplayName, Percent(alert.Window?.UsedPercent));

        return alert.Kind switch
        {
            AlertKind.Approaching => (approaching, limitAndReset),
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
