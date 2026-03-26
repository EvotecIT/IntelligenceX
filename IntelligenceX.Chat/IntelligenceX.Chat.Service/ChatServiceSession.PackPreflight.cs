using System;
using System.Collections.Generic;
using System.Linq;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Tooling;
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
        Dictionary<string, string[]> ContractHelperToolNamesByToolName,
        HashSet<string> PreflightToolNames);

    private IReadOnlyList<ToolCall> BuildHostPackPreflightCalls(
        string threadId,
        IReadOnlyList<ToolDefinition> allToolDefinitions,
        IReadOnlyList<ToolCall> extractedCalls) {
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0 || allToolDefinitions.Count == 0 || extractedCalls.Count == 0) {
            return Array.Empty<ToolCall>();
        }

        allToolDefinitions = EnsureDeferredPackPreflightHelperDefinitionsLoaded(allToolDefinitions, extractedCalls);
        var catalog = BuildPackPreflightCatalog(allToolDefinitions);
        if (catalog.PackInfoByPackId.Count == 0) {
            return Array.Empty<ToolCall>();
        }

        var operationalPackIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var contractHelperToolNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var explicitRoundPreflightNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var extractedRoundToolNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < extractedCalls.Count; i++) {
            var callName = (extractedCalls[i].Name ?? string.Empty).Trim();
            if (callName.Length == 0) {
                continue;
            }

            extractedRoundToolNames.Add(callName);
            if (catalog.PreflightToolNames.Contains(callName)) {
                explicitRoundPreflightNames.Add(callName);
                continue;
            }

            if (catalog.OperationalPackIdByToolName.TryGetValue(callName, out var packId)) {
                operationalPackIds.Add(packId);
            }

            var helperToolNames = catalog.ContractHelperToolNamesByToolName.TryGetValue(callName, out var cachedHelperToolNames)
                ? cachedHelperToolNames
                : Array.Empty<string>();
            if (helperToolNames is { Length: > 0 }) {
                for (var helperIndex = 0; helperIndex < helperToolNames.Length; helperIndex++) {
                    var helperToolName = (helperToolNames[helperIndex] ?? string.Empty).Trim();
                    if (helperToolName.Length > 0) {
                        contractHelperToolNames.Add(helperToolName);
                    }
                }
            }
        }

        if (operationalPackIds.Count == 0 && contractHelperToolNames.Count == 0) {
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

        var excludedContractHelperToolNames = new HashSet<string>(extractedRoundToolNames, StringComparer.OrdinalIgnoreCase);
        foreach (var rememberedToolName in rememberedPreflightTools) {
            excludedContractHelperToolNames.Add(rememberedToolName);
        }
        foreach (var selectedToolName in selectedPreflightToolNames) {
            excludedContractHelperToolNames.Add(selectedToolName);
        }

        var orderedContractHelperToolNames = OrderBootstrapToolNamesByHealth(
            contractHelperToolNames,
            suppressedPreflightTools,
            excludedContractHelperToolNames);
        for (var i = 0; i < orderedContractHelperToolNames.Length; i++) {
            var helperToolName = orderedContractHelperToolNames[i];
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

        RememberSelectedPackPreflightTools(normalizedThreadId, selectedPreflightToolNames);
        return preflightCalls.Count == 0 ? Array.Empty<ToolCall>() : preflightCalls;
    }

    private IReadOnlyList<ToolDefinition> EnsureDeferredPackPreflightHelperDefinitionsLoaded(
        IReadOnlyList<ToolDefinition> allToolDefinitions,
        IReadOnlyList<ToolCall> extractedCalls) {
        if (allToolDefinitions.Count == 0 || extractedCalls.Count == 0) {
            return allToolDefinitions;
        }

        var definitionsByToolName = new Dictionary<string, ToolDefinition>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < allToolDefinitions.Count; i++) {
            var definition = allToolDefinitions[i];
            var toolName = (definition.Name ?? string.Empty).Trim();
            if (toolName.Length == 0 || definitionsByToolName.ContainsKey(toolName)) {
                continue;
            }

            definitionsByToolName[toolName] = definition;
        }

        var deferredHelperToolNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < extractedCalls.Count; i++) {
            var callName = (extractedCalls[i].Name ?? string.Empty).Trim();
            if (callName.Length == 0 || !definitionsByToolName.TryGetValue(callName, out var definition)) {
                continue;
            }

            foreach (var helperToolName in ResolveContractHelperToolNames(definition, _toolOrchestrationCatalog)) {
                var normalizedHelperToolName = (helperToolName ?? string.Empty).Trim();
                if (normalizedHelperToolName.Length > 0) {
                    deferredHelperToolNames.Add(normalizedHelperToolName);
                }
            }

            if (_toolOrchestrationCatalog.TryGetEntry(callName, out var orchestrationEntry)
                && orchestrationEntry.RecoveryToolNames.Count > 0) {
                for (var helperIndex = 0; helperIndex < orchestrationEntry.RecoveryToolNames.Count; helperIndex++) {
                    var helperToolName = (orchestrationEntry.RecoveryToolNames[helperIndex] ?? string.Empty).Trim();
                    if (helperToolName.Length > 0) {
                        deferredHelperToolNames.Add(helperToolName);
                    }
                }
            } else {
                var recoveryToolNames = NormalizePackPreflightRecoveryToolNames(definition.Recovery?.RecoveryToolNames);
                for (var helperIndex = 0; helperIndex < recoveryToolNames.Length; helperIndex++) {
                    var helperToolName = (recoveryToolNames[helperIndex] ?? string.Empty).Trim();
                    if (helperToolName.Length > 0) {
                        deferredHelperToolNames.Add(helperToolName);
                    }
                }
            }
        }

        var activatedAny = false;
        foreach (var helperToolName in deferredHelperToolNames) {
            if (definitionsByToolName.ContainsKey(helperToolName)
                || !TryResolveDeferredActivationPackId(helperToolName, out var activationPackId)) {
                continue;
            }

            try {
                activatedAny |= TryActivatePackOnDemand(activationPackId, out _);
            } catch {
                // Keep host preflight best-effort. Recovery/bootstrap diagnostics will surface later if needed.
            }
        }

        if (!activatedAny) {
            return allToolDefinitions;
        }

        return _registry.GetDefinitions()
            .Where(static definition => definition is not null)
            .ToArray();
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
                || !entry.IsEnvironmentDiscoverTool
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

        var rememberedPreflightTools = SnapshotRememberedPackPreflightTools(normalizedThreadId);
        var successfulCallIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < outputs.Count; i++) {
            var output = outputs[i];
            var outputCallId = (output.CallId ?? string.Empty).Trim();
            if (outputCallId.Length == 0) {
                continue;
            }

            if (IsSuccessfulToolOutput(output)) {
                successfulCallIds.Add(outputCallId);
            }
        }

        var executedBootstrapToolNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var successfulBootstrapToolNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < executedCalls.Count; i++) {
            var call = executedCalls[i];
            var callId = (call.CallId ?? string.Empty).Trim();
            var callName = (call.Name ?? string.Empty).Trim();
            if (callId.Length == 0
                || callName.Length == 0
                || !IsRememberablePackPreflightToolCall(callId, callName, rememberedPreflightTools)) {
                continue;
            }

            executedBootstrapToolNames.Add(callName);
            if (successfulCallIds.Contains(callId)) {
                successfulBootstrapToolNames.Add(callName);
            }
        }

        if (executedBootstrapToolNames.Count == 0) {
            return;
        }

        string[] snapshotToolNames;
        long snapshotSeenUtcTicks;
        lock (_toolRoutingContextLock) {
            var updated = new HashSet<string>(rememberedPreflightTools, StringComparer.OrdinalIgnoreCase);
            foreach (var executedName in executedBootstrapToolNames) {
                updated.Remove(executedName);
            }

            foreach (var successfulName in successfulBootstrapToolNames) {
                updated.Add(successfulName);
            }

            snapshotToolNames = updated
                .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
                .Take(MaxRememberedPackPreflightToolNames)
                .ToArray();
            snapshotSeenUtcTicks = DateTime.UtcNow.Ticks;
            if (snapshotToolNames.Length == 0) {
                _packPreflightToolNamesByThreadId.Remove(normalizedThreadId);
                _packPreflightSeenUtcTicks.Remove(normalizedThreadId);
            } else {
                _packPreflightToolNamesByThreadId[normalizedThreadId] = snapshotToolNames;
                _packPreflightSeenUtcTicks[normalizedThreadId] = snapshotSeenUtcTicks;
            }
            TrimWeightedRoutingContextsNoLock();
        }

        PersistPackPreflightSnapshot(normalizedThreadId, snapshotToolNames, snapshotSeenUtcTicks);
        foreach (var successfulToolName in successfulBootstrapToolNames) {
            ClearHostBootstrapFailure(normalizedThreadId, successfulToolName);
        }
    }

    private void RememberSelectedPackPreflightTools(string normalizedThreadId, IReadOnlyCollection<string> selectedToolNames) {
        if (normalizedThreadId.Length == 0 || selectedToolNames is not { Count: > 0 }) {
            return;
        }

        var rememberedPreflightTools = SnapshotRememberedPackPreflightTools(normalizedThreadId);
        foreach (var selectedToolName in selectedToolNames) {
            rememberedPreflightTools.Add(selectedToolName);
        }

        var snapshotToolNames = rememberedPreflightTools
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .Take(MaxRememberedPackPreflightToolNames)
            .ToArray();
        if (snapshotToolNames.Length == 0) {
            return;
        }

        var snapshotSeenUtcTicks = DateTime.UtcNow.Ticks;
        lock (_toolRoutingContextLock) {
            _packPreflightToolNamesByThreadId[normalizedThreadId] = snapshotToolNames;
            _packPreflightSeenUtcTicks[normalizedThreadId] = snapshotSeenUtcTicks;
            TrimWeightedRoutingContextsNoLock();
        }

        PersistPackPreflightSnapshot(normalizedThreadId, snapshotToolNames, snapshotSeenUtcTicks);
    }

    private bool IsRememberablePackPreflightToolCall(
        string callId,
        string toolName,
        IReadOnlySet<string> rememberedPreflightTools) {
        var normalizedCallId = (callId ?? string.Empty).Trim();
        var normalizedToolName = (toolName ?? string.Empty).Trim();
        if (normalizedToolName.Length == 0) {
            return false;
        }

        if (ResolveHostBootstrapFailureKind(normalizedCallId, normalizedToolName).Length > 0) {
            return true;
        }

        return rememberedPreflightTools.Contains(normalizedToolName);
    }

    private PackPreflightCatalog BuildPackPreflightCatalog(IReadOnlyList<ToolDefinition> definitions) {
        var packInfoByPackId = new Dictionary<string, ToolDefinition>(StringComparer.OrdinalIgnoreCase);
        var discoverByPackId = new Dictionary<string, ToolDefinition>(StringComparer.OrdinalIgnoreCase);
        var definitionsByToolName = new Dictionary<string, ToolDefinition>(StringComparer.OrdinalIgnoreCase);
        var operationalPackIdByToolName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var contractHelperToolNamesByToolName = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
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

            if (orchestrationEntry.IsPackInfoTool) {
                if (!packInfoByPackId.ContainsKey(orchestrationEntry.PackId)) {
                    packInfoByPackId[orchestrationEntry.PackId] = definition;
                }

                preflightToolNames.Add(name);
                continue;
            }

            if (orchestrationEntry.IsEnvironmentDiscoverTool) {
                if (!discoverByPackId.ContainsKey(orchestrationEntry.PackId)) {
                    discoverByPackId[orchestrationEntry.PackId] = definition;
                }

                preflightToolNames.Add(name);
                continue;
            }

            if (!operationalPackIdByToolName.ContainsKey(name)) {
                operationalPackIdByToolName[name] = orchestrationEntry.PackId;
            }

            if (!contractHelperToolNamesByToolName.ContainsKey(name)) {
                var contractHelperToolNames = BuildPackPreflightContractHelperToolNames(definition, orchestrationEntry);
                if (contractHelperToolNames.Length > 0) {
                    contractHelperToolNamesByToolName[name] = contractHelperToolNames;
                }
            }
        }

        return new PackPreflightCatalog(
            PackInfoByPackId: packInfoByPackId,
            EnvironmentDiscoverByPackId: discoverByPackId,
            DefinitionsByToolName: definitionsByToolName,
            OperationalPackIdByToolName: operationalPackIdByToolName,
            ContractHelperToolNamesByToolName: contractHelperToolNamesByToolName,
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

        return entry.IsPackInfoTool || entry.IsEnvironmentDiscoverTool;
    }

    private static bool MatchesPreflightClassifier(ToolOrchestrationCatalogEntry entry, string normalizedRole) {
        if (string.Equals(normalizedRole, ToolRoutingTaxonomy.RolePackInfo, StringComparison.OrdinalIgnoreCase)) {
            return entry.IsPackInfoTool;
        }

        if (string.Equals(normalizedRole, ToolRoutingTaxonomy.RoleEnvironmentDiscover, StringComparison.OrdinalIgnoreCase)) {
            return entry.IsEnvironmentDiscoverTool;
        }

        return string.Equals(entry.Role, normalizedRole, StringComparison.OrdinalIgnoreCase);
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

    private string[] BuildPackPreflightContractHelperToolNames(
        ToolDefinition definition,
        ToolOrchestrationCatalogEntry orchestrationEntry) {
        var helperToolNames = new List<string>(6);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var resolvedHelperToolNames = ResolveContractHelperToolNames(definition, _toolOrchestrationCatalog);
        for (var helperIndex = 0; helperIndex < resolvedHelperToolNames.Length; helperIndex++) {
            AddPackPreflightContractHelperToolName(helperToolNames, seen, resolvedHelperToolNames[helperIndex]);
        }

        var recoveryToolNames = orchestrationEntry.RecoveryToolNames.Count > 0
            ? orchestrationEntry.RecoveryToolNames.ToArray()
            : NormalizePackPreflightRecoveryToolNames(definition.Recovery?.RecoveryToolNames);
        for (var i = 0; i < recoveryToolNames.Length; i++) {
            AddPackPreflightContractHelperToolName(helperToolNames, seen, recoveryToolNames[i]);
        }

        return helperToolNames.Count == 0 ? Array.Empty<string>() : helperToolNames.ToArray();
    }

    private static void AddPackPreflightContractHelperToolName(
        ICollection<string> helperToolNames,
        ISet<string> seen,
        string? toolName) {
        var normalizedToolName = (toolName ?? string.Empty).Trim();
        if (normalizedToolName.Length == 0 || !seen.Add(normalizedToolName)) {
            return;
        }

        helperToolNames.Add(normalizedToolName);
    }

    private static string[] NormalizePackPreflightRecoveryToolNames(IReadOnlyList<string>? values) {
        if (values is not { Count: > 0 }) {
            return Array.Empty<string>();
        }

        var names = new List<string>(values.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < values.Count; i++) {
            var normalized = (values[i] ?? string.Empty).Trim();
            if (normalized.Length == 0 || !seen.Add(normalized)) {
                continue;
            }

            names.Add(normalized);
        }

        return names.ToArray();
    }
}
