namespace OrbMet.Core.Providers.Codex;

/// <summary>A JSON-RPC level failure from the app-server, including its error code.</summary>
public sealed class JsonRpcException : Exception
{
    public JsonRpcException(long? code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }

    public long? Code { get; }

    /// <summary>JSON-RPC "method not found" code, used to report an unsupported method.</summary>
    public bool IsMethodNotFound => Code == -32601;
}
