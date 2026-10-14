using OrbMet.Core.Providers.Claude;
using OrbMet.Desktop.Infrastructure;

namespace OrbMet.Desktop.ViewModels;

/// <summary>
/// Drives the connect/disconnect surface for the Claude Code status line. The
/// heavy work runs off the UI thread, and every outcome is described in plain
/// language rather than exposing JSON or stack traces. A state is kept as
/// its text keys, so a language change shows the same state in new words.
/// </summary>
public sealed class ConnectionsViewModel : ObservableObject, IDisposable
{

    private readonly Func<ClaudeStatusLineIntegration?> _integrationFactory;
    private readonly Func<bool> _isReceiving;
    private readonly string? _captureExecutablePath;
    private readonly string? _settingsPath;

    private string _statusKey = "CheckingConnection";
    private string? _detailKey;
    private string _statusText = string.Empty;
    private string _detailText = string.Empty;
    private bool _canToggle;
    private bool _isBusy;
    private bool _connected;
    private bool _needsReconnect;
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
        ReconnectCommand = new RelayCommand(ReconnectAsync, () => CanToggle);
        ShowState();
        Localizer.Instance.LanguageChanged += OnLanguageChanged;
    }

    public RelayCommand ToggleCommand { get; }

    /// <summary>Points Claude at this copy's helper when it runs another's.</summary>
    public RelayCommand ReconnectCommand { get; }

    /// <summary>
    /// The one file Connect changes, with the profile folder as "~"; the
    /// window shows no other path (design.md 12).
    /// </summary>
    public string? SettingsFootnote => _settingsPath is null
        ? null
        : Localizer.Instance.Text.Format("SettingsFileFormat", ShortPath(_settingsPath));

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

    /// <summary>Whether the button offers Disconnect rather than Connect.</summary>
    public bool IsConnected
    {
        get => _connected;
        private set => SetProperty(ref _connected, value);
    }

    /// <summary>
    /// Connected, but to another OrbMet copy's helper (this folder was moved,
    /// or another copy connected last): Reconnect is offered beside Disconnect.
    /// </summary>
    public bool NeedsReconnect
    {
        get => _needsReconnect;
        private set => SetProperty(ref _needsReconnect, value);
    }

    public bool CanToggle
    {
        get => _canToggle;
        private set
        {
            if (SetProperty(ref _canToggle, value))
            {
                ToggleCommand.RaiseCanExecuteChanged();
                ReconnectCommand.RaiseCanExecuteChanged();
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
                helperMissing ? "HelperMissing" : "SettingsMissing",
                helperMissing ? "HelperMissingExplained" : "SettingsMissingExplained",
                canToggle: false,
                connected: false);
            return;
        }

        var state = await Task.Run(integration.GetState).ConfigureAwait(true);

        if (!state.SettingsReadable)
        {
            SetState(
                "SettingsUnreadable",
                "SettingsInvalidExplained",
                canToggle: true,
                connected: false);
            return;
        }

        if (state.Connected && state.PointsElsewhere)
        {
            // Connecting again is the fix, so it is offered, never done here:
            // Claude's settings change only when the user asks (AGENTS.md 6).
            SetState(
                state.HelperMissing ? "LinkBroken" : "ConnectedElsewhere",
                state.HelperMissing ? "LinkBrokenExplained" : "ElsewhereExplained",
                canToggle: true,
                connected: true);
            NeedsReconnect = true;
        }
        else if (state.Connected)
        {
            SetConnected("Connected");
        }
        else
        {
            SetState(
                "NotConnected",
                "ConnectExplained",
                canToggle: true,
                connected: false);
        }
    }

    private Task ToggleAsync() => RunAsync(disconnect: IsConnected);

    private Task ReconnectAsync() => RunAsync(disconnect: false);

    private async Task RunAsync(bool disconnect)
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
                .Run(() => disconnect ? integration.Disconnect() : integration.Connect())
                .ConfigureAwait(true);
        }
        catch (Exception)
        {
            IsBusy = false;
            SetState(
                "UnexpectedError",
                "UnexpectedErrorExplained",
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
                SetConnected("JustConnected");
                break;

            case ClaudeIntegrationStatus.Disconnected:
                SetState(
                    "Disconnected",
                    "DisconnectedExplained",
                    canToggle: true,
                    connected: false);
                break;

            case ClaudeIntegrationStatus.Conflict:
                SetState(
                    "Conflict",
                    wasConnected ? "ConflictUserChanged" : "ConflictFileChanged",
                    canToggle: true,
                    connected: wasConnected);
                break;

            case ClaudeIntegrationStatus.NotConnected:
                SetState("NotConnected", null, canToggle: true, connected: false);
                break;

            default:
                SetState(
                    "SettingsUnreadable",
                    "NothingChanged",
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
            ShowState();
        }
    }

    public void Dispose() => Localizer.Instance.LanguageChanged -= OnLanguageChanged;

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        ShowState();
        OnPropertyChanged(nameof(SettingsFootnote));
    }

    // Connected only says the status line points at OrbMet; whether data has
    // actually come through is a separate fact, and the detail says which.
    private string ConnectedDetailKey => _isReceiving() ? "ClaudeReceiving" : "ClaudeWaiting";

    private void SetConnected(string statusKey)
    {
        SetState(statusKey, null, canToggle: true, connected: true);
        _showsConnectedDetail = true;
        ShowState();
    }

    private void SetState(string statusKey, string? detailKey, bool canToggle, bool connected)
    {
        _showsConnectedDetail = false;
        NeedsReconnect = false;
        _statusKey = statusKey;
        _detailKey = detailKey;
        CanToggle = canToggle;
        IsConnected = connected;
        ShowState();
    }

    private void ShowState()
    {
        var text = Localizer.Instance.Text;
        StatusText = text[_statusKey];
        var detailKey = _showsConnectedDetail ? ConnectedDetailKey : _detailKey;
        DetailText = detailKey is null ? string.Empty : text[detailKey];
    }

    private static string ShortPath(string path)
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return profile.Length > 0 && path.StartsWith(profile + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            ? "~" + path[profile.Length..]
            : path;
    }
}
