using System.Text.Json;
using System.Text.Json.Nodes;
using OrbMet.Core.Models;
using OrbMet.Core.Storage;

namespace OrbMet.Core.Usage;

/// <summary>Something worth a notification (design.md 10, Bildirimler).</summary>
public enum AlertKind
{
    /// <summary>A limit went past the threshold the user picked.</summary>
    Approaching,

    /// <summary>The provider says a limit is spent, or it reports 100% or more.</summary>
    Spent,

    /// <summary>A limit already warned about has passed its reset time.</summary>
    Reset,

    /// <summary>Claude Code runs a helper that is gone.</summary>
    ClaudeLinkBroken,

    /// <summary>Several reads of Codex in a row failed.</summary>
    CodexUnreadable,
}

/// <param name="Window">The limit it is about; null for the connection alerts.</param>
public sealed record UsageAlert(ProviderId ProviderId, AlertKind Kind, UsageWindow? Window = null);

/// <summary>One provider's state after a read, as the alert rules need it.</summary>
public sealed record AlertReading(ProviderSnapshot Snapshot, bool ClaudeLinkBroken = false);

/// <summary>
/// What OrbMet has already said, so it is said once: kept in settings.json so
/// a restart does not say it again.
/// </summary>
public sealed class AlertMemory
{
    /// <summary>Per limit ("provider|group|id"): the highest step announced in its current period.</summary>
    public Dictionary<string, WindowMemory> Windows { get; set; } = new();

    /// <summary>Codex reads that failed in a row.</summary>
    public int CodexFailures { get; set; }

    public bool CodexFailureReported { get; set; }

    public bool ClaudeLinkReported { get; set; }

    public sealed class WindowMemory
    {
        /// <summary>The threshold announced for this period, 100 once spent was; 0 for nothing said.</summary>
        public int Announced { get; set; }

        /// <summary>The period's reset time as the provider last reported it.</summary>
        public DateTimeOffset? ResetsAtUtc { get; set; }

        /// <summary>The limit's name parts, so a reset can be named without a new reading.</summary>
        public long? DurationSeconds { get; set; }

        public string? ScopeLabel { get; set; }
    }
}

/// <summary>
/// The notification rules: each thing is said once, only from what the
/// provider reported, and never about a period whose reset time has passed.
/// </summary>
public static class UsageAlerts
{
    /// <summary>The thresholds the menu offers; 0 is off.</summary>
    public static readonly IReadOnlyList<int> Thresholds = new[] { 0, 75, 80, 90, 95 };

    /// <summary>Reads in a row that must fail before Codex is called unreadable; about six minutes at the two-minute timer.</summary>
    public const int FailuresBeforeSaying = 3;

    private const int SpentStep = 100;

    /// <summary>
    /// Compares the readings with what was already said and returns what to
    /// say now, most pressing first. Updates <paramref name="memory"/>. With
    /// the threshold off nothing is said and nothing is remembered.
    /// <paramref name="afterRead"/> is false when the same readings are looked
    /// at again (a new threshold), so a failed Codex read is not counted twice.
    /// </summary>
    public static IReadOnlyList<UsageAlert> Evaluate(
        AlertMemory memory,
        int threshold,
        IEnumerable<AlertReading> readings,
        DateTimeOffset nowUtc,
        bool afterRead = true)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(readings);

        var alerts = new List<UsageAlert>();
        if (threshold <= 0)
        {
            return alerts;
        }

        foreach (var reading in readings)
        {
            var snapshot = reading.Snapshot;
            switch (snapshot.ProviderId)
            {
                case ProviderId.ClaudeCode:
                    CheckClaudeLink(memory, reading.ClaudeLinkBroken, alerts);
                    break;
                case ProviderId.Codex when afterRead:
                    CheckCodexReads(memory, snapshot, alerts);
                    break;
            }

            foreach (var window in snapshot.Windows)
            {
                CheckWindow(memory, threshold, snapshot.ProviderId, window, nowUtc, alerts);
            }
        }

        CheckPassedResets(memory, nowUtc, alerts);

        return alerts.OrderBy(alert => alert.Kind switch
        {
            AlertKind.Spent => 0,
            AlertKind.Approaching => 1,
            AlertKind.ClaudeLinkBroken or AlertKind.CodexUnreadable => 2,
            _ => 3,
        }).ToList();
    }

    public static string KeyFor(ProviderId providerId, UsageWindow window) =>
        $"{providerId}|{window.GroupId}|{window.Id}";

    private static void CheckWindow(
        AlertMemory memory,
        int threshold,
        ProviderId providerId,
        UsageWindow window,
        DateTimeOffset nowUtc,
        List<UsageAlert> alerts)
    {
        // A period whose reset has passed is the old one; its value says nothing now.
        if (window.UsedPercent is not { } percent || UsageText.IsPast(window.ResetsAtUtc, nowUtc))
        {
            return;
        }

        var key = KeyFor(providerId, window);
        if (!memory.Windows.TryGetValue(key, out var seen))
        {
            seen = new AlertMemory.WindowMemory();
            memory.Windows[key] = seen;
        }

        // A later reset time is a new period; what was said was about the old one.
        if (seen.ResetsAtUtc is { } before && window.ResetsAtUtc is { } after && after > before)
        {
            if (seen.Announced > 0)
            {
                alerts.Add(new UsageAlert(providerId, AlertKind.Reset, window));
            }

            seen.Announced = 0;
        }

        seen.ResetsAtUtc = window.ResetsAtUtc;
        seen.DurationSeconds = window.DurationSeconds;
        seen.ScopeLabel = window.ScopeLabel;

        var spent = window.ExhaustionEvidence == ExhaustionEvidence.Exhausted || percent >= 100;
        if (spent)
        {
            if (seen.Announced < SpentStep)
            {
                seen.Announced = SpentStep;
                alerts.Add(new UsageAlert(providerId, AlertKind.Spent, window));
            }
        }
        else if (percent >= threshold && seen.Announced < threshold)
        {
            seen.Announced = threshold;
            alerts.Add(new UsageAlert(providerId, AlertKind.Approaching, window));
        }
    }

    /// <summary>
    /// A warned-about limit whose reported reset time has passed has come back,
    /// even before a new reading says so: Claude only reports while in use.
    /// </summary>
    private static void CheckPassedResets(AlertMemory memory, DateTimeOffset nowUtc, List<UsageAlert> alerts)
    {
        foreach (var (key, seen) in memory.Windows)
        {
            if (seen.Announced > 0 && UsageText.IsPast(seen.ResetsAtUtc, nowUtc))
            {
                seen.Announced = 0;
                var parts = key.Split('|');
                if (parts.Length == 3 && Enum.TryParse<ProviderId>(parts[0], out var providerId))
                {
                    alerts.Add(new UsageAlert(providerId, AlertKind.Reset, new UsageWindow
                    {
                        Id = parts[2],
                        GroupId = parts[1].Length > 0 ? parts[1] : null,
                        ScopeLabel = seen.ScopeLabel,
                        DurationSeconds = seen.DurationSeconds,
                        ResetsAtUtc = seen.ResetsAtUtc,
                    }));
                }
            }
        }
    }

    private static void CheckClaudeLink(AlertMemory memory, bool broken, List<UsageAlert> alerts)
    {
        if (!broken)
        {
            memory.ClaudeLinkReported = false;
            return;
        }

        if (!memory.ClaudeLinkReported)
        {
            memory.ClaudeLinkReported = true;
            alerts.Add(new UsageAlert(ProviderId.ClaudeCode, AlertKind.ClaudeLinkBroken));
        }
    }

    private static void CheckCodexReads(AlertMemory memory, ProviderSnapshot snapshot, List<UsageAlert> alerts)
    {
        // Codex not installed or not supporting rate limits is the card's to
        // say, not a failure worth interrupting for.
        if (snapshot.ConnectionState is not (ConnectionState.Error or ConnectionState.SignInRequired))
        {
            memory.CodexFailures = 0;
            memory.CodexFailureReported = false;
            return;
        }

        memory.CodexFailures++;
        if (memory.CodexFailures >= FailuresBeforeSaying && !memory.CodexFailureReported)
        {
            memory.CodexFailureReported = true;
            alerts.Add(new UsageAlert(ProviderId.Codex, AlertKind.CodexUnreadable));
        }
    }
}

/// <summary>
/// The notification threshold (<c>"AlertThreshold": 80</c>, 0 or missing is
/// off) and what was already said (<c>"Alerts"</c>) in settings.json.
/// </summary>
public sealed class AlertStore
{
    private const string ThresholdKey = "AlertThreshold";
    private const string MemoryKey = "Alerts";

    private readonly SettingsFile _file;

    public AlertStore(string path)
    {
        _file = new SettingsFile(path);
    }

    /// <summary>The picked threshold; off when none was picked or it is not one the menu offers.</summary>
    public int LoadThreshold() =>
        _file.Read(ThresholdKey) is JsonValue value && value.TryGetValue<int>(out var threshold)
            && UsageAlerts.Thresholds.Contains(threshold)
            ? threshold
            : 0;

    public bool SaveThreshold(int threshold) => _file.Write(ThresholdKey, JsonValue.Create(threshold));

    public AlertMemory LoadMemory()
    {
        try
        {
            return _file.Read(MemoryKey)?.Deserialize<AlertMemory>() ?? new AlertMemory();
        }
        catch (JsonException)
        {
            return new AlertMemory();
        }
    }

    public bool SaveMemory(AlertMemory memory) =>
        _file.Write(MemoryKey, JsonSerializer.SerializeToNode(memory) ?? new JsonObject());
}
