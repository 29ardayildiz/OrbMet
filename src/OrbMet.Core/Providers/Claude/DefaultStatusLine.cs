using System.Globalization;

namespace OrbMet.Core.Providers.Claude;

/// <summary>The short line OrbMet prints when it owns the status line outright.</summary>
internal static class DefaultStatusLine
{
    public static string Build(ClaudeCaptureRecord? record)
    {
        if (record is null)
        {
            return "OrbMet";
        }

        var parts = new List<string>();
        foreach (var window in record.Windows)
        {
            var label = window.Key switch
            {
                "five_hour" => "5h",
                "seven_day" => "7d",
                "spend_limit" => "spend",
                _ => null,
            };

            if (label is null || window.UsedPercent is not { } percent)
            {
                continue;
            }

            parts.Add($"{label} {percent.ToString("0", CultureInfo.InvariantCulture)}%");
        }

        return parts.Count == 0 ? "OrbMet" : string.Join("  ·  ", parts);
    }
}
