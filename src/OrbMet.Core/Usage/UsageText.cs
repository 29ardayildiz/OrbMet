using System.Globalization;
using OrbMet.Core.Models;

namespace OrbMet.Core.Usage;

/// <summary>
/// User-facing Turkish wording for usage windows (design.md sections 6 and 7).
/// Kept free of WPF so the rules (no invented reset times, no old value shown
/// as the new period's) are unit-tested. Times are shown in the zone passed
/// in, never a fixed one.
/// </summary>
public static class UsageText
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    private const long Hour = 60 * 60;
    private const long Day = 24 * Hour;

    /// <summary>Turkish puts the sign first: %19. Missing is a dash, never 0.</summary>
    public static string Percent(double? usedPercent) => usedPercent is { } value
        ? $"%{value.ToString("0", CultureInfo.InvariantCulture)}"
        : "—";

    /// <summary>The card's figure under a bar: "%6 kullanıldı".</summary>
    public static string Used(double? usedPercent) => usedPercent is null
        ? "Kullanım bildirilmedi"
        : $"{Percent(usedPercent)} kullanıldı";

    /// <summary>
    /// Short window name such as "5 saatlik" or "Haftalık", used where space
    /// is tight. Falls back to the scope label, then to a neutral name.
    /// </summary>
    public static string WindowLabel(UsageWindow window, string providerDisplayName)
    {
        ArgumentNullException.ThrowIfNull(window);

        var label = DurationLabel(window.DurationSeconds) ?? window.ScopeLabel ?? "Kullanım limiti";
        return WithScope(label, window.ScopeLabel, providerDisplayName);
    }

    /// <summary>
    /// The card's row title: "5 saatlik limit", "Haftalık limit · gpt-5", or
    /// "Kullanım limiti" when the length is not reported.
    /// </summary>
    public static string LimitTitle(UsageWindow window, string providerDisplayName)
    {
        ArgumentNullException.ThrowIfNull(window);

        var label = DurationLabel(window.DurationSeconds) is { } duration
            ? $"{duration} limit"
            : "Kullanım limiti";
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

    private static string? DurationLabel(long? seconds) => seconds switch
    {
        null or <= 0 => null,
        7 * Day => "Haftalık",
        Day => "Günlük",
        { } s when s % Day == 0 => $"{s / Day} günlük",
        { } s when s % Hour == 0 => $"{s / Hour} saatlik",
        _ => null,
    };

    /// <summary>
    /// When a window resets, as a clock time: "Sıfırlanma 13:30" today,
    /// "Sıfırlanma 18 Eki 09:00" on another day. An unknown reset is left
    /// empty rather than guessed; a reset already behind us is the old period.
    /// </summary>
    public static string ResetAt(DateTimeOffset? resetsAtUtc, DateTimeOffset nowUtc, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);

        if (resetsAtUtc is not { } resetsAt)
        {
            return string.Empty;
        }

        if (IsPast(resetsAtUtc, nowUtc))
        {
            return "Yeni veri bekleniyor";
        }

        var local = TimeZoneInfo.ConvertTime(resetsAt, zone);
        var today = TimeZoneInfo.ConvertTime(nowUtc, zone).Date;
        return local.Date == today
            ? $"Sıfırlanma {local.ToString("HH:mm", Turkish)}"
            : $"Sıfırlanma {local.ToString("d MMM HH:mm", Turkish)}";
    }

    /// <summary>
    /// "Plan: Plus" for the Connections window, or null when the provider did
    /// not report a plan. The reported name is only capitalised, never mapped,
    /// and invariantly so the English "individual" does not become "İndividual".
    /// </summary>
    public static string? Plan(string? planName)
    {
        if (string.IsNullOrWhiteSpace(planName))
        {
            return null;
        }

        var name = planName.Trim();
        return $"Plan: {char.ToUpperInvariant(name[0])}{name[1..]}";
    }

    /// <summary>True once a reported reset moment has passed.</summary>
    public static bool IsPast(DateTimeOffset? resetsAtUtc, DateTimeOffset nowUtc) =>
        resetsAtUtc is { } resetsAt && resetsAt <= nowUtc;

    /// <summary>
    /// The card's footnote for a stale reading: "12 dk önce itibarıyla". This
    /// is when the provider last reported, not when OrbMet last read a file.
    /// </summary>
    public static string StaleFootnote(DateTimeOffset? observedAtUtc, DateTimeOffset nowUtc, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);

        if (observedAtUtc is not { } observed)
        {
            return "Veri güncel olmayabilir";
        }

        var age = nowUtc - observed;
        if (age < TimeSpan.FromMinutes(1))
        {
            return "Az önce itibarıyla";
        }

        if (age < TimeSpan.FromHours(1))
        {
            return $"{(int)age.TotalMinutes} dk önce itibarıyla";
        }

        if (age < TimeSpan.FromHours(24))
        {
            return $"{(int)age.TotalHours} sa önce itibarıyla";
        }

        return $"{TimeZoneInfo.ConvertTime(observed, zone).ToString("d MMM HH:mm", Turkish)} itibarıyla";
    }
}
