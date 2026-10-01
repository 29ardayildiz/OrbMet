namespace OrbMet.Core.Models;

/// <summary>How a provider's data reaches OrbMet. Future transports get their own value.</summary>
public enum SourceKind
{
    ClaudeStatusLine,
    CodexAppServer,
}
