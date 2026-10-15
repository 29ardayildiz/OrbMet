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
    private bool _reversed;
    private PanelPlan _plan = null!;
    private readonly List<SlotView> _slotViews = new();
    private ConnectionsWindow? _connectionsWindow;
    private DetailsWindow? _detailsWindow;

    // A press on the strip: a click until it travels past the threshold, a drag after.
    private bool _pressed;
    private bool _dragging;
    private Point _pressPoint;
    private Point _lastPointer;
    private Vector _grab;
    private ProviderSlotViewModel? _pressedRing;
    private PanelPosition? _dragPosition;

    // The window is larger than any outline and keeps one size: the strip is
    // drawn inside it with clear room all round. A window that moves while
    // its drawing changes shows the old drawing at the new place for a frame,
    // so the two never change together by more than a few DIPs.
    private readonly Size _windowSize;
    private Point _windowAt;
    private Vector _inset;

    // The strip's top-left on screen; while the outline changes, where it is heading.
    private Point _origin;

    // A change of outline in progress and the pose it started from.
    private StripPose? _reshapeFrom;
    private readonly Stopwatch _reshapeClock = new();
    private TimeSpan _lastFrame;
    private bool _exitRequested;
    private bool _cleanedUp;

    // Folding when idle: how long the pointer has been away, and whether the
    // strip is the sliver now (design.md 4.3).
    private readonly DispatcherTimer _idleWatch = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly Stopwatch _awayFor = new();
    private bool _collapsed;
    private Rect _sliverTarget;

    // Following the pointer's display: the display it was last seen on, which
    // must be seen twice running before the strip moves there.
    private readonly DispatcherTimer _displayFollow = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private string? _pendingDisplay;

    // Whether each agent is mid-turn, read off the UI thread every few
    // seconds while "Show when an agent is working" is on (design.md 6.7).
    private readonly DispatcherTimer _activityWatch = new() { Interval = TimeSpan.FromSeconds(3) };
    private bool _readingActivity;

    public MainWindow()
    {
        InitializeComponent();
        StripMenu.Items.Insert(2, AppearanceMenu.Create());
        StripMenu.Items.Insert(3, AlertsMenu.Create());
        StripMenu.Items.Insert(5, LanguageMenu.Create());
        StripMenu.Items.Insert(6, StartupMenu.Create());
        StripMenu.Items.Insert(7, UpdateMenu.CreateCheckItem());
        StripMenu.Items.Insert(0, UpdateMenu.CreateUpdateItem());
        StartupMenu.FollowMove();

        var paths = OrbMetPaths.FromEnvironment();
        var coordinator = new UsageCoordinator(new IUsageProvider[]
        {
            new ClaudeCaptureProvider(paths.ClaudeCapturesDirectory),
            new CodexUsageProvider(),
        })
        {
            RingChoice = RingChoiceMenu.Current,
        };

        _positionStore = new PanelPositionStore(paths.PanelPositionFile);
        _position = _positionStore.Load();
        _shownDock = _position.Dock;
        _reversed = _position.Reversed;

        _viewModel = new PanelViewModel(coordinator, () => ReadClaudeLink(paths));
        DataContext = _viewModel;
        RingChoiceMenu.Changed += _viewModel.UseRingChoice;

        foreach (var slot in _viewModel.Providers)
        {
            var view = new SlotView { DataContext = slot };
            view.RingElement.MouseEnter += OnRingMouseEnter;
            _slotViews.Add(view);
            SlotLayer.Children.Add(view);
        }

        _windowSize = RoomyWindowSize(SlotCount);
        UsePlan(_shownDock, _reversed);
        ShowPlanAt(default, centre: true);

        _tray = new TrayIcon(
            togglePanel: TogglePanel,
            openDetails: OpenDetails,
            openConnections: OpenConnections,
            refresh: () => _ = _viewModel.RefreshAsync(showProgress: true),
            exit: ExitApplication);
        foreach (var slot in _viewModel.Providers)
        {
            slot.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(ProviderSlotViewModel.RingPercent) or nameof(ProviderSlotViewModel.IsExhausted)
                    or nameof(ProviderSlotViewModel.GoodBrush))
                {
                    ShowUsageInTray();
                }
            };
        }

        Localizer.Instance.LanguageChanged += (_, _) => ShowUsageInTray();
        AppearanceMenu.Changed += ShowUsageInTray;
        ShowUsageInTray();

        var notifier = new UsageNotifier(
            _tray,
            readings: () => _viewModel.Providers
                .Where(slot => slot.Usage is not null)
                .Select(slot => new AlertReading(slot.Usage!.Snapshot, slot.LinkBroken))
                .ToList(),
            displayName: id => _viewModel.Providers.First(slot => slot.ProviderId == id).DisplayName,
            openDetails: OpenDetails,
            openConnections: OpenConnections);
        _viewModel.ReadCompleted += (_, _) => notifier.Check();
        UpdateMenu.Start(_tray, ExitApplication);

        _idleWatch.Tick += (_, _) => WatchIdle();
        _displayFollow.Tick += (_, _) => FollowPointerDisplay();
        _activityWatch.Tick += async (_, _) => await ReadActivityAsync();
        AppearanceMenu.Changed += ApplyStripOptions;
        ApplyStripOptions();

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

        var welcome = new WelcomeStore(paths.SettingsFile);
        Loaded += async (_, _) =>
        {
            await _viewModel.StartAsync();
            await OfferConnectionsOnceAsync(welcome);
        };
        Closing += OnClosing;
        Closed += (_, _) => CleanUp();
    }

    private int SlotCount => _viewModel.Providers.Count;

    /// <summary>
    /// The tray icon shows the fullest ring and its tip lists every provider
    /// (design.md 3.1). A spent ring counts as the fullest.
    /// </summary>
    private void ShowUsageInTray()
    {
        var fullest = _viewModel.Providers
            .Where(slot => slot.RingPercent is not null)
            .OrderByDescending(slot => slot.IsExhausted ? double.MaxValue : slot.RingPercent)
            .FirstOrDefault();

        // With "show what is left" the tip says so in words; the icon still fills with use.
        var text = Localizer.Instance.Usage;
        var remaining = AppearanceMenu.Current.ShowRemaining;
        var lines = _viewModel.Providers.Select(slot => remaining && slot.RingPercent is not null
            ? $"{slot.DisplayName}: {text.UsedOrLeft(slot.RingPercent, slot.IsExhausted, showRemaining: true)}"
            : $"{slot.DisplayName}: {text.Percent(slot.RingPercent)}");
        _tray.ShowUsage(
            fullest?.RingPercent,
            fullest?.IsExhausted == true,
            fullest?.GoodBrush,
            string.Join("\n", lines.Prepend("OrbMet")));
        if (_collapsed)
        {
            ColourSliver();
        }
    }

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
        UsePlan(_position.Dock, _position.Reversed);
        var (x, y) = PanelLayout.RailOrigin(_position, area, _plan.Size.Width, _plan.Size.Height);
        ShowPlanAt(new Point(x, y), centre: true);
        if (_collapsed)
        {
            ShowSliver();
        }
    }

    /// <summary>Starts or stops folding and display following as the Appearance menu says.</summary>
    private void ApplyStripOptions()
    {
        var options = AppearanceMenu.Current;
        if (options.CollapseWhenIdle)
        {
            _idleWatch.Start();
        }
        else
        {
            _idleWatch.Stop();
            Expand();
        }

        if (options.FollowPointer)
        {
            _displayFollow.Start();
        }
        else
        {
            _displayFollow.Stop();
            _pendingDisplay = null;
        }

        // Off means no session file is opened at all.
        if (options.ShowActivity)
        {
            if (!_activityWatch.IsEnabled)
            {
                _activityWatch.Start();
                _ = ReadActivityAsync();
            }
        }
        else
        {
            _activityWatch.Stop();
            foreach (var slot in _viewModel.Providers)
            {
                slot.IsWorking = false;
            }
        }
    }

    private async Task ReadActivityAsync()
    {
        if (_readingActivity || !AppearanceMenu.Current.ShowActivity)
        {
            return;
        }

        _readingActivity = true;
        try
        {
            var ids = _viewModel.Providers.Select(slot => slot.ProviderId).ToList();
            var working = await Task.Run(() => ids.ToDictionary(
                id => id,
                id => AgentActivity.SessionsRoot(id) is { } root && AgentActivity.IsWorking(id, root, DateTimeOffset.UtcNow)));

            // Turned off while reading: say nothing.
            if (!AppearanceMenu.Current.ShowActivity)
            {
                return;
            }

            foreach (var slot in _viewModel.Providers)
            {
                slot.IsWorking = working.GetValueOrDefault(slot.ProviderId);
            }
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
        }
        finally
        {
            _readingActivity = false;
        }
    }

    /// <summary>
    /// Folds a docked strip to its sliver once the pointer has been off it,
    /// its card and its menu for the wait picked in the menu (10 s to 2 min);
    /// never mid-drag or mid-change, and never a floating strip.
    /// </summary>
    private void WatchIdle()
    {
        if (_collapsed)
        {
            return;
        }

        var busy = !IsVisible || _pressed || Reshaping || _card.IsVisible || ContextMenuIsOpen()
            || _shownDock == PanelDock.Floating;
        if (busy || PointerPosition.InDips(this) is not { } pointer
            || _plan.Outline.FillContains(new Point(pointer.X - _origin.X, pointer.Y - _origin.Y))
            || _sliverTarget.Contains(pointer))
        {
            // On the sliver's target counts as here, or the strip just opened
            // from it would fold again before the pointer reached it.
            _awayFor.Reset();
            return;
        }

        if (!_awayFor.IsRunning)
        {
            _awayFor.Restart();
        }
        else if (_awayFor.Elapsed >= AppearanceMenu.Current.CollapseDelay)
        {
            Collapse();
        }
    }

    private void Collapse()
    {
        _collapsed = true;
        _awayFor.Reset();
        Outline.Visibility = Visibility.Hidden;
        SlotLayer.Visibility = Visibility.Hidden;
        ShowSliver();
        SliverLayer.Visibility = Visibility.Visible;
    }

    private void Expand()
    {
        if (!_collapsed)
        {
            return;
        }

        _collapsed = false;
        _awayFor.Reset();
        SliverLayer.Visibility = Visibility.Collapsed;
        Outline.Visibility = Visibility.Visible;
        SlotLayer.Visibility = Visibility.Visible;
    }

    // The pointer reaching the sliver, or its wider target, opens the strip.
    private void OnSliverEnter(object sender, MouseEventArgs e) => Expand();

    /// <summary>
    /// Lays the sliver against the screen edge the strip is docked to, level
    /// with the strip (in a corner, with its upright arm), and colours it:
    /// neutral, or the warning colour once any ring is past 75%, the spent
    /// colour once one is spent (design.md 4.3).
    /// </summary>
    private void ShowSliver()
    {
        var strip = new Rect(_origin, _plan.Size);
        var (area, _) = Displays.At(new Point(strip.X + strip.Width / 2, strip.Y + strip.Height / 2), this);
        var length = PanelMetrics.SliverLength;
        var thickness = PanelMetrics.SliverThickness;
        var reach = PanelMetrics.SliverTarget;

        Rect sliver;
        if (PanelLayout.IsLeft(_shownDock) || PanelLayout.IsRight(_shownDock))
        {
            var arm = _plan.VerticalArm;
            arm.Offset(_origin.X, _origin.Y);
            var top = arm.Y + arm.Height / 2 - length / 2;
            var left = PanelLayout.IsLeft(_shownDock);
            sliver = new Rect(left ? area.X : area.Right - thickness, top, thickness, length);
            _sliverTarget = new Rect(left ? area.X : area.Right - reach, top, reach, length);
        }
        else
        {
            var arm = _plan.HorizontalArm;
            arm.Offset(_origin.X, _origin.Y);
            var left = arm.X + arm.Width / 2 - length / 2;
            var top = PanelLayout.IsTop(_shownDock);
            sliver = new Rect(left, top ? area.Y : area.Bottom - thickness, length, thickness);
            _sliverTarget = new Rect(left, top ? area.Y : area.Bottom - reach, length, reach);
        }

        Lay(Sliver, sliver);
        Lay(SliverTarget, _sliverTarget);
        ColourSliver();

        void Lay(FrameworkElement element, Rect onScreen)
        {
            System.Windows.Controls.Canvas.SetLeft(element, onScreen.X - _windowAt.X);
            System.Windows.Controls.Canvas.SetTop(element, onScreen.Y - _windowAt.Y);
            element.Width = onScreen.Width;
            element.Height = onScreen.Height;
        }
    }

    private void ColourSliver()
    {
        var slots = _viewModel.Providers;
        if (slots.Any(slot => slot.IsSpent))
        {
            Sliver.Fill = UsagePalette.Spent;
            Sliver.Stroke = null;
        }
        else if (slots.Any(slot => slot.RingPercent >= 75))
        {
            Sliver.Fill = UsagePalette.Warning;
            Sliver.Stroke = null;
        }
        else
        {
            // Black with a faint white edge reads on dark and light desktops alike.
            Sliver.Fill = UsagePalette.Surface;
            Sliver.Stroke = UsagePalette.RingTrack;
        }
    }

    /// <summary>
    /// Moves the strip to the display the pointer has settled on, keeping its
    /// edge and place along it (design.md 9). Not while it is held, its card
    /// or menu is open, or it is hidden.
    /// </summary>
    private void FollowPointerDisplay()
    {
        if (!IsVisible || _pressed || _card.IsVisible || ContextMenuIsOpen()
            || PointerPosition.InDips(this) is not { } pointer)
        {
            _pendingDisplay = null;
            return;
        }

        var (_, under) = Displays.At(pointer, this);
        var (_, shown) = Displays.For(_position.DisplayId, this);
        if (under == shown)
        {
            _pendingDisplay = null;
            return;
        }

        // Seen twice running: the pointer has settled there, not crossed through.
        if (_pendingDisplay != under)
        {
            _pendingDisplay = under;
            return;
        }

        _pendingDisplay = null;
        _position = _position with { DisplayId = under };
        _positionStore.Save(_position);
        Place();
    }

    /// <summary>The largest outline with <see cref="PanelMetrics.WindowRoom"/> on every side.</summary>
    private static Size RoomyWindowSize(int slots)
    {
        var sizes = new[] { PanelDock.Right, PanelDock.Top, PanelDock.TopRight }
            .Select(dock => PanelOutline.For(dock, slots).Size)
            .ToList();
        return new Size(
            sizes.Max(size => size.Width) + 2 * PanelMetrics.WindowRoom,
            sizes.Max(size => size.Height) + 2 * PanelMetrics.WindowRoom);
    }

    /// <summary>
    /// Takes the drawing for a dock (design.md 4.2): its size, outline and
    /// where each provider sits. Ends any change of outline in progress;
    /// nothing on screen changes until it is shown.
    /// </summary>
    private void UsePlan(PanelDock dock, bool reversed)
    {
        StopReshape();
        _plan = PanelOutline.For(dock, SlotCount, reversed);
        _shownDock = dock;
        _reversed = reversed;
    }

    /// <summary>
    /// Shows the plan with its top-left at <paramref name="origin"/>: in the
    /// middle of the window when <paramref name="centre"/>, otherwise where it
    /// already sits in it, so only the window moves.
    /// </summary>
    private void ShowPlanAt(Point origin, bool centre)
    {
        StopReshape();
        _origin = origin;
        if (centre || !new Rect(_windowSize).Contains(new Rect(new Point(_inset.X, _inset.Y), _plan.Size)))
        {
            _inset = CentredInset();
        }

        MoveWindow(origin - _inset);
        Draw(StripPose.Of(_plan, new Point(_inset.X, _inset.Y)));
    }

    private Vector CentredInset() =>
        new((_windowSize.Width - _plan.Size.Width) / 2, (_windowSize.Height - _plan.Size.Height) / 2);

    private static double Creep(double wanted) => Math.Clamp(wanted, -PanelMetrics.WindowCreep, PanelMetrics.WindowCreep);

    private void MoveWindow(Point at)
    {
        _windowAt = at;
        WindowBounds.Set(this, new Rect(at, _windowSize));
    }

    /// <summary>Draws the outline and puts each slot where a pose, in window DIPs, has them.</summary>
    private void Draw(StripPose pose)
    {
        Outline.Data = PanelOutline.Bars(pose.Horizontal, pose.Vertical);

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
    /// change of outline, or the plan at its place.
    /// </summary>
    private StripPose ShownPose() => _reshapeFrom is { } from
        ? StripPose.Between(from, StripPose.Of(_plan, _origin), ReshapeProgress())
        : StripPose.Of(_plan, _origin);

    private double ReshapeProgress() =>
        Motion.EaseOut.Ease(Math.Min(_reshapeClock.Elapsed / Motion.Reshape.TimeSpan, 1));

    /// <summary>
    /// Stretches the strip from <paramref name="from"/> into the current plan
    /// at <see cref="_origin"/>, which a drag may keep moving meanwhile.
    /// </summary>
    private void BeginReshape(StripPose from)
    {
        _reshapeFrom = from;
        _reshapeClock.Restart();
        CompositionTarget.Rendering -= OnReshapeFrame;
        CompositionTarget.Rendering += OnReshapeFrame;
        OnReshapeFrame(this, EventArgs.Empty);
    }

    /// <summary>
    /// One frame of the change: the drawing moves inside the window, and the
    /// window creeps at most <see cref="PanelMetrics.WindowCreep"/> per frame
    /// towards keeping the strip in its middle, which no one sees.
    /// </summary>
    private void OnReshapeFrame(object? sender, EventArgs e)
    {
        if (_reshapeFrom is not { } from)
        {
            return;
        }

        // Rendering can be raised more than once for one frame; the window
        // must creep once per frame, not once per call.
        if (e is RenderingEventArgs { RenderingTime: var frame })
        {
            if (frame == _lastFrame)
            {
                return;
            }

            _lastFrame = frame;
        }

        if (_reshapeClock.Elapsed >= Motion.Reshape.TimeSpan)
        {
            // The strip stays where the change left it in the window.
            _inset = _origin - _windowAt;
            ShowPlanAt(_origin, centre: false);
            return;
        }

        var pose = StripPose.Between(from, StripPose.Of(_plan, _origin), ReshapeProgress());
        var bounds = pose.Bounds;
        var middle = new Point(
            bounds.X + bounds.Width / 2 - _windowSize.Width / 2,
            bounds.Y + bounds.Height / 2 - _windowSize.Height / 2);
        var at = new Point(_windowAt.X + Creep(middle.X - _windowAt.X), _windowAt.Y + Creep(middle.Y - _windowAt.Y));

        // Only if the strip would leave the window does it jump to the middle.
        if (!new Rect(at, _windowSize).Contains(bounds))
        {
            at = middle;
        }

        if (at != _windowAt)
        {
            MoveWindow(at);
        }

        Draw(pose.Offset(new Vector(-at.X, -at.Y)));
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
        _lastPointer = pointer;
        _grab = pointer - _origin;
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
        // Moving the window under a still pointer raises a move of its own;
        // only a pointer that went somewhere counts.
        if (!_pressed || PointerPosition.InDips(this) is not { } pointer || pointer == _lastPointer)
        {
            return;
        }

        _lastPointer = pointer;
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
            UsePlan(dock, _reversed);

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

        // The rings keep their order as the outline changes, so they carry on
        // the way they were going: whichever order of the new outline they
        // reach with the least travel.
        if (from is not null)
        {
            var arrangements = new[] { false, true }
                .Select(reversed => StripPose.Of(PanelOutline.For(dock, SlotCount, reversed), _origin).Centres())
                .ToList();
            UsePlan(dock, PanelLayout.NearestArrangement(from.Centres(), arrangements) == 1);
        }

        // The strip stretches into its new outline; while it does, each frame
        // follows _origin. Otherwise only the window moves, its drawing as it is.
        if (from is not null && Motion.Enabled)
        {
            BeginReshape(from);
        }
        else if (from is not null)
        {
            ShowPlanAt(_origin, centre: true);
        }
        else if (!Reshaping)
        {
            // The strip creeps back towards the window's middle while it is
            // carried, a step too small to see, so later changes have room.
            var centred = CentredInset();
            var step = new Vector(Creep(centred.X - _inset.X), Creep(centred.Y - _inset.Y));
            if (step != default)
            {
                _inset += step;
                Draw(StripPose.Of(_plan, new Point(_inset.X, _inset.Y)));
            }

            MoveWindow(_origin - _inset);
        }

        var (horizontal, vertical) = PanelLayout.Ratios(dock, x, y, area, size.Width, size.Height);
        _dragPosition = new PanelPosition(dock, horizontal, vertical, displayId, _reversed);
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

        // A click, not a drag: on a ring it refreshes that provider alone, or
        // opens Connections when nothing can arrive until it is connected.
        if (ring is { NeedsConnecting: true })
        {
            OpenConnections();
        }
        else if (ring is not null)
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
        var ringCentre = new Point(_windowAt.X + centre.X, _windowAt.Y + centre.Y);
        var strip = new Rect(_origin, _plan.Size);
        var (area, _) = Displays.At(new Point(strip.X + strip.Width / 2, strip.Y + strip.Height / 2), this);
        var bounds = new Rect(area.X, area.Y, area.Width, area.Height);

        var plan = _plan;
        var index = _viewModel.Providers.IndexOf(slot);
        var onHorizontalArm = index >= 0 && index < plan.Slots.Count && plan.Slots[index].PercentBeside;
        var arm = onHorizontalArm ? plan.HorizontalArm : plan.VerticalArm;
        arm.Offset(_origin.X, _origin.Y);

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
            other.Offset(_origin.X, _origin.Y);
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
        var onStrip = _plan.Outline.FillContains(new Point(pointer.X - _origin.X, pointer.Y - _origin.Y));

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
            _connectionsWindow.Closed += (_, _) =>
            {
                _connectionsWindow = null;
                // A reconnect there may have mended what the card warns about.
                _ = _viewModel.CheckClaudeLinkAsync();
            };
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

    /// <summary>
    /// The first time OrbMet runs and finds Claude Code unconnected, it opens
    /// Connections once; the user started OrbMet, so taking focus is expected.
    /// Already connected counts as offered. Unknown (unreadable settings)
    /// tries again next start.
    /// </summary>
    private async Task OfferConnectionsOnceAsync(WelcomeStore welcome)
    {
        if (await Task.Run(() => welcome.ConnectionsOffered))
        {
            return;
        }

        if (await _viewModel.CheckClaudeLinkAsync() is not { SettingsReadable: true } state)
        {
            return;
        }

        if (!state.Connected)
        {
            OpenConnections();
        }

        await Task.Run(welcome.MarkConnectionsOffered);
    }

    private void OnConnectionsClick(object sender, RoutedEventArgs e) => OpenConnections();

    // Only reads Claude's settings; changing them stays the Connections window's.
    private static ClaudeIntegrationState? ReadClaudeLink(OrbMetPaths paths) =>
        paths.ClaudeSettingsFile is not null && CaptureHelperLocator.Locate() is { } helper
            ? ClaudeStatusLineIntegration.FromPaths(paths, helper).GetState()
            : null;

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
        _idleWatch.Stop();
        _displayFollow.Stop();
        _activityWatch.Stop();
        _card.Close();
        _viewModel.Dispose();
        UpdateMenu.Stop();
        _tray.Dispose();
        _connectionsWindow?.Close();
        _detailsWindow?.Close();
    }
}
