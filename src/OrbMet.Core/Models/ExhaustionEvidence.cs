namespace OrbMet.Core.Models;

/// <summary>
/// Provider's explicit statement about being blocked. High usage alone is not
/// evidence of blocking.
/// </summary>
public enum ExhaustionEvidence
{
    Unknown,
    NotExhausted,
    Exhausted,
}
