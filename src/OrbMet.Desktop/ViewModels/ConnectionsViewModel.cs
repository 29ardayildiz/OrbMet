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
    private string _buttonText = "Bağla";
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

    public string SettingsPathText => _settingsPath ?? "Claude ayarları bulunamadı";

    public string CapturePathText => _captureExecutablePath ?? "Capture yardımcısı bulunamadı";

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

    public string ButtonText
    {
        get => _buttonText;
        private set => SetProperty(ref _buttonText, value);
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
                "Bağla",
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
                "Bağla",
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
                "Bağla",
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

        var wasConnected = _connected;
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
                wasConnected ? "Ayır" : "Bağla",
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
                    "Bağla",
                    canToggle: true,
                    connected: false);
                break;

            case ClaudeIntegrationStatus.Conflict:
                SetState(
                    "Değişiklik çakıştı",
                    wasConnected
                        ? "Status line'ı siz değiştirdiniz; OrbMet dokunmadı."
                        : "Dosya siz düzenlerken değişti; tekrar deneyin.",
                    wasConnected ? "Ayır" : "Bağla",
                    canToggle: true,
                    connected: wasConnected);
                break;

            case ClaudeIntegrationStatus.NotConnected:
                SetState("Bağlı değil", string.Empty, "Bağla", canToggle: true, connected: false);
                break;

            default:
                SetState(
                    "Claude ayarları okunamadı",
                    "Dosya değiştirilmedi.",
                    wasConnected ? "Ayır" : "Bağla",
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
        SetState(status, ConnectedDetail, "Ayır", canToggle: true, connected: true);
        _showsConnectedDetail = true;
    }

    private void SetState(string status, string detail, string button, bool canToggle, bool connected)
    {
        _showsConnectedDetail = false;
        StatusText = status;
        DetailText = detail;
        ButtonText = button;
        CanToggle = canToggle;
        _connected = connected;
    }
}
