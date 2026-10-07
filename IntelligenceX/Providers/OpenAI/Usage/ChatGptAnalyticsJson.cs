using System;
using System.Collections.Generic;
using System.Globalization;
using IntelligenceX.Json;

namespace IntelligenceX.OpenAI.Usage;

internal static class ChatGptAnalyticsJson {
    internal static double? Number(JsonObject obj, string key) {
        var value = obj.GetDouble(key);
        if (!value.HasValue && double.TryParse(obj.GetString(key), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            value = parsed;
        return value.HasValue && !double.IsNaN(value.Value) && !double.IsInfinity(value.Value) ? value : null;
    }
    internal static bool? Boolean(JsonObject obj, string key) =>
        obj.TryGetValue(key, out var value) && value?.Kind == JsonValueKind.Boolean ? value.AsBoolean() : null;
    internal static IReadOnlyList<T> Rows<T>(JsonObject obj, string key, Func<JsonObject, T> parse) {
        var result = new List<T>();
        foreach (var item in obj.GetArray(key) ?? new JsonArray())
            if (item.AsObject() is { } row) result.Add(parse(row));
        return result;
    }
}
