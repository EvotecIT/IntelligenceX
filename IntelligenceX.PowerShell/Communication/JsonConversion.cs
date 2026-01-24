using System;
using System.Collections;
using IntelligenceX.Json;

namespace IntelligenceX.PowerShell;

internal static class JsonConversion {
    public static JsonObject? ToJsonObject(object? value) {
        if (value is null) {
            return null;
        }
        if (value is JsonObject jsonObject) {
            return jsonObject;
        }
        if (value is JsonValue jsonValue) {
            return jsonValue.AsObject();
        }
        if (value is IDictionary dictionary) {
            return JsonMapper.FromDictionary(dictionary);
        }

        throw new InvalidOperationException("Params must be a hashtable or JsonObject.");
    }

    public static JsonValue ToJsonValue(object? value) {
        return JsonMapper.FromObject(value);
    }
}
