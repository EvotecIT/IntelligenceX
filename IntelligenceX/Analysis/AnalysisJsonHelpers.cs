using System;
using System.Collections.Generic;
using System.Linq;
using IntelligenceX.Json;

namespace IntelligenceX.Analysis;

internal static class AnalysisJsonHelpers {
    internal static IReadOnlyList<string>? ReadStringList(JsonObject obj, string key) {
        if (!obj.TryGetValue(key, out var value)) {
            return null;
        }
        var array = value?.AsArray();
        if (array is not null) {
            var list = array
                .Select(item => item.AsString())
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .Select(text => text!)
                .ToList();
            return list.Count == 0 ? null : list;
        }
        var textValue = value?.AsString();
        if (!string.IsNullOrWhiteSpace(textValue)) {
            return textValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
        return null;
    }

    internal static IReadOnlyDictionary<string, string>? ReadStringMap(JsonObject obj, string key) {
        if (!obj.TryGetValue(key, out var value)) {
            return null;
        }
        var mapObj = value?.AsObject();
        if (mapObj is null || mapObj.Count == 0) {
            return null;
        }
        var map = mapObj
            .Select(entry => new { entry.Key, Value = entry.Value?.AsString() })
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Key) && !string.IsNullOrWhiteSpace(entry.Value))
            .ToDictionary(entry => entry.Key, entry => entry.Value!, StringComparer.OrdinalIgnoreCase);
        return map.Count == 0 ? null : map;
    }
}
