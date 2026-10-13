using System.ComponentModel;
using System.Diagnostics;
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
    private PanelPlan _plan = null!;
    private readonly List<SlotView> _slotViews = new();
    private ConnectionsWindow? _connectionsWindow;
    private DetailsWindow? _detailsWindow;

    // A press on the strip: a click until it travels past the threshold, a drag after.
    private bool _pressed;
    private bool _dragging;
    private Point _pressPoint;
    private Vector _grab;
    private ProviderSlotViewModel? _pressedRing;
    private PanelPosition? _dragPosition;

    // A change of outline in progress: the pose it started from, the strip's
    // place once it ends, and the box the window covers until then.
    private StripPose? _reshapeFrom;
    private Point _origin;
    private Rect _reshapeBox;
    private readonly Stopwatch _reshapeClock = new();
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

        foreach (var slot in _viewModel.Providers)
        {
            var view = new SlotView { DataContext = slot };
            view.RingElement.MouseEnter += OnRingMouseEnter;
            _slotViews.Add(view);
            SlotLayer.Children.Add(view);
        }

        ShowDock(_shownDock);

        _tray = new TrayIcon(
            togglePanel: TogglePanel,
            openDetails: OpenDetails,
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

    /// <summary>
    /// Draws the strip for a dock at once (design.md 4.2): its size, outline
    /// and where each provider sits. Ends any change of outline in progress.
    /// </summary>
    private void ShowDock(PanelDock dock)
    {
        StopReshape();
        _plan = PanelOutline.For(dock, SlotCount);
        _shownDock = dock;
        Width = _plan.Size.Width;
        Height = _plan.Size.Height;
        Outline.Data = _plan.Outline;
        Draw(StripPose.Of(_plan, default), drawOutline: false);
    }

    /// <summary>Puts each slot, and the outline unless it is already set, where a pose has them in the window.</summary>
    private void Draw(StripPose pose, bool drawOutline = true)
    {
        if (drawOutline)
        {
            Outline.Data = PanelOutline.Bars(pose.Horizontal, pose.Vertical);
        }

        for (var i = 0; i < _slotViews.Count && i < pose.Slots.Count; i++)
        {
            var (frame, beside) = pose.Slots[i];
            var view = _slotViews[i];
            System.Windows.Controls.Canvas.SetLeft(view, frame.X);
            System.Windows.Controls.Canvas.SetTop(view, frame.Y);
            view.Width = frame.Width;
            view.Height = frame.Height;
            view.Beside = beside;
        }
    }

    private bool Reshaping => _reshapeFrom is not null;

    /// <summary>
    /// Where the strip is drawn right now, in screen DIPs: part way through a
    /// change of outline, or the plan at the window's place.
    /// </summary>
    private StripPose ShownPose() => _reshapeFrom is { } from
        ? StripPose.Between(from, StripPose.Of(_plan, _origin), ReshapeProgress())
        : StripPose.Of(_plan, new Point(Left, Top));

    private double ReshapeProgress() =>
        Motion.EaseOut.Ease(Math.Min(_reshapeClock.Elapsed / Motion.Reshape.TimeSpan, 1));

    /// <summary>
    /// Stretches the strip from <paramref name="from"/> into the current plan
    /// at <see cref="_origin"/>, which a drag may keep moving meanwhile. The
    /// window covers both, with room to spare, so it rarely has to move.
    /// </summary>
    private void BeginReshape(StripPose from)
    {
        _reshapeFrom = from;
        _reshapeBox = Rect.Empty;
        _reshapeClock.Restart();
        CompositionTarget.Rendering -= OnReshapeFrame;
        CompositionTarget.Rendering += OnReshapeFrame;
        OnReshapeFrame(this, EventArgs.Empty);
    }

    private void OnReshapeFrame(object? sender, EventArgs e)
    {
        if (_reshapeFrom is not { } from)
        {
            return;
        }

        if (_reshapeClock.Elapsed >= Motion.Reshape.TimeSpan)
        {
            ShowDock(_shownDock);
            Left = _origin.X;
            Top = _origin.Y;
            return;
        }

        var target = StripPose.Of(_plan, _origin);
        var pose = StripPose.Between(from, target, ReshapeProgress());
        if (!_reshapeBox.Contains(pose.Bounds))
        {
            var box = Rect.Union(Rect.Union(pose.Bounds, from.Bounds), target.Bounds);
            box.Inflate(PanelMetrics.StripWidth, PanelMetrics.StripWidth);
            _reshapeBox = box;
            Left = box.X;
            Top = box.Y;
            Width = box.Width;
            Height = box.Height;
        }

        Draw(pose.Offset(new Vector(-_reshapeBox.X, -_reshapeBox.Y)));
    }

    private void StopReshape()
    {
        _reshapeFrom = null;
        CompositionTarget.Rendering -= OnReshapeFrame;
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
    /// Carries the strip under the pointer on whichever display it is over,
    /// fusing it to an edge or corner the pointer reaches while it moves and
    /// redrawing it as a capsule or an L to suit (design.md 4.2, 8.2).
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
        var dock = PanelLayout.DockFor(pointer.X, pointer.Y, area);
        StripPose? from = null;
        if (dock != _shownDock)
        {
            from = ShownPose();
            var before = _plan.Size;
            ShowDock(dock);

            // A new outline has nothing in common with where the old one was
            // held, so it is carried by its middle from here on.
            if (before != _plan.Size)
            {
                _grab = new Vector(_plan.Size.Width / 2, _plan.Size.Height / 2);
            }
        }

        var size = _plan.Size;
        var wanted = pointer - _grab;
        var (x, y) = PanelLayout.DragOrigin(dock, wanted.X, wanted.Y, area, size.Width, size.Height);
        _origin = new Point(x, y);

        // The strip stretches into its new outline; while it does, each frame
        // follows _origin instead of the window moving here.
        if (from is not null && Motion.Enabled)
        {
            BeginReshape(from);
        }
        else if (!Reshaping)
        {
            Left = x;
            Top = y;
        }

        var (horizontal, vertical) = PanelLayout.Ratios(dock, x, y, area, size.Width, size.Height);
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
        // Mid-change the rings are not where the card would be laid out against.
        if (_pressed || Reshaping)
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

        var centre = ring.TranslatePoint(new Point(ring.ActualWidth / 2, ring.ActualHeight / 2), this);
        var ringCentre = new Point(Left + centre.X, Top + centre.Y);
        var strip = new Rect(Left, Top, Width, Height);
        var (area, _) = Displays.At(new Point(strip.X + strip.Width / 2, strip.Y + strip.Height / 2), this);
        var bounds = new Rect(area.X, area.Y, area.Width, area.Height);

        var plan = _plan;
        var index = _viewModel.Providers.IndexOf(slot);
        var onHorizontalArm = index >= 0 && index < plan.Slots.Count && plan.Slots[index].PercentBeside;
        var arm = onHorizontalArm ? plan.HorizontalArm : plan.VerticalArm;
        arm.Offset(Left, Top);

        // The card opens into the screen, away from the edges the strip is on.
        var dock = _shownDock;
        CardSide side;
        if (onHorizontalArm)
        {
            side = PanelLayout.IsBottom(dock) ? CardSide.Above : CardSide.Below;
        }
        else if (PanelLayout.IsLeft(dock))
        {
            side = CardSide.Right;
        }
        else if (PanelLayout.IsRight(dock))
        {
            side = CardSide.Left;
        }
        else
        {
            side = strip.X + strip.Width / 2 < area.CenterX ? CardSide.Right : CardSide.Left;
        }

        // In a corner the card also keeps clear of the L's other arm.
        if (PanelLayout.ShapeOf(dock) == PanelShape.Corner)
        {
            var other = onHorizontalArm ? plan.VerticalArm : plan.HorizontalArm;
            other.Offset(Left, Top);
            bounds = Clear(bounds, other, onHorizontalArm);
        }

        _card.ShowFor(slot, ringCentre, arm, bounds, side);
        _pointerSampler.Start();
    }

    /// <summary>
    /// The part of <paramref name="bounds"/> beside <paramref name="obstacle"/>:
    /// left or right of it when <paramref name="sideways"/>, above or below otherwise.
    /// </summary>
    private static Rect Clear(Rect bounds, Rect obstacle, bool sideways)
    {
        var gap = PanelMetrics.CardGap;
        if (sideways)
        {
            return obstacle.Left > bounds.Left + bounds.Width / 2
                ? new Rect(bounds.Left, bounds.Top, Math.Max(obstacle.Left - gap - bounds.Left, 0), bounds.Height)
                : new Rect(obstacle.Right + gap, bounds.Top, Math.Max(bounds.Right - obstacle.Right - gap, 0), bounds.Height);
        }

        return obstacle.Top < bounds.Top + bounds.Height / 2
            ? new Rect(bounds.Left, obstacle.Bottom + gap, bounds.Width, Math.Max(bounds.Bottom - obstacle.Bottom - gap, 0))
            : new Rect(bounds.Left, bounds.Top, bounds.Width, Math.Max(obstacle.Top - gap - bounds.Top, 0));
    }

    private void SamplePointer()
    {
        // The menu runs its own loop with the pointer on it, off the panel.
        if (ContextMenuIsOpen() || PointerPosition.InDips(this) is not { } pointer)
        {
            return;
        }

        // The strip is its drawn outline, not its window: the empty quarter
        // of an L's box is the desktop.
        var onStrip = _plan.Outline.FillContains(new Point(pointer.X - Left, pointer.Y - Top));

        // The card's rect is widened over the gap so crossing it never closes the card.
        var card = new Rect(_card.Left, _card.Top, _card.ActualWidth, _card.ActualHeight);
        card.Inflate(PanelMetrics.CardGap, PanelMetrics.CardGap);

        if (!onStrip && !card.Contains(pointer))
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
            _connectionsWindow = new ConnectionsWindow(_viewModel.Claude, _viewModel.Codex);
            _connectionsWindow.Closed += (_, _) => _connectionsWindow = null;
            _connectionsWindow.Show();
        }

        _connectionsWindow.Activate();
    }

    private void OpenDetails()
    {
        HideCard();
        if (_detailsWindow is null)
        {
            _detailsWindow = new DetailsWindow(_viewModel.Providers);
            _detailsWindow.Closed += (_, _) => _detailsWindow = null;
            _detailsWindow.Show();
        }

        _detailsWindow.Activate();
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
        StopReshape();
        SystemParameters.StaticPropertyChanged -= OnSystemParameterChanged;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _pointerSampler.Stop();
        _card.Close();
        _viewModel.Dispose();
        _tray.Dispose();
        _connectionsWindow?.Close();
        _detailsWindow?.Close();
    }
}
