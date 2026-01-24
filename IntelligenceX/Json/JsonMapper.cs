using System;
using System.Collections;
using System.Collections.Generic;

namespace IntelligenceX.Json;

public static class JsonMapper {
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

    public static JsonObject FromDictionary(IDictionary dictionary) {
        var obj = new JsonObject();
        foreach (DictionaryEntry entry in dictionary) {
            var key = entry.Key?.ToString() ?? string.Empty;
            obj.Add(key, FromObject(entry.Value));
        }
        return obj;
    }

    public static JsonArray FromEnumerable(IEnumerable enumerable) {
        var array = new JsonArray();
        foreach (var item in enumerable) {
            array.Add(FromObject(item));
        }
        return array;
    }

    public static JsonObject FromDictionary(IReadOnlyDictionary<string, object?> dictionary) {
        var obj = new JsonObject();
        foreach (var entry in dictionary) {
            obj.Add(entry.Key, FromObject(entry.Value));
        }
        return obj;
    }
}
