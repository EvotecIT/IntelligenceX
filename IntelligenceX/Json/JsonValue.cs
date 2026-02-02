using System;

namespace IntelligenceX.Json;

/// <summary>
/// Represents a JSON value with a kind and raw value.
/// </summary>
public enum JsonValueKind {
    /// <summary>Represents a JSON null.</summary>
    Null,
    /// <summary>Represents a JSON boolean.</summary>
    Boolean,
    /// <summary>Represents a JSON number.</summary>
    Number,
    /// <summary>Represents a JSON string.</summary>
    String,
    /// <summary>Represents a JSON object.</summary>
    Object,
    /// <summary>Represents a JSON array.</summary>
    Array
}

/// <summary>
/// Represents a JSON value with helper conversions.
/// </summary>
public sealed class JsonValue {
    /// <summary>Represents a JSON null value.</summary>
    public static readonly JsonValue Null = new(JsonValueKind.Null, null);

    /// <summary>Gets the value kind.</summary>
    public JsonValueKind Kind { get; }
    /// <summary>Gets the raw value for the current kind.</summary>
    public object? Value { get; }

    private JsonValue(JsonValueKind kind, object? value) {
        Kind = kind;
        Value = value;
    }

    /// <summary>Creates a JSON boolean value.</summary>
    /// <param name="value">The boolean value.</param>
    public static JsonValue From(bool value) => new(JsonValueKind.Boolean, value);
    /// <summary>Creates a JSON string value (or null).</summary>
    /// <param name="value">The string value.</param>
    public static JsonValue From(string? value) => value is null ? Null : new JsonValue(JsonValueKind.String, value);
    /// <summary>Creates a JSON number value from a 64-bit integer.</summary>
    /// <param name="value">The numeric value.</param>
    public static JsonValue From(long value) => new(JsonValueKind.Number, value);
    /// <summary>Creates a JSON number value from a double.</summary>
    /// <param name="value">The numeric value.</param>
    public static JsonValue From(double value) => new(JsonValueKind.Number, value);
    /// <summary>Creates a JSON object value.</summary>
    /// <param name="value">The JSON object.</param>
    public static JsonValue From(JsonObject value) => new(JsonValueKind.Object, value);
    /// <summary>Creates a JSON array value.</summary>
    /// <param name="value">The JSON array.</param>
    public static JsonValue From(JsonArray value) => new(JsonValueKind.Array, value);

    /// <summary>Returns the boolean value or the provided default.</summary>
    /// <param name="defaultValue">The fallback value when not a boolean.</param>
    public bool AsBoolean(bool defaultValue = false) => Kind == JsonValueKind.Boolean && Value is bool b ? b : defaultValue;

    /// <summary>Returns the string value or null when not a string.</summary>
    public string? AsString() => Kind == JsonValueKind.String ? Value as string : null;

    /// <summary>Returns the 64-bit integer value or null when not a number.</summary>
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

    /// <summary>Returns the double value or null when not a number.</summary>
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

    /// <summary>Returns the object value or null when not an object.</summary>
    public JsonObject? AsObject() => Kind == JsonValueKind.Object ? Value as JsonObject : null;
    /// <summary>Returns the array value or null when not an array.</summary>
    public JsonArray? AsArray() => Kind == JsonValueKind.Array ? Value as JsonArray : null;

    /// <inheritdoc />
    public override string ToString() => JsonLite.Serialize(this);
}
