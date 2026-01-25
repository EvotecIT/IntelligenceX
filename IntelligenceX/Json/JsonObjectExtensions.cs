using System;
using System.Collections.Generic;

namespace IntelligenceX.Json;

public static class JsonObjectExtensions {
    public static JsonObject? ExtractAdditional(this JsonObject obj, params string[] knownKeys) {
        if (obj is null) {
            throw new ArgumentNullException(nameof(obj));
        }
        var additional = new JsonObject(StringComparer.Ordinal);
        if (knownKeys.Length == 0) {
            foreach (var entry in obj) {
                additional.Add(entry.Key, entry.Value);
            }
            return additional.Count == 0 ? null : additional;
        }

        var known = new HashSet<string>(knownKeys, StringComparer.Ordinal);
        foreach (var entry in obj) {
            if (!known.Contains(entry.Key)) {
                additional.Add(entry.Key, entry.Value);
            }
        }
        return additional.Count == 0 ? null : additional;
    }
}
