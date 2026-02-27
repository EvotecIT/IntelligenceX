using System;
using System.Collections.Generic;
using System.Linq;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Json;
using IntelligenceX.OpenAI.ToolCalling;
using IntelligenceX.Tools;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    private const string PackInfoToolSuffix = "_pack_info";
    private const string EnvironmentDiscoverToolSuffix = "_environment_discover";
    private const string HostPackPreflightCallIdPrefix = "host_pack_preflight_";

    private readonly record struct PackPreflightCatalog(
        Dictionary<string, ToolDefinition> PackInfoByPrefix,
        Dictionary<string, ToolDefinition> EnvironmentDiscoverByPrefix,
        string[] PrefixesByLengthDescending);

    private IReadOnlyList<ToolCall> BuildHostPackPreflightCalls(
        string threadId,
        IReadOnlyList<ToolDefinition> allToolDefinitions,
        IReadOnlyList<ToolCall> extractedCalls) {
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0 || allToolDefinitions.Count == 0 || extractedCalls.Count == 0) {
            return Array.Empty<ToolCall>();
        }

        var catalog = BuildPackPreflightCatalog(allToolDefinitions);
        if (catalog.PackInfoByPrefix.Count == 0) {
            return Array.Empty<ToolCall>();
        }

        var operationalPrefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var explicitRoundPreflightNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < extractedCalls.Count; i++) {
            var callName = (extractedCalls[i].Name ?? string.Empty).Trim();
            if (callName.Length == 0) {
                continue;
            }

            if (TryExtractPackPrefixFromPackInfoTool(callName, out _)
                || TryExtractPackPrefixFromEnvironmentDiscoverTool(callName, out _)) {
                explicitRoundPreflightNames.Add(callName);
                continue;
            }

            if (TryResolvePackPrefixForOperationalTool(callName, catalog.PrefixesByLengthDescending, out var prefix)) {
                operationalPrefixes.Add(prefix);
            }
        }

        if (operationalPrefixes.Count == 0) {
            return Array.Empty<ToolCall>();
        }

        var rememberedPreflightTools = SnapshotRememberedPackPreflightTools(normalizedThreadId);
        var preflightCalls = new List<ToolCall>();
        var orderedPrefixes = operationalPrefixes
            .OrderBy(static prefix => prefix, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        for (var i = 0; i < orderedPrefixes.Length; i++) {
            var prefix = orderedPrefixes[i];
            if (catalog.PackInfoByPrefix.TryGetValue(prefix, out var packInfoDefinition)) {
                var packInfoName = (packInfoDefinition.Name ?? string.Empty).Trim();
                if (packInfoName.Length > 0
                    && !explicitRoundPreflightNames.Contains(packInfoName)
                    && !rememberedPreflightTools.Contains(packInfoName)) {
                    preflightCalls.Add(BuildHostPackPreflightCall(packInfoName, "pack_info"));
                }
            }

            if (!catalog.EnvironmentDiscoverByPrefix.TryGetValue(prefix, out var discoverDefinition)
                || ToolDefinitionHasRequiredArguments(discoverDefinition)) {
                continue;
            }

            var discoverName = (discoverDefinition.Name ?? string.Empty).Trim();
            if (discoverName.Length == 0
                || explicitRoundPreflightNames.Contains(discoverName)
                || rememberedPreflightTools.Contains(discoverName)) {
                continue;
            }

            preflightCalls.Add(BuildHostPackPreflightCall(discoverName, "environment_discover"));
        }

        return preflightCalls.Count == 0 ? Array.Empty<ToolCall>() : preflightCalls;
    }

    private void RememberSuccessfulPackPreflightCalls(
        string threadId,
        IReadOnlyList<ToolCall> executedCalls,
        IReadOnlyList<ToolOutputDto> outputs) {
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0 || executedCalls.Count == 0 || outputs.Count == 0) {
            return;
        }

        var successfulCallIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < outputs.Count; i++) {
            var output = outputs[i];
            if (!IsSuccessfulToolOutput(output)) {
                continue;
            }

            var outputCallId = (output.CallId ?? string.Empty).Trim();
            if (outputCallId.Length > 0) {
                successfulCallIds.Add(outputCallId);
            }
        }

        if (successfulCallIds.Count == 0) {
            return;
        }

        var successfulPreflightToolNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < executedCalls.Count; i++) {
            var call = executedCalls[i];
            var callId = (call.CallId ?? string.Empty).Trim();
            var callName = (call.Name ?? string.Empty).Trim();
            if (callId.Length == 0
                || callName.Length == 0
                || !successfulCallIds.Contains(callId)
                || !IsHostPackPreflightToolName(callName)) {
                continue;
            }

            successfulPreflightToolNames.Add(callName);
        }

        if (successfulPreflightToolNames.Count == 0) {
            return;
        }

        lock (_toolRoutingContextLock) {
            var updated = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (_packPreflightToolNamesByThreadId.TryGetValue(normalizedThreadId, out var existingNames)
                && existingNames is { Length: > 0 }) {
                for (var i = 0; i < existingNames.Length; i++) {
                    var existingName = (existingNames[i] ?? string.Empty).Trim();
                    if (existingName.Length > 0) {
                        updated.Add(existingName);
                    }
                }
            }

            var changed = false;
            foreach (var successfulName in successfulPreflightToolNames) {
                if (updated.Add(successfulName)) {
                    changed = true;
                }
            }

            if (!changed) {
                if (!_packPreflightSeenUtcTicks.ContainsKey(normalizedThreadId)) {
                    _packPreflightSeenUtcTicks[normalizedThreadId] = DateTime.UtcNow.Ticks;
                    TrimWeightedRoutingContextsNoLock();
                }

                return;
            }

            _packPreflightToolNamesByThreadId[normalizedThreadId] = updated
                .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            _packPreflightSeenUtcTicks[normalizedThreadId] = DateTime.UtcNow.Ticks;
            TrimWeightedRoutingContextsNoLock();
        }
    }

    private static PackPreflightCatalog BuildPackPreflightCatalog(IReadOnlyList<ToolDefinition> definitions) {
        var packInfoByPrefix = new Dictionary<string, ToolDefinition>(StringComparer.OrdinalIgnoreCase);
        var discoverByPrefix = new Dictionary<string, ToolDefinition>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < definitions.Count; i++) {
            var definition = definitions[i];
            if (definition is null) {
                continue;
            }

            var name = (definition.Name ?? string.Empty).Trim();
            if (name.Length == 0) {
                continue;
            }

            if (TryExtractPackPrefixFromPackInfoTool(name, out var packInfoPrefix)) {
                if (!packInfoByPrefix.ContainsKey(packInfoPrefix)) {
                    packInfoByPrefix[packInfoPrefix] = definition;
                }

                continue;
            }

            if (TryExtractPackPrefixFromEnvironmentDiscoverTool(name, out var discoverPrefix)
                && !discoverByPrefix.ContainsKey(discoverPrefix)) {
                discoverByPrefix[discoverPrefix] = definition;
            }
        }

        if (packInfoByPrefix.Count == 0 && discoverByPrefix.Count == 0) {
            return new PackPreflightCatalog(
                PackInfoByPrefix: packInfoByPrefix,
                EnvironmentDiscoverByPrefix: discoverByPrefix,
                PrefixesByLengthDescending: Array.Empty<string>());
        }

        var prefixes = packInfoByPrefix.Keys
            .Concat(discoverByPrefix.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(static prefix => prefix.Length)
            .ThenBy(static prefix => prefix, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new PackPreflightCatalog(packInfoByPrefix, discoverByPrefix, prefixes);
    }

    private HashSet<string> SnapshotRememberedPackPreflightTools(string normalizedThreadId) {
        lock (_toolRoutingContextLock) {
            if (!_packPreflightToolNamesByThreadId.TryGetValue(normalizedThreadId, out var names) || names is not { Length: > 0 }) {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            _packPreflightSeenUtcTicks[normalizedThreadId] = DateTime.UtcNow.Ticks;
            TrimWeightedRoutingContextsNoLock();
            return new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        }
    }

    private static bool TryResolvePackPrefixForOperationalTool(
        string toolName,
        IReadOnlyList<string> knownPrefixes,
        out string prefix) {
        prefix = string.Empty;
        var normalizedName = (toolName ?? string.Empty).Trim();
        if (normalizedName.Length == 0 || knownPrefixes.Count == 0) {
            return false;
        }

        for (var i = 0; i < knownPrefixes.Count; i++) {
            var candidate = knownPrefixes[i];
            if (candidate.Length == 0 || normalizedName.Length <= candidate.Length) {
                continue;
            }

            if (!normalizedName.StartsWith(candidate, StringComparison.OrdinalIgnoreCase)
                || normalizedName[candidate.Length] != '_') {
                continue;
            }

            prefix = candidate;
            return true;
        }

        return false;
    }

    private static bool ToolDefinitionHasRequiredArguments(ToolDefinition definition) {
        if (definition?.Parameters is null) {
            return false;
        }

        var required = definition.Parameters.GetArray("required");
        return required is { Count: > 0 };
    }

    private static bool TryExtractPackPrefixFromPackInfoTool(string toolName, out string prefix) {
        return TryExtractPackPrefixFromPreflightTool(toolName, PackInfoToolSuffix, out prefix);
    }

    private static bool TryExtractPackPrefixFromEnvironmentDiscoverTool(string toolName, out string prefix) {
        return TryExtractPackPrefixFromPreflightTool(toolName, EnvironmentDiscoverToolSuffix, out prefix);
    }

    private static bool TryExtractPackPrefixFromPreflightTool(string toolName, string suffix, out string prefix) {
        prefix = string.Empty;
        var normalized = (toolName ?? string.Empty).Trim();
        if (normalized.Length <= suffix.Length || !normalized.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        prefix = normalized[..^suffix.Length].Trim();
        return prefix.Length > 0;
    }

    private static bool IsHostPackPreflightToolName(string toolName) {
        var normalized = (toolName ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return false;
        }

        return normalized.EndsWith(PackInfoToolSuffix, StringComparison.OrdinalIgnoreCase)
               || normalized.EndsWith(EnvironmentDiscoverToolSuffix, StringComparison.OrdinalIgnoreCase);
    }

    private static ToolCall BuildHostPackPreflightCall(string toolName, string role) {
        var callId = HostPackPreflightCallIdPrefix + role + "_" + Guid.NewGuid().ToString("N");
        var arguments = new JsonObject(StringComparer.Ordinal);
        var serializedArguments = JsonLite.Serialize(arguments);
        var raw = new JsonObject(StringComparer.Ordinal)
            .Add("type", "tool_call")
            .Add("call_id", callId)
            .Add("name", toolName)
            .Add("arguments", serializedArguments);

        return new ToolCall(
            callId: callId,
            name: toolName,
            input: serializedArguments,
            arguments: arguments,
            raw: raw);
    }

    private static bool IsSuccessfulToolOutput(ToolOutputDto output) {
        return output.Ok != false
               && string.IsNullOrWhiteSpace(output.ErrorCode)
               && string.IsNullOrWhiteSpace(output.Error);
    }
}
