using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;

namespace IntelligenceX.Json;

/// <summary>
/// Maps common CLR types to <see cref="JsonValue"/> representations.
/// </summary>
public static class JsonMapper {
    /// <summary>
    /// Converts an object into a <see cref="JsonValue"/>.
    /// </summary>
    /// <param name="value">Value to map.</param>
    /// <returns>The mapped JSON value.</returns>
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
            case DateTime dt:
                return JsonValue.From(dt.ToUniversalTime().ToString("O"));
            case DateTimeOffset dto:
                return JsonValue.From(dto.ToUniversalTime().ToString("O"));
            case TimeSpan ts:
                return JsonValue.From(ts.ToString("c", CultureInfo.InvariantCulture));
            case Enum e:
                return JsonValue.From(e.ToString());
            case byte or sbyte or short or ushort or int or uint or long:
                return JsonValue.From(Convert.ToInt64(value));
            case ulong unsignedLong:
                // JsonValue stores numbers as Int64/Double. Keep exact Int64 when possible;
                // otherwise fall back to Double to avoid overflow exceptions.
                return unsignedLong <= long.MaxValue
                    ? JsonValue.From((long)unsignedLong)
                    : JsonValue.From((double)unsignedLong);
            case float floatValue:
                return float.IsNaN(floatValue) || float.IsInfinity(floatValue)
                    ? JsonValue.Null
                    : JsonValue.From((double)floatValue);
            case double doubleValue:
                return double.IsNaN(doubleValue) || double.IsInfinity(doubleValue)
                    ? JsonValue.Null
                    : JsonValue.From(doubleValue);
            case decimal decimalValue:
                return JsonValue.From(Convert.ToDouble(decimalValue));
            case IDictionary dictionary:
                return JsonValue.From(FromDictionary(dictionary));
            case IEnumerable enumerable:
                return JsonValue.From(FromEnumerable(enumerable));
            default:
                return JsonValue.From(value.ToString());
        }
    }

    /// <summary>
    /// Converts a dictionary to a <see cref="JsonObject"/>.
    /// </summary>
    /// <param name="dictionary">Dictionary to map.</param>
    /// <returns>The mapped JSON object.</returns>
    public static JsonObject FromDictionary(IDictionary dictionary) {
        var obj = new JsonObject();
        foreach (DictionaryEntry entry in dictionary) {
            var key = entry.Key?.ToString() ?? string.Empty;
            obj.Add(key, FromObject(entry.Value));
        }
        return obj;
    }

    /// <summary>
    /// Converts an enumerable into a <see cref="JsonArray"/>.
    /// </summary>
    /// <param name="enumerable">Enumerable to map.</param>
    /// <returns>The mapped JSON array.</returns>
    public static JsonArray FromEnumerable(IEnumerable enumerable) {
        var array = new JsonArray();
        foreach (var item in enumerable) {
            array.Add(FromObject(item));
        }
        return array;
    }

    /// <summary>
    /// Converts a dictionary with string keys to a <see cref="JsonObject"/>.
    /// </summary>
    /// <param name="dictionary">Dictionary to map.</param>
    /// <returns>The mapped JSON object.</returns>
    public static JsonObject FromDictionary(IReadOnlyDictionary<string, object?> dictionary) {
        var obj = new JsonObject();
        foreach (var entry in dictionary) {
            obj.Add(entry.Key, FromObject(entry.Value));
        }
        return obj;
    }
}
