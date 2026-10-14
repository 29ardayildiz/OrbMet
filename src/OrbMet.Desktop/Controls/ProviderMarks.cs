using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;

namespace OrbMet.Desktop.Controls;

/// <summary>
/// Provider logos from Lobe Icons (MIT, see THIRD_PARTY_NOTICES.md). Each is a
/// single even-odd path in a 24x24 box with no colour of its own; the caller
/// fills it. Parsed once and frozen.
/// </summary>
internal static partial class ProviderMarks
{
    public const double ViewBoxSize = 24;

    private static readonly Dictionary<string, Geometry?> Cache = new(StringComparer.Ordinal);

    /// <summary>The mark's geometry, or null when the asset is missing or unreadable.</summary>
    public static Geometry? Get(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        lock (Cache)
        {
            if (!Cache.TryGetValue(name, out var geometry))
            {
                geometry = Load(name);
                Cache[name] = geometry;
            }

            return geometry;
        }
    }

    private static Geometry? Load(string name)
    {
        try
        {
            var info = Application.GetResourceStream(new Uri($"pack://application:,,,/OrbMet;component/Assets/Marks/{name}.svg"));
            if (info is null)
            {
                return null;
            }

            using var reader = new StreamReader(info.Stream);
            var match = PathData().Match(reader.ReadToEnd());
            if (!match.Success)
            {
                return null;
            }

            // F0 is WPF's even-odd fill rule, matching the SVG's fill-rule.
            var geometry = Geometry.Parse("F0 " + match.Groups[1].Value);
            geometry.Freeze();
            return geometry;
        }
        catch (Exception ex) when (ex is IOException or FormatException or InvalidOperationException)
        {
            return null;
        }
    }

    [GeneratedRegex("<path[^>]*?\\sd=\"([^\"]+)\"")]
    private static partial Regex PathData();
}
