using System;

namespace IntelligenceX.Rpc;

public sealed class JsonRpcException : Exception {
    public JsonRpcException(JsonRpcError error) : base(error.ToString()) {
        Error = error;
    }

    public JsonRpcException(JsonRpcError error, Exception innerException) : base(error.ToString(), innerException) {
        Error = error;
    }

    public JsonRpcError Error { get; }
}
