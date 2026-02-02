using System;
using System.Collections;
using System.Collections.Generic;

namespace IntelligenceX.Json;

/// <summary>
/// Converts CLR values into <see cref="JsonValue"/> representations.
/// </summary>
public static class JsonMapper {
    /// <summary>Converts a CLR value to a <see cref="JsonValue"/>.</summary>
    /// <param name="value">The value to convert.</param>
    public static JsonValue FromObject(object? value) {
        if (value is null) {
            return JsonValue.Null;
        }

        switch (value) {
            case JsonValue jsonValue:
                return jsonValue;
            case JsonObject jsonObject:
                return JsonValue.From(jsonObject);
            case JsonArray jsonArray:
                return JsonValue.From(jsonArray);
            case string str:
                return JsonValue.From(str);
            case bool b:
                return JsonValue.From(b);
            case byte or sbyte or short or ushort or int or uint or long or ulong:
                return JsonValue.From(Convert.ToInt64(value));
            case float or double or decimal:
                return JsonValue.From(Convert.ToDouble(value));
            case IDictionary dictionary:
                return JsonValue.From(FromDictionary(dictionary));
            case IEnumerable enumerable:
                return JsonValue.From(FromEnumerable(enumerable));
            default:
                return JsonValue.From(value.ToString());
        }
    }

    /// <summary>Converts a dictionary into a <see cref="JsonObject"/>.</summary>
    /// <param name="dictionary">The dictionary to convert.</param>
    public static JsonObject FromDictionary(IDictionary dictionary) {
        var obj = new JsonObject();
        foreach (DictionaryEntry entry in dictionary) {
            var key = entry.Key?.ToString() ?? string.Empty;
            obj.Add(key, FromObject(entry.Value));
        }
        return obj;
    }

    /// <summary>Converts an enumerable into a <see cref="JsonArray"/>.</summary>
    /// <param name="enumerable">The enumerable to convert.</param>
    public static JsonArray FromEnumerable(IEnumerable enumerable) {
        var array = new JsonArray();
        foreach (var item in enumerable) {
            array.Add(FromObject(item));
        }
        return array;
    }

    /// <summary>Converts a read-only dictionary into a <see cref="JsonObject"/>.</summary>
    /// <param name="dictionary">The dictionary to convert.</param>
    public static JsonObject FromDictionary(IReadOnlyDictionary<string, object?> dictionary) {
        var obj = new JsonObject();
        foreach (var entry in dictionary) {
            obj.Add(entry.Key, FromObject(entry.Value));
        }
        return obj;
    }
}
