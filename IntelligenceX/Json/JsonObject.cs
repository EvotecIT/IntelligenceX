using System;
using System.Collections;
using System.Collections.Generic;

namespace IntelligenceX.Json;

public sealed class JsonObject : IEnumerable<KeyValuePair<string, JsonValue>> {
    private readonly Dictionary<string, JsonValue> _values;

    public JsonObject() : this(StringComparer.Ordinal) { }

    public JsonObject(StringComparer comparer) {
        _values = new Dictionary<string, JsonValue>(comparer);
    }

    public int Count => _values.Count;

    public JsonValue this[string key] {
        get => _values[key];
        set => _values[key] = value;
    }

    public JsonObject Add(string key, JsonValue value) {
        _values[key] = value;
        return this;
    }

    public JsonObject Add(string key, string? value) => Add(key, JsonValue.From(value));
    public JsonObject Add(string key, bool value) => Add(key, JsonValue.From(value));
    public JsonObject Add(string key, long value) => Add(key, JsonValue.From(value));
    public JsonObject Add(string key, double value) => Add(key, JsonValue.From(value));
    public JsonObject Add(string key, JsonObject value) => Add(key, JsonValue.From(value));
    public JsonObject Add(string key, JsonArray value) => Add(key, JsonValue.From(value));

    public bool TryGetValue(string key, out JsonValue? value) => _values.TryGetValue(key, out value);

    public string? GetString(string key) => _values.TryGetValue(key, out var value) ? value?.AsString() : null;
    public long? GetInt64(string key) => _values.TryGetValue(key, out var value) ? value?.AsInt64() : null;
    public double? GetDouble(string key) => _values.TryGetValue(key, out var value) ? value?.AsDouble() : null;
    public bool GetBoolean(string key, bool defaultValue = false) => _values.TryGetValue(key, out var value) ? value?.AsBoolean(defaultValue) ?? defaultValue : defaultValue;
    public JsonObject? GetObject(string key) => _values.TryGetValue(key, out var value) ? value?.AsObject() : null;
    public JsonArray? GetArray(string key) => _values.TryGetValue(key, out var value) ? value?.AsArray() : null;

    public IEnumerator<KeyValuePair<string, JsonValue>> GetEnumerator() => _values.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => _values.GetEnumerator();
}
