using System.Threading;
using System.Windows;

namespace OrbMet.Desktop;

public partial class App : Application
{
    private const string InstanceName = "OrbMet.SingleInstance.7c1f6e4a";

    private Mutex? _instanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Run by the uninstaller: undo this copy's settings outside its folder, no panel.
        if (e.Args.Contains(Infrastructure.UninstallCleanup.Argument))
        {
            Infrastructure.UninstallCleanup.Run();
            Shutdown();
            return;
        }

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
