using OrbMet.Core.Providers.Claude;
using OrbMet.Core.Startup;
using OrbMet.Core.Storage;

namespace OrbMet.Desktop.Infrastructure;

/// <summary>
/// What the uninstaller runs (<c>OrbMet.exe --uninstall-cleanup</c>) before
/// it deletes the files: undoes what this copy set outside its folder. Claude
/// Code gets its previous status line back, and the Start with Windows entry
/// goes, but only when they belong to this copy or to one that is gone;
/// another copy that still exists keeps them. No window, no questions.
/// </summary>
internal static class UninstallCleanup
{
    public const string Argument = "--uninstall-cleanup";

    public static void Run()
    {
        try
        {
            var paths = OrbMetPaths.FromEnvironment();
            if (paths.ClaudeSettingsFile is not null && CaptureHelperLocator.Locate() is { } helper)
            {
                var claude = ClaudeStatusLineIntegration.FromPaths(paths, helper);
                var state = claude.GetState();
                if (state.Connected && (!state.PointsElsewhere || state.HelperMissing))
                {
                    claude.Disconnect();
                }
            }
        }
        catch (Exception)
        {
            // An uninstall goes on regardless; the README says how to undo it by hand.
        }

        try
        {
            if (Environment.ProcessPath is { } exe)
            {
                new StartupEntry(new WindowsStartupList(), exe).RemoveIfThisCopy();
            }
        }
        catch (Exception)
        {
        }
    }
}
