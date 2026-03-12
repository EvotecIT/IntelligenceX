using System;
using System.Collections.Generic;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Chat.Tooling;

/// <summary>
/// Shared host-target argument and field-name helpers derived from tool schemas/contracts.
/// </summary>
public static class ToolHostTargeting {

    /// <summary>
    /// Ordered well-known host-target input arguments.
    /// </summary>
    public static IReadOnlyList<string> HostTargetArguments => ToolHostTargetArgumentNames.OrderedInputArguments;

    /// <summary>
    /// Returns true when the provided key is a known host-target input argument or field.
    /// </summary>
    public static bool IsHostTargetArgumentName(string? key) {
        var normalized = NormalizeKey(key);
        return normalized.Length > 0 && ToolHostTargetArgumentNames.IsKnownArgumentOrField(normalized);
    }

    /// <summary>
    /// Returns aliases compatible with the provided input key.
    /// </summary>
    public static IReadOnlyList<string> GetCompatibleArgumentAliases(string? inputKey) {
        var normalized = NormalizeKey(inputKey);
        if (normalized.Length == 0) {
            return Array.Empty<string>();
        }

        if (!IsHostTargetArgumentName(normalized)) {
            return new[] { normalized };
        }

        return ToolHostTargetArgumentNames.OrderedInputArguments;
    }

    /// <summary>
    /// Returns true when the tool schema exposes any host-target input arguments.
    /// </summary>
    public static bool ToolSupportsHostTargetInputs(ToolDefinition? definition) {
        return GetSupportedHostTargetArguments(definition).Count > 0;
    }

    /// <summary>
    /// Returns the schema-supported host-target input arguments for the provided definition.
    /// </summary>
    public static IReadOnlyList<string> GetSupportedHostTargetArguments(ToolDefinition? definition) {
        if (definition?.Parameters is null) {
            return Array.Empty<string>();
        }

        var properties = definition.Parameters.GetObject("properties");
        if (properties is null || properties.Count == 0) {
            return Array.Empty<string>();
        }

        var available = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in properties) {
            var normalized = NormalizeKey(pair.Key);
            if (normalized.Length == 0 || !IsHostTargetArgumentName(normalized)) {
                continue;
            }

            available.Add(normalized);
        }

        if (available.Count == 0) {
            return Array.Empty<string>();
        }

        var ordered = new List<string>(available.Count);
        for (var i = 0; i < ToolHostTargetArgumentNames.OrderedInputArguments.Count; i++) {
            var candidate = ToolHostTargetArgumentNames.OrderedInputArguments[i];
            if (available.Contains(candidate)) {
                ordered.Add(candidate);
            }
        }

        return ordered.Count == 0 ? Array.Empty<string>() : ordered.ToArray();
    }

    /// <summary>
    /// Tries to read host-target values from tool arguments using shared aliases.
    /// </summary>
    public static bool TryReadHostTargetValues(JsonObject? arguments, out IReadOnlyList<string> values) {
        values = Array.Empty<string>();
        if (arguments is null || arguments.Count == 0) {
            return false;
        }

        var collected = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < ToolHostTargetArgumentNames.OrderedInputArguments.Count; i++) {
            if (!TryReadInputValuesByKey(arguments, ToolHostTargetArgumentNames.OrderedInputArguments[i], out var candidateValues)) {
                continue;
            }

            for (var valueIndex = 0; valueIndex < candidateValues.Count; valueIndex++) {
                var value = (candidateValues[valueIndex] ?? string.Empty).Trim();
                if (value.Length == 0 || !seen.Add(value)) {
                    continue;
                }

                collected.Add(value);
            }
        }

        if (collected.Count == 0) {
            return false;
        }

        values = collected.ToArray();
        return true;
    }

    /// <summary>
    /// Picks the preferred host-target input argument for an existing call or schema definition.
    /// </summary>
    public static bool TryPickPreferredInputArgument(
        ToolDefinition? definition,
        JsonObject? arguments,
        out string key,
        out bool keyIsArray) {
        key = string.Empty;
        keyIsArray = false;

        if (arguments is not null) {
            for (var i = 0; i < ToolHostTargetArgumentNames.OrderedInputArguments.Count; i++) {
                var preferred = ToolHostTargetArgumentNames.OrderedInputArguments[i];
                foreach (var pair in arguments) {
                    if (!string.Equals(pair.Key, preferred, StringComparison.OrdinalIgnoreCase)) {
                        continue;
                    }

                    key = pair.Key;
                    keyIsArray = pair.Value?.AsArray() is not null || IsArrayHostTargetArgument(preferred);
                    return true;
                }
            }
        }

        var supported = GetSupportedHostTargetArguments(definition);
        if (supported.Count == 0) {
            return false;
        }

        key = supported[0];
        keyIsArray = IsArrayHostTargetArgument(key);
        return true;
    }

    private static bool TryReadInputValuesByKey(JsonObject arguments, string inputKey, out IReadOnlyList<string> values) {
        values = Array.Empty<string>();
        if (arguments.Count == 0 || string.IsNullOrWhiteSpace(inputKey)) {
            return false;
        }

        var normalizedKey = inputKey.Trim();
        if (arguments.TryGetValue(normalizedKey, out var exactValue)
            && TryCollectNormalizedInputValues(exactValue, out var normalizedValues)) {
            values = normalizedValues;
            return true;
        }

        foreach (var pair in arguments) {
            if (!string.Equals(pair.Key, normalizedKey, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            if (TryCollectNormalizedInputValues(pair.Value, out var aliasValues)) {
                values = aliasValues;
                return true;
            }
        }

        return false;
    }

    private static bool TryCollectNormalizedInputValues(JsonValue? value, out IReadOnlyList<string> normalizedValues) {
        normalizedValues = Array.Empty<string>();
        if (value is null) {
            return false;
        }

        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectNormalizedInputValues(value, values);
        if (values.Count == 0) {
            return false;
        }

        normalizedValues = new List<string>(values).ToArray();
        return true;
    }

    private static void CollectNormalizedInputValues(JsonValue? value, HashSet<string> target) {
        if (value is null) {
            return;
        }

        switch (value.Kind) {
            case JsonValueKind.String:
                var stringValue = (value.AsString() ?? string.Empty).Trim();
                if (stringValue.Length > 0) {
                    target.Add(stringValue);
                }
                break;
            case JsonValueKind.Number:
            case JsonValueKind.Boolean:
                var scalarValue = value.ToString().Trim();
                if (scalarValue.Length > 0) {
                    target.Add(scalarValue);
                }
                break;
            case JsonValueKind.Array:
                var array = value.AsArray();
                if (array is null) {
                    return;
                }

                foreach (var item in array) {
                    CollectNormalizedInputValues(item, target);
                }
                break;
        }
    }

    private static bool IsArrayHostTargetArgument(string key) {
        var normalized = NormalizeKey(key);
        return ToolHostTargetArgumentNames.IsArrayArgument(normalized);
    }

    private static string NormalizeKey(string? value) {
        return (value ?? string.Empty).Trim();
    }
}
