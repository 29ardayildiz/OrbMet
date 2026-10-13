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
    private readonly DispatcherTimer _cardOpenTimer;
    private readonly DispatcherTimer _cardCloseTimer;
    private FrameworkElement? _hoveredSlot;
    private bool _cardPinned;
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

        _card = new DetailCardWindow(
            refresh: () => _ = _viewModel.RefreshAsync(),
            openConnections: OpenConnections,
            close: HideCard);

        _cardOpenTimer = new DispatcherTimer { Interval = PanelMetrics.CardOpenDelay };
        _cardOpenTimer.Tick += (_, _) =>
        {
            _cardOpenTimer.Stop();
            if (_hoveredSlot is { IsMouseOver: true } slot)
            {
                ShowCardFor(slot);
            }
        };

        _cardCloseTimer = new DispatcherTimer { Interval = PanelMetrics.CardCloseDelay };
        _cardCloseTimer.Tick += (_, _) =>
        {
            _cardCloseTimer.Stop();

            // The pointer may be crossing the gap between strip and card.
            if (!_cardPinned && !IsMouseOver && !_card.IsMouseOver)
            {
                _card.Hide();
            }
        };

        _card.MouseEnter += (_, _) => _cardCloseTimer.Stop();
        _card.MouseLeave += (_, _) => ScheduleCardClose();

        SourceInitialized += (_, _) => NoActivateWindow.Apply(this);
        SizeChanged += (_, _) => PlaceOnRightEdge();
        SystemParameters.StaticPropertyChanged += OnSystemParameterChanged;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        Loaded += async (_, _) => await _viewModel.StartAsync();
        Closing += OnClosing;
        Closed += (_, _) => CleanUp();
    }

    /// <summary>
    /// Pins the strip to the right edge of the primary work area, vertically
    /// centred. WorkArea already excludes the taskbar and is in DIPs.
    /// </summary>
    private void PlaceOnRightEdge()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - ActualWidth - PanelMetrics.WorkAreaMargin;
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

    private void OnSlotMouseEnter(object sender, MouseEventArgs e)
    {
        _cardCloseTimer.Stop();
        _hoveredSlot = (FrameworkElement)sender;

        if (_cardPinned)
        {
            return;
        }

        // Moving between slots while the card is up swaps it without a delay.
        if (_card.IsVisible)
        {
            ShowCardFor(_hoveredSlot);
        }
        else
        {
            _cardOpenTimer.Stop();
            _cardOpenTimer.Start();
        }
    }

    private void OnSlotClick(object sender, MouseButtonEventArgs e)
    {
        var element = (FrameworkElement)sender;
        _cardOpenTimer.Stop();

        // A click pins the card; clicking the same slot again releases it.
        if (_cardPinned && ReferenceEquals(_card.Slot, element.DataContext))
        {
            HideCard();
            return;
        }

        _cardPinned = true;
        ShowCardFor(element);
    }

    private void OnStripMouseLeave(object sender, MouseEventArgs e)
    {
        _cardOpenTimer.Stop();
        ScheduleCardClose();
    }

    private void ScheduleCardClose()
    {
        if (_cardPinned || !_card.IsVisible)
        {
            return;
        }

        _cardCloseTimer.Stop();
        _cardCloseTimer.Start();
    }

    private void ShowCardFor(FrameworkElement slotElement)
    {
        if (slotElement.DataContext is not ProviderSlotViewModel slot || !IsVisible)
        {
            return;
        }

        var slotTop = Top + slotElement.TranslatePoint(new Point(0, 0), this).Y;
        _card.ShowFor(slot, new Rect(Left, Top, ActualWidth, ActualHeight), slotTop);
    }

    private void HideCard()
    {
        _cardPinned = false;
        _cardOpenTimer.Stop();
        _cardCloseTimer.Stop();
        _card.Hide();
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
        _cardOpenTimer.Stop();
        _cardCloseTimer.Stop();
        _card.Close();
        _viewModel.Dispose();
        _tray.Dispose();
        _connectionsWindow?.Close();
    }
}
