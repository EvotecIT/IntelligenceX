using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Chat.Tooling;

public static partial class ToolPackBootstrap {
    internal static void ConfigurePackOptionsFromRuntimeBag(
        object options,
        ToolPackBootstrapOptions bootstrapOptions,
        Type packType,
        string? explicitPackKey = null) {
        if (options is null) {
            throw new ArgumentNullException(nameof(options));
        }
        if (bootstrapOptions is null) {
            throw new ArgumentNullException(nameof(bootstrapOptions));
        }
        if (packType is null) {
            throw new ArgumentNullException(nameof(packType));
        }

        var effectiveBag = BuildEffectivePackRuntimeOptionBag(bootstrapOptions);
        if (effectiveBag.Count == 0) {
            return;
        }

        var packKeys = ResolvePackRuntimeOptionKeys(options, packType, explicitPackKey);
        var effectiveProperties = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var packKey in packKeys) {
            if (!effectiveBag.TryGetValue(packKey, out var properties) || properties.Count == 0) {
                continue;
            }

            foreach (var property in properties) {
                var propertyName = (property.Key ?? string.Empty).Trim();
                if (propertyName.Length == 0) {
                    continue;
                }

                // Merge by precedence first, then apply once for deterministic behavior:
                // later, more-specific keys win over earlier defaults.
                effectiveProperties[propertyName] = property.Value;
            }
        }

        foreach (var property in effectiveProperties.OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)) {
            ApplyPackOptionValueIfPresent(options, property.Key, property.Value);
        }
    }

    internal static IReadOnlyList<string> ResolvePackRuntimeOptionKeys(Type packType, string? explicitPackKey = null) {
        return ResolvePackRuntimeOptionKeys(options: null, packType, explicitPackKey);
    }

    internal static IReadOnlyList<string> ResolvePackRuntimeOptionKeys(object? options, Type packType, string? explicitPackKey = null) {
        if (packType is null) {
            throw new ArgumentNullException(nameof(packType));
        }

        var keys = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddPackOptionKey(keys, seen, PackOptionKeyGlobal);
        AddPackOptionKey(keys, seen, packType.Assembly.GetName().Name);
        if (options is IToolPackRuntimeOptionTarget runtimeOptionTarget) {
            foreach (var runtimeOptionKey in runtimeOptionTarget.RuntimeOptionKeys ?? Array.Empty<string>()) {
                AddPackOptionKey(keys, seen, runtimeOptionKey);
            }
        }

        var namespaceValue = packType.Namespace ?? string.Empty;
        const string toolsNamespacePrefix = "IntelligenceX.Tools.";
        if (namespaceValue.StartsWith(toolsNamespacePrefix, StringComparison.OrdinalIgnoreCase)) {
            var suffix = namespaceValue.Substring(toolsNamespacePrefix.Length);
            var segments = suffix.Split('.', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length > 0) {
                AddPackOptionKey(keys, seen, segments[0]);
            }
        } else {
            AddPackOptionKey(keys, seen, namespaceValue);
        }

        var typeName = packType.Name;
        if (typeName.EndsWith("ToolPack", StringComparison.OrdinalIgnoreCase)) {
            typeName = typeName[..^"ToolPack".Length];
        } else if (typeName.EndsWith("Pack", StringComparison.OrdinalIgnoreCase)) {
            typeName = typeName[..^"Pack".Length];
        }
        AddPackOptionKey(keys, seen, typeName);
        AddPackOptionKey(keys, seen, explicitPackKey);

        return keys;
    }

    private static Dictionary<string, Dictionary<string, object?>> BuildEffectivePackRuntimeOptionBag(ToolPackBootstrapOptions options) {
        var merged = new Dictionary<string, Dictionary<string, object?>>(StringComparer.OrdinalIgnoreCase);
        MergePackRuntimeOptionBag(merged, BuildGlobalRuntimeOptionBag(options));
        MergePackRuntimeOptionBag(merged, options.PackRuntimeOptionBag);
        return merged;
    }

    private static void MergePackRuntimeOptionBag(
        Dictionary<string, Dictionary<string, object?>> target,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>>? source) {
        if (source is null || source.Count == 0) {
            return;
        }

        foreach (var entry in source) {
            var normalizedPackKey = NormalizePackRuntimeOptionKey(entry.Key);
            if (normalizedPackKey.Length == 0) {
                continue;
            }

            if (!target.TryGetValue(normalizedPackKey, out var propertyBag)) {
                propertyBag = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                target[normalizedPackKey] = propertyBag;
            }

            if (entry.Value is null || entry.Value.Count == 0) {
                continue;
            }

            foreach (var property in entry.Value) {
                var propertyName = (property.Key ?? string.Empty).Trim();
                if (propertyName.Length == 0) {
                    continue;
                }
                propertyBag[propertyName] = property.Value;
            }
        }
    }

    private static void AddPackOptionKey(List<string> keys, HashSet<string> seen, string? rawPackKey) {
        var normalized = NormalizePackRuntimeOptionKey(rawPackKey);
        if (normalized.Length == 0) {
            return;
        }

        if (seen.Add(normalized)) {
            keys.Add(normalized);
        }
    }

    private static string NormalizePackRuntimeOptionKey(string? rawPackKey) {
        var normalized = (rawPackKey ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return string.Empty;
        }
        if (string.Equals(normalized, PackOptionKeyGlobal, StringComparison.Ordinal)) {
            return PackOptionKeyGlobal;
        }

        const string toolsPrefix = "IntelligenceX.Tools.";
        if (normalized.StartsWith(toolsPrefix, StringComparison.OrdinalIgnoreCase)) {
            normalized = normalized.Substring(toolsPrefix.Length);
        }

        return NormalizePackId(normalized);
    }

    private static void ApplyPackOptionValueIfPresent(object instance, string propertyName, object? value) {
        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        if (property is null) {
            return;
        }

        if (TryAddStringListValues(property, instance, value)) {
            return;
        }

        if (!property.CanWrite) {
            return;
        }

        if (value is null) {
            property.SetValue(instance, null);
            return;
        }

        var targetType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        var valueType = value.GetType();

        if (targetType.IsAssignableFrom(valueType)) {
            property.SetValue(instance, value);
            return;
        }

        try {
            var converted = Convert.ChangeType(value, targetType);
            property.SetValue(instance, converted);
        } catch {
            // Keep defaults when conversion fails.
        }
    }

    private static bool TryAddStringListValues(PropertyInfo property, object instance, object? value) {
        if (value is null || value is string || !property.CanRead) {
            return false;
        }

        if (property.GetValue(instance) is not IList list) {
            return false;
        }

        if (value is not IEnumerable<string> stringValues) {
            return false;
        }

        foreach (var stringValue in stringValues) {
            if (string.IsNullOrWhiteSpace(stringValue)) {
                continue;
            }

            list.Add(stringValue.Trim());
        }

        return true;
    }
}
