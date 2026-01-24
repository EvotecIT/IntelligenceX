using System;

namespace IntelligenceX.Rpc;

public sealed class JsonRpcRequestEventArgs : EventArgs {
    public JsonRpcRequestEventArgs(JsonRpcRequest request) {
        Request = request;
    }

    public JsonRpcRequest Request { get; }
}
