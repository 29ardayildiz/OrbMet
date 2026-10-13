using OrbMet.Core.Models;
using OrbMet.Core.Usage;

namespace OrbMet.Desktop.ViewModels;

/// <summary>
/// One provider's fixed slot: the strip's ring and percentage, and the detail
/// card's per-window rows. Relative times are rebuilt from the last snapshot
/// whenever <see cref="UpdateDetails"/> runs, so they never go stale on screen.
/// </summary>
public sealed class ProviderSlotViewModel : ObservableObject
{
    private ProviderUsage? _usage;
    private double? _ringPercent;
    private bool _isExhausted;
    private string _percentText = "—";
    private string _windowLabel = string.Empty;
    private string _statusText = "Bağlantı kontrol ediliyor…";
    private string _accessibleText = string.Empty;
    private bool _isStale;
    private string? _planText;
    private string _observedText = string.Empty;
    private bool _needsConnectionCheck;
    private IReadOnlyList<UsageWindowRowViewModel> _rows = Array.Empty<UsageWindowRowViewModel>();

    public ProviderSlotViewModel(ProviderId providerId, string displayName, string glyph, string hintText)
    {
        ProviderId = providerId;
        DisplayName = displayName;
        Glyph = glyph;
        HintText = hintText;
    }

    public ProviderId ProviderId { get; }

    public string DisplayName { get; }

    /// <summary>Temporary text marker until real provider icons exist.</summary>
    public string Glyph { get; }

    /// <summary>How this provider's data gets refreshed, so "Yenile" is not oversold.</summary>
    public string HintText { get; }

    /// <summary>Provider-reported percentage for the ring, or null when missing.</summary>
    public double? RingPercent
    {
        get => _ringPercent;
        private set => SetProperty(ref _ringPercent, value);
    }

    /// <summary>True only when the provider explicitly reports being blocked.</summary>
    public bool IsExhausted
    {
        get => _isExhausted;
        private set => SetProperty(ref _isExhausted, value);
    }

    public string PercentText
    {
        get => _percentText;
        private set => SetProperty(ref _percentText, value);
    }

    public string WindowLabel
    {
        get => _windowLabel;
        private set => SetProperty(ref _windowLabel, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    /// <summary>Spoken description, since colour never carries the meaning alone.</summary>
    public string AccessibleText
    {
        get => _accessibleText;
        private set => SetProperty(ref _accessibleText, value);
    }

    /// <summary>The shown values are a last known reading, not a fresh one.</summary>
    public bool IsStale
    {
        get => _isStale;
        private set => SetProperty(ref _isStale, value);
    }

    /// <summary>Plan name, only when the provider reports one.</summary>
    public string? PlanText
    {
        get => _planText;
        private set => SetProperty(ref _planText, value);
    }

    public string ObservedText
    {
        get => _observedText;
        private set => SetProperty(ref _observedText, value);
    }

    /// <summary>The card offers the connections view instead of a refresh.</summary>
    public bool NeedsConnectionCheck
    {
        get => _needsConnectionCheck;
        private set => SetProperty(ref _needsConnectionCheck, value);
    }

    public IReadOnlyList<UsageWindowRowViewModel> Rows
    {
        get => _rows;
        private set => SetProperty(ref _rows, value);
    }

    public void Apply(ProviderUsage usage)
    {
        _usage = usage;
        var snapshot = usage.Snapshot;
        var window = usage.RingWindow;

        IsExhausted = window?.ExhaustionEvidence == ExhaustionEvidence.Exhausted;
        RingPercent = window?.UsedPercent;
        PercentText = UsageText.Percent(window?.UsedPercent);
        WindowLabel = window is null ? "Kullanım verisi yok" : UsageText.WindowLabel(window, DisplayName);
        StatusText = BuildStatus(snapshot);
        IsStale = snapshot.ConnectionState == ConnectionState.Ready && snapshot.Freshness == Freshness.Stale;
        PlanText = string.IsNullOrWhiteSpace(snapshot.PlanName) ? null : $"Plan: {snapshot.PlanName}";
        NeedsConnectionCheck = snapshot.ConnectionState != ConnectionState.Ready;
        AccessibleText = window?.UsedPercent is { } percent
            ? $"{DisplayName}, {WindowLabel}, kullanım yüzde {UsageText.Percent(percent).TrimStart('%')}, {StatusText}"
            : $"{DisplayName}, kullanım verisi yok, {StatusText}";

        UpdateDetails(DateTimeOffset.UtcNow, TimeZoneInfo.Local);
    }

    /// <summary>Rebuilds the card's time-dependent text against the given moment.</summary>
    public void UpdateDetails(DateTimeOffset nowUtc, TimeZoneInfo zone)
    {
        if (_usage is not { } usage)
        {
            return;
        }

        var snapshot = usage.Snapshot;
        ObservedText = snapshot.Windows.Count == 0 && snapshot.ObservedAtUtc is null
            ? string.Empty
            : UsageText.ObservedAge(snapshot.ObservedAtUtc, nowUtc, zone);

        Rows = snapshot.Windows
            .Select(window => BuildRow(window, usage.RingWindow, nowUtc, zone))
            .ToList();
    }

    private UsageWindowRowViewModel BuildRow(UsageWindow window, UsageWindow? ringWindow, DateTimeOffset nowUtc, TimeZoneInfo zone)
    {
        var title = UsageText.WindowLabel(window, DisplayName);
        var expired = UsageText.IsPast(window.ResetsAtUtc, nowUtc);
        var resetText = UsageText.Reset(window.ResetsAtUtc, nowUtc, zone);

        // A window whose reset has passed belongs to the old period; its value
        // is hidden rather than shown as the new period's usage.
        var percent = expired ? null : window.UsedPercent;
        var percentText = expired ? string.Empty : UsageText.Percent(percent);
        var exhausted = !expired && window.ExhaustionEvidence == ExhaustionEvidence.Exhausted;

        var spoken = expired
            ? $"{title}, {resetText}"
            : percent is { } value
                ? $"{title}, kullanım yüzde {UsageText.Percent(value).TrimStart('%')}, {resetText}"
                : $"{title}, kullanım bildirilmedi, {resetText}";

        return new UsageWindowRowViewModel
        {
            Title = title,
            Percent = percent,
            PercentText = percentText,
            IsExhausted = exhausted,
            ResetText = exhausted ? $"Sağlayıcı limite ulaşıldığını bildirdi · {resetText}" : resetText,
            IsExpired = expired,
            IsRingWindow = ReferenceEquals(window, ringWindow),
            AccessibleText = spoken,
        };
    }

    private string BuildStatus(ProviderSnapshot snapshot)
    {
        switch (snapshot.ConnectionState)
        {
            case ConnectionState.Disabled:
                return "İzleme kapalı";
            case ConnectionState.MissingTool:
                return $"{DisplayName} bulunamadı";
            case ConnectionState.SignInRequired:
                return "Oturum açman gerekiyor";
            case ConnectionState.Unsupported:
                return "Bu sürüm kullanım bilgisi sunmuyor";
            case ConnectionState.AwaitingData:
                return "Yeni kullanım verisi bekleniyor";
            case ConnectionState.Error:
                return "Kullanım şu anda okunamadı";
            case ConnectionState.Ready:
                if (snapshot.Windows.Count == 0)
                {
                    return "Yeni kullanım verisi bekleniyor";
                }

                return snapshot.Freshness switch
                {
                    Freshness.Fresh => "Güncel",
                    Freshness.Stale => snapshot.ObservedAtUtc is { } observed
                        ? $"Son bilinen veri: {observed.ToLocalTime():HH:mm}"
                        : "Son bilinen veri",
                    _ => "Veri bekleniyor",
                };
            default:
                return string.Empty;
        }
    }
}
