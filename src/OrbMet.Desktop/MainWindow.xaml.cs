using System.Windows;
using OrbMet.Core.Providers;
using OrbMet.Core.Providers.Claude;
using OrbMet.Core.Providers.Codex;
using OrbMet.Core.Storage;
using OrbMet.Core.Usage;
using OrbMet.Desktop.Infrastructure;
using OrbMet.Desktop.ViewModels;

namespace OrbMet.Desktop;

/// <summary>
/// The compact panel. It shows both providers' reported usage, hides to the
/// tray instead of exiting, and never blocks the UI thread while reading a
/// provider.
/// </summary>
public partial class MainWindow : Window
{
    private readonly PanelViewModel _viewModel;
    private readonly TrayIcon _tray;
    private ConnectionsWindow? _connectionsWindow;
    private bool _exitRequested;
    private bool _cleanedUp;

    public MainWindow()
    {
        InitializeComponent();

        var paths = OrbMetPaths.FromEnvironment();
        var coordinator = new UsageCoordinator(new IUsageProvider[]
        {
            new ClaudeCaptureProvider(paths.ClaudeCapturesDirectory),
            new CodexUsageProvider(),
        });

        _viewModel = new PanelViewModel(coordinator);
        DataContext = _viewModel;

        _tray = new TrayIcon(
            togglePanel: TogglePanel,
            openConnections: OpenConnections,
            refresh: () => _ = _viewModel.RefreshAsync(),
            exit: ExitApplication);

        Loaded += async (_, _) => await _viewModel.StartAsync();
        Closing += OnClosing;
        Closed += (_, _) => CleanUp();
    }

    private void TogglePanel()
    {
        if (IsVisible)
        {
            Hide();
        }
        else
        {
            Show();
            Activate();
        }
    }

    private void OpenConnections()
    {
        Show();

        if (_connectionsWindow is null)
        {
            _connectionsWindow = new ConnectionsWindow();
            _connectionsWindow.Closed += (_, _) => _connectionsWindow = null;
            _connectionsWindow.Show();
        }
        else
        {
            _connectionsWindow.Activate();
        }
    }

    private void OnConnectionsClick(object sender, RoutedEventArgs e) => OpenConnections();

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_exitRequested)
        {
            return;
        }

        // Hiding is not exiting; the tray keeps a way back to the panel.
        e.Cancel = true;
        Hide();
    }

    private void ExitApplication()
    {
        _exitRequested = true;
        Application.Current.Shutdown();
    }

    private void CleanUp()
    {
        if (_cleanedUp)
        {
            return;
        }

        _cleanedUp = true;
        _viewModel.Dispose();
        _tray.Dispose();
        _connectionsWindow?.Close();
    }
}
