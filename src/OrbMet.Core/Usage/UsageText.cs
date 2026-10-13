using System.Globalization;
using OrbMet.Core.Models;

namespace OrbMet.Core.Usage;

/// <summary>
/// User-facing Turkish wording for usage windows. Kept free of WPF so the
/// rules (no negative countdowns, no invented reset times) are unit-tested.
/// Times are shown in the zone passed in, never a fixed one.
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

    /// <summary>
    /// Short window name such as "5 saatlik" or "Haftalık". Falls back to the
    /// provider's scope label, then to a neutral name when nothing is known.
    /// </summary>
    public static string WindowLabel(UsageWindow window, string providerDisplayName)
    {
        ArgumentNullException.ThrowIfNull(window);

        var label = DurationLabel(window.DurationSeconds) ?? window.ScopeLabel ?? "Kullanım limiti";

        if (window.ScopeLabel is { Length: > 0 } scope &&
            !label.Contains(scope, StringComparison.Ordinal) &&
            !string.Equals(scope, providerDisplayName, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(scope, "default", StringComparison.OrdinalIgnoreCase))
        {
            label = $"{label} · {scope}";
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
    /// Reset line for the detail card. An unknown reset is said plainly; a
    /// reset already behind us means the window is over, not a negative count.
    /// </summary>
    public static string Reset(DateTimeOffset? resetsAtUtc, DateTimeOffset nowUtc, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);

        if (resetsAtUtc is not { } resetsAt)
        {
            return "Sıfırlanma zamanı bildirilmedi";
        }

        if (IsPast(resetsAtUtc, nowUtc))
        {
            return "Yeni veri bekleniyor";
        }

        var remaining = resetsAt - nowUtc;
        if (remaining < TimeSpan.FromMinutes(1))
        {
            return "Sıfırlanmaya 1 dk'dan az";
        }

        if (remaining < TimeSpan.FromHours(1))
        {
            return $"Sıfırlanmaya {(int)remaining.TotalMinutes} dk";
        }

        if (remaining < TimeSpan.FromHours(24))
        {
            return $"Sıfırlanmaya {(int)remaining.TotalHours} sa {remaining.Minutes} dk";
        }

        return $"Sıfırlanma: {LocalDateTime(resetsAt, zone)}";
    }

    /// <summary>True once a reported reset moment has passed.</summary>
    public static bool IsPast(DateTimeOffset? resetsAtUtc, DateTimeOffset nowUtc) =>
        resetsAtUtc is { } resetsAt && resetsAt <= nowUtc;

    /// <summary>
    /// When the provider last reported, as "Son veri: 12 dk önce". This is the
    /// observation time, not when OrbMet last read a file.
    /// </summary>
    public static string ObservedAge(DateTimeOffset? observedAtUtc, DateTimeOffset nowUtc, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);

        if (observedAtUtc is not { } observed)
        {
            return "Son veri zamanı bilinmiyor";
        }

        var age = nowUtc - observed;
        if (age < TimeSpan.FromMinutes(1))
        {
            return "Son veri: az önce";
        }

        if (age < TimeSpan.FromHours(1))
        {
            return $"Son veri: {(int)age.TotalMinutes} dk önce";
        }

        if (age < TimeSpan.FromHours(24))
        {
            return $"Son veri: {(int)age.TotalHours} sa önce";
        }

        return $"Son veri: {LocalDateTime(observed, zone)}";
    }

    private static string LocalDateTime(DateTimeOffset moment, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTime(moment, zone).ToString("d MMMM HH:mm", Turkish);
}
