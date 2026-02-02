using System;
using System.Collections;
using System.Collections.Generic;

namespace IntelligenceX.Json;

/// <summary>
/// Represents a JSON object.
/// </summary>
public sealed class JsonObject : IEnumerable<KeyValuePair<string, JsonValue>> {
    private readonly Dictionary<string, JsonValue> _values;

    /// <summary>Creates an empty JSON object with ordinal key comparison.</summary>
    public JsonObject() : this(StringComparer.Ordinal) { }

    /// <summary>Creates an empty JSON object with the specified comparer.</summary>
    /// <param name="comparer">The key comparer.</param>
    public JsonObject(StringComparer comparer) {
        _values = new Dictionary<string, JsonValue>(comparer);
    }

    /// <summary>Gets the number of properties in the object.</summary>
    public int Count => _values.Count;

    /// <summary>Gets or sets the value for the specified key.</summary>
    /// <param name="key">The property name.</param>
    public JsonValue this[string key] {
        get => _values[key];
        set => _values[key] = value;
    }

    /// <summary>Adds or replaces a property value.</summary>
    /// <param name="key">The property name.</param>
    /// <param name="value">The value to set.</param>
    public JsonObject Add(string key, JsonValue value) {
        _values[key] = value;
        return this;
    }

    /// <summary>Adds or replaces a string property.</summary>
    /// <param name="key">The property name.</param>
    /// <param name="value">The value to set.</param>
    public JsonObject Add(string key, string? value) => Add(key, JsonValue.From(value));
    /// <summary>Adds or replaces a boolean property.</summary>
    /// <param name="key">The property name.</param>
    /// <param name="value">The value to set.</param>
    public JsonObject Add(string key, bool value) => Add(key, JsonValue.From(value));
    /// <summary>Adds or replaces a 64-bit integer property.</summary>
    /// <param name="key">The property name.</param>
    /// <param name="value">The value to set.</param>
    public JsonObject Add(string key, long value) => Add(key, JsonValue.From(value));
    /// <summary>Adds or replaces a double property.</summary>
    /// <param name="key">The property name.</param>
    /// <param name="value">The value to set.</param>
    public JsonObject Add(string key, double value) => Add(key, JsonValue.From(value));
    /// <summary>Adds or replaces a JSON object property.</summary>
    /// <param name="key">The property name.</param>
    /// <param name="value">The value to set.</param>
    public JsonObject Add(string key, JsonObject value) => Add(key, JsonValue.From(value));
    /// <summary>Adds or replaces a JSON array property.</summary>
    /// <param name="key">The property name.</param>
    /// <param name="value">The value to set.</param>
    public JsonObject Add(string key, JsonArray value) => Add(key, JsonValue.From(value));

    /// <summary>Attempts to get a property value.</summary>
    /// <param name="key">The property name.</param>
    /// <param name="value">The value if found.</param>
    public bool TryGetValue(string key, out JsonValue? value) => _values.TryGetValue(key, out value);

    /// <summary>Gets a string property or null when missing.</summary>
    /// <param name="key">The property name.</param>
    public string? GetString(string key) => _values.TryGetValue(key, out var value) ? value?.AsString() : null;
    /// <summary>Gets a 64-bit integer property or null when missing.</summary>
    /// <param name="key">The property name.</param>
    public long? GetInt64(string key) => _values.TryGetValue(key, out var value) ? value?.AsInt64() : null;
    /// <summary>Gets a double property or null when missing.</summary>
    /// <param name="key">The property name.</param>
    public double? GetDouble(string key) => _values.TryGetValue(key, out var value) ? value?.AsDouble() : null;
    /// <summary>Gets a boolean property or the default when missing.</summary>
    /// <param name="key">The property name.</param>
    /// <param name="defaultValue">The fallback value.</param>
    public bool GetBoolean(string key, bool defaultValue = false) => _values.TryGetValue(key, out var value) ? value?.AsBoolean(defaultValue) ?? defaultValue : defaultValue;
    /// <summary>Gets a JSON object property or null when missing.</summary>
    /// <param name="key">The property name.</param>
    public JsonObject? GetObject(string key) => _values.TryGetValue(key, out var value) ? value?.AsObject() : null;
    /// <summary>Gets a JSON array property or null when missing.</summary>
    /// <param name="key">The property name.</param>
    public JsonArray? GetArray(string key) => _values.TryGetValue(key, out var value) ? value?.AsArray() : null;

    /// <summary>Returns an enumerator for the object.</summary>
    public IEnumerator<KeyValuePair<string, JsonValue>> GetEnumerator() => _values.GetEnumerator();
    /// <summary>Returns an enumerator for the object.</summary>
    IEnumerator IEnumerable.GetEnumerator() => _values.GetEnumerator();
}
