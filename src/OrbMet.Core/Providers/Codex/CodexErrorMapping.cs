using OrbMet.Core.Models;

namespace OrbMet.Core.Providers.Codex;

/// <summary>
/// Turns a failed Codex read into a connection state and a safe error code.
/// Authentication cannot be told apart from other errors by code alone, so the
/// message is inspected for known sign-in wording; anything unclear stays a
/// generic error rather than a wrong "signed out" claim.
/// </summary>
internal static class CodexErrorMapping
{
    private static readonly string[] SignInMarkers =
    {
        "not logged in",
        "not signed in",
        "sign in",
        "sign-in",
        "login required",
        "log in",
        "unauthorized",
        "authentication",
        "no account",
        "invalid session",
    };

    public static ProviderSnapshot FromRpcError(DateTimeOffset at, JsonRpcException exception)
    {
        var message = exception.Message.ToLowerInvariant();
        if (SignInMarkers.Any(marker => message.Contains(marker, StringComparison.Ordinal)))
        {
            return CodexSnapshotFactory.Failure(at, ConnectionState.SignInRequired, "sign-in-required");
        }

        return CodexSnapshotFactory.Failure(at, ConnectionState.Error, "codex-rpc-error");
    }

    public static ProviderSnapshot FromReadError(DateTimeOffset at, CodexReadException exception) =>
        CodexSnapshotFactory.Failure(at, ToState(exception.Kind), exception.ErrorCode);

    private static ConnectionState ToState(CodexFailureKind kind) => kind switch
    {
        CodexFailureKind.ToolNotFound => ConnectionState.MissingTool,
        CodexFailureKind.SignInRequired => ConnectionState.SignInRequired,
        CodexFailureKind.Unsupported => ConnectionState.Unsupported,
        _ => ConnectionState.Error,
    };
}
