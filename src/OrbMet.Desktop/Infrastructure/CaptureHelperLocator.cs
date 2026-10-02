using System.IO;

namespace OrbMet.Desktop.Infrastructure;

/// <summary>
/// Finds OrbMet.Capture.exe relative to the app, so no machine-specific path is
/// hardcoded. The environment override exists for development; the packaged
/// layout is settled in stage 4.
/// </summary>
public static class CaptureHelperLocator
{
    private const string FileName = "OrbMet.Capture.exe";

    public static string? Locate()
    {
        var configured = Environment.GetEnvironmentVariable("ORBMET_CAPTURE_PATH");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
        {
            return Path.GetFullPath(configured);
        }

        var baseDirectory = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDirectory, FileName),
            Path.Combine(baseDirectory, "capture", FileName),
            Path.GetFullPath(Path.Combine(baseDirectory, "..", "capture", FileName)),
            Path.GetFullPath(Path.Combine(baseDirectory, "..", "OrbMet.Capture", FileName)),
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
