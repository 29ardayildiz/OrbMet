using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using OrbMet.Core.Providers;
using OrbMet.Core.Providers.Claude;
using OrbMet.Core.Providers.Codex;
using OrbMet.Core.Storage;
using OrbMet.Core.Usage;
using OrbMet.Desktop.Controls;
using OrbMet.Desktop.Infrastructure;
using OrbMet.Desktop.ViewModels;

namespace OrbMet.Desktop;

/// <summary>
/// The compact panel: a narrow strip on the right edge of the primary work
/// area. It never takes keyboard focus, hides to the tray instead of exiting,
/// and never blocks the UI thread while reading a provider.
/// </summary>
public partial class MainWindow : Window
{
    private readonly PanelViewModel _viewModel;
    private readonly TrayIcon _tray;
    private readonly DetailCardWindow _card;
    private readonly DispatcherTimer _pointerSampler;
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
            refresh: () => _ = _viewModel.RefreshAsync(showProgress: true),
            exit: ExitApplication);

        _card = new DetailCardWindow();

        // While the card is up, the real pointer is sampled to decide when it
        // goes; enter events only ever open it (design.md 8.1).
        _pointerSampler = new DispatcherTimer { Interval = PanelMetrics.PointerSampleInterval };
        _pointerSampler.Tick += (_, _) => SamplePointer();

        SourceInitialized += (_, _) => NoActivateWindow.Apply(this);
        SizeChanged += (_, _) =>
        {
            Berth.Data = DockBerth.Build(ActualWidth, ActualHeight);
            PlaceOnRightEdge();
        };
        SystemParameters.StaticPropertyChanged += OnSystemParameterChanged;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        Loaded += async (_, _) => await _viewModel.StartAsync();
        Closing += OnClosing;
        Closed += (_, _) => CleanUp();
    }

    /// <summary>
    /// Docks the strip flush against the right edge of the primary work area,
    /// vertically centred. WorkArea already excludes the taskbar and is in DIPs.
    /// </summary>
    private void PlaceOnRightEdge()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - ActualWidth;
        Top = area.Top + Math.Max(0, (area.Height - ActualHeight) / 2);
    }

    private void OnSystemParameterChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.WorkArea))
        {
            Dispatcher.BeginInvoke(PlaceOnRightEdge);
        }
    }

    // Raised on a system thread; the move is marshalled to the UI thread.
    private void OnDisplaySettingsChanged(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(PlaceOnRightEdge);

    /// <summary>Entering a ring's circle opens its card at once, or swaps an open card to it.</summary>
    private void OnRingMouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ProviderSlotViewModel slot } ring)
        {
            ShowCardFor(ring, slot);
        }
    }

    /// <summary>A click on a ring refreshes that provider; it does not pin the card.</summary>
    private void OnRingClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ProviderSlotViewModel slot })
        {
            _ = _viewModel.RefreshProviderAsync(slot);
        }
    }

    private void ShowCardFor(FrameworkElement ring, ProviderSlotViewModel slot)
    {
        if (!IsVisible)
        {
            return;
        }

        foreach (var other in _viewModel.Providers)
        {
            other.IsSelected = ReferenceEquals(other, slot);
        }

        var ringCentreY = Top + ring.TranslatePoint(new Point(0, ring.ActualHeight / 2), this).Y;
        _card.ShowFor(slot, new Rect(Left, Top, ActualWidth, ActualHeight), ringCentreY);
        _pointerSampler.Start();
    }

    private void SamplePointer()
    {
        // The menu runs its own loop with the pointer on it, off the panel.
        if (ContextMenuIsOpen() || PointerPosition.InDips(this) is not { } pointer)
        {
            return;
        }

        var strip = new Rect(Left, Top, ActualWidth, ActualHeight);

        // The card's rect is widened over the gap so crossing it never closes the card.
        var card = new Rect(_card.Left, _card.Top, _card.ActualWidth, _card.ActualHeight);
        card.Inflate(PanelMetrics.CardGap, 0);

        if (!strip.Contains(pointer) && !card.Contains(pointer))
        {
            HideCard();
        }
    }

    private bool ContextMenuIsOpen() => Content is FrameworkElement { ContextMenu.IsOpen: true };

    private void HideCard()
    {
        _pointerSampler.Stop();
        _card.Hide();
        foreach (var slot in _viewModel.Providers)
        {
            slot.IsSelected = false;
        }
    }

    private void HidePanel()
    {
        HideCard();
        Hide();
    }

    private void TogglePanel()
    {
        if (IsVisible)
        {
            HidePanel();
        }
        else
        {
            Show();
        }
    }

    private void OpenConnections()
    {
        if (_connectionsWindow is null)
        {
            _connectionsWindow = new ConnectionsWindow();
            _connectionsWindow.Closed += (_, _) => _connectionsWindow = null;
            _connectionsWindow.Show();
        }

        _connectionsWindow.Activate();
    }

    private void OnConnectionsClick(object sender, RoutedEventArgs e) => OpenConnections();

    private void OnHideClick(object sender, RoutedEventArgs e) => HidePanel();

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_exitRequested)
        {
            return;
        }

        // Hiding is not exiting; the tray keeps a way back to the panel.
        e.Cancel = true;
        HidePanel();
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
        SystemParameters.StaticPropertyChanged -= OnSystemParameterChanged;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _pointerSampler.Stop();
        _card.Close();
        _viewModel.Dispose();
        _tray.Dispose();
        _connectionsWindow?.Close();
    }
}
