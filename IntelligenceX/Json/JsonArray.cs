using System.Collections;
using System.Collections.Generic;

namespace IntelligenceX.Json;

/// <summary>
/// Represents a JSON array.
/// </summary>
public sealed class JsonArray : IEnumerable<JsonValue> {
    private readonly List<JsonValue> _values = new();

    /// <summary>Gets the number of items in the array.</summary>
    public int Count => _values.Count;

    /// <summary>Gets or sets the item at the specified index.</summary>
    /// <param name="index">The zero-based index.</param>
    public JsonValue this[int index] {
        get => _values[index];
        set => _values[index] = value;
    }

    /// <summary>Adds a JSON value to the array.</summary>
    /// <param name="value">The value to add.</param>
    public JsonArray Add(JsonValue value) {
        _values.Add(value);
        return this;
    }

    /// <summary>Adds a string value to the array.</summary>
    /// <param name="value">The value to add.</param>
    public JsonArray Add(string? value) => Add(JsonValue.From(value));
    /// <summary>Adds a boolean value to the array.</summary>
    /// <param name="value">The value to add.</param>
    public JsonArray Add(bool value) => Add(JsonValue.From(value));
    /// <summary>Adds a 64-bit integer value to the array.</summary>
    /// <param name="value">The value to add.</param>
    public JsonArray Add(long value) => Add(JsonValue.From(value));
    /// <summary>Adds a double value to the array.</summary>
    /// <param name="value">The value to add.</param>
    public JsonArray Add(double value) => Add(JsonValue.From(value));
    /// <summary>Adds a JSON object to the array.</summary>
    /// <param name="value">The value to add.</param>
    public JsonArray Add(JsonObject value) => Add(JsonValue.From(value));
    /// <summary>Adds a JSON array to the array.</summary>
    /// <param name="value">The value to add.</param>
    public JsonArray Add(JsonArray value) => Add(JsonValue.From(value));

    /// <summary>Returns an enumerator for the array.</summary>
    public IEnumerator<JsonValue> GetEnumerator() => _values.GetEnumerator();
    /// <summary>Returns an enumerator for the array.</summary>
    IEnumerator IEnumerable.GetEnumerator() => _values.GetEnumerator();
}
