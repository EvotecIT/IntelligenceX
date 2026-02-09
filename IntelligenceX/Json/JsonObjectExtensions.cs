using System;
using System.Collections.Generic;

namespace IntelligenceX.Json;

/// <summary>
/// Extension helpers for <see cref="JsonObject"/>.
/// </summary>
public static class JsonObjectExtensions {
    /// <summary>
    /// Returns a new object containing properties that are not in <paramref name="knownKeys"/>.
    /// </summary>
    /// <param name="obj">Source JSON object.</param>
    /// <param name="knownKeys">Keys to exclude from the result.</param>
    /// <returns>A new object containing unknown keys, or null when none exist.</returns>
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

    /// <summary>
    /// Adds the array property only when the value is not null and contains at least one element.
    /// </summary>
    /// <param name="obj">Target JSON object.</param>
    /// <param name="key">Property name.</param>
    /// <param name="value">Array to add when non-empty.</param>
    /// <returns>The same instance for fluent chaining.</returns>
    public static JsonObject AddIfNotEmpty(this JsonObject obj, string key, JsonArray? value) {
        if (obj is null) {
            throw new ArgumentNullException(nameof(obj));
        }
        if (value is not null && value.Count > 0) {
            obj.Add(key, value);
        }
        return obj;
    }
}
