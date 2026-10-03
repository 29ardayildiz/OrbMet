using System.Collections.ObjectModel;
using System.Windows.Threading;
using OrbMet.Core.Models;
using OrbMet.Core.Usage;

namespace OrbMet.Desktop.ViewModels;

/// <summary>
/// Drives the panel: reads both providers off the UI thread on a timer and on
/// demand, and maps each result onto its fixed slot. No ambient CPU loop; the
/// work happens only when a refresh is due.
/// </summary>
public sealed class PanelViewModel : ObservableObject, IDisposable
{
    private readonly UsageCoordinator _coordinator;
    private readonly DispatcherTimer _timer;
    private readonly CancellationTokenSource _cancellation = new();
    private bool _disposed;

    private bool _isRefreshing;
    private string _lastUpdatedText = string.Empty;

    public PanelViewModel(UsageCoordinator coordinator)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));

        Claude = new ProviderSlotViewModel("Claude Code", "C");
        Codex = new ProviderSlotViewModel("Codex", "X");
        Providers = new ObservableCollection<ProviderSlotViewModel> { Claude, Codex };

        RefreshCommand = new RelayCommand(RefreshAsync, () => !IsRefreshing);

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(120) };
        _timer.Tick += async (_, _) => await RefreshAsync();
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

    public async Task RefreshAsync()
    {
        if (IsRefreshing || _disposed)
        {
            return;
        }

        IsRefreshing = true;
        try
        {
            var results = await Task.Run(() => _coordinator.ReadAllAsync(_cancellation.Token));
            foreach (var usage in results)
            {
                SlotFor(usage.Snapshot.ProviderId)?.Apply(usage);
            }

            LastUpdatedText = $"Son kontrol: {DateTimeOffset.Now:HH:mm}";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            LastUpdatedText = "Kullanım okunamadı";
        }
        finally
        {
            IsRefreshing = false;
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
