using System;
using System.Collections.Generic;
using System.Linq;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Json;
using IntelligenceX.OpenAI.ToolCalling;
using IntelligenceX.Tools;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    private const string HostPackPreflightCallIdPrefix = "host_pack_preflight_";

    private readonly record struct PackPreflightCatalog(
        Dictionary<string, ToolDefinition> PackInfoByPackId,
        Dictionary<string, ToolDefinition> EnvironmentDiscoverByPackId,
        Dictionary<string, string> OperationalPackIdByToolName,
        HashSet<string> PreflightToolNames);

    private IReadOnlyList<ToolCall> BuildHostPackPreflightCalls(
        string threadId,
        IReadOnlyList<ToolDefinition> allToolDefinitions,
        IReadOnlyList<ToolCall> extractedCalls) {
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0 || allToolDefinitions.Count == 0 || extractedCalls.Count == 0) {
            return Array.Empty<ToolCall>();
        }

        var catalog = BuildPackPreflightCatalog(allToolDefinitions);
        if (catalog.PackInfoByPackId.Count == 0) {
            return Array.Empty<ToolCall>();
        }

        var operationalPackIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var explicitRoundPreflightNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < extractedCalls.Count; i++) {
            var callName = (extractedCalls[i].Name ?? string.Empty).Trim();
            if (callName.Length == 0) {
                continue;
            }

            if (catalog.PreflightToolNames.Contains(callName)) {
                explicitRoundPreflightNames.Add(callName);
                continue;
            }

            if (catalog.OperationalPackIdByToolName.TryGetValue(callName, out var packId)) {
                operationalPackIds.Add(packId);
            }
        }

        if (operationalPackIds.Count == 0) {
            return Array.Empty<ToolCall>();
        }

        var rememberedPreflightTools = SnapshotRememberedPackPreflightTools(normalizedThreadId);
        var preflightCalls = new List<ToolCall>();
        var orderedPackIds = operationalPackIds
            .OrderBy(static packId => packId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        for (var i = 0; i < orderedPackIds.Length; i++) {
            var packId = orderedPackIds[i];
            if (catalog.PackInfoByPackId.TryGetValue(packId, out var packInfoDefinition)) {
                var packInfoName = (packInfoDefinition.Name ?? string.Empty).Trim();
                if (packInfoName.Length > 0
                    && !explicitRoundPreflightNames.Contains(packInfoName)
                    && !rememberedPreflightTools.Contains(packInfoName)) {
                    preflightCalls.Add(BuildHostPackPreflightCall(packInfoName, ToolRoutingTaxonomy.RolePackInfo));
                }
            }

            if (!catalog.EnvironmentDiscoverByPackId.TryGetValue(packId, out var discoverDefinition)
                || ToolDefinitionHasRequiredArguments(discoverDefinition)) {
                continue;
            }

            var discoverName = (discoverDefinition.Name ?? string.Empty).Trim();
            if (discoverName.Length == 0
                || explicitRoundPreflightNames.Contains(discoverName)
                || rememberedPreflightTools.Contains(discoverName)) {
                continue;
            }

            preflightCalls.Add(BuildHostPackPreflightCall(discoverName, ToolRoutingTaxonomy.RoleEnvironmentDiscover));
        }

        return preflightCalls.Count == 0 ? Array.Empty<ToolCall>() : preflightCalls;
    }

    private bool TryBuildHostDomainIntentEnvironmentBootstrapCall(
        string threadId,
        string userRequest,
        IReadOnlyList<ToolDefinition> toolDefinitions,
        out ToolCall call,
        out string reason) {
        call = default!;
        reason = string.Empty;

        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0 || toolDefinitions.Count == 0) {
            return false;
        }

        if (!TryGetCurrentDomainIntentFamily(normalizedThreadId, out var family)
            && !TryResolveDomainIntentFamilyFromUserSignals(userRequest, toolDefinitions, out family)) {
            return false;
        }

        if (!TryNormalizeDomainIntentFamily(family, out var normalizedFamily)) {
            return false;
        }

        var candidates = new List<(ToolDefinition Definition, string PackId, string Family)>();
        for (var i = 0; i < toolDefinitions.Count; i++) {
            var definition = toolDefinitions[i];
            if (definition is null) {
                continue;
            }

            var toolName = (definition.Name ?? string.Empty).Trim();
            if (toolName.Length == 0) {
                continue;
            }

            if (!_toolOrchestrationCatalog.TryGetEntry(toolName, out var entry)
                || !string.Equals(entry.Role, ToolRoutingTaxonomy.RoleEnvironmentDiscover, StringComparison.OrdinalIgnoreCase)
                || entry.PackId.Length == 0
                || ToolDefinitionHasRequiredArguments(definition)) {
                continue;
            }

            var candidateFamily = string.Empty;
            if (TryNormalizeDomainIntentFamily(entry.DomainIntentFamily, out var catalogFamily)) {
                candidateFamily = catalogFamily;
            } else {
                candidateFamily = ResolveDomainIntentFamily(definition);
            }

            if (!string.Equals(candidateFamily, normalizedFamily, StringComparison.Ordinal)) {
                continue;
            }

            candidates.Add((definition, entry.PackId, candidateFamily));
        }

        if (candidates.Count == 0) {
            return false;
        }

        var selected = candidates
            .OrderBy(static candidate => candidate.PackId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static candidate => candidate.Definition.Name, StringComparer.OrdinalIgnoreCase)
            .First();
        var selectedToolName = (selected.Definition.Name ?? string.Empty).Trim();
        if (selectedToolName.Length == 0) {
            return false;
        }

        call = BuildHostPackPreflightCall(selectedToolName, ToolRoutingTaxonomy.RoleEnvironmentDiscover);
        reason = $"domain_intent_family_{normalizedFamily}_pack_{selected.PackId}";
        return true;
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

    private PackPreflightCatalog BuildPackPreflightCatalog(IReadOnlyList<ToolDefinition> definitions) {
        var packInfoByPackId = new Dictionary<string, ToolDefinition>(StringComparer.OrdinalIgnoreCase);
        var discoverByPackId = new Dictionary<string, ToolDefinition>(StringComparer.OrdinalIgnoreCase);
        var operationalPackIdByToolName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var preflightToolNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < definitions.Count; i++) {
            var definition = definitions[i];
            if (definition is null) {
                continue;
            }

            var name = (definition.Name ?? string.Empty).Trim();
            if (name.Length == 0) {
                continue;
            }

            if (!_toolOrchestrationCatalog.TryGetEntry(name, out var orchestrationEntry)
                || orchestrationEntry.PackId.Length == 0) {
                continue;
            }

            if (string.Equals(orchestrationEntry.Role, ToolRoutingTaxonomy.RolePackInfo, StringComparison.OrdinalIgnoreCase)) {
                if (!packInfoByPackId.ContainsKey(orchestrationEntry.PackId)) {
                    packInfoByPackId[orchestrationEntry.PackId] = definition;
                }

                preflightToolNames.Add(name);
                continue;
            }

            if (string.Equals(orchestrationEntry.Role, ToolRoutingTaxonomy.RoleEnvironmentDiscover, StringComparison.OrdinalIgnoreCase)) {
                if (!discoverByPackId.ContainsKey(orchestrationEntry.PackId)) {
                    discoverByPackId[orchestrationEntry.PackId] = definition;
                }

                preflightToolNames.Add(name);
                continue;
            }

            if (!operationalPackIdByToolName.ContainsKey(name)) {
                operationalPackIdByToolName[name] = orchestrationEntry.PackId;
            }
        }

        return new PackPreflightCatalog(
            PackInfoByPackId: packInfoByPackId,
            EnvironmentDiscoverByPackId: discoverByPackId,
            OperationalPackIdByToolName: operationalPackIdByToolName,
            PreflightToolNames: preflightToolNames);
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

    private static bool ToolDefinitionHasRequiredArguments(ToolDefinition definition) {
        if (definition?.Parameters is null) {
            return false;
        }

        var required = definition.Parameters.GetArray("required");
        return required is { Count: > 0 };
    }

    private bool IsHostPackPreflightToolName(string toolName) {
        var normalized = (toolName ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return false;
        }

        if (!_toolOrchestrationCatalog.TryGetEntry(normalized, out var entry)) {
            return false;
        }

        return string.Equals(entry.Role, ToolRoutingTaxonomy.RolePackInfo, StringComparison.OrdinalIgnoreCase)
               || string.Equals(entry.Role, ToolRoutingTaxonomy.RoleEnvironmentDiscover, StringComparison.OrdinalIgnoreCase);
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
