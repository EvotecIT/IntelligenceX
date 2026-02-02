using System;

namespace IntelligenceX.Json;

/// <summary>
/// Enumerates the supported JSON value kinds.
/// </summary>
public enum JsonValueKind {
    /// <summary>
    /// Represents a JSON null literal.
    /// </summary>
    Null,
    /// <summary>
    /// Represents a JSON boolean.
    /// </summary>
    Boolean,
    /// <summary>
    /// Represents a JSON number.
    /// </summary>
    Number,
    /// <summary>
    /// Represents a JSON string.
    /// </summary>
    String,
    /// <summary>
    /// Represents a JSON object.
    /// </summary>
    Object,
    /// <summary>
    /// Represents a JSON array.
    /// </summary>
    Array
}

/// <summary>
/// Represents a JSON value with a kind and raw value storage.
/// </summary>
public sealed class JsonValue {
    /// <summary>
    /// Singleton null JSON value.
    /// </summary>
    public static readonly JsonValue Null = new(JsonValueKind.Null, null);

    /// <summary>
    /// Gets the JSON kind of this value.
    /// </summary>
    public JsonValueKind Kind { get; }
    /// <summary>
    /// Gets the raw value payload.
    /// </summary>
    public object? Value { get; }

    private JsonValue(JsonValueKind kind, object? value) {
        Kind = kind;
        Value = value;
    }

    /// <summary>
    /// Creates a boolean JSON value.
    /// </summary>
    public static JsonValue From(bool value) => new(JsonValueKind.Boolean, value);
    /// <summary>
    /// Creates a string JSON value (or null if the input is null).
    /// </summary>
    public static JsonValue From(string? value) => value is null ? Null : new JsonValue(JsonValueKind.String, value);
    /// <summary>
    /// Creates a numeric JSON value from a 64-bit integer.
    /// </summary>
    public static JsonValue From(long value) => new(JsonValueKind.Number, value);
    /// <summary>
    /// Creates a numeric JSON value from a double.
    /// </summary>
    public static JsonValue From(double value) => new(JsonValueKind.Number, value);
    /// <summary>
    /// Wraps a JSON object value.
    /// </summary>
    public static JsonValue From(JsonObject value) => new(JsonValueKind.Object, value);
    /// <summary>
    /// Wraps a JSON array value.
    /// </summary>
    public static JsonValue From(JsonArray value) => new(JsonValueKind.Array, value);

    /// <summary>
    /// Reads this value as a boolean, returning <paramref name="defaultValue"/> if not a boolean.
    /// </summary>
    public bool AsBoolean(bool defaultValue = false) => Kind == JsonValueKind.Boolean && Value is bool b ? b : defaultValue;

    /// <summary>
    /// Reads this value as a string, or null if not a string.
    /// </summary>
    public string? AsString() => Kind == JsonValueKind.String ? Value as string : null;

    /// <summary>
    /// Reads this value as a 64-bit integer when possible.
    /// </summary>
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

    /// <summary>
    /// Reads this value as a double when possible.
    /// </summary>
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

    /// <summary>
    /// Reads this value as a JSON object, or null if not an object.
    /// </summary>
    public JsonObject? AsObject() => Kind == JsonValueKind.Object ? Value as JsonObject : null;
    /// <summary>
    /// Reads this value as a JSON array, or null if not an array.
    /// </summary>
    public JsonArray? AsArray() => Kind == JsonValueKind.Array ? Value as JsonArray : null;

    /// <summary>
    /// Serializes the JSON value to a string.
    /// </summary>
    public override string ToString() => JsonLite.Serialize(this);
}
