using System;
using IntelligenceX.Json;

namespace IntelligenceX.Rpc;

public sealed class JsonRpcNotificationEventArgs : EventArgs {
    public JsonRpcNotificationEventArgs(string method, JsonValue? @params) {
        Method = method;
        Params = @params;
    }

    public string Method { get; }
    public JsonValue? Params { get; }
}
