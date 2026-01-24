using IntelligenceX.Json;

namespace IntelligenceX.Rpc;

public sealed class JsonRpcError {
    public JsonRpcError(int code, string message, JsonValue? data) {
        Code = code;
        Message = message;
        Data = data;
    }

    public int Code { get; }
    public string Message { get; }
    public JsonValue? Data { get; }

    public override string ToString() => $"{Code}: {Message}";
}
