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
    private const int MaxRememberedPackPreflightToolNames = 16;

    private readonly record struct PackPreflightCatalog(
        Dictionary<string, ToolDefinition> PackInfoByPackId,
        Dictionary<string, ToolDefinition> EnvironmentDiscoverByPackId,
        Dictionary<string, ToolDefinition> DefinitionsByToolName,
        Dictionary<string, string> OperationalPackIdByToolName,
        Dictionary<string, string[]> RecoveryToolNamesByToolName,
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
        var recoveryHelperToolNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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

            if (catalog.RecoveryToolNamesByToolName.TryGetValue(callName, out var helperToolNames)
                && helperToolNames is { Length: > 0 }) {
                for (var helperIndex = 0; helperIndex < helperToolNames.Length; helperIndex++) {
                    var helperToolName = (helperToolNames[helperIndex] ?? string.Empty).Trim();
                    if (helperToolName.Length > 0) {
                        recoveryHelperToolNames.Add(helperToolName);
                    }
                }
            }
        }

        if (operationalPackIds.Count == 0 && recoveryHelperToolNames.Count == 0) {
            return Array.Empty<ToolCall>();
        }

        var rememberedPreflightTools = SnapshotRememberedPackPreflightTools(normalizedThreadId);
        var suppressedPreflightTools = SnapshotRecentHostBootstrapFailureToolNames(normalizedThreadId);
        var preflightCalls = new List<ToolCall>();
        var selectedPreflightToolNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var orderedPackIds = operationalPackIds
            .OrderBy(static packId => packId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        for (var i = 0; i < orderedPackIds.Length; i++) {
            var packId = orderedPackIds[i];
            if (TryResolvePreferredPackPreflightDefinition(
                    catalog,
                    packId,
                    ToolRoutingTaxonomy.RolePackInfo,
                    explicitRoundPreflightNames,
                    rememberedPreflightTools,
                    suppressedPreflightTools,
                    selectedPreflightToolNames,
                    out var packInfoDefinition)) {
                var packInfoName = (packInfoDefinition.Name ?? string.Empty).Trim();
                if (packInfoName.Length > 0) {
                    preflightCalls.Add(BuildHostPackPreflightCall(packInfoName, ToolRoutingTaxonomy.RolePackInfo));
                    selectedPreflightToolNames.Add(packInfoName);
                }
            }

            if (TryResolvePreferredPackPreflightDefinition(
                    catalog,
                    packId,
                    ToolRoutingTaxonomy.RoleEnvironmentDiscover,
                    explicitRoundPreflightNames,
                    rememberedPreflightTools,
                    suppressedPreflightTools,
                    selectedPreflightToolNames,
                    out var discoverDefinition)) {
                var discoverName = (discoverDefinition.Name ?? string.Empty).Trim();
                if (discoverName.Length > 0) {
                    preflightCalls.Add(BuildHostPackPreflightCall(discoverName, ToolRoutingTaxonomy.RoleEnvironmentDiscover));
                    selectedPreflightToolNames.Add(discoverName);
                }
            }
        }

        var excludedRecoveryHelperToolNames = new HashSet<string>(explicitRoundPreflightNames, StringComparer.OrdinalIgnoreCase);
        foreach (var rememberedToolName in rememberedPreflightTools) {
            excludedRecoveryHelperToolNames.Add(rememberedToolName);
        }
        foreach (var selectedToolName in selectedPreflightToolNames) {
            excludedRecoveryHelperToolNames.Add(selectedToolName);
        }

        var orderedRecoveryHelperToolNames = OrderBootstrapToolNamesByHealth(
            recoveryHelperToolNames,
            suppressedPreflightTools,
            excludedRecoveryHelperToolNames);
        for (var i = 0; i < orderedRecoveryHelperToolNames.Length; i++) {
            var helperToolName = orderedRecoveryHelperToolNames[i];
            if (!catalog.DefinitionsByToolName.TryGetValue(helperToolName, out var helperDefinition)
                || ToolDefinitionHasRequiredArguments(helperDefinition)
                || helperDefinition.WriteGovernance?.IsWriteCapable == true) {
                continue;
            }

            var helperRole = string.Empty;
            if (_toolOrchestrationCatalog.TryGetEntry(helperToolName, out var helperEntry)) {
                helperRole = helperEntry.Role;
            }

            preflightCalls.Add(BuildHostPackPreflightCall(
                helperToolName,
                helperRole.Length == 0 ? ToolRoutingTaxonomy.RoleDiagnostic : helperRole));
            selectedPreflightToolNames.Add(helperToolName);
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

        string[] snapshotToolNames;
        long snapshotSeenUtcTicks;
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

            foreach (var successfulName in successfulPreflightToolNames) {
                updated.Add(successfulName);
            }

            snapshotToolNames = updated
                .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
                .Take(MaxRememberedPackPreflightToolNames)
                .ToArray();
            snapshotSeenUtcTicks = DateTime.UtcNow.Ticks;
            _packPreflightToolNamesByThreadId[normalizedThreadId] = snapshotToolNames;
            _packPreflightSeenUtcTicks[normalizedThreadId] = snapshotSeenUtcTicks;
            TrimWeightedRoutingContextsNoLock();
        }

        PersistPackPreflightSnapshot(normalizedThreadId, snapshotToolNames, snapshotSeenUtcTicks);
        for (var i = 0; i < snapshotToolNames.Length; i++) {
            ClearHostBootstrapFailure(normalizedThreadId, snapshotToolNames[i]);
        }
    }

    private PackPreflightCatalog BuildPackPreflightCatalog(IReadOnlyList<ToolDefinition> definitions) {
        var packInfoByPackId = new Dictionary<string, ToolDefinition>(StringComparer.OrdinalIgnoreCase);
        var discoverByPackId = new Dictionary<string, ToolDefinition>(StringComparer.OrdinalIgnoreCase);
        var definitionsByToolName = new Dictionary<string, ToolDefinition>(StringComparer.OrdinalIgnoreCase);
        var operationalPackIdByToolName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var recoveryToolNamesByToolName = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
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

            if (!definitionsByToolName.ContainsKey(name)) {
                definitionsByToolName[name] = definition;
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

            if (orchestrationEntry.RecoveryToolNames.Count > 0 && !recoveryToolNamesByToolName.ContainsKey(name)) {
                recoveryToolNamesByToolName[name] = orchestrationEntry.RecoveryToolNames.ToArray();
            }
        }

        return new PackPreflightCatalog(
            PackInfoByPackId: packInfoByPackId,
            EnvironmentDiscoverByPackId: discoverByPackId,
            DefinitionsByToolName: definitionsByToolName,
            OperationalPackIdByToolName: operationalPackIdByToolName,
            RecoveryToolNamesByToolName: recoveryToolNamesByToolName,
            PreflightToolNames: preflightToolNames);
    }

    private HashSet<string> SnapshotRememberedPackPreflightTools(string normalizedThreadId) {
        string[]? cachedNames = null;
        long cachedSeenUtcTicks = 0;
        lock (_toolRoutingContextLock) {
            if (!_packPreflightToolNamesByThreadId.TryGetValue(normalizedThreadId, out var names) || names is not { Length: > 0 }) {
                names = null;
            } else {
                cachedSeenUtcTicks = DateTime.UtcNow.Ticks;
                _packPreflightSeenUtcTicks[normalizedThreadId] = cachedSeenUtcTicks;
                TrimWeightedRoutingContextsNoLock();
                cachedNames = names.ToArray();
            }
        }

        if (cachedNames is { Length: > 0 }) {
            PersistPackPreflightSnapshot(normalizedThreadId, cachedNames, cachedSeenUtcTicks);
            return new HashSet<string>(cachedNames, StringComparer.OrdinalIgnoreCase);
        }

        if (!TryLoadPackPreflightSnapshot(normalizedThreadId, out var persistedNames, out _)
            || persistedNames.Length == 0) {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        var seenUtcTicks = DateTime.UtcNow.Ticks;
        lock (_toolRoutingContextLock) {
            _packPreflightToolNamesByThreadId[normalizedThreadId] = persistedNames;
            _packPreflightSeenUtcTicks[normalizedThreadId] = seenUtcTicks;
            TrimWeightedRoutingContextsNoLock();
        }
        PersistPackPreflightSnapshot(normalizedThreadId, persistedNames, seenUtcTicks);
        return new HashSet<string>(persistedNames, StringComparer.OrdinalIgnoreCase);
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
        var callId = BuildHostGeneratedToolCallId(HostPackPreflightCallIdPrefix.TrimEnd('_'), role);
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
