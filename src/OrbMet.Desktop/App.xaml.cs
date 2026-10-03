using System.Threading;
using System.Windows;

namespace OrbMet.Desktop;

public partial class App : Application
{
    private const string InstanceName = "OrbMet.SingleInstance.7c1f6e4a";

    private Mutex? _instanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        // One instance owns the tray and the Codex helper. A second launch
        // steps aside rather than fighting over either.
        _instanceMutex = new Mutex(initiallyOwned: true, InstanceName, out var createdNew);
        if (!createdNew)
        {
            Shutdown();
            return;
        }

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
