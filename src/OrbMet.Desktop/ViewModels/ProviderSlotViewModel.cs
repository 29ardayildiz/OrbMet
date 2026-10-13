using System.Globalization;
using OrbMet.Core.Models;
using OrbMet.Core.Usage;

namespace OrbMet.Desktop.ViewModels;

/// <summary>One provider's fixed slot in the panel: ring, percentage and status.</summary>
public sealed class ProviderSlotViewModel : ObservableObject
{
    private double? _ringPercent;
    private bool _isExhausted;
    private string _percentText = "—";
    private string _windowLabel = string.Empty;
    private string _statusText = "Bağlantı kontrol ediliyor…";
    private string _accessibleText = string.Empty;
    private string _toolTipText;

    public ProviderSlotViewModel(string displayName, string glyph)
    {
        DisplayName = displayName;
        Glyph = glyph;
        _toolTipText = $"{displayName}\n{_statusText}";
    }

    public string DisplayName { get; }

    /// <summary>Temporary text marker until real provider icons exist.</summary>
    public string Glyph { get; }

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

    /// <summary>Hover text for the strip, which has no room for labels.</summary>
    public string ToolTipText
    {
        get => _toolTipText;
        private set => SetProperty(ref _toolTipText, value);
    }

    public void Apply(ProviderUsage usage)
    {
        var window = usage.RingWindow;
        IsExhausted = window?.ExhaustionEvidence == ExhaustionEvidence.Exhausted;
        RingPercent = window?.UsedPercent is { } percent ? percent : null;
        // Turkish puts the sign first: %19, not 19%.
        PercentText = window?.UsedPercent is { } value
            ? $"%{value.ToString("0", CultureInfo.InvariantCulture)}"
            : "—";
        WindowLabel = BuildWindowLabel(window);
        StatusText = BuildStatus(usage.Snapshot);
        AccessibleText = BuildAccessibleText(usage.Snapshot, window);
        ToolTipText = window is null
            ? $"{DisplayName}\n{StatusText}"
            : $"{DisplayName}\n{WindowLabel} · {PercentText}\n{StatusText}";
    }

    private string BuildWindowLabel(UsageWindow? window)
    {
        if (window is null)
        {
            return "Kullanım verisi yok";
        }

        var label = window.DurationSeconds switch
        {
            { } seconds when seconds <= 6 * 60 * 60 => "5 saatlik",
            { } seconds when seconds <= 8 * 24 * 60 * 60 => "Haftalık",
            _ => null,
        } ?? window.ScopeLabel ?? "Kullanım limiti";

        if (window.ScopeLabel is { Length: > 0 } scope &&
            !label.Contains(scope, StringComparison.Ordinal) &&
            !string.Equals(scope, DisplayName, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(scope, "default", StringComparison.OrdinalIgnoreCase))
        {
            label = $"{label} · {scope}";
        }

        return label;
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

    private string BuildAccessibleText(ProviderSnapshot snapshot, UsageWindow? window)
    {
        if (window?.UsedPercent is { } percent)
        {
            return $"{DisplayName}, {BuildWindowLabel(window)}, kullanım yüzde {percent.ToString("0", CultureInfo.InvariantCulture)}";
        }

        return $"{DisplayName}, kullanım verisi yok";
    }
}
