namespace OrbMet.Core.Providers.Codex;

/// <summary>Classification of a failed Codex usage read, mapped to a connection state by the provider.</summary>
public enum CodexFailureKind
{
    ToolNotFound,
    SignInRequired,
    Unsupported,
    Timeout,
    ProtocolError,
    Transport,
    Unknown,
}

/// <summary>Surfaces a diagnosed Codex failure with a safe error code that the UI can localise.</summary>
public sealed class CodexReadException : Exception
{
    public CodexReadException(CodexFailureKind kind, string errorCode, Exception? innerException = null)
        : base(errorCode, innerException)
    {
        Kind = kind;
        ErrorCode = errorCode;
    }

    public CodexFailureKind Kind { get; }

    public string ErrorCode { get; }
}
