using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using OrbMet.Desktop.Infrastructure;
using OrbMet.Desktop.ViewModels;

namespace OrbMet.Desktop;

/// <summary>
/// Both providers' details in an ordinary, focusable window, opened from the
/// tray. The hover card never takes focus, so this is how the keyboard and a
/// screen reader reach the same content. It shows the panel's slots as they
/// are and starts no read of its own.
/// </summary>
public partial class DetailsWindow : Window
{
    private readonly IReadOnlyList<ProviderSlotViewModel> _slots;
    private readonly DispatcherTimer _clock;

    public DetailsWindow(IReadOnlyList<ProviderSlotViewModel> slots)
    {
        InitializeComponent();
        _slots = slots;
        Providers.ItemsSource = slots;

        // Relative times ("12 dk önce") are rebuilt while the window is open.
        _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _clock.Tick += (_, _) => UpdateDetails();
        Closed += (_, _) => _clock.Stop();

        SourceInitialized += (_, _) => DarkTitleBar.Apply(this);
        Loaded += (_, _) =>
        {
            UpdateDetails();
            _clock.Start();
            MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        };
    }

    private void UpdateDetails()
    {
        foreach (var slot in _slots)
        {
            slot.UpdateDetails(DateTimeOffset.UtcNow, TimeZoneInfo.Local);
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }
}
