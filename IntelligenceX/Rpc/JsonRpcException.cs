using System;

namespace IntelligenceX.Rpc;

public sealed class JsonRpcException : Exception {
    public JsonRpcException(JsonRpcError error) : this(string.Empty, error, null) { }

    public JsonRpcException(JsonRpcError error, Exception innerException) : this(string.Empty, error, innerException) { }

    public JsonRpcException(string method, JsonRpcError error) : this(method, error, null) { }

    public JsonRpcException(string method, JsonRpcError error, Exception? innerException)
        : base(BuildMessage(method, error), innerException) {
        Error = error;
        Method = method;
        Hint = JsonRpcErrorHints.GetHint(error.Code);
    }

    public JsonRpcError Error { get; }
    public string Method { get; }
    public string? Hint { get; }

    private static string BuildMessage(string method, JsonRpcError error) {
        var hint = JsonRpcErrorHints.GetHint(error.Code);
        var prefix = string.IsNullOrWhiteSpace(method) ? string.Empty : method + ": ";
        if (string.IsNullOrWhiteSpace(hint)) {
            return prefix + error;
        }
        return $"{prefix}{error} ({hint})";
    }
}
