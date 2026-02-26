using System;
using IntelligenceX.Json;

namespace IntelligenceX.OpenAI.Native;

internal sealed partial class OpenAINativeTransport {

    private static string? GetStringIgnoreCase(JsonObject source, string key) {
        if (source is null || string.IsNullOrWhiteSpace(key)) {
            return null;
        }

        foreach (var pair in source) {
            if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase)) {
                return pair.Value?.AsString();
            }
        }

        return null;
    }

    private static JsonObject? GetObjectIgnoreCase(JsonObject source, string key) {
        if (source is null || string.IsNullOrWhiteSpace(key)) {
            return null;
        }

        foreach (var pair in source) {
            if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase)) {
                return pair.Value?.AsObject();
            }
        }

        return null;
    }

    private static string? GetStringOrSerializedIgnoreCase(JsonObject source, string key) {
        if (source is null || string.IsNullOrWhiteSpace(key)) {
            return null;
        }

        foreach (var pair in source) {
            if (!string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            var asString = pair.Value?.AsString();
            if (!string.IsNullOrWhiteSpace(asString)) {
                return asString;
            }

            if (pair.Value is not null) {
                return JsonLite.Serialize(pair.Value);
            }

            return null;
        }

        return null;
    }

    private static string? FirstNonEmpty(params string?[] values) {
        if (values is null || values.Length == 0) {
            return null;
        }

        for (var i = 0; i < values.Length; i++) {
            var value = values[i];
            if (!string.IsNullOrWhiteSpace(value)) {
                return value;
            }
        }

        return null;
    }
}
