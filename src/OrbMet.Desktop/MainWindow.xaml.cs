using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using OrbMet.Core.Placement;
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
    private readonly PanelPositionStore _positionStore;
    private PanelPosition _position;
    private PanelDock _shownDock;
    private ConnectionsWindow? _connectionsWindow;

    // A press on the strip: a click until it travels past the threshold, a drag after.
    private bool _pressed;
    private bool _dragging;
    private Point _pressPoint;
    private Vector _grab;
    private ProviderSlotViewModel? _pressedRing;
    private PanelPosition? _dragPosition;
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

        _positionStore = new PanelPositionStore(paths.PanelPositionFile);
        _position = _positionStore.Load();
        _shownDock = _position.Dock;

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

        SourceInitialized += (_, _) =>
        {
            NoActivateWindow.Apply(this);
            Place();
        };
        SystemParameters.StaticPropertyChanged += OnSystemParameterChanged;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        Loaded += async (_, _) => await _viewModel.StartAsync();
        Closing += OnClosing;
        Closed += (_, _) => CleanUp();
    }

    private int SlotCount => _viewModel.Providers.Count;

    /// <summary>
    /// Puts the strip where it was left: on the remembered display, or the
    /// primary when that one is gone (design.md 9). Never during a press.
    /// </summary>
    private void Place()
    {
        if (_pressed || PresentationSource.FromVisual(this) is null)
        {
            return;
        }

        var (area, _) = Displays.For(_position.DisplayId, this);
        ShowDock(_position.Dock);
        var (x, y) = PanelLayout.RailOrigin(_position, area, Width, Height);
        Left = x;
        Top = y;
    }

    /// <summary>Gives the strip the size, padding and outline of a dock state.</summary>
    private void ShowDock(PanelDock dock)
    {
        // Each slot carries 15 DIP of margin above it, so the list's own
        // margin is the end padding less that.
        const double SlotMargin = 15;
        var docked = dock != PanelDock.Floating;
        var padding = docked ? PanelMetrics.DockedEndPadding : PanelMetrics.FloatingEndPadding;

        Slots.Margin = new Thickness(0, padding - SlotMargin, 0, 0);
        Width = PanelMetrics.StripWidth;
        Height = PanelMetrics.RailHeight(SlotCount, docked);
        Berth.Data = docked
            ? DockBerth.Build(Width, Height, dockedRight: dock == PanelDock.Right)
            : DockBerth.Floating(Width, Height);
        _shownDock = dock;
    }

    private void OnSystemParameterChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.WorkArea))
        {
            Dispatcher.BeginInvoke(Place);
        }
    }

    // Raised on a system thread; the move is marshalled to the UI thread.
    private void OnDisplaySettingsChanged(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(Place);

    /// <summary>A press anywhere on the strip may become a drag; on a ring it may be a click.</summary>
    private void OnPress(object sender, MouseButtonEventArgs e)
    {
        if (PointerPosition.InDips(this) is not { } pointer)
        {
            return;
        }

        _pressed = true;
        _dragging = false;
        _dragPosition = null;
        _pressPoint = pointer;
        _grab = pointer - new Point(Left, Top);
        _pressedRing = FindRing(e.OriginalSource as DependencyObject)?.DataContext as ProviderSlotViewModel;
        Root.CaptureMouse();
        e.Handled = true;
    }

    /// <summary>
    /// Carries the strip under the pointer, fusing it to a side within the
    /// dock distance while it moves, on whichever display the pointer is over.
    /// </summary>
    private void OnPointerMove(object sender, MouseEventArgs e)
    {
        if (!_pressed || PointerPosition.InDips(this) is not { } pointer)
        {
            return;
        }

        if (!_dragging)
        {
            if ((pointer - _pressPoint).Length < PanelMetrics.DragThreshold)
            {
                return;
            }

            _dragging = true;
            HideCard();
        }

        var (area, displayId) = Displays.At(pointer, this);
        var wanted = pointer - _grab;
        var dock = PanelLayout.DockFor(wanted.X, Width, area);

        // Docked and floating strips differ in length; keep the grabbed spot
        // under the pointer by moving the grab with the change.
        var landingHeight = PanelMetrics.RailHeight(SlotCount, dock != PanelDock.Floating);
        if (landingHeight != Height)
        {
            _grab.Y -= (Height - landingHeight) / 2;
            wanted = pointer - _grab;
        }

        if (dock != _shownDock)
        {
            ShowDock(dock);
        }

        var (x, y) = PanelLayout.Clamp(wanted.X, wanted.Y, area, Width, Height);
        x = dock switch
        {
            PanelDock.Left => area.X,
            PanelDock.Right => area.Right - Width,
            _ => x,
        };

        Left = x;
        Top = y;

        var (horizontal, vertical) = PanelLayout.Ratios(x, y, area, Width, Height);
        _dragPosition = new PanelPosition(dock, horizontal, vertical, displayId);
    }

    private void OnRelease(object sender, MouseButtonEventArgs e)
    {
        var wasDragging = _dragging;
        var ring = _pressedRing;
        EndPress();

        if (wasDragging)
        {
            KeepDroppedPosition();
            return;
        }

        // A click, not a drag: on a ring it refreshes that provider alone.
        if (ring is not null)
        {
            _ = _viewModel.RefreshProviderAsync(ring);
        }
    }

    // Capture can be taken away (another window, a dialog); the press ends there.
    private void OnLostCapture(object sender, MouseEventArgs e)
    {
        if (!_pressed)
        {
            return;
        }

        var wasDragging = _dragging;
        EndPress();
        if (wasDragging)
        {
            KeepDroppedPosition();
        }
    }

    private void KeepDroppedPosition()
    {
        if (_dragPosition is not { } dropped)
        {
            return;
        }

        _position = dropped;

        // A failed save leaves the strip where it is; the next drop tries again.
        _positionStore.Save(dropped);
    }

    private void EndPress()
    {
        _pressed = false;
        _dragging = false;
        _pressedRing = null;
        if (Root.IsMouseCaptured)
        {
            Root.ReleaseMouseCapture();
        }
    }

    private static UsageRing? FindRing(DependencyObject? element)
    {
        while (element is not null)
        {
            if (element is UsageRing ring)
            {
                return ring;
            }

            element = VisualTreeHelper.GetParent(element);
        }

        return null;
    }

    /// <summary>Entering a ring's circle opens its card at once, or swaps an open card to it.</summary>
    private void OnRingMouseEnter(object sender, MouseEventArgs e)
    {
        if (_pressed)
        {
            return;
        }

        if (sender is FrameworkElement { DataContext: ProviderSlotViewModel slot } ring)
        {
            ShowCardFor(ring, slot);
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
        var strip = new Rect(Left, Top, Width, Height);
        var (area, _) = Displays.At(new Point(strip.X + strip.Width / 2, strip.Y + strip.Height / 2), this);

        // The card opens away from the edge the strip sits on; floating, away
        // from the nearer side of the display.
        var opensRight = _shownDock switch
        {
            PanelDock.Left => true,
            PanelDock.Right => false,
            _ => strip.X + strip.Width / 2 < area.CenterX,
        };
        _card.ShowFor(slot, strip, ringCentreY, new Rect(area.X, area.Y, area.Width, area.Height), opensRight);
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
