namespace OrbMet.Core.Models;

/// <summary>Where a window duration came from, so a semantic guess is never shown as reported fact.</summary>
public enum DurationOrigin
{
    Unknown,
    KnownSemantic,
    Reported,
}
