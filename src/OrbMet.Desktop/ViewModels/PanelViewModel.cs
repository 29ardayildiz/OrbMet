using System.Diagnostics;
using System.Collections.ObjectModel;
using System.Windows.Threading;
using OrbMet.Core.Models;
using OrbMet.Core.Providers.Claude;
using OrbMet.Core.Usage;
using OrbMet.Desktop.Infrastructure;

namespace OrbMet.Desktop.ViewModels;

/// <summary>
/// Drives the panel: reads both providers off the UI thread on a timer and on
/// demand, and maps each result onto its fixed slot. No ambient CPU loop; the
/// work happens only when a refresh is due.
/// </summary>
public sealed class PanelViewModel : ObservableObject, IDisposable
{
    /// <summary>A refresh stays visible at least this long so even a fast local read registers.</summary>
    private static readonly TimeSpan MinimumRefreshDisplay = TimeSpan.FromMilliseconds(650);

    private readonly UsageCoordinator _coordinator;
    private readonly Func<ClaudeIntegrationState?>? _claudeLink;
    private readonly DispatcherTimer _timer;
    private readonly CancellationTokenSource _cancellation = new();
    private bool _disposed;

    private bool _isRefreshing;
    private string _lastUpdatedText = string.Empty;

    // What the menu's last line says, kept as facts so a language change can
    // say it again: when the last read finished, or that it failed.
    private DateTimeOffset? _lastChecked;
    private bool _lastReadFailed;

    /// <param name="claudeLink">
    /// Reads Claude's status line setting, so the card can say when it runs a
    /// helper that is gone. Read only; null when it cannot be resolved.
    /// </param>
    public PanelViewModel(UsageCoordinator coordinator, Func<ClaudeIntegrationState?>? claudeLink = null)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _claudeLink = claudeLink;

        Claude = new ProviderSlotViewModel(ProviderId.ClaudeCode, "Claude Code", "claude");
        Codex = new ProviderSlotViewModel(ProviderId.Codex, "Codex", "openai");
        Providers = new ObservableCollection<ProviderSlotViewModel> { Claude, Codex };

        RefreshCommand = new RelayCommand(() => RefreshAsync(showProgress: true), () => !IsRefreshing);

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(120) };
        _timer.Tick += async (_, _) => await RefreshAsync();
        Localizer.Instance.LanguageChanged += (_, _) => ShowLastUpdated();
    }

    public ProviderSlotViewModel Claude { get; }

    public ProviderSlotViewModel Codex { get; }

    public ObservableCollection<ProviderSlotViewModel> Providers { get; }

    public RelayCommand RefreshCommand { get; }

    public bool IsRefreshing
    {
        get => _isRefreshing;
        private set
        {
            if (SetProperty(ref _isRefreshing, value))
            {
                OnPropertyChanged(nameof(CanRefresh));
                RefreshCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool CanRefresh => !IsRefreshing;

    public string LastUpdatedText
    {
        get => _lastUpdatedText;
        private set => SetProperty(ref _lastUpdatedText, value);
    }

    public async Task StartAsync()
    {
        _timer.Start();
        await RefreshAsync();
    }

    /// <summary>
    /// Reads every provider. The timer refreshes quietly; a refresh the user
    /// asked for shows each ring's refreshing state for at least
    /// <see cref="MinimumRefreshDisplay"/> so even a fast local read registers.
    /// </summary>
    public async Task RefreshAsync(bool showProgress = false)
    {
        if (IsRefreshing || _disposed)
        {
            return;
        }

        IsRefreshing = true;
        var started = Stopwatch.StartNew();
        if (showProgress)
        {
            SetSlotsRefreshing(true);
        }

        try
        {
            var results = await Task.Run(() => _coordinator.ReadAllAsync(_cancellation.Token));
            foreach (var usage in results)
            {
                SlotFor(usage.Snapshot.ProviderId)?.Apply(usage);
            }

            await CheckClaudeLinkAsync();

            _lastChecked = DateTimeOffset.Now;
            _lastReadFailed = false;
            ShowLastUpdated();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            _lastReadFailed = true;
            ShowLastUpdated();
        }
        finally
        {
            if (showProgress)
            {
                await HoldAsync(started);
                SetSlotsRefreshing(false);
            }

            IsRefreshing = false;
        }
    }

    /// <summary>A click on one ring: reads that provider alone.</summary>
    public async Task RefreshProviderAsync(ProviderSlotViewModel slot)
    {
        ArgumentNullException.ThrowIfNull(slot);
        if (IsRefreshing || slot.IsRefreshing || _disposed)
        {
            return;
        }

        slot.IsRefreshing = true;
        var started = Stopwatch.StartNew();
        try
        {
            var usage = await Task.Run(() => _coordinator.ReadAsync(slot.ProviderId, _cancellation.Token));
            if (usage is not null)
            {
                slot.Apply(usage);
            }

            if (slot == Claude)
            {
                await CheckClaudeLinkAsync();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            _lastReadFailed = true;
            ShowLastUpdated();
        }
        finally
        {
            await HoldAsync(started);
            slot.IsRefreshing = false;
        }
    }

    /// <summary>Shows another limit in the rings at once; later reads pick by it too.</summary>
    public void UseRingChoice(RingChoice choice)
    {
        _coordinator.RingChoice = choice;
        foreach (var slot in Providers)
        {
            slot.Reselect(choice);
        }
    }

    /// <summary>
    /// Whether Claude is connected, and whether it runs a helper that is gone;
    /// cheap, a read of its settings file. Returns what was read, or null.
    /// </summary>
    public async Task<ClaudeIntegrationState?> CheckClaudeLinkAsync()
    {
        if (_claudeLink is null || _disposed)
        {
            return null;
        }

        try
        {
            var state = await Task.Run(_claudeLink);
            Claude.LinkBroken = state?.HelperMissing == true;

            // Only a settings file that was read says "not connected"; an
            // unreadable one is the Connections window's to report.
            Claude.NotConnected = state is { SettingsReadable: true, Connected: false };
            return state;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void ShowLastUpdated()
    {
        var text = Localizer.Instance.Text;
        LastUpdatedText = _lastReadFailed
            ? text["ReadFailed"]
            : _lastChecked is { } checkedAt
                ? text.Format("LastCheckFormat", checkedAt.ToString("HH:mm", text.Culture))
                : string.Empty;
    }

    private async Task HoldAsync(Stopwatch started)
    {
        var remaining = MinimumRefreshDisplay - started.Elapsed;
        if (remaining > TimeSpan.Zero && !_disposed)
        {
            try
            {
                await Task.Delay(remaining, _cancellation.Token);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private void SetSlotsRefreshing(bool value)
    {
        foreach (var slot in Providers)
        {
            slot.IsRefreshing = value;
        }
    }

    private ProviderSlotViewModel? SlotFor(ProviderId providerId) => providerId switch
    {
        ProviderId.ClaudeCode => Claude,
        ProviderId.Codex => Codex,
        _ => null,
    };

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Stop();
        _cancellation.Cancel();
        _cancellation.Dispose();
    }
}
