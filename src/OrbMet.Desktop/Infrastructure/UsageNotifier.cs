using System.Text.Json;
using OrbMet.Core.Models;
using OrbMet.Core.Usage;

namespace OrbMet.Desktop.Infrastructure;

/// <summary>
/// Turns readings into notifications (design.md 10): asks the alert rules
/// what is new, shows it from the tray icon and remembers what was said.
/// Several things at once become one notification, the most pressing first.
/// </summary>
internal sealed class UsageNotifier
{
    private readonly TrayIcon _tray;
    private readonly Func<IReadOnlyList<AlertReading>> _readings;
    private readonly Func<ProviderId, string> _displayName;
    private readonly Action _openDetails;
    private readonly Action _openConnections;
    private AlertMemory _memory;

    public UsageNotifier(
        TrayIcon tray,
        Func<IReadOnlyList<AlertReading>> readings,
        Func<ProviderId, string> displayName,
        Action openDetails,
        Action openConnections)
    {
        _tray = tray;
        _readings = readings;
        _displayName = displayName;
        _openDetails = openDetails;
        _openConnections = openConnections;
        _memory = AlertsMenu.Store.LoadMemory();

        AlertsMenu.Changed += threshold =>
        {
            // Off forgets what was said, so turning it on again starts afresh;
            // a new threshold says at once what is already past it.
            if (threshold == 0)
            {
                _memory = new AlertMemory();
                AlertsMenu.Store.SaveMemory(_memory);
            }
            else
            {
                Check(afterRead: false);
            }
        };
    }

    /// <summary>Called after every full read of the providers.</summary>
    public void Check(bool afterRead = true)
    {
        var before = JsonSerializer.Serialize(_memory);
        var alerts = UsageAlerts.Evaluate(_memory, AlertsMenu.Current, _readings(), DateTimeOffset.UtcNow, afterRead);
        if (JsonSerializer.Serialize(_memory) != before)
        {
            AlertsMenu.Store.SaveMemory(_memory);
        }

        if (alerts.Count == 0)
        {
            return;
        }

        var usage = Localizer.Instance.Usage;
        var now = DateTimeOffset.UtcNow;
        var first = alerts[0];
        var (title, body) = usage.Alert(first, _displayName(first.ProviderId), now, TimeZoneInfo.Local);
        var lines = new List<string>();
        if (!string.IsNullOrEmpty(body))
        {
            lines.Add(body);
        }

        // The rest by their titles; a notification has room for a few lines.
        lines.AddRange(alerts.Skip(1).Take(2).Select(alert => usage.Alert(alert, _displayName(alert.ProviderId), now, TimeZoneInfo.Local).Title));
        if (alerts.Count > 3)
        {
            lines.Add(Localizer.Instance.Text.Format("AlertMoreFormat", alerts.Count - 3));
        }

        _tray.ShowNotification(
            title,
            string.Join("\n", lines),
            first.Kind == AlertKind.ClaudeLinkBroken ? _openConnections : _openDetails);
    }
}
