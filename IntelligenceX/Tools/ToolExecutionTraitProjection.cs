using System;
using System.Collections.Generic;
using IntelligenceX.Json;

namespace IntelligenceX.Tools;

/// <summary>
/// Structured execution traits projected from a tool definition.
/// </summary>
public readonly record struct ToolExecutionTraits(
    string ExecutionScope,
    IReadOnlyList<string> TargetScopeArguments,
    IReadOnlyList<string> RemoteHostArguments) {

    /// <summary>
    /// Indicates whether the tool exposes any target-scope arguments.
    /// </summary>
    public bool SupportsTargetScoping => TargetScopeArguments?.Count > 0;

    /// <summary>
    /// Indicates whether the tool exposes any remote-host targeting arguments.
    /// </summary>
    public bool SupportsRemoteHostTargeting => RemoteHostArguments?.Count > 0;
}

/// <summary>
/// Shared projection helpers that resolve tool execution traits from explicit contracts first, then schema hints.
/// </summary>
public static class ToolExecutionTraitProjection {
    private static readonly IReadOnlyList<string> TargetScopeArgumentNames = ToolScopeArgumentNames.TargetScopeArguments;
    private static readonly IReadOnlyList<string> RemoteHostArgumentNames = ToolScopeArgumentNames.HostTargetInputArguments;

    /// <summary>
    /// Projects normalized execution traits from a tool definition.
    /// </summary>
    public static ToolExecutionTraits Project(ToolDefinition? definition) {
        if (definition is null) {
            return new ToolExecutionTraits(ToolExecutionScopes.LocalOnly, Array.Empty<string>(), Array.Empty<string>());
        }

        var schemaPropertyNames = ReadSchemaPropertyNames(definition.Parameters);
        var schemaRemoteHostArguments = IntersectKnownArguments(schemaPropertyNames, RemoteHostArgumentNames);
        var schemaTargetScopeArguments = MergeKnownArguments(
            IntersectKnownArguments(schemaPropertyNames, TargetScopeArgumentNames),
            schemaRemoteHostArguments);

        var explicitRemoteHostArguments = NormalizeDeclaredArguments(definition.Execution?.RemoteHostArguments, RemoteHostArgumentNames);
        var resolvedRemoteHostArguments = explicitRemoteHostArguments.Count > 0
            ? explicitRemoteHostArguments
            : schemaRemoteHostArguments;

        var explicitTargetScopeArguments = NormalizeDeclaredArguments(
            definition.Execution?.TargetScopeArguments,
            MergeKnownArguments(TargetScopeArgumentNames, RemoteHostArgumentNames));
        var resolvedTargetScopeArguments = MergeKnownArguments(
            explicitTargetScopeArguments.Count > 0 ? explicitTargetScopeArguments : schemaTargetScopeArguments,
            resolvedRemoteHostArguments);

        var executionScope = ToolExecutionScopes.Resolve(
            definition.Execution?.ExecutionScope,
            supportsRemoteExecution: resolvedRemoteHostArguments.Count > 0);

        return new ToolExecutionTraits(
            ExecutionScope: executionScope,
            TargetScopeArguments: resolvedTargetScopeArguments,
            RemoteHostArguments: resolvedRemoteHostArguments);
    }

    private static IReadOnlyList<string> ReadSchemaPropertyNames(JsonObject? parameters) {
        if (parameters is null) {
            return Array.Empty<string>();
        }

        var properties = parameters.GetObject("properties");
        if (properties is null || properties.Count == 0) {
            return Array.Empty<string>();
        }

        var list = new List<string>(properties.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in properties) {
            var normalized = NormalizeSchemaToken(kv.Key);
            if (normalized.Length == 0 || !seen.Add(normalized)) {
                continue;
            }

            list.Add(normalized);
        }

        return list.Count == 0 ? Array.Empty<string>() : list;
    }

    private static IReadOnlyList<string> NormalizeDeclaredArguments(
        IReadOnlyList<string>? values,
        IReadOnlyList<string> allowedNames) {
        if (values is not { Count: > 0 } || allowedNames.Count == 0) {
            return Array.Empty<string>();
        }

        var allowed = new HashSet<string>(allowedNames, StringComparer.OrdinalIgnoreCase);
        var list = new List<string>(values.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < values.Count; i++) {
            var normalized = NormalizeSchemaToken(values[i]);
            if (normalized.Length == 0 || !allowed.Contains(normalized) || !seen.Add(normalized)) {
                continue;
            }

            list.Add(normalized);
        }

        return list.Count == 0 ? Array.Empty<string>() : list;
    }

    private static IReadOnlyList<string> IntersectKnownArguments(
        IReadOnlyList<string> names,
        IReadOnlyList<string> knownNames) {
        if (names.Count == 0 || knownNames.Count == 0) {
            return Array.Empty<string>();
        }

        var set = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        for (var i = 0; i < knownNames.Count; i++) {
            var known = (knownNames[i] ?? string.Empty).Trim();
            if (known.Length == 0 || !set.Contains(known)) {
                continue;
            }

            result.Add(known);
        }

        return result.Count == 0 ? Array.Empty<string>() : result;
    }

    private static IReadOnlyList<string> MergeKnownArguments(
        IReadOnlyList<string> first,
        IReadOnlyList<string> second) {
        if (first.Count == 0 && second.Count == 0) {
            return Array.Empty<string>();
        }

        var merged = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AppendDistinct(first, merged, seen);
        AppendDistinct(second, merged, seen);
        return merged.Count == 0 ? Array.Empty<string>() : merged;
    }

    private static void AppendDistinct(
        IReadOnlyList<string> source,
        List<string> destination,
        HashSet<string> seen) {
        for (var i = 0; i < source.Count; i++) {
            var value = (source[i] ?? string.Empty).Trim();
            if (value.Length == 0 || !seen.Add(value)) {
                continue;
            }

            destination.Add(value);
        }
    }

    private static string NormalizeSchemaToken(string? token) {
        var value = (token ?? string.Empty).Trim();
        if (value.Length == 0) {
            return string.Empty;
        }

        var buffer = new char[value.Length];
        var length = 0;
        for (var i = 0; i < value.Length; i++) {
            var ch = value[i];
            if (char.IsLetterOrDigit(ch) || ch is '_' or '-') {
                buffer[length++] = ch;
            } else if (char.IsWhiteSpace(ch)) {
                buffer[length++] = '_';
            }
        }

        return length == 0 ? string.Empty : new string(buffer, 0, length).Trim('_');
    }
}
