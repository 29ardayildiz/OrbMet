using OrbMet.Core.Providers.Claude;

namespace OrbMet.Desktop.ViewModels;

/// <summary>
/// Drives the connect/disconnect surface for the Claude Code status line. The
/// heavy work runs off the UI thread, and every outcome is described in plain
/// language rather than exposing JSON or stack traces.
/// </summary>
public sealed class ConnectionsViewModel : ObservableObject
{
    private const string WaitingDetail = "Claude Code'un sonraki yanıtı bekleniyor.";
    private const string ReceivingDetail = "Claude Code kullanım verisini OrbMet'e gönderiyor.";

    private readonly Func<ClaudeStatusLineIntegration?> _integrationFactory;
    private readonly Func<bool> _isReceiving;
    private readonly string? _captureExecutablePath;
    private readonly string? _settingsPath;

    private string _statusText = "Bağlantı kontrol ediliyor…";
    private string _detailText = string.Empty;
    private bool _canToggle;
    private bool _isBusy;
    private bool _connected;
    private bool _showsConnectedDetail;

    public ConnectionsViewModel(
        string? captureExecutablePath,
        string? settingsPath,
        Func<ClaudeStatusLineIntegration?> integrationFactory,
        Func<bool> isReceiving)
    {
        _isReceiving = isReceiving ?? throw new ArgumentNullException(nameof(isReceiving));
        _captureExecutablePath = captureExecutablePath;
        _settingsPath = settingsPath;
        _integrationFactory = integrationFactory ?? throw new ArgumentNullException(nameof(integrationFactory));
        ToggleCommand = new RelayCommand(ToggleAsync, () => CanToggle);
    }

    public RelayCommand ToggleCommand { get; }

    /// <summary>
    /// The one file Connect changes, with the profile folder as "~"; the
    /// window shows no other path (design.md 12).
    /// </summary>
    public string? SettingsFootnote => _settingsPath is null ? null : "Değişen ayar: " + ShortPath(_settingsPath);

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string DetailText
    {
        get => _detailText;
        private set => SetProperty(ref _detailText, value);
    }

    /// <summary>Whether the button offers Ayır rather than Bağla.</summary>
    public bool IsConnected
    {
        get => _connected;
        private set => SetProperty(ref _connected, value);
    }

    public bool CanToggle
    {
        get => _canToggle;
        private set
        {
            if (SetProperty(ref _canToggle, value))
            {
                ToggleCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public async Task RefreshAsync()
    {
        var integration = _integrationFactory();
        if (integration is null)
        {
            var helperMissing = _captureExecutablePath is null;
            SetState(
                helperMissing ? "Yardımcı bulunamadı" : "Claude ayarları bulunamadı",
                helperMissing
                    ? "Capture yardımcısı uygulamanın yanında bulunamadı."
                    : "Kullanıcı profilindeki Claude ayar yolu çözülemedi.",
                canToggle: false,
                connected: false);
            return;
        }

        var state = await Task.Run(integration.GetState).ConfigureAwait(true);

        if (!state.SettingsReadable)
        {
            SetState(
                "Claude ayarları okunamadı",
                "Dosya değiştirilmedi. Ayarlar geçerli JSON olmadan OrbMet yazmaz.",
                canToggle: true,
                connected: false);
            return;
        }

        if (state.Connected)
        {
            SetConnected("Bağlı");
        }
        else
        {
            SetState(
                "Bağlı değil",
                "Bağla, Claude Code'un status line'ını OrbMet'e yönlendirir. Mevcut status line ve diğer ayarlar korunur.",
                canToggle: true,
                connected: false);
        }
    }

    private async Task ToggleAsync()
    {
        var integration = _integrationFactory();
        if (integration is null)
        {
            await RefreshAsync().ConfigureAwait(true);
            return;
        }

        var wasConnected = IsConnected;
        IsBusy = true;
        CanToggle = false;

        ClaudeIntegrationResult result;
        try
        {
            result = await Task
                .Run(() => wasConnected ? integration.Disconnect() : integration.Connect())
                .ConfigureAwait(true);
        }
        catch (Exception)
        {
            IsBusy = false;
            SetState(
                "Beklenmeyen hata",
                "İşlem tamamlanamadı; dosya değiştirilmedi.",
                canToggle: true,
                connected: wasConnected);
            return;
        }

        IsBusy = false;
        ApplyResult(result, wasConnected);
    }

    private void ApplyResult(ClaudeIntegrationResult result, bool wasConnected)
    {
        switch (result.Status)
        {
            case ClaudeIntegrationStatus.Connected:
                SetConnected("Bağlandı");
                break;

            case ClaudeIntegrationStatus.Disconnected:
                SetState(
                    "Bağlantı kaldırıldı",
                    "Önceki status line geri yüklendi.",
                    canToggle: true,
                    connected: false);
                break;

            case ClaudeIntegrationStatus.Conflict:
                SetState(
                    "Değişiklik çakıştı",
                    wasConnected
                        ? "Status line'ı siz değiştirdiniz; OrbMet dokunmadı."
                        : "Dosya siz düzenlerken değişti; tekrar deneyin.",
                    canToggle: true,
                    connected: wasConnected);
                break;

            case ClaudeIntegrationStatus.NotConnected:
                SetState("Bağlı değil", string.Empty, canToggle: true, connected: false);
                break;

            default:
                SetState(
                    "Claude ayarları okunamadı",
                    "Dosya değiştirilmedi.",
                    canToggle: true,
                    connected: wasConnected);
                break;
        }
    }

    /// <summary>
    /// Re-reads whether Claude's data is arriving; the panel calls this when a
    /// reading lands while the window is open.
    /// </summary>
    public void RefreshReceiving()
    {
        if (_showsConnectedDetail)
        {
            DetailText = ConnectedDetail;
        }
    }

    // Connected only says the status line points at OrbMet; whether data has
    // actually come through is a separate fact, and the detail says which.
    private string ConnectedDetail => _isReceiving() ? ReceivingDetail : WaitingDetail;

    private void SetConnected(string status)
    {
        SetState(status, ConnectedDetail, canToggle: true, connected: true);
        _showsConnectedDetail = true;
    }

    private void SetState(string status, string detail, bool canToggle, bool connected)
    {
        _showsConnectedDetail = false;
        StatusText = status;
        DetailText = detail;
        CanToggle = canToggle;
        IsConnected = connected;
    }

    private static string ShortPath(string path)
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return profile.Length > 0 && path.StartsWith(profile + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            ? "~" + path[profile.Length..]
            : path;
    }
}
