using System;
using System.Collections.Generic;
using System.Text.Json;
using JsonValueKind = System.Text.Json.JsonValueKind;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Json;
using IntelligenceX.OpenAI.ToolCalling;
using IntelligenceX.Tools;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    private readonly record struct PackCapabilityFallbackContract(
        string PackId,
        string[] FallbackTools);

    private void RebuildPackCapabilityFallbackContracts(IReadOnlyList<ToolDefinition> definitions) {
        _packCapabilityFallbackContractsByPackId.Clear();
        if (definitions.Count == 0 || _toolPackIdsByToolName.Count == 0) {
            return;
        }

        var availableToolNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < definitions.Count; i++) {
            var name = (definitions[i].Name ?? string.Empty).Trim();
            if (name.Length > 0) {
                availableToolNames.Add(name);
            }
        }

        AddPackCapabilityFallbackContract(
            packId: "active_directory",
            candidateTools: new[] {
                "ad_scope_discovery",
                "ad_forest_discover",
                "ad_domain_controllers",
                "ad_directory_discovery_diagnostics"
            },
            availableToolNames: availableToolNames);

        AddPackCapabilityFallbackContract(
            packId: "eventlog",
            candidateTools: new[] {
                "eventlog_live_query",
                "eventlog_live_stats",
                "eventlog_top_events",
                "eventlog_timeline_query"
            },
            availableToolNames: availableToolNames);

        AddPackCapabilityFallbackContract(
            packId: "system",
            candidateTools: new[] {
                "system_updates_installed",
                "system_patch_compliance",
                "system_security_options",
                "system_info"
            },
            availableToolNames: availableToolNames);

        AddPackCapabilityFallbackContract(
            packId: "hyperv",
            candidateTools: new[] {
                "hyperv_host_info",
                "hyperv_vm_list",
                "hyperv_switch_list"
            },
            availableToolNames: availableToolNames);
    }

    private void AddPackCapabilityFallbackContract(
        string packId,
        IReadOnlyList<string> candidateTools,
        IReadOnlySet<string> availableToolNames) {
        var normalizedPackId = NormalizePackId(packId);
        if (normalizedPackId.Length == 0 || candidateTools.Count == 0) {
            return;
        }

        var fallback = new List<string>(candidateTools.Count);
        for (var i = 0; i < candidateTools.Count; i++) {
            var toolName = (candidateTools[i] ?? string.Empty).Trim();
            if (toolName.Length == 0 || !availableToolNames.Contains(toolName)) {
                continue;
            }

            if (!_toolPackIdsByToolName.TryGetValue(toolName, out var candidatePackId)) {
                continue;
            }

            var normalizedCandidatePackId = NormalizePackId(candidatePackId);
            if (!string.Equals(normalizedCandidatePackId, normalizedPackId, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            if (!ContainsToolName(fallback, toolName)) {
                fallback.Add(toolName);
            }
        }

        if (fallback.Count == 0) {
            return;
        }

        _packCapabilityFallbackContractsByPackId[normalizedPackId] = new PackCapabilityFallbackContract(
            PackId: normalizedPackId,
            FallbackTools: fallback.ToArray());
    }

    private bool TryBuildPackCapabilityFallbackToolCall(
        IReadOnlyList<ToolDefinition> toolDefinitions,
        IReadOnlyList<ToolCallDto> toolCalls,
        IReadOnlyList<ToolOutputDto> toolOutputs,
        string? userRequest,
        IReadOnlyDictionary<string, bool>? mutatingToolHintsByName,
        out ToolCall toolCall,
        out string reason) {
        toolCall = null!;
        reason = "not_eligible";

        if (toolDefinitions.Count == 0 || toolCalls.Count == 0 || toolOutputs.Count == 0) {
            reason = "missing_tool_context";
            return false;
        }

        var callNameById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var priorCalledTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < toolCalls.Count; i++) {
            var call = toolCalls[i];
            var callId = (call.CallId ?? string.Empty).Trim();
            var name = (call.Name ?? string.Empty).Trim();
            if (name.Length == 0) {
                continue;
            }

            priorCalledTools.Add(name);
            if (callId.Length > 0) {
                callNameById[callId] = name;
            }
        }

        for (var outputIndex = toolOutputs.Count - 1; outputIndex >= 0; outputIndex--) {
            var output = toolOutputs[outputIndex];
            var callId = (output.CallId ?? string.Empty).Trim();
            if (callId.Length == 0 || !callNameById.TryGetValue(callId, out var sourceTool)) {
                continue;
            }

            if (!_toolPackIdsByToolName.TryGetValue(sourceTool, out var sourcePackIdRaw)) {
                continue;
            }

            var sourcePackId = NormalizePackId(sourcePackIdRaw);
            if (sourcePackId.Length == 0
                || !_packCapabilityFallbackContractsByPackId.TryGetValue(sourcePackId, out var packContract)
                || packContract.FallbackTools.Length == 0) {
                continue;
            }

            if (!TryReadDiscoveryPartialScopeHints(output.Output, out var partialScopeHints, out var partialScopeReason)) {
                if (!TryReadPackCapabilityErrorFallbackHints(
                        sourcePackId: sourcePackId,
                        sourceTool: sourceTool,
                        output: output,
                        toolOutputs: toolOutputs,
                        userRequest: userRequest,
                        out partialScopeHints,
                        out partialScopeReason)) {
                    continue;
                }
            }

            if (TryBuildCrossDomainControllerDiscoveryFirstFallbackToolCall(
                    toolDefinitions: toolDefinitions,
                    priorCalledTools: priorCalledTools,
                    sourcePackId: sourcePackId,
                    partialScopeHints: partialScopeHints,
                    partialScopeReason: partialScopeReason,
                    userRequest: userRequest,
                    mutatingToolHintsByName: mutatingToolHintsByName,
                    out toolCall,
                    out reason)) {
                return true;
            }

            for (var i = 0; i < packContract.FallbackTools.Length; i++) {
                var candidateTool = packContract.FallbackTools[i];
                if (string.Equals(candidateTool, sourceTool, StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }

                if (priorCalledTools.Contains(candidateTool)) {
                    continue;
                }

                if (!TryGetToolDefinitionByName(toolDefinitions, candidateTool, out var toolDefinition)) {
                    continue;
                }

                var mutability = ResolveStructuredNextActionMutability(
                    declaredMutability: ActionMutability.Unknown,
                    toolName: candidateTool,
                    toolDefinition: toolDefinition,
                    mutatingToolHintsByName: mutatingToolHintsByName);
                if (mutability != ActionMutability.ReadOnly) {
                    continue;
                }

                var fallbackArguments = BuildPackFallbackArguments(
                    sourcePackId: sourcePackId,
                    candidateTool: candidateTool,
                    partialScopeHints: partialScopeHints);
                var normalizedArguments = CoerceStructuredNextActionArgumentsForTool(fallbackArguments, toolDefinition);
                var serializedArguments = JsonLite.Serialize(normalizedArguments);
                var fallbackCallId = "host_pack_fallback_" + Guid.NewGuid().ToString("N");
                var raw = new JsonObject()
                    .Add("type", "tool_call")
                    .Add("call_id", fallbackCallId)
                    .Add("name", candidateTool)
                    .Add("arguments", serializedArguments);

                toolCall = new ToolCall(
                    callId: fallbackCallId,
                    name: candidateTool,
                    input: serializedArguments,
                    arguments: normalizedArguments,
                    raw: raw);
                reason = "pack_contract_partial_scope_autofallback:"
                         + sourcePackId
                         + ":"
                         + partialScopeReason
                         + "->"
                         + candidateTool;
                return true;
            }
        }

        reason = "pack_contract_no_applicable_fallback";
        return false;
    }

    private bool TryBuildCrossDomainControllerDiscoveryFirstFallbackToolCall(
        IReadOnlyList<ToolDefinition> toolDefinitions,
        IReadOnlySet<string> priorCalledTools,
        string sourcePackId,
        JsonObject partialScopeHints,
        string partialScopeReason,
        string? userRequest,
        IReadOnlyDictionary<string, bool>? mutatingToolHintsByName,
        out ToolCall toolCall,
        out string reason) {
        toolCall = null!;
        reason = "cross_dc_discovery_first_not_applicable";

        if (!ShouldPreferAdDiscoveryBeforeEventlogFanOut(
                sourcePackId: sourcePackId,
                partialScopeReason: partialScopeReason,
                userRequest: userRequest)) {
            reason = "cross_dc_discovery_first_not_required";
            return false;
        }

        var adPackId = NormalizePackId("active_directory");
        if (adPackId.Length == 0
            || !_packCapabilityFallbackContractsByPackId.TryGetValue(adPackId, out var adContract)
            || adContract.FallbackTools.Length == 0) {
            reason = "cross_dc_discovery_pack_unavailable";
            return false;
        }

        for (var i = 0; i < adContract.FallbackTools.Length; i++) {
            var candidateTool = adContract.FallbackTools[i];
            if (priorCalledTools.Contains(candidateTool)) {
                continue;
            }

            if (!TryGetToolDefinitionByName(toolDefinitions, candidateTool, out var toolDefinition)) {
                continue;
            }

            if (!_toolPackIdsByToolName.TryGetValue(candidateTool, out var candidatePackIdRaw)
                || !string.Equals(NormalizePackId(candidatePackIdRaw), adPackId, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            var mutability = ResolveStructuredNextActionMutability(
                declaredMutability: ActionMutability.Unknown,
                toolName: candidateTool,
                toolDefinition: toolDefinition,
                mutatingToolHintsByName: mutatingToolHintsByName);
            if (mutability != ActionMutability.ReadOnly) {
                continue;
            }

            var fallbackArguments = BuildPackFallbackArguments(
                sourcePackId: adPackId,
                candidateTool: candidateTool,
                partialScopeHints: partialScopeHints);
            var normalizedArguments = CoerceStructuredNextActionArgumentsForTool(fallbackArguments, toolDefinition);
            var serializedArguments = JsonLite.Serialize(normalizedArguments);
            var fallbackCallId = "host_pack_fallback_" + Guid.NewGuid().ToString("N");
            var raw = new JsonObject()
                .Add("type", "tool_call")
                .Add("call_id", fallbackCallId)
                .Add("name", candidateTool)
                .Add("arguments", serializedArguments);

            toolCall = new ToolCall(
                callId: fallbackCallId,
                name: candidateTool,
                input: serializedArguments,
                arguments: normalizedArguments,
                raw: raw);
            reason = "pack_contract_cross_dc_discovery_first:"
                     + sourcePackId
                     + ":"
                     + partialScopeReason
                     + "->"
                     + candidateTool;
            return true;
        }

        reason = "cross_dc_discovery_first_no_ad_fallback";
        return false;
    }

    private static bool ShouldPreferAdDiscoveryBeforeEventlogFanOut(
        string sourcePackId,
        string partialScopeReason,
        string? userRequest) {
        if (!string.Equals(sourcePackId, NormalizePackId("eventlog"), StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        if (!LooksLikeConstrainedDiscoveryScopeReason(partialScopeReason)) {
            return false;
        }

        // Keep explicit host-targeted requests on host-scoped Event Log flow.
        if (!string.IsNullOrWhiteSpace(TryExtractHostHintFromUserRequest(userRequest))) {
            return false;
        }

        return true;
    }

}
