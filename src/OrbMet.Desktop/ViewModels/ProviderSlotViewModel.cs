using OrbMet.Core.Models;
using OrbMet.Core.Usage;
using OrbMet.Desktop.Infrastructure;

namespace OrbMet.Desktop.ViewModels;

/// <summary>
/// One provider's fixed slot: the strip's ring and percentage, and the detail
/// card's content (design.md 6 and 7). Time-dependent text is rebuilt from the
/// last snapshot whenever <see cref="UpdateDetails"/> runs, so it never goes
/// stale on screen, and again in the new words when the language changes.
/// </summary>
public sealed class ProviderSlotViewModel : ObservableObject
{
    private ProviderUsage? _usage;
    private double? _ringPercent;
    private double? _clockFraction;
    private System.Windows.Media.Brush _goodBrush;
    private bool _isExhausted;
    private bool _hasReading;
    private bool _isSpent;
    private bool _isRefreshing;
    private bool _isSelected;
    private string _percentText = "—";
    private string _accessibleText = string.Empty;
    private string? _message = Localizer.Instance["CheckingConnection"];
    private string? _footnote;
    private string? _planText;
    private bool _isReceiving;
    private bool _linkBroken;
    private bool _notConnected;
    private IReadOnlyList<UsageWindowRowViewModel> _rows = Array.Empty<UsageWindowRowViewModel>();

    public ProviderSlotViewModel(ProviderId providerId, string displayName, string mark)
    {
        ProviderId = providerId;
        DisplayName = displayName;
        Mark = mark;
        _goodBrush = AppearanceMenu.GoodFor(providerId);
        Localizer.Instance.LanguageChanged += (_, _) => OnLanguageChanged();
        AppearanceMenu.Changed += Rebuild;
    }

    public ProviderId ProviderId { get; }

    public string DisplayName { get; }

    /// <summary>The card heading's spoken name, "Claude Code usage".</summary>
    public string CardTitle => Localizer.Instance.Text.Format("CardTitleFormat", DisplayName);

    /// <summary>Asset name of the provider logo drawn in the ring and the card.</summary>
    public string Mark { get; }

    /// <summary>Provider-reported percentage for the ring, or null when missing.</summary>
    public double? RingPercent
    {
        get => _ringPercent;
        private set => SetProperty(ref _ringPercent, value);
    }

    /// <summary>
    /// How much of the ring's window has passed, 0 to 1, for the window clock
    /// arc; null when it is off or the window's length or reset is unknown.
    /// </summary>
    public double? ClockFraction
    {
        get => _clockFraction;
        private set => SetProperty(ref _clockFraction, value);
    }

    /// <summary>The colour this provider's ring, bars and figure take while below the warning thresholds.</summary>
    public System.Windows.Media.Brush GoodBrush
    {
        get => _goodBrush;
        private set => SetProperty(ref _goodBrush, value);
    }

    /// <summary>True only when the provider explicitly reports being blocked.</summary>
    public bool IsExhausted
    {
        get => _isExhausted;
        private set => SetProperty(ref _isExhausted, value);
    }

    /// <summary>The ring has a reported value; without one its percentage is dimmed.</summary>
    public bool HasReading
    {
        get => _hasReading;
        private set => SetProperty(ref _hasReading, value);
    }

    /// <summary>Spent colour on the strip's percentage: blocked, or at or past 100%.</summary>
    public bool IsSpent
    {
        get => _isSpent;
        private set => SetProperty(ref _isSpent, value);
    }

    /// <summary>A refresh the user asked for is in flight: the arc dims and a segment travels.</summary>
    public bool IsRefreshing
    {
        get => _isRefreshing;
        internal set => SetProperty(ref _isRefreshing, value);
    }

    /// <summary>The card is open for this slot: the ring grows slightly and casts a halo.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        internal set => SetProperty(ref _isSelected, value);
    }

    public string PercentText
    {
        get => _percentText;
        private set => SetProperty(ref _percentText, value);
    }

    /// <summary>Spoken description, since colour never carries the meaning alone.</summary>
    public string AccessibleText
    {
        get => _accessibleText;
        private set => SetProperty(ref _accessibleText, value);
    }

    /// <summary>A line explaining why there is nothing (or something wrong) to show; null when all is well.</summary>
    public string? Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    /// <summary>Shown only for a stale reading: when the provider last reported.</summary>
    public string? Footnote
    {
        get => _footnote;
        private set => SetProperty(ref _footnote, value);
    }

    /// <summary>"Plan: Plus" in the Connections window; null when the provider did not report one.</summary>
    public string? PlanText
    {
        get => _planText;
        private set => SetProperty(ref _planText, value);
    }

    /// <summary>
    /// The provider is connected and its last reading is fresh: data is
    /// arriving, not merely remembered from an earlier session.
    /// </summary>
    public bool IsReceiving
    {
        get => _isReceiving;
        private set => SetProperty(ref _isReceiving, value);
    }

    /// <summary>
    /// Claude is set to run a helper that is gone (OrbMet was moved): the card
    /// says so ahead of anything else, since no data can arrive until the user
    /// reconnects.
    /// </summary>
    public bool LinkBroken
    {
        get => _linkBroken;
        internal set
        {
            if (SetProperty(ref _linkBroken, value))
            {
                OnPropertyChanged(nameof(NeedsConnecting));
                Rebuild();
            }
        }
    }

    /// <summary>
    /// Claude Code's settings carry no OrbMet status line: nothing can arrive
    /// until the user connects it, which the card says and a click on the ring
    /// leads to.
    /// </summary>
    public bool NotConnected
    {
        get => _notConnected;
        internal set
        {
            if (SetProperty(ref _notConnected, value))
            {
                OnPropertyChanged(nameof(NeedsConnecting));
                Rebuild();
            }
        }
    }

    /// <summary>A click on the ring opens Connections rather than refreshing.</summary>
    public bool NeedsConnecting => _linkBroken || _notConnected;

    public IReadOnlyList<UsageWindowRowViewModel> Rows
    {
        get => _rows;
        private set => SetProperty(ref _rows, value);
    }

    public void Apply(ProviderUsage usage)
    {
        var text = Localizer.Instance.Usage;
        _usage = usage;
        var window = usage.RingWindow;

        IsExhausted = window?.ExhaustionEvidence == ExhaustionEvidence.Exhausted;
        RingPercent = window?.UsedPercent;
        HasReading = RingPercent is not null;
        IsSpent = IsExhausted || RingPercent >= 100;
        var display = AppearanceMenu.Current;
        PercentText = text.Figure(RingPercent, IsExhausted, display.ShowRemaining);
        PlanText = text.Plan(usage.Snapshot.PlanName);
        GoodBrush = AppearanceMenu.GoodFor(ProviderId);

        UpdateDetails(DateTimeOffset.UtcNow, TimeZoneInfo.Local);

        var words = Localizer.Instance.Text;
        AccessibleText = window?.UsedPercent is { } percent
            ? display.ShowRemaining
                ? words.Format("SpokenRemainingFormat", DisplayName, text.LimitTitle(window, DisplayName), UsageText.Remaining(percent, IsExhausted)!.Value.ToString("0", words.Culture))
                : words.Format("SpokenUsageFormat", DisplayName, text.LimitTitle(window, DisplayName), percent.ToString("0", words.Culture))
            : words.Format("SpokenNoDataFormat", DisplayName);
        if (ClockFraction is { } elapsed)
        {
            AccessibleText += ", " + words.Format("WindowElapsedFormat", text.Percent(Math.Round(elapsed * 100)));
        }

        if ((Message ?? Footnote) is { } note)
        {
            AccessibleText += $", {note}";
        }
    }

    /// <summary>The last reading, or null before the first one.</summary>
    public ProviderUsage? Usage => _usage;

    /// <summary>Picks the ring's window again from the last reading, without reading the provider.</summary>
    public void Reselect(RingChoice choice)
    {
        if (_usage is { } usage)
        {
            Apply(usage with { RingWindow = UsageSelection.SelectRingWindow(usage.Snapshot, choice) });
        }
    }

    /// <summary>Rebuilds the card's time-dependent text against the given moment.</summary>
    public void UpdateDetails(DateTimeOffset nowUtc, TimeZoneInfo zone)
    {
        if (_usage is not { } usage)
        {
            return;
        }

        var snapshot = usage.Snapshot;
        ClockFraction = AppearanceMenu.Current.WindowClock && usage.RingWindow is { } ringWindow
            ? UsagePace.Elapsed(ringWindow, nowUtc)
            : null;
        IsReceiving = snapshot.ConnectionState == ConnectionState.Ready
            && snapshot.Freshness == Freshness.Fresh
            && snapshot.Windows.Count > 0;
        Rows = snapshot.Windows.Select(window => BuildRow(window, nowUtc, zone)).ToList();
        Message = BuildMessage(snapshot, nowUtc);
        Footnote = snapshot.Windows.Count > 0 && snapshot.Freshness == Freshness.Stale
            ? Localizer.Instance.Usage.StaleFootnote(snapshot.ObservedAtUtc, nowUtc, zone)
            : null;
    }

    private UsageWindowRowViewModel BuildRow(UsageWindow window, DateTimeOffset nowUtc, TimeZoneInfo zone)
    {
        var text = Localizer.Instance.Usage;
        var title = text.LimitTitle(window, DisplayName);
        var expired = UsageText.IsPast(window.ResetsAtUtc, nowUtc);
        var resetText = text.ResetAt(window.ResetsAtUtc, nowUtc, zone);

        // A window whose reset has passed belongs to the old period; its value
        // is hidden rather than shown as the new period's usage.
        var percent = expired ? null : window.UsedPercent;
        var exhausted = !expired && window.ExhaustionEvidence == ExhaustionEvidence.Exhausted;
        var display = AppearanceMenu.Current;
        var usedText = expired ? string.Empty : text.UsedOrLeft(percent, exhausted, display.ShowRemaining);

        // A forecast, marked as one, never in place of the figure.
        var forecast = display.Forecast && !expired ? text.Forecast(UsagePace.Forecast(window, nowUtc)) : null;

        var spoken = expired
            ? $"{title}, {resetText}"
            : string.IsNullOrEmpty(resetText) ? $"{title}, {usedText}" : $"{title}, {usedText}, {resetText}";
        if (forecast is not null)
        {
            spoken += $", {forecast}";
        }

        return new UsageWindowRowViewModel
        {
            Title = title,
            Percent = percent,
            UsedText = usedText,
            ForecastText = forecast,
            GoodBrush = AppearanceMenu.GoodFor(ProviderId),
            IsExhausted = exhausted,
            IsSpent = exhausted || percent >= 100,
            ResetText = resetText,
            IsExpired = expired,
            AccessibleText = spoken,
        };
    }

    /// <summary>The card's message line from design.md section 12; null when the rows say it all.</summary>
    private string? BuildMessage(ProviderSnapshot snapshot, DateTimeOffset nowUtc)
    {
        var words = Localizer.Instance.Text;
        if (_linkBroken)
        {
            return words["ClaudeLinkBroken"];
        }

        if (_notConnected)
        {
            return words["ClaudeNotConnected"];
        }

        switch (snapshot.ConnectionState)
        {
            case ConnectionState.Disabled:
                return words["MonitoringOff"];
            case ConnectionState.MissingTool:
                return words.Format("NotFoundFormat", DisplayName);
            case ConnectionState.SignInRequired:
                return ProviderId == ProviderId.Codex
                    ? words["CodexSignInRequired"]
                    : words["SignInRequired"];
            case ConnectionState.Unsupported:
                return words["Unsupported"];
            case ConnectionState.Error:
                return words["CouldNotRead"];
            case ConnectionState.AwaitingData when snapshot.ErrorCode == "claude-no-capture":
                return words["ClaudeAwaiting"];
            case ConnectionState.AwaitingData:
                return words["AwaitingUsage"];
        }

        if (snapshot.Windows.Count == 0)
        {
            return words["AwaitingUsage"];
        }

        var blocked = snapshot.Windows.Any(window =>
            window.ExhaustionEvidence == ExhaustionEvidence.Exhausted &&
            !UsageText.IsPast(window.ResetsAtUtc, nowUtc));
        return blocked ? words["LimitReached"] : null;
    }

    private void OnLanguageChanged()
    {
        OnPropertyChanged(nameof(CardTitle));
        Rebuild();
    }

    // Says the last reading again: in a new language, or with the link state.
    private void Rebuild()
    {
        if (_usage is { } usage)
        {
            Apply(usage);
        }
        else
        {
            Message = Localizer.Instance[
                _linkBroken ? "ClaudeLinkBroken" : _notConnected ? "ClaudeNotConnected" : "CheckingConnection"];
        }
    }
}
