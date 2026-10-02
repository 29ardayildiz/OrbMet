using System.Windows;
using OrbMet.Core.Providers.Claude;
using OrbMet.Core.Storage;
using OrbMet.Desktop.Infrastructure;
using OrbMet.Desktop.ViewModels;

namespace OrbMet.Desktop;

/// <summary>
/// The first stage-2 surface: connect or disconnect Claude Code's status line.
/// The usage panel itself arrives in stage 3.
/// </summary>
public partial class MainWindow : Window
{
    private readonly ConnectionsViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = BuildViewModel();
        DataContext = _viewModel;
        Loaded += async (_, _) => await _viewModel.RefreshAsync();
    }

    private static ConnectionsViewModel BuildViewModel()
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
                : ClaudeStatusLineIntegration.FromPaths(paths, capturePath));
    }
}
