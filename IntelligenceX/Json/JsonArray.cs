using System.Collections;
using System.Collections.Generic;

namespace IntelligenceX.Json;

/// <summary>
/// Represents a mutable JSON array.
/// </summary>
public sealed class JsonArray : IEnumerable<JsonValue> {
    private readonly List<JsonValue> _values = new();

    /// <summary>
    /// Gets the number of elements in the array.
    /// </summary>
    public int Count => _values.Count;

    /// <summary>
    /// Gets or sets a value at the specified index.
    /// </summary>
    public JsonValue this[int index] {
        get => _values[index];
        set => _values[index] = value;
    }

    /// <summary>
    /// Adds a JSON value to the array.
    /// </summary>
    public JsonArray Add(JsonValue value) {
        _values.Add(value);
        return this;
    }

    /// <summary>
    /// Adds a string value to the array.
    /// </summary>
    public JsonArray Add(string? value) => Add(JsonValue.From(value));
    /// <summary>
    /// Adds a boolean value to the array.
    /// </summary>
    public JsonArray Add(bool value) => Add(JsonValue.From(value));
    /// <summary>
    /// Adds an integer value to the array.
    /// </summary>
    public JsonArray Add(long value) => Add(JsonValue.From(value));
    /// <summary>
    /// Adds a double value to the array.
    /// </summary>
    public JsonArray Add(double value) => Add(JsonValue.From(value));
    /// <summary>
    /// Adds a JSON object to the array.
    /// </summary>
    public JsonArray Add(JsonObject value) => Add(JsonValue.From(value));
    /// <summary>
    /// Adds a nested JSON array to the array.
    /// </summary>
    public JsonArray Add(JsonArray value) => Add(JsonValue.From(value));

    /// <summary>
    /// Returns a typed enumerator over the array values.
    /// </summary>
    public IEnumerator<JsonValue> GetEnumerator() => _values.GetEnumerator();
    /// <summary>
    /// Returns a non-generic enumerator over the array values.
    /// </summary>
    IEnumerator IEnumerable.GetEnumerator() => _values.GetEnumerator();
}
