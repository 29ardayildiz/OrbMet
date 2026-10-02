namespace OrbMet.Core.Providers.Claude;

/// <summary>
/// One window as the provider reported it, before reset-time filtering. Percent
/// stays null when the payload omitted it; it is never turned into zero.
/// </summary>
public sealed record ClaudeCaptureWindow
{
    /// <summary>Wire key: five_hour, seven_day or spend_limit.</summary>
    public required string Key { get; init; }

    public double? UsedPercent { get; init; }

    public DateTimeOffset? ResetsAtUtc { get; init; }
}

/// <summary>
/// The usage part of a single status-line payload, banked for the app to read.
/// It carries no raw payload, prompt, transcript or working directory; the
/// session is represented only by a hash so concurrent sessions stay apart.
/// </summary>
public sealed record ClaudeCaptureRecord
{
    public required DateTimeOffset CapturedAtUtc { get; init; }

    /// <summary>Hash of the session id. A technical separator, never an account identity.</summary>
    public string? SessionHash { get; init; }

    public IReadOnlyList<ClaudeCaptureWindow> Windows { get; init; } = Array.Empty<ClaudeCaptureWindow>();
}
