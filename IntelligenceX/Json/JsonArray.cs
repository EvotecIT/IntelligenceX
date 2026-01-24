using System.Collections;
using System.Collections.Generic;

namespace IntelligenceX.Json;

public sealed class JsonArray : IEnumerable<JsonValue> {
    private readonly List<JsonValue> _values = new();

    public int Count => _values.Count;

    public JsonValue this[int index] {
        get => _values[index];
        set => _values[index] = value;
    }

    public JsonArray Add(JsonValue value) {
        _values.Add(value);
        return this;
    }

    public JsonArray Add(string? value) => Add(JsonValue.From(value));
    public JsonArray Add(bool value) => Add(JsonValue.From(value));
    public JsonArray Add(long value) => Add(JsonValue.From(value));
    public JsonArray Add(double value) => Add(JsonValue.From(value));
    public JsonArray Add(JsonObject value) => Add(JsonValue.From(value));
    public JsonArray Add(JsonArray value) => Add(JsonValue.From(value));

    public IEnumerator<JsonValue> GetEnumerator() => _values.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => _values.GetEnumerator();
}
