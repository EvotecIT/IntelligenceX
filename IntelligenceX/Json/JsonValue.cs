using System;

namespace IntelligenceX.Json;

public enum JsonValueKind {
    Null,
    Boolean,
    Number,
    String,
    Object,
    Array
}

public sealed class JsonValue {
    public static readonly JsonValue Null = new(JsonValueKind.Null, null);

    public JsonValueKind Kind { get; }
    public object? Value { get; }

    private JsonValue(JsonValueKind kind, object? value) {
        Kind = kind;
        Value = value;
    }

    public static JsonValue From(bool value) => new(JsonValueKind.Boolean, value);
    public static JsonValue From(string? value) => value is null ? Null : new JsonValue(JsonValueKind.String, value);
    public static JsonValue From(long value) => new(JsonValueKind.Number, value);
    public static JsonValue From(double value) => new(JsonValueKind.Number, value);
    public static JsonValue From(JsonObject value) => new(JsonValueKind.Object, value);
    public static JsonValue From(JsonArray value) => new(JsonValueKind.Array, value);

    public bool AsBoolean(bool defaultValue = false) => Kind == JsonValueKind.Boolean && Value is bool b ? b : defaultValue;

    public string? AsString() => Kind == JsonValueKind.String ? Value as string : null;

    public long? AsInt64() {
        if (Kind != JsonValueKind.Number || Value is null) {
            return null;
        }
        if (Value is long l) {
            return l;
        }
        if (Value is int i) {
            return i;
        }
        if (Value is double d) {
            return (long)d;
        }
        return null;
    }

    public double? AsDouble() {
        if (Kind != JsonValueKind.Number || Value is null) {
            return null;
        }
        if (Value is double d) {
            return d;
        }
        if (Value is long l) {
            return l;
        }
        if (Value is int i) {
            return i;
        }
        return null;
    }

    public JsonObject? AsObject() => Kind == JsonValueKind.Object ? Value as JsonObject : null;
    public JsonArray? AsArray() => Kind == JsonValueKind.Array ? Value as JsonArray : null;

    public override string ToString() => JsonLite.Serialize(this);
}
