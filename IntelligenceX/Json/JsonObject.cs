using System;
using System.Collections;
using System.Collections.Generic;

namespace IntelligenceX.Json;

/// <summary>
/// Represents a mutable JSON object.
/// </summary>
public sealed class JsonObject : IEnumerable<KeyValuePair<string, JsonValue>> {
    private readonly Dictionary<string, JsonValue> _values;

    /// <summary>
    /// Initializes an empty JSON object with ordinal key comparison.
    /// </summary>
    public JsonObject() : this(StringComparer.Ordinal) { }

    /// <summary>
    /// Initializes an empty JSON object with a custom key comparer.
    /// </summary>
    /// <param name="comparer">Comparer used for key lookup.</param>
    public JsonObject(StringComparer comparer) {
        _values = new Dictionary<string, JsonValue>(comparer);
    }

    /// <summary>
    /// Gets the number of properties in the object.
    /// </summary>
    public int Count => _values.Count;

    /// <summary>
    /// Gets or sets a value by property key.
    /// </summary>
    public JsonValue this[string key] {
        get => _values[key];
        set => _values[key] = value;
    }

    /// <summary>
    /// Adds or replaces a JSON value for the specified key.
    /// </summary>
    public JsonObject Add(string key, JsonValue value) {
        _values[key] = value;
        return this;
    }

    /// <summary>
    /// Adds or replaces a string value for the specified key.
    /// </summary>
    public JsonObject Add(string key, string? value) => Add(key, JsonValue.From(value));
    /// <summary>
    /// Adds or replaces a boolean value for the specified key.
    /// </summary>
    public JsonObject Add(string key, bool value) => Add(key, JsonValue.From(value));
    /// <summary>
    /// Adds or replaces an integer value for the specified key.
    /// </summary>
    public JsonObject Add(string key, long value) => Add(key, JsonValue.From(value));
    /// <summary>
    /// Adds or replaces a double value for the specified key.
    /// </summary>
    public JsonObject Add(string key, double value) => Add(key, JsonValue.From(value));
    /// <summary>
    /// Adds or replaces a JSON object value for the specified key.
    /// </summary>
    public JsonObject Add(string key, JsonObject value) => Add(key, JsonValue.From(value));
    /// <summary>
    /// Adds or replaces a JSON array value for the specified key.
    /// </summary>
    public JsonObject Add(string key, JsonArray value) => Add(key, JsonValue.From(value));

    /// <summary>
    /// Attempts to retrieve a JSON value by key.
    /// </summary>
    public bool TryGetValue(string key, out JsonValue? value) => _values.TryGetValue(key, out value);

    /// <summary>
    /// Returns a string value for the specified key.
    /// </summary>
    public string? GetString(string key) => _values.TryGetValue(key, out var value) ? value?.AsString() : null;
    /// <summary>
    /// Returns an integer value for the specified key.
    /// </summary>
    public long? GetInt64(string key) => _values.TryGetValue(key, out var value) ? value?.AsInt64() : null;
    /// <summary>
    /// Returns a double value for the specified key.
    /// </summary>
    public double? GetDouble(string key) => _values.TryGetValue(key, out var value) ? value?.AsDouble() : null;
    /// <summary>
    /// Returns a boolean value for the specified key or a default when missing.
    /// </summary>
    public bool GetBoolean(string key, bool defaultValue = false) => _values.TryGetValue(key, out var value) ? value?.AsBoolean(defaultValue) ?? defaultValue : defaultValue;
    /// <summary>
    /// Returns a JSON object value for the specified key.
    /// </summary>
    public JsonObject? GetObject(string key) => _values.TryGetValue(key, out var value) ? value?.AsObject() : null;
    /// <summary>
    /// Returns a JSON array value for the specified key.
    /// </summary>
    public JsonArray? GetArray(string key) => _values.TryGetValue(key, out var value) ? value?.AsArray() : null;

    /// <summary>
    /// Returns a typed enumerator over the object's properties.
    /// </summary>
    public IEnumerator<KeyValuePair<string, JsonValue>> GetEnumerator() => _values.GetEnumerator();
    /// <summary>
    /// Returns a non-generic enumerator over the object's properties.
    /// </summary>
    IEnumerator IEnumerable.GetEnumerator() => _values.GetEnumerator();
}
