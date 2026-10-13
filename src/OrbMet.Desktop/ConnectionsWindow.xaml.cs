using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using OrbMet.Core.Providers.Claude;
using OrbMet.Core.Storage;
using OrbMet.Desktop.Infrastructure;
using OrbMet.Desktop.ViewModels;

namespace OrbMet.Desktop;

/// <summary>
/// Connect or disconnect Claude Code's status line, and see each provider's
/// reported plan, which the detail card leaves out (design.md 7).
/// </summary>
public partial class ConnectionsWindow : Window
{
    private readonly ConnectionsViewModel _viewModel;

    public ConnectionsWindow(ProviderSlotViewModel claude, ProviderSlotViewModel codex)
    {
        InitializeComponent();
        OrbMetFrame.Attach(this);
        _viewModel = BuildViewModel(() => claude.IsReceiving);
        DataContext = _viewModel;
        ClaudePlan.DataContext = claude;
        CodexCard.DataContext = codex;

        PropertyChangedEventHandler onClaudeChanged = (_, e) =>
        {
            if (e.PropertyName == nameof(ProviderSlotViewModel.IsReceiving))
            {
                _viewModel.RefreshReceiving();
            }
        };
        claude.PropertyChanged += onClaudeChanged;
        Closed += (_, _) =>
        {
            claude.PropertyChanged -= onClaudeChanged;
            _viewModel.Dispose();
        };
        Loaded += async (_, _) => await _viewModel.RefreshAsync();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private static ConnectionsViewModel BuildViewModel(Func<bool> isReceiving)
    {
        string? capturePath = null;
        string? settingsPath = null;
        OrbMetPaths? paths = null;

        try
        {
            capturePath = CaptureHelperLocator.Locate();
            paths = OrbMetPaths.FromEnvironment();
            settingsPath = paths.ClaudeSettingsFile;
        }
        catch (Exception)
        {
            // A missing helper or unresolvable path is shown in the window, not thrown.
        }

        return new ConnectionsViewModel(
            capturePath,
            settingsPath,
            () => paths is null || capturePath is null || settingsPath is null
                ? null
                : ClaudeStatusLineIntegration.FromPaths(paths, capturePath),
            isReceiving);
    }
}
