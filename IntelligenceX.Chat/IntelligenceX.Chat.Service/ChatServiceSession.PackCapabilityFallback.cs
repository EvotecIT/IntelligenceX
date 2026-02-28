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
    private readonly record struct CrossPackFallbackCandidate(
        string ToolName,
        ToolDefinition Definition);

    private void RebuildPackCapabilityFallbackContracts(IReadOnlyList<ToolDefinition> definitions) {
        _packCapabilityFallbackContractsByPackId.Clear();
        if (definitions.Count == 0 || _toolPackIdsByToolName.Count == 0) {
            return;
        }

        var availableToolNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var definitionsByToolName = new Dictionary<string, ToolDefinition>(StringComparer.OrdinalIgnoreCase);
        var packIdByToolName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var toolNamesByPackId = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < definitions.Count; i++) {
            var name = (definitions[i].Name ?? string.Empty).Trim();
            if (name.Length == 0 || !availableToolNames.Add(name)) {
                continue;
            }

            definitionsByToolName[name] = definitions[i];
            if (!_toolPackIdsByToolName.TryGetValue(name, out var packIdRaw)) {
                continue;
            }

            var normalizedPackId = NormalizePackId(packIdRaw);
            if (normalizedPackId.Length == 0) {
                continue;
            }

            packIdByToolName[name] = normalizedPackId;
            if (!toolNamesByPackId.TryGetValue(normalizedPackId, out var packTools)) {
                packTools = new List<string>();
                toolNamesByPackId[normalizedPackId] = packTools;
            }

            packTools.Add(name);
        }

        foreach (var entry in toolNamesByPackId) {
            var packId = entry.Key;
            var candidateTools = BuildPackCapabilityFallbackCandidates(packId, entry.Value, definitionsByToolName, packIdByToolName);
            AddPackCapabilityFallbackContract(
                packId: packId,
                candidateTools: candidateTools,
                availableToolNames: availableToolNames);
        }
    }

    private static IReadOnlyList<string> BuildPackCapabilityFallbackCandidates(
        string packId,
        IReadOnlyList<string> packToolNames,
        IReadOnlyDictionary<string, ToolDefinition> definitionsByToolName,
        IReadOnlyDictionary<string, string> packIdByToolName) {
        if (packToolNames.Count == 0) {
            return Array.Empty<string>();
        }

        var ranked = new List<(string Name, int Priority)>(packToolNames.Count);
        var packInfoName = string.Empty;
        for (var i = 0; i < packToolNames.Count; i++) {
            var toolName = (packToolNames[i] ?? string.Empty).Trim();
            if (toolName.Length == 0) {
                continue;
            }

            if (!definitionsByToolName.TryGetValue(toolName, out var definition)) {
                continue;
            }

            if (!packIdByToolName.TryGetValue(toolName, out var candidatePackId)
                || !PackIdMatches(candidatePackId, packId)) {
                continue;
            }

            if (IsHintlessSafeFallbackCandidate(toolName)) {
                packInfoName = toolName;
            }

            var priority = GetPackCapabilityFallbackToolPriority(toolName, definition);
            ranked.Add((toolName, priority));
        }

        if (ranked.Count == 0) {
            return Array.Empty<string>();
        }

        ranked.Sort(static (left, right) => {
            var priorityCompare = right.Priority.CompareTo(left.Priority);
            if (priorityCompare != 0) {
                return priorityCompare;
            }

            return StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name);
        });

        var result = new List<string>(ranked.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < ranked.Count; i++) {
            if (seen.Add(ranked[i].Name)) {
                result.Add(ranked[i].Name);
            }
        }

        if (packInfoName.Length > 0 && seen.Add(packInfoName)) {
            result.Add(packInfoName);
        }

        return result.Count == 0 ? Array.Empty<string>() : result.ToArray();
    }

    private static int GetPackCapabilityFallbackToolPriority(string toolName, ToolDefinition definition) {
        var normalizedName = (toolName ?? string.Empty).Trim();
        if (normalizedName.Length == 0) {
            return int.MinValue;
        }

        var priority = 0;
        var normalizedLower = normalizedName.ToLowerInvariant();
        if (normalizedLower.EndsWith("_scope_discovery", StringComparison.Ordinal)
            || normalizedLower.EndsWith("_environment_discover", StringComparison.Ordinal)
            || normalizedLower.EndsWith("_forest_discover", StringComparison.Ordinal)) {
            priority += 500;
        } else if (normalizedLower.Contains("_discover", StringComparison.Ordinal)
                   || normalizedLower.EndsWith("_domain_controllers", StringComparison.Ordinal)) {
            priority += 420;
        } else if (normalizedLower.Contains("_query", StringComparison.Ordinal)) {
            priority += 360;
        } else if (normalizedLower.Contains("_stats", StringComparison.Ordinal)
                   || normalizedLower.Contains("_summary", StringComparison.Ordinal)) {
            priority += 320;
        } else if (normalizedLower.Contains("_list", StringComparison.Ordinal)
                   || normalizedLower.Contains("_catalog", StringComparison.Ordinal)
                   || normalizedLower.Contains("_status", StringComparison.Ordinal)) {
            priority += 280;
        } else if (normalizedLower.Contains("_probe", StringComparison.Ordinal)
                   || normalizedLower.Contains("_diagnostics", StringComparison.Ordinal)) {
            priority += 260;
        } else if (normalizedLower.Contains("_run", StringComparison.Ordinal)) {
            priority += 200;
        }

        var routing = ToolSelectionMetadata.ResolveRouting(definition, toolType: null);
        var operation = (routing.Operation ?? string.Empty).Trim();
        if (string.Equals(operation, "query", StringComparison.OrdinalIgnoreCase)) {
            priority += 120;
        } else if (string.Equals(operation, "list", StringComparison.OrdinalIgnoreCase)) {
            priority += 100;
        } else if (string.Equals(operation, "search", StringComparison.OrdinalIgnoreCase)) {
            priority += 95;
        } else if (string.Equals(operation, "probe", StringComparison.OrdinalIgnoreCase)) {
            priority += 80;
        } else if (string.Equals(operation, "read", StringComparison.OrdinalIgnoreCase)) {
            priority += 70;
        } else if (string.Equals(operation, "guide", StringComparison.OrdinalIgnoreCase)) {
            priority += 10;
        }

        if (definition.WriteGovernance?.IsWriteCapable == true) {
            priority -= 1_000;
        }

        if (TryGetToolRequiredArgumentCount(definition, out var requiredCount)) {
            if (requiredCount == 0) {
                priority += 40;
            } else if (requiredCount == 1) {
                priority += 10;
            } else {
                priority -= requiredCount * 10;
            }
        }

        if (IsHintlessSafeFallbackCandidate(normalizedName)) {
            priority -= 900;
        }

        return priority;
    }

    private static bool TryGetToolRequiredArgumentCount(ToolDefinition definition, out int count) {
        count = 0;
        if (definition.Parameters is null) {
            return false;
        }

        var required = definition.Parameters.GetArray("required");
        if (required is null || required.Count == 0) {
            count = 0;
            return true;
        }

        for (var i = 0; i < required.Count; i++) {
            var name = (required[i]?.AsString() ?? string.Empty).Trim();
            if (name.Length > 0) {
                count++;
            }
        }

        return true;
    }

    private void AddPackCapabilityFallbackContract(
        string packId,
        IReadOnlyList<string> candidateTools,
        IReadOnlySet<string> availableToolNames) {
        var normalizedPackId = NormalizePackId(packId);
        if (normalizedPackId.Length == 0 || candidateTools.Count == 0) {
            return;
        }

        var normalizedPackAliases = GetNormalizedPackAliases(packId);
        if (normalizedPackAliases.Count == 0) {
            normalizedPackAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { normalizedPackId };
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
            if (!normalizedPackAliases.Contains(normalizedCandidatePackId)) {
                continue;
            }

            if (!ContainsToolName(fallback, toolName)) {
                fallback.Add(toolName);
            }
        }

        if (fallback.Count == 0) {
            return;
        }

        var contract = new PackCapabilityFallbackContract(
            PackId: normalizedPackId,
            FallbackTools: fallback.ToArray());
        foreach (var alias in normalizedPackAliases) {
            _packCapabilityFallbackContractsByPackId[alias] = contract;
        }
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
        var callArgumentsById = new Dictionary<string, JsonObject>(StringComparer.OrdinalIgnoreCase);
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
                if (TryParseArgumentsObject(call.ArgumentsJson, out var callArguments)) {
                    callArgumentsById[callId] = callArguments;
                }
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
            TryGetToolDefinitionByName(toolDefinitions, sourceTool, out var sourceToolDefinition);

            var hasPartialScopeHints = TryReadDiscoveryPartialScopeHints(output.Output, out var partialScopeHints, out var partialScopeReason);
            if (!hasPartialScopeHints) {
                if (!TryReadPackCapabilityErrorFallbackHints(
                        sourcePackId: sourcePackId,
                        sourceTool: sourceTool,
                        sourceToolDefinition: sourceToolDefinition,
                        output: output,
                        toolOutputs: toolOutputs,
                        userRequest: userRequest,
                        out partialScopeHints,
                        out partialScopeReason)) {
                    partialScopeHints = new JsonObject(StringComparer.Ordinal);
                    partialScopeReason = "source_call_arguments";
                }
            }

            if (callArgumentsById.TryGetValue(callId, out var sourceCallArguments)) {
                CopyFallbackHintsFromToolArguments(sourceCallArguments, partialScopeHints);
                if (partialScopeHints.Count > 0 && string.Equals(partialScopeReason, "source_call_arguments", StringComparison.Ordinal)) {
                    hasPartialScopeHints = true;
                }
            }

            var hasFallbackHints = hasPartialScopeHints || partialScopeHints.Count > 0;
            var hasSourceFailureSignal = HasToolFailureSignal(output);
            if (!hasFallbackHints && !hasSourceFailureSignal) {
                continue;
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

            if (TryBuildCrossAdPostureEvidenceFallbackToolCall(
                    toolDefinitions: toolDefinitions,
                    priorCalledTools: priorCalledTools,
                    sourcePackId: sourcePackId,
                    partialScopeHints: partialScopeHints,
                    partialScopeReason: partialScopeReason,
                    hasSourceFailureSignal: hasSourceFailureSignal,
                    mutatingToolHintsByName: mutatingToolHintsByName,
                    out toolCall,
                    out reason)) {
                return true;
            }

            if (TryBuildCrossAdDiscoveryFallbackFromTestimoXToolCall(
                    toolDefinitions: toolDefinitions,
                    priorCalledTools: priorCalledTools,
                    sourcePackId: sourcePackId,
                    partialScopeHints: partialScopeHints,
                    partialScopeReason: partialScopeReason,
                    hasSourceFailureSignal: hasSourceFailureSignal,
                    mutatingToolHintsByName: mutatingToolHintsByName,
                    out toolCall,
                    out reason)) {
                return true;
            }

            if (TryBuildCrossAdHostSystemBaselineFallbackToolCall(
                    toolDefinitions: toolDefinitions,
                    priorCalledTools: priorCalledTools,
                    sourcePackId: sourcePackId,
                    sourceTool: sourceTool,
                    sourceToolDefinition: sourceToolDefinition,
                    partialScopeHints: partialScopeHints,
                    partialScopeReason: partialScopeReason,
                    userRequest: userRequest,
                    hasSourceFailureSignal: hasSourceFailureSignal,
                    mutatingToolHintsByName: mutatingToolHintsByName,
                    out toolCall,
                    out reason)) {
                return true;
            }

            if (TryBuildCrossPublicPostureEvidenceFallbackToTestimoX(
                    toolDefinitions: toolDefinitions,
                    priorCalledTools: priorCalledTools,
                    sourcePackId: sourcePackId,
                    partialScopeHints: partialScopeHints,
                    partialScopeReason: partialScopeReason,
                    hasSourceFailureSignal: hasSourceFailureSignal,
                    mutatingToolHintsByName: mutatingToolHintsByName,
                    out toolCall,
                    out reason)) {
                return true;
            }

            if (TryBuildCrossPublicPostureEvidenceFallbackToDomainDetective(
                    toolDefinitions: toolDefinitions,
                    priorCalledTools: priorCalledTools,
                    sourcePackId: sourcePackId,
                    partialScopeHints: partialScopeHints,
                    partialScopeReason: partialScopeReason,
                    hasSourceFailureSignal: hasSourceFailureSignal,
                    mutatingToolHintsByName: mutatingToolHintsByName,
                    out toolCall,
                    out reason)) {
                return true;
            }

            if (TryBuildCrossPublicDnsEvidenceFallbackToolCall(
                    toolDefinitions: toolDefinitions,
                    priorCalledTools: priorCalledTools,
                    sourcePackId: sourcePackId,
                    sourceTool: sourceTool,
                    sourceToolDefinition: sourceToolDefinition,
                    partialScopeHints: partialScopeHints,
                    partialScopeReason: partialScopeReason,
                    hasSourceFailureSignal: hasSourceFailureSignal,
                    mutatingToolHintsByName: mutatingToolHintsByName,
                    out toolCall,
                    out reason)) {
                return true;
            }

            if (TryBuildCrossPublicDomainPostureFallbackToolCall(
                    toolDefinitions: toolDefinitions,
                    priorCalledTools: priorCalledTools,
                    sourcePackId: sourcePackId,
                    sourceTool: sourceTool,
                    sourceToolDefinition: sourceToolDefinition,
                    partialScopeHints: partialScopeHints,
                    partialScopeReason: partialScopeReason,
                    hasSourceFailureSignal: hasSourceFailureSignal,
                    mutatingToolHintsByName: mutatingToolHintsByName,
                    out toolCall,
                    out reason)) {
                return true;
            }

            if (TryBuildCrossHostSystemBaselineFallbackToolCall(
                    toolDefinitions: toolDefinitions,
                    priorCalledTools: priorCalledTools,
                    sourcePackId: sourcePackId,
                    sourceTool: sourceTool,
                    sourceToolDefinition: sourceToolDefinition,
                    partialScopeHints: partialScopeHints,
                    partialScopeReason: partialScopeReason,
                    userRequest: userRequest,
                    hasSourceFailureSignal: hasSourceFailureSignal,
                    mutatingToolHintsByName: mutatingToolHintsByName,
                    out toolCall,
                    out reason)) {
                return true;
            }

            if (TryBuildCrossHostEventlogEvidenceFallbackToolCall(
                    toolDefinitions: toolDefinitions,
                    priorCalledTools: priorCalledTools,
                    sourcePackId: sourcePackId,
                    sourceTool: sourceTool,
                    sourceToolDefinition: sourceToolDefinition,
                    partialScopeHints: partialScopeHints,
                    partialScopeReason: partialScopeReason,
                    userRequest: userRequest,
                    hasSourceFailureSignal: hasSourceFailureSignal,
                    mutatingToolHintsByName: mutatingToolHintsByName,
                    out toolCall,
                    out reason)) {
                return true;
            }

            if (TryBuildCrossSystemAdDiscoveryFallbackToolCall(
                    toolDefinitions: toolDefinitions,
                    priorCalledTools: priorCalledTools,
                    sourcePackId: sourcePackId,
                    sourceTool: sourceTool,
                    sourceToolDefinition: sourceToolDefinition,
                    partialScopeHints: partialScopeHints,
                    partialScopeReason: partialScopeReason,
                    userRequest: userRequest,
                    hasSourceFailureSignal: hasSourceFailureSignal,
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

                if (!hasFallbackHints && !IsHintlessSafeFallbackCandidate(candidateTool)) {
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
                AddSchemaAwareFallbackHintArguments(toolDefinition, fallbackArguments, partialScopeHints);
                AddSchemaAwareFallbackDefaultArguments(sourcePackId, toolDefinition, fallbackArguments, partialScopeHints);
                var normalizedArguments = CoerceStructuredNextActionArgumentsForTool(fallbackArguments, toolDefinition);
                if (!HasRequiredToolArguments(toolDefinition, normalizedArguments)
                    || ShouldSkipFallbackCandidate(candidateTool, normalizedArguments)) {
                    continue;
                }
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
                var reasonPrefix = hasFallbackHints
                    ? "pack_contract_partial_scope_autofallback:"
                    : "pack_contract_failure_autofallback:";
                var reasonDetail = hasFallbackHints ? partialScopeReason : "source_tool_failed_without_scope_hints";
                reason = reasonPrefix
                         + sourcePackId
                         + ":"
                         + reasonDetail
                         + "->"
                         + candidateTool;
                reason = AppendPackFallbackTelemetryMarker(reason, sourcePackId, candidateTool);
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

        if (!TryGetPackCapabilityFallbackContract("active_directory", out var adContract)) {
            reason = "cross_dc_discovery_pack_unavailable";
            return false;
        }

        if (adContract.FallbackTools.Length == 0) {
            reason = "cross_dc_discovery_pack_unavailable";
            return false;
        }

        if (!TryResolveCrossPackCandidateToolsByRouting(
                toolDefinitions: toolDefinitions,
                priorCalledTools: priorCalledTools,
                targetPackId: "active_directory",
                preferredScope: "domain",
                preferredOperation: "discover",
                mutatingToolHintsByName: mutatingToolHintsByName,
                out var candidates)) {
            reason = "cross_dc_discovery_first_no_ad_candidate";
            return false;
        }

        for (var i = 0; i < candidates.Count; i++) {
            var candidateTool = candidates[i].ToolName;
            var toolDefinition = candidates[i].Definition;

            var fallbackArguments = BuildPackFallbackArguments(
                sourcePackId: NormalizePackId("active_directory"),
                candidateTool: candidateTool,
                partialScopeHints: partialScopeHints);
            AddSchemaAwareFallbackHintArguments(toolDefinition, fallbackArguments, partialScopeHints);
            AddSchemaAwareFallbackDefaultArguments("active_directory", toolDefinition, fallbackArguments, partialScopeHints);
            var normalizedArguments = CoerceStructuredNextActionArgumentsForTool(fallbackArguments, toolDefinition);
            if (!HasRequiredToolArguments(toolDefinition, normalizedArguments)
                || ShouldSkipFallbackCandidate(candidateTool, normalizedArguments)) {
                continue;
            }
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
            reason = AppendPackFallbackTelemetryMarker(reason, sourcePackId, candidateTool);
            return true;
        }

        reason = "cross_dc_discovery_first_no_ad_fallback";
        return false;
    }

    private bool TryBuildCrossAdPostureEvidenceFallbackToolCall(
        IReadOnlyList<ToolDefinition> toolDefinitions,
        IReadOnlySet<string> priorCalledTools,
        string sourcePackId,
        JsonObject partialScopeHints,
        string partialScopeReason,
        bool hasSourceFailureSignal,
        IReadOnlyDictionary<string, bool>? mutatingToolHintsByName,
        out ToolCall toolCall,
        out string reason) {
        toolCall = null!;
        reason = "cross_ad_posture_not_applicable";

        if (!PackIdMatches(sourcePackId, "active_directory")
            || !hasSourceFailureSignal) {
            reason = "cross_ad_posture_source_not_eligible";
            return false;
        }

        if (!TryResolveCrossPackCandidateToolsByRouting(
                toolDefinitions: toolDefinitions,
                priorCalledTools: priorCalledTools,
                targetPackId: "testimox",
                preferredScope: "domain",
                preferredOperation: "query",
                mutatingToolHintsByName: mutatingToolHintsByName,
                out var candidates)) {
            reason = "cross_ad_posture_candidate_unavailable";
            return false;
        }

        for (var i = 0; i < candidates.Count; i++) {
            var candidateTool = candidates[i].ToolName;
            var toolDefinition = candidates[i].Definition;

            var fallbackArguments = BuildPackFallbackArguments(
                sourcePackId: sourcePackId,
                candidateTool: candidateTool,
                partialScopeHints: partialScopeHints);
            AddSchemaAwareFallbackHintArguments(toolDefinition, fallbackArguments, partialScopeHints);
            AddSchemaAwareFallbackDefaultArguments(sourcePackId, toolDefinition, fallbackArguments, partialScopeHints);
            var normalizedArguments = CoerceStructuredNextActionArgumentsForTool(fallbackArguments, toolDefinition);
            if (!HasRequiredToolArguments(toolDefinition, normalizedArguments)
                || ShouldSkipFallbackCandidate(candidateTool, normalizedArguments)) {
                continue;
            }

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
            reason = "pack_contract_cross_ad_posture_evidence:"
                     + sourcePackId
                     + ":"
                     + partialScopeReason
                     + "->"
                     + candidateTool;
            reason = AppendPackFallbackTelemetryMarker(reason, sourcePackId, candidateTool);
            return true;
        }

        reason = "cross_ad_posture_candidate_missing_required_args";
        return false;
    }

    private bool TryBuildCrossAdDiscoveryFallbackFromTestimoXToolCall(
        IReadOnlyList<ToolDefinition> toolDefinitions,
        IReadOnlySet<string> priorCalledTools,
        string sourcePackId,
        JsonObject partialScopeHints,
        string partialScopeReason,
        bool hasSourceFailureSignal,
        IReadOnlyDictionary<string, bool>? mutatingToolHintsByName,
        out ToolCall toolCall,
        out string reason) {
        toolCall = null!;
        reason = "cross_testimox_ad_discovery_not_applicable";

        if (!PackIdMatches(sourcePackId, "testimox")
            || !hasSourceFailureSignal) {
            reason = "cross_testimox_ad_discovery_source_not_eligible";
            return false;
        }

        if (!TryResolveCrossPackCandidateToolsByRouting(
                toolDefinitions: toolDefinitions,
                priorCalledTools: priorCalledTools,
                targetPackId: "active_directory",
                preferredScope: "domain",
                preferredOperation: "discover",
                mutatingToolHintsByName: mutatingToolHintsByName,
                out var candidates)) {
            reason = "cross_testimox_ad_discovery_candidate_unavailable";
            return false;
        }

        for (var i = 0; i < candidates.Count; i++) {
            var candidateTool = candidates[i].ToolName;
            var toolDefinition = candidates[i].Definition;

            var fallbackArguments = BuildPackFallbackArguments(
                sourcePackId: NormalizePackId("active_directory"),
                candidateTool: candidateTool,
                partialScopeHints: partialScopeHints);
            AddSchemaAwareFallbackHintArguments(toolDefinition, fallbackArguments, partialScopeHints);
            AddSchemaAwareFallbackDefaultArguments("active_directory", toolDefinition, fallbackArguments, partialScopeHints);
            var normalizedArguments = CoerceStructuredNextActionArgumentsForTool(fallbackArguments, toolDefinition);
            if (!HasRequiredToolArguments(toolDefinition, normalizedArguments)
                || ShouldSkipFallbackCandidate(candidateTool, normalizedArguments)) {
                continue;
            }

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
            reason = "pack_contract_cross_testimox_ad_discovery:"
                     + sourcePackId
                     + ":"
                     + partialScopeReason
                     + "->"
                     + candidateTool;
            reason = AppendPackFallbackTelemetryMarker(reason, sourcePackId, candidateTool);
            return true;
        }

        reason = "cross_testimox_ad_discovery_candidate_missing_required_args";
        return false;
    }

    private bool TryBuildCrossAdHostSystemBaselineFallbackToolCall(
        IReadOnlyList<ToolDefinition> toolDefinitions,
        IReadOnlySet<string> priorCalledTools,
        string sourcePackId,
        string sourceTool,
        ToolDefinition? sourceToolDefinition,
        JsonObject partialScopeHints,
        string partialScopeReason,
        string? userRequest,
        bool hasSourceFailureSignal,
        IReadOnlyDictionary<string, bool>? mutatingToolHintsByName,
        out ToolCall toolCall,
        out string reason) {
        toolCall = null!;
        reason = "cross_ad_host_system_baseline_not_applicable";

        if (!PackIdMatches(sourcePackId, "active_directory")
            || !hasSourceFailureSignal) {
            reason = "cross_ad_host_system_baseline_source_not_eligible";
            return false;
        }

        if (!IsSourceToolEligibleForCrossAdHostSystemBaselineFallback(sourceTool, sourceToolDefinition)) {
            reason = "cross_ad_host_system_baseline_source_tool_not_supported";
            return false;
        }

        if (!TryResolveCrossPackCandidateToolsByRouting(
                toolDefinitions: toolDefinitions,
                priorCalledTools: priorCalledTools,
                targetPackId: "system",
                preferredScope: "host",
                preferredOperation: ToolRoutingTaxonomy.OperationRead,
                mutatingToolHintsByName: mutatingToolHintsByName,
                out var candidates)) {
            reason = "cross_ad_host_system_baseline_candidate_unavailable";
            return false;
        }

        var computerName = ResolveEventlogHostHint(partialScopeHints, userRequest);
        if (string.IsNullOrWhiteSpace(computerName)) {
            reason = "cross_ad_host_system_baseline_missing_host_hint";
            return false;
        }

        for (var i = 0; i < candidates.Count; i++) {
            var candidateTool = candidates[i].ToolName;
            var toolDefinition = candidates[i].Definition;
            var fallbackArguments = new JsonObject(StringComparer.Ordinal);
            if (!TryAddFallbackHintArgumentForCandidate(toolDefinition, fallbackArguments, computerName, "computer_name", "machine_name", "host", "target", "name")) {
                fallbackArguments.Add("computer_name", computerName);
            }
            AddSchemaAwareFallbackHintArguments(toolDefinition, fallbackArguments, partialScopeHints);
            AddSchemaAwareFallbackDefaultArguments("system", toolDefinition, fallbackArguments, partialScopeHints);

            var normalizedArguments = CoerceStructuredNextActionArgumentsForTool(fallbackArguments, toolDefinition);
            if (!HasRequiredToolArguments(toolDefinition, normalizedArguments)
                || ShouldSkipFallbackCandidate(candidateTool, normalizedArguments)) {
                continue;
            }

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
            reason = "pack_contract_cross_ad_host_system_baseline:"
                     + sourcePackId
                     + ":"
                     + partialScopeReason
                     + "->"
                     + candidateTool;
            reason = AppendPackFallbackTelemetryMarker(reason, sourcePackId, candidateTool);
            return true;
        }

        reason = "cross_ad_host_system_baseline_candidate_missing_required_args";
        return false;
    }

    private bool TryBuildCrossPublicPostureEvidenceFallbackToTestimoX(
        IReadOnlyList<ToolDefinition> toolDefinitions,
        IReadOnlySet<string> priorCalledTools,
        string sourcePackId,
        JsonObject partialScopeHints,
        string partialScopeReason,
        bool hasSourceFailureSignal,
        IReadOnlyDictionary<string, bool>? mutatingToolHintsByName,
        out ToolCall toolCall,
        out string reason) {
        toolCall = null!;
        reason = "cross_public_posture_testimox_not_applicable";

        if (!PackIdMatches(sourcePackId, "domaindetective")
            || !hasSourceFailureSignal) {
            reason = "cross_public_posture_testimox_source_not_eligible";
            return false;
        }

        if (!TryResolveCrossPackCandidateToolsByRouting(
                toolDefinitions: toolDefinitions,
                priorCalledTools: priorCalledTools,
                targetPackId: "testimox",
                preferredScope: "domain",
                preferredOperation: "query",
                mutatingToolHintsByName: mutatingToolHintsByName,
                out var candidates)) {
            reason = "cross_public_posture_testimox_candidate_unavailable";
            return false;
        }

        for (var i = 0; i < candidates.Count; i++) {
            var candidateTool = candidates[i].ToolName;
            var toolDefinition = candidates[i].Definition;

            var fallbackArguments = BuildPackFallbackArguments(
                sourcePackId: NormalizePackId("testimox"),
                candidateTool: candidateTool,
                partialScopeHints: partialScopeHints);
            AddSchemaAwareFallbackHintArguments(toolDefinition, fallbackArguments, partialScopeHints);
            AddSchemaAwareFallbackDefaultArguments("testimox", toolDefinition, fallbackArguments, partialScopeHints);
            var normalizedArguments = CoerceStructuredNextActionArgumentsForTool(fallbackArguments, toolDefinition);
            if (!HasRequiredToolArguments(toolDefinition, normalizedArguments)
                || ShouldSkipFallbackCandidate(candidateTool, normalizedArguments)) {
                continue;
            }

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
            reason = "pack_contract_cross_public_posture_testimox:"
                     + sourcePackId
                     + ":"
                     + partialScopeReason
                     + "->"
                     + candidateTool;
            reason = AppendPackFallbackTelemetryMarker(reason, sourcePackId, candidateTool);
            return true;
        }

        reason = "cross_public_posture_testimox_candidate_missing_required_args";
        return false;
    }

    private bool TryBuildCrossPublicPostureEvidenceFallbackToDomainDetective(
        IReadOnlyList<ToolDefinition> toolDefinitions,
        IReadOnlySet<string> priorCalledTools,
        string sourcePackId,
        JsonObject partialScopeHints,
        string partialScopeReason,
        bool hasSourceFailureSignal,
        IReadOnlyDictionary<string, bool>? mutatingToolHintsByName,
        out ToolCall toolCall,
        out string reason) {
        toolCall = null!;
        reason = "cross_public_posture_domaindetective_not_applicable";

        if (!PackIdMatches(sourcePackId, "testimox")
            || !hasSourceFailureSignal) {
            reason = "cross_public_posture_domaindetective_source_not_eligible";
            return false;
        }

        if (!TryResolveCrossPackCandidateToolsByRouting(
                toolDefinitions: toolDefinitions,
                priorCalledTools: priorCalledTools,
                targetPackId: "domaindetective",
                preferredScope: "domain",
                preferredOperation: "query",
                mutatingToolHintsByName: mutatingToolHintsByName,
                out var candidates)) {
            reason = "cross_public_posture_domaindetective_candidate_unavailable";
            return false;
        }

        for (var i = 0; i < candidates.Count; i++) {
            var candidateTool = candidates[i].ToolName;
            var toolDefinition = candidates[i].Definition;

            var fallbackArguments = BuildPackFallbackArguments(
                sourcePackId: NormalizePackId("domaindetective"),
                candidateTool: candidateTool,
                partialScopeHints: partialScopeHints);
            AddSchemaAwareFallbackHintArguments(toolDefinition, fallbackArguments, partialScopeHints);
            AddSchemaAwareFallbackDefaultArguments("domaindetective", toolDefinition, fallbackArguments, partialScopeHints);
            var normalizedArguments = CoerceStructuredNextActionArgumentsForTool(fallbackArguments, toolDefinition);
            if (!HasRequiredToolArguments(toolDefinition, normalizedArguments)
                || ShouldSkipFallbackCandidate(candidateTool, normalizedArguments)) {
                continue;
            }

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
            reason = "pack_contract_cross_public_posture_domaindetective:"
                     + sourcePackId
                     + ":"
                     + partialScopeReason
                     + "->"
                     + candidateTool;
            reason = AppendPackFallbackTelemetryMarker(reason, sourcePackId, candidateTool);
            return true;
        }

        reason = "cross_public_posture_domaindetective_candidate_missing_required_args";
        return false;
    }

    private bool TryBuildCrossPublicDnsEvidenceFallbackToolCall(
        IReadOnlyList<ToolDefinition> toolDefinitions,
        IReadOnlySet<string> priorCalledTools,
        string sourcePackId,
        string sourceTool,
        ToolDefinition? sourceToolDefinition,
        JsonObject partialScopeHints,
        string partialScopeReason,
        bool hasSourceFailureSignal,
        IReadOnlyDictionary<string, bool>? mutatingToolHintsByName,
        out ToolCall toolCall,
        out string reason) {
        toolCall = null!;
        reason = "cross_public_dns_not_applicable";

        if (!PackIdMatches(sourcePackId, "domaindetective")
            || !hasSourceFailureSignal) {
            reason = "cross_public_dns_source_not_eligible";
            return false;
        }

        if (!TryResolveCrossPublicRoutingPreference(sourceTool, sourceToolDefinition, out var preferredScope, out var preferredOperation)) {
            reason = "cross_public_dns_source_tool_not_supported";
            return false;
        }

        if (!TryResolveCrossPackCandidateToolsByRouting(
                toolDefinitions: toolDefinitions,
                priorCalledTools: priorCalledTools,
                targetPackId: "dnsclientx",
                preferredScope: preferredScope,
                preferredOperation: preferredOperation,
                mutatingToolHintsByName: mutatingToolHintsByName,
                out var candidates)) {
            reason = "cross_public_dns_candidate_unavailable";
            return false;
        }

        var name = ResolveDnsClientXNameHint(partialScopeHints);
        if (string.IsNullOrWhiteSpace(name)) {
            reason = "cross_public_dns_missing_name_hint";
            return false;
        }

        var preferTarget = string.Equals(preferredScope, "host", StringComparison.OrdinalIgnoreCase)
                           || string.Equals(preferredOperation, "probe", StringComparison.OrdinalIgnoreCase);
        for (var i = 0; i < candidates.Count; i++) {
            var candidateTool = candidates[i].ToolName;
            var toolDefinition = candidates[i].Definition;

            var fallbackArguments = new JsonObject(StringComparer.Ordinal);
            if (preferTarget) {
                if (!TryAddFallbackHintArgumentForCandidate(toolDefinition, fallbackArguments, name, "target", "host", "name", "domain")) {
                    fallbackArguments.Add("target", name);
                }
            } else {
                if (!TryAddFallbackHintArgumentForCandidate(toolDefinition, fallbackArguments, name, "name", "domain", "host", "target")) {
                    fallbackArguments.Add("name", name);
                }
            }

            var normalizedArguments = CoerceStructuredNextActionArgumentsForTool(fallbackArguments, toolDefinition);
            if (!HasRequiredToolArguments(toolDefinition, normalizedArguments)
                || ShouldSkipFallbackCandidate(candidateTool, normalizedArguments)) {
                continue;
            }

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
            reason = "pack_contract_cross_public_dns_evidence:"
                     + sourcePackId
                     + ":"
                     + partialScopeReason
                     + "->"
                     + candidateTool;
            reason = AppendPackFallbackTelemetryMarker(reason, sourcePackId, candidateTool);
            return true;
        }

        reason = "cross_public_dns_candidate_missing_required_args";
        return false;
    }

    private bool TryBuildCrossPublicDomainPostureFallbackToolCall(
        IReadOnlyList<ToolDefinition> toolDefinitions,
        IReadOnlySet<string> priorCalledTools,
        string sourcePackId,
        string sourceTool,
        ToolDefinition? sourceToolDefinition,
        JsonObject partialScopeHints,
        string partialScopeReason,
        bool hasSourceFailureSignal,
        IReadOnlyDictionary<string, bool>? mutatingToolHintsByName,
        out ToolCall toolCall,
        out string reason) {
        toolCall = null!;
        reason = "cross_public_domain_not_applicable";

        if (!PackIdMatches(sourcePackId, "dnsclientx")
            || !hasSourceFailureSignal) {
            reason = "cross_public_domain_source_not_eligible";
            return false;
        }

        if (!TryResolveCrossPublicRoutingPreference(sourceTool, sourceToolDefinition, out var preferredScope, out var preferredOperation)) {
            reason = "cross_public_domain_source_tool_not_supported";
            return false;
        }

        if (!TryResolveCrossPackCandidateToolsByRouting(
                toolDefinitions: toolDefinitions,
                priorCalledTools: priorCalledTools,
                targetPackId: "domaindetective",
                preferredScope: preferredScope,
                preferredOperation: preferredOperation,
                mutatingToolHintsByName: mutatingToolHintsByName,
                out var candidates)) {
            reason = "cross_public_domain_candidate_unavailable";
            return false;
        }

        var preferHostScope = string.Equals(preferredScope, "host", StringComparison.OrdinalIgnoreCase)
                              || string.Equals(preferredOperation, "probe", StringComparison.OrdinalIgnoreCase);
        string fallbackValue;
        string[] preferredKeys;
        if (preferHostScope) {
            var host = ResolveDnsClientXNameHint(partialScopeHints);
            if (string.IsNullOrWhiteSpace(host)) {
                reason = "cross_public_domain_missing_host_hint";
                return false;
            }
            fallbackValue = host;
            preferredKeys = new[] { "host", "target", "name", "domain" };
        } else {
            var domain = ResolveDomainDetectiveDomainHint(partialScopeHints);
            if (string.IsNullOrWhiteSpace(domain)) {
                reason = "cross_public_domain_missing_domain_hint";
                return false;
            }
            fallbackValue = domain;
            preferredKeys = new[] { "domain", "name", "host", "target" };
        }

        for (var i = 0; i < candidates.Count; i++) {
            var candidateTool = candidates[i].ToolName;
            var toolDefinition = candidates[i].Definition;
            var fallbackArguments = new JsonObject(StringComparer.Ordinal);
            if (!TryAddFallbackHintArgumentForCandidate(toolDefinition, fallbackArguments, fallbackValue, preferredKeys)) {
                fallbackArguments[preferredKeys[0]] = JsonValue.From(fallbackValue);
            }

            var normalizedArguments = CoerceStructuredNextActionArgumentsForTool(fallbackArguments, toolDefinition);
            if (!HasRequiredToolArguments(toolDefinition, normalizedArguments)
                || ShouldSkipFallbackCandidate(candidateTool, normalizedArguments)) {
                continue;
            }

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
            reason = "pack_contract_cross_public_domain_posture:"
                     + sourcePackId
                     + ":"
                     + partialScopeReason
                     + "->"
                     + candidateTool;
            reason = AppendPackFallbackTelemetryMarker(reason, sourcePackId, candidateTool);
            return true;
        }

        reason = "cross_public_domain_candidate_missing_required_args";
        return false;
    }

    private bool TryBuildCrossHostSystemBaselineFallbackToolCall(
        IReadOnlyList<ToolDefinition> toolDefinitions,
        IReadOnlySet<string> priorCalledTools,
        string sourcePackId,
        string sourceTool,
        ToolDefinition? sourceToolDefinition,
        JsonObject partialScopeHints,
        string partialScopeReason,
        string? userRequest,
        bool hasSourceFailureSignal,
        IReadOnlyDictionary<string, bool>? mutatingToolHintsByName,
        out ToolCall toolCall,
        out string reason) {
        toolCall = null!;
        reason = "cross_host_system_baseline_not_applicable";

        if (!PackIdMatches(sourcePackId, "eventlog")
            || !hasSourceFailureSignal) {
            reason = "cross_host_system_baseline_source_not_eligible";
            return false;
        }

        if (!IsSourceToolEligibleForCrossHostSystemBaselineFallback(sourceTool, sourceToolDefinition)) {
            reason = "cross_host_system_baseline_source_tool_not_supported";
            return false;
        }

        if (!TryResolveCrossPackCandidateToolsByRouting(
                toolDefinitions: toolDefinitions,
                priorCalledTools: priorCalledTools,
                targetPackId: "system",
                preferredScope: "host",
                preferredOperation: ToolRoutingTaxonomy.OperationRead,
                mutatingToolHintsByName: mutatingToolHintsByName,
                out var candidates)) {
            reason = "cross_host_system_baseline_candidate_unavailable";
            return false;
        }

        var computerName = ResolveEventlogHostHint(partialScopeHints, userRequest);
        if (string.IsNullOrWhiteSpace(computerName)) {
            reason = "cross_host_system_baseline_missing_host_hint";
            return false;
        }

        for (var i = 0; i < candidates.Count; i++) {
            var candidateTool = candidates[i].ToolName;
            var toolDefinition = candidates[i].Definition;
            var fallbackArguments = new JsonObject(StringComparer.Ordinal);
            if (!TryAddFallbackHintArgumentForCandidate(toolDefinition, fallbackArguments, computerName, "computer_name", "machine_name", "host", "target", "name")) {
                fallbackArguments.Add("computer_name", computerName);
            }

            var normalizedArguments = CoerceStructuredNextActionArgumentsForTool(fallbackArguments, toolDefinition);
            if (!HasRequiredToolArguments(toolDefinition, normalizedArguments)
                || ShouldSkipFallbackCandidate(candidateTool, normalizedArguments)) {
                continue;
            }

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
            reason = "pack_contract_cross_host_system_baseline:"
                     + sourcePackId
                     + ":"
                     + partialScopeReason
                     + "->"
                     + candidateTool;
            reason = AppendPackFallbackTelemetryMarker(reason, sourcePackId, candidateTool);
            return true;
        }

        reason = "cross_host_system_baseline_candidate_missing_required_args";
        return false;
    }

    private static bool IsSourceToolEligibleForCrossHostSystemBaselineFallback(
        string sourceTool,
        ToolDefinition? sourceToolDefinition) {
        if (!TryResolveSourceRoutingInfo(sourceTool, sourceToolDefinition, out var routingInfo)) {
            return false;
        }

        var scope = (routingInfo.Scope ?? string.Empty).Trim();
        if (!string.Equals(scope, "host", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(scope, "file", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        return IsCrossHostFallbackReadOnlyOperation(routingInfo.Operation);
    }

    private static bool IsSourceToolEligibleForCrossAdHostSystemBaselineFallback(
        string sourceTool,
        ToolDefinition? sourceToolDefinition) {
        if (!TryResolveSourceRoutingInfo(sourceTool, sourceToolDefinition, out var routingInfo)) {
            return false;
        }

        var scope = (routingInfo.Scope ?? string.Empty).Trim();
        if (!string.Equals(scope, "domain", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(scope, "host", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        return IsCrossHostFallbackReadOnlyOperation(routingInfo.Operation);
    }

    private static bool IsSourceToolEligibleForCrossHostEventlogEvidenceFallback(
        string sourceTool,
        ToolDefinition? sourceToolDefinition) {
        if (!TryResolveSourceRoutingInfo(sourceTool, sourceToolDefinition, out var routingInfo)) {
            return false;
        }

        var scope = (routingInfo.Scope ?? string.Empty).Trim();
        if (!string.Equals(scope, "host", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        return IsCrossHostFallbackReadOnlyOperation(routingInfo.Operation);
    }

    private static bool IsSourceToolEligibleForCrossSystemAdDiscoveryFallback(
        string sourceTool,
        ToolDefinition? sourceToolDefinition) {
        if (!TryResolveSourceRoutingInfo(sourceTool, sourceToolDefinition, out var routingInfo)) {
            return false;
        }

        var scope = (routingInfo.Scope ?? string.Empty).Trim();
        if (!string.Equals(scope, "host", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(scope, "file", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        return IsCrossHostFallbackReadOnlyOperation(routingInfo.Operation);
    }

    private bool TryResolveCrossPackCandidateToolByRouting(
        IReadOnlyList<ToolDefinition> toolDefinitions,
        IReadOnlySet<string> priorCalledTools,
        string targetPackId,
        string preferredScope,
        string preferredOperation,
        IReadOnlyDictionary<string, bool>? mutatingToolHintsByName,
        out string candidateTool,
        out ToolDefinition toolDefinition) {
        candidateTool = string.Empty;
        toolDefinition = null!;

        if (!TryResolveCrossPackCandidateToolsByRouting(
                toolDefinitions: toolDefinitions,
                priorCalledTools: priorCalledTools,
                targetPackId: targetPackId,
                preferredScope: preferredScope,
                preferredOperation: preferredOperation,
                mutatingToolHintsByName: mutatingToolHintsByName,
                out var candidates)
            || candidates.Count == 0) {
            return false;
        }

        candidateTool = candidates[0].ToolName;
        toolDefinition = candidates[0].Definition;
        return true;
    }

    private bool TryResolveCrossPackCandidateToolsByRouting(
        IReadOnlyList<ToolDefinition> toolDefinitions,
        IReadOnlySet<string> priorCalledTools,
        string targetPackId,
        string preferredScope,
        string preferredOperation,
        IReadOnlyDictionary<string, bool>? mutatingToolHintsByName,
        out IReadOnlyList<CrossPackFallbackCandidate> candidates) {
        candidates = Array.Empty<CrossPackFallbackCandidate>();

        var rankedCandidates = new List<(CrossPackFallbackCandidate Candidate, int Score)>(toolDefinitions.Count);
        for (var i = 0; i < toolDefinitions.Count; i++) {
            var candidateDefinition = toolDefinitions[i];
            var name = (candidateDefinition.Name ?? string.Empty).Trim();
            if (name.Length == 0
                || priorCalledTools.Contains(name)
                || IsHintlessSafeFallbackCandidate(name)) {
                continue;
            }

            if (!_toolPackIdsByToolName.TryGetValue(name, out var candidatePackIdRaw)
                || !PackIdMatches(candidatePackIdRaw, targetPackId)) {
                continue;
            }

            var mutability = ResolveStructuredNextActionMutability(
                declaredMutability: ActionMutability.Unknown,
                toolName: name,
                toolDefinition: candidateDefinition,
                mutatingToolHintsByName: mutatingToolHintsByName);
            if (mutability != ActionMutability.ReadOnly) {
                continue;
            }

            var routing = ToolSelectionMetadata.ResolveRouting(candidateDefinition, toolType: null);
            var operation = (routing.Operation ?? string.Empty).Trim();
            if (!IsCrossHostFallbackReadOnlyOperation(operation)) {
                continue;
            }

            var score = 0;
            var scope = (routing.Scope ?? string.Empty).Trim();
            if (string.Equals(scope, preferredScope, StringComparison.OrdinalIgnoreCase)) {
                score += 500;
            } else if (string.Equals(scope, "general", StringComparison.OrdinalIgnoreCase)) {
                score += 50;
            }

            if (string.Equals(operation, preferredOperation, StringComparison.OrdinalIgnoreCase)) {
                score += 300;
            } else if (string.Equals(preferredOperation, "probe", StringComparison.OrdinalIgnoreCase)
                       && string.Equals(operation, "query", StringComparison.OrdinalIgnoreCase)) {
                score += 120;
            } else if (string.Equals(preferredOperation, "query", StringComparison.OrdinalIgnoreCase)
                       && string.Equals(operation, "probe", StringComparison.OrdinalIgnoreCase)) {
                score += 120;
            } else if (string.Equals(preferredOperation, "discover", StringComparison.OrdinalIgnoreCase)
                       && (string.Equals(operation, "query", StringComparison.OrdinalIgnoreCase)
                           || string.Equals(operation, "list", StringComparison.OrdinalIgnoreCase)
                           || string.Equals(operation, "search", StringComparison.OrdinalIgnoreCase)
                           || string.Equals(operation, ToolRoutingTaxonomy.OperationRead, StringComparison.OrdinalIgnoreCase))) {
                score += 120;
            } else if ((string.Equals(preferredOperation, "query", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(preferredOperation, "list", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(preferredOperation, "search", StringComparison.OrdinalIgnoreCase))
                       && string.Equals(operation, "discover", StringComparison.OrdinalIgnoreCase)) {
                score += 120;
            }

            if (TryGetToolRequiredArgumentCount(candidateDefinition, out var requiredArgumentCount)) {
                if (requiredArgumentCount == 0) {
                    score += 50;
                } else if (requiredArgumentCount == 1) {
                    score += 25;
                }
            }

            if (ToolDefinitionHasInputProperty(candidateDefinition, "name")
                || ToolDefinitionHasInputProperty(candidateDefinition, "target")
                || ToolDefinitionHasInputProperty(candidateDefinition, "domain")
                || ToolDefinitionHasInputProperty(candidateDefinition, "host")
                || ToolDefinitionHasInputProperty(candidateDefinition, "computer_name")
                || ToolDefinitionHasInputProperty(candidateDefinition, "machine_name")
                || ToolDefinitionHasInputProperty(candidateDefinition, "log_name")) {
                score += 50;
            }

            rankedCandidates.Add((new CrossPackFallbackCandidate(name, candidateDefinition), score));
        }

        if (rankedCandidates.Count == 0) {
            return false;
        }

        rankedCandidates.Sort(static (left, right) => {
            var scoreCompare = right.Score.CompareTo(left.Score);
            if (scoreCompare != 0) {
                return scoreCompare;
            }

            return StringComparer.OrdinalIgnoreCase.Compare(left.Candidate.ToolName, right.Candidate.ToolName);
        });

        var orderedCandidates = new List<CrossPackFallbackCandidate>(rankedCandidates.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < rankedCandidates.Count; i++) {
            var candidate = rankedCandidates[i].Candidate;
            if (seen.Add(candidate.ToolName)) {
                orderedCandidates.Add(candidate);
            }
        }

        if (orderedCandidates.Count == 0) {
            return false;
        }

        candidates = orderedCandidates;
        return true;
    }

    private static bool TryResolveCrossPublicRoutingPreference(
        string sourceTool,
        ToolDefinition? sourceToolDefinition,
        out string preferredScope,
        out string preferredOperation) {
        preferredScope = string.Empty;
        preferredOperation = string.Empty;
        if (!TryResolveSourceRoutingInfo(sourceTool, sourceToolDefinition, out var routingInfo)
            || !IsCrossHostFallbackReadOnlyOperation(routingInfo.Operation)) {
            return false;
        }

        var scope = (routingInfo.Scope ?? string.Empty).Trim();
        if (string.Equals(scope, "host", StringComparison.OrdinalIgnoreCase)) {
            preferredScope = "host";
            preferredOperation = "probe";
            return true;
        }

        if (string.Equals(scope, "domain", StringComparison.OrdinalIgnoreCase)) {
            preferredScope = "domain";
            preferredOperation = "query";
            return true;
        }

        preferredOperation = string.Equals((routingInfo.Operation ?? string.Empty).Trim(), "probe", StringComparison.OrdinalIgnoreCase)
            ? "probe"
            : "query";
        preferredScope = string.Equals(preferredOperation, "probe", StringComparison.OrdinalIgnoreCase)
            ? "host"
            : "domain";
        return true;
    }

    private static bool TryAddFallbackHintArgumentForCandidate(
        ToolDefinition toolDefinition,
        JsonObject arguments,
        string value,
        params string[] preferredKeys) {
        if (toolDefinition.Parameters?.GetObject("properties") is not JsonObject properties) {
            return false;
        }

        for (var i = 0; i < preferredKeys.Length; i++) {
            var key = (preferredKeys[i] ?? string.Empty).Trim();
            if (key.Length == 0 || !TryGetToolSchemaProperty(properties, key, out _)) {
                continue;
            }

            arguments[key] = JsonValue.From(value);
            return true;
        }

        return false;
    }

    private static bool ToolDefinitionHasInputProperty(ToolDefinition toolDefinition, string propertyName) {
        var key = (propertyName ?? string.Empty).Trim();
        if (key.Length == 0) {
            return false;
        }

        if (toolDefinition.Parameters?.GetObject("properties") is not JsonObject properties) {
            return false;
        }

        return TryGetToolSchemaProperty(properties, key, out _);
    }

    private static bool TryResolveSourceRoutingInfo(
        string sourceTool,
        ToolDefinition? sourceToolDefinition,
        out ToolSelectionMetadata.ToolSelectionRoutingInfo routingInfo) {
        routingInfo = null!;

        if (sourceToolDefinition?.WriteGovernance?.IsWriteCapable == true) {
            return false;
        }

        var routingDefinition = sourceToolDefinition;
        if (routingDefinition is null) {
            var normalizedSourceTool = (sourceTool ?? string.Empty).Trim();
            if (normalizedSourceTool.Length == 0) {
                return false;
            }

            routingDefinition = new ToolDefinition(normalizedSourceTool);
        }

        routingInfo = ToolSelectionMetadata.ResolveRouting(routingDefinition, toolType: null);
        return true;
    }

    private static bool IsCrossHostFallbackReadOnlyOperation(string? operation) {
        var normalizedOperation = (operation ?? string.Empty).Trim();
        return string.Equals(normalizedOperation, ToolRoutingTaxonomy.OperationRead, StringComparison.OrdinalIgnoreCase)
               || string.Equals(normalizedOperation, "query", StringComparison.OrdinalIgnoreCase)
               || string.Equals(normalizedOperation, "list", StringComparison.OrdinalIgnoreCase)
               || string.Equals(normalizedOperation, "search", StringComparison.OrdinalIgnoreCase)
               || string.Equals(normalizedOperation, "summarize", StringComparison.OrdinalIgnoreCase)
               || string.Equals(normalizedOperation, "probe", StringComparison.OrdinalIgnoreCase)
               || string.Equals(normalizedOperation, "discover", StringComparison.OrdinalIgnoreCase)
               || string.Equals(normalizedOperation, "resolve", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ResolveEventlogHostHint(JsonObject hints, string? userRequest) {
        var preferred = ReadNonEmptyHint(hints, "machine_name")
                        ?? ReadNonEmptyHint(hints, "computer_name")
                        ?? ReadNonEmptyHint(hints, "host");
        if (string.IsNullOrWhiteSpace(preferred)) {
            preferred = TryExtractHostHintFromUserRequest(userRequest);
        }

        if (string.IsNullOrWhiteSpace(preferred)) {
            return null;
        }

        var normalized = preferred.Trim();
        return normalized.IndexOf(' ', StringComparison.Ordinal) >= 0 ? null : normalized;
    }

    private bool TryBuildCrossHostEventlogEvidenceFallbackToolCall(
        IReadOnlyList<ToolDefinition> toolDefinitions,
        IReadOnlySet<string> priorCalledTools,
        string sourcePackId,
        string sourceTool,
        ToolDefinition? sourceToolDefinition,
        JsonObject partialScopeHints,
        string partialScopeReason,
        string? userRequest,
        bool hasSourceFailureSignal,
        IReadOnlyDictionary<string, bool>? mutatingToolHintsByName,
        out ToolCall toolCall,
        out string reason) {
        toolCall = null!;
        reason = "cross_host_eventlog_evidence_not_applicable";

        if (!PackIdMatches(sourcePackId, "system")
            || !hasSourceFailureSignal) {
            reason = "cross_host_eventlog_evidence_source_not_eligible";
            return false;
        }

        if (!IsSourceToolEligibleForCrossHostEventlogEvidenceFallback(sourceTool, sourceToolDefinition)) {
            reason = "cross_host_eventlog_evidence_source_tool_not_supported";
            return false;
        }

        if (!TryResolveCrossPackCandidateToolsByRouting(
                toolDefinitions: toolDefinitions,
                priorCalledTools: priorCalledTools,
                targetPackId: "eventlog",
                preferredScope: "host",
                preferredOperation: "query",
                mutatingToolHintsByName: mutatingToolHintsByName,
                out var candidates)) {
            reason = "cross_host_eventlog_evidence_candidate_unavailable";
            return false;
        }

        var machineName = ResolveEventlogHostHint(partialScopeHints, userRequest);
        if (string.IsNullOrWhiteSpace(machineName)) {
            reason = "cross_host_eventlog_evidence_missing_host_hint";
            return false;
        }

        for (var i = 0; i < candidates.Count; i++) {
            var candidateTool = candidates[i].ToolName;
            var toolDefinition = candidates[i].Definition;
            var fallbackArguments = new JsonObject(StringComparer.Ordinal);
            if (!TryAddFallbackHintArgumentForCandidate(toolDefinition, fallbackArguments, machineName, "machine_name", "computer_name", "host", "target", "name")) {
                fallbackArguments.Add("machine_name", machineName);
            }
            if (ToolDefinitionHasInputProperty(toolDefinition, "log_name")) {
                fallbackArguments.Add("log_name", "System");
            }
            if (ToolDefinitionHasInputProperty(toolDefinition, "max_events")) {
                fallbackArguments.Add("max_events", 200);
            }

            var normalizedArguments = CoerceStructuredNextActionArgumentsForTool(fallbackArguments, toolDefinition);
            if (!HasRequiredToolArguments(toolDefinition, normalizedArguments)
                || ShouldSkipFallbackCandidate(candidateTool, normalizedArguments)) {
                continue;
            }

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
            reason = "pack_contract_cross_host_eventlog_evidence:"
                     + sourcePackId
                     + ":"
                     + partialScopeReason
                     + "->"
                     + candidateTool;
            reason = AppendPackFallbackTelemetryMarker(reason, sourcePackId, candidateTool);
            return true;
        }

        reason = "cross_host_eventlog_evidence_candidate_missing_required_args";
        return false;
    }

    private bool TryBuildCrossSystemAdDiscoveryFallbackToolCall(
        IReadOnlyList<ToolDefinition> toolDefinitions,
        IReadOnlySet<string> priorCalledTools,
        string sourcePackId,
        string sourceTool,
        ToolDefinition? sourceToolDefinition,
        JsonObject partialScopeHints,
        string partialScopeReason,
        string? userRequest,
        bool hasSourceFailureSignal,
        IReadOnlyDictionary<string, bool>? mutatingToolHintsByName,
        out ToolCall toolCall,
        out string reason) {
        toolCall = null!;
        reason = "cross_system_ad_discovery_not_applicable";

        if (!PackIdMatches(sourcePackId, "system")
            || !hasSourceFailureSignal) {
            reason = "cross_system_ad_discovery_source_not_eligible";
            return false;
        }

        if (!IsSourceToolEligibleForCrossSystemAdDiscoveryFallback(sourceTool, sourceToolDefinition)) {
            reason = "cross_system_ad_discovery_source_tool_not_supported";
            return false;
        }

        var domainName = ResolveDomainDetectiveDomainHint(partialScopeHints)
                         ?? TryExtractDomainHintFromUserRequest(userRequest);
        if (string.IsNullOrWhiteSpace(domainName)) {
            reason = "cross_system_ad_discovery_missing_domain_hint";
            return false;
        }

        if (!TryResolveCrossPackCandidateToolsByRouting(
                toolDefinitions: toolDefinitions,
                priorCalledTools: priorCalledTools,
                targetPackId: "active_directory",
                preferredScope: "domain",
                preferredOperation: "discover",
                mutatingToolHintsByName: mutatingToolHintsByName,
                out var candidates)) {
            reason = "cross_system_ad_discovery_candidate_unavailable";
            return false;
        }

        for (var i = 0; i < candidates.Count; i++) {
            var candidateTool = candidates[i].ToolName;
            var toolDefinition = candidates[i].Definition;
            var fallbackArguments = BuildPackFallbackArguments(
                sourcePackId: NormalizePackId("active_directory"),
                candidateTool: candidateTool,
                partialScopeHints: partialScopeHints);
            if (!TryAddFallbackHintArgumentForCandidate(toolDefinition, fallbackArguments, domainName, "domain_name", "dns_domain_name", "domain", "name")) {
                fallbackArguments["domain_name"] = JsonValue.From(domainName);
            }
            AddSchemaAwareFallbackHintArguments(toolDefinition, fallbackArguments, partialScopeHints);
            AddSchemaAwareFallbackDefaultArguments("active_directory", toolDefinition, fallbackArguments, partialScopeHints);

            var normalizedArguments = CoerceStructuredNextActionArgumentsForTool(fallbackArguments, toolDefinition);
            if (!HasRequiredToolArguments(toolDefinition, normalizedArguments)
                || ShouldSkipFallbackCandidate(candidateTool, normalizedArguments)) {
                continue;
            }

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
            reason = "pack_contract_cross_system_ad_discovery:"
                     + sourcePackId
                     + ":"
                     + partialScopeReason
                     + "->"
                     + candidateTool;
            reason = AppendPackFallbackTelemetryMarker(reason, sourcePackId, candidateTool);
            return true;
        }

        reason = "cross_system_ad_discovery_candidate_missing_required_args";
        return false;
    }

    private static string? ResolveDnsClientXNameHint(JsonObject hints) {
        var preferred = ReadNonEmptyHint(hints, "domain_name")
                        ?? ReadNonEmptyHint(hints, "dns_domain_name")
                        ?? ReadNonEmptyHint(hints, "domain")
                        ?? ReadNonEmptyHint(hints, "host")
                        ?? ReadNonEmptyHint(hints, "machine_name")
                        ?? ReadNonEmptyHint(hints, "computer_name")
                        ?? ReadNonEmptyHint(hints, "target")
                        ?? ReadNonEmptyHint(hints, "name");
        if (string.IsNullOrWhiteSpace(preferred)) {
            return null;
        }

        var normalized = preferred.Trim();
        return normalized.IndexOf(' ') >= 0 ? null : normalized;
    }

    private static string? ResolveDomainDetectiveDomainHint(JsonObject hints) {
        var preferred = ReadNonEmptyHint(hints, "domain")
                        ?? ReadNonEmptyHint(hints, "domain_name")
                        ?? ReadNonEmptyHint(hints, "dns_domain_name")
                        ?? ReadNonEmptyHint(hints, "name")
                        ?? ReadNonEmptyHint(hints, "target")
                        ?? ReadNonEmptyHint(hints, "host");
        if (string.IsNullOrWhiteSpace(preferred)) {
            return null;
        }

        var normalized = preferred.Trim().TrimEnd('.');
        return IsLikelyDomainName(normalized) ? normalized : null;
    }

    private static string? TryExtractDomainHintFromUserRequest(string? userRequest) {
        var text = NormalizeRoutingUserText((userRequest ?? string.Empty).Trim());
        if (text.Length == 0) {
            return null;
        }

        var bestCandidate = string.Empty;
        var bestScore = 0;
        var tokenStart = -1;
        for (var i = 0; i <= text.Length; i++) {
            var ch = i < text.Length ? text[i] : '\0';
            var tokenChar = i < text.Length
                            && (char.IsLetterOrDigit(ch) || ch == '.' || ch == '-' || ch == '_');
            if (tokenChar) {
                if (tokenStart < 0) {
                    tokenStart = i;
                }
                continue;
            }

            if (tokenStart < 0) {
                continue;
            }

            var candidate = text.Substring(tokenStart, i - tokenStart).Trim().TrimEnd('.');
            tokenStart = -1;
            var score = ScoreDomainHintCandidate(candidate);
            if (score <= bestScore) {
                continue;
            }

            bestScore = score;
            bestCandidate = candidate;
        }

        return bestScore > 0 ? bestCandidate : null;
    }

    private static int ScoreDomainHintCandidate(string candidate) {
        var normalized = (candidate ?? string.Empty).Trim().TrimEnd('.');
        if (!IsLikelyDomainName(normalized) || LooksLikeHostFqdnCandidate(normalized)) {
            return 0;
        }

        var score = 1;
        var labels = normalized.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (labels.Length >= 2) {
            score += Math.Min(labels.Length, 3);
        }

        var rootLabel = labels.Length > 0 ? labels[0] : string.Empty;
        if (rootLabel.Length >= 4) {
            score += 1;
        }

        var tldLabel = labels.Length > 1 ? labels[^1] : string.Empty;
        if (tldLabel.Length is >= 2 and <= 6) {
            score += 1;
        }

        return score;
    }

    private static bool LooksLikeHostFqdnCandidate(string candidate) {
        var normalized = (candidate ?? string.Empty).Trim().TrimEnd('.');
        var dotIndex = normalized.IndexOf('.', StringComparison.Ordinal);
        if (dotIndex <= 0) {
            return false;
        }

        var leftLabel = normalized[..dotIndex];
        for (var i = 0; i < leftLabel.Length; i++) {
            if (char.IsDigit(leftLabel[i])) {
                return true;
            }
        }

        return false;
    }

    private static bool ShouldPreferAdDiscoveryBeforeEventlogFanOut(
        string sourcePackId,
        string partialScopeReason,
        string? userRequest) {
        if (!PackIdMatches(sourcePackId, "eventlog")) {
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

    private static bool LooksLikeConstrainedDiscoveryScopeReason(string reason) {
        var normalized = (reason ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return false;
        }

        if (normalized.StartsWith("evtx_access_denied", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        return normalized.Equals("single_domain_controller", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals("single_row", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals("limited_discovery", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals("truncated", StringComparison.OrdinalIgnoreCase);
    }

    private static JsonObject BuildPackFallbackArguments(
        string sourcePackId,
        string candidateTool,
        JsonObject partialScopeHints) {
        var args = new JsonObject(StringComparer.Ordinal);

        var domain = ReadNonEmptyHint(partialScopeHints, "domain")
                     ?? ReadNonEmptyHint(partialScopeHints, "domain_name")
                     ?? ReadNonEmptyHint(partialScopeHints, "dns_domain_name");
        var domainName = ReadNonEmptyHint(partialScopeHints, "domain_name")
                         ?? ReadNonEmptyHint(partialScopeHints, "dns_domain_name");
        var forestName = ReadNonEmptyHint(partialScopeHints, "forest_name")
                         ?? ReadNonEmptyHint(partialScopeHints, "forest_dns_name");
        var host = ReadNonEmptyHint(partialScopeHints, "host");
        var computerName = ReadNonEmptyHint(partialScopeHints, "computer_name")
                           ?? ReadNonEmptyHint(partialScopeHints, "machine_name");
        var logName = ReadNonEmptyHint(partialScopeHints, "log_name");
        var includeTrusts = ReadHintBoolean(partialScopeHints, "include_trusts");

        if (PackIdMatches(sourcePackId, "active_directory")) {
            if (string.Equals(candidateTool, "ad_scope_discovery", StringComparison.OrdinalIgnoreCase)
                || string.Equals(candidateTool, "ad_forest_discover", StringComparison.OrdinalIgnoreCase)) {
                args.Add("discovery_fallback", "current_forest");
                args.Add("include_forest_domains", true);
                args.Add("include_trusts", includeTrusts ?? true);
                args.Add("max_domain_controllers_total", 5000);
                args.Add("max_domain_controllers_per_domain", 500);
                if (!string.IsNullOrWhiteSpace(domainName)) {
                    args.Add("domain_name", domainName);
                }
                if (!string.IsNullOrWhiteSpace(forestName)) {
                    args.Add("forest_name", forestName);
                }
                return args;
            }

            if (string.Equals(candidateTool, "ad_domain_controllers", StringComparison.OrdinalIgnoreCase)) {
                args.Add("max_results", 500);
                if (!string.IsNullOrWhiteSpace(domainName)) {
                    args.Add("domain_name", domainName);
                }
                return args;
            }

            if (string.Equals(candidateTool, "ad_directory_discovery_diagnostics", StringComparison.OrdinalIgnoreCase)) {
                args.Add("max_issues", 2000);
                args.Add("include_dns_srv_comparison", true);
                args.Add("include_host_resolution", true);
                args.Add("include_directory_topology", true);
                if (!string.IsNullOrWhiteSpace(forestName)) {
                    args.Add("forest_name", forestName);
                }
                return args;
            }
        }

        if (PackIdMatches(sourcePackId, "eventlog")) {
            if (!string.IsNullOrWhiteSpace(logName)) {
                args.Add("log_name", logName);
            }
            if (!string.IsNullOrWhiteSpace(computerName)) {
                args.Add("machine_name", computerName);
            }

            if (string.Equals(candidateTool, "eventlog_live_query", StringComparison.OrdinalIgnoreCase)) {
                args.Add("max_events", 200);
                return args;
            }

            if (string.Equals(candidateTool, "eventlog_live_stats", StringComparison.OrdinalIgnoreCase)) {
                args.Add("max_events_scanned", 1200);
                return args;
            }

            if (string.Equals(candidateTool, "eventlog_top_events", StringComparison.OrdinalIgnoreCase)) {
                args.Add("max_events", 20);
                return args;
            }
        }

        if (PackIdMatches(sourcePackId, "system")) {
            if (!string.IsNullOrWhiteSpace(computerName)) {
                args.Add("computer_name", computerName);
            }
            if (string.Equals(candidateTool, "system_updates_installed", StringComparison.OrdinalIgnoreCase)) {
                args.Add("include_pending_local", false);
            }
        }

        if (PackIdMatches(sourcePackId, "domaindetective")) {
            var hostOrDomain = host ?? computerName ?? domainName ?? domain;
            if (string.Equals(candidateTool, "domaindetective_domain_summary", StringComparison.OrdinalIgnoreCase)) {
                var preferredDomain = domainName ?? domain ?? hostOrDomain;
                if (!string.IsNullOrWhiteSpace(preferredDomain) && IsLikelyDomainName(preferredDomain)) {
                    args.Add("domain", preferredDomain);
                }
                return args;
            }

            if (string.Equals(candidateTool, "domaindetective_network_probe", StringComparison.OrdinalIgnoreCase)) {
                if (!string.IsNullOrWhiteSpace(hostOrDomain)) {
                    args.Add("host", hostOrDomain);
                }
                return args;
            }
        }

        if (PackIdMatches(sourcePackId, "dnsclientx")) {
            var preferredName = ReadNonEmptyHint(partialScopeHints, "name")
                                ?? ReadNonEmptyHint(partialScopeHints, "target")
                                ?? domainName
                                ?? domain
                                ?? host
                                ?? computerName;
            if (string.Equals(candidateTool, "dnsclientx_query", StringComparison.OrdinalIgnoreCase)) {
                if (!string.IsNullOrWhiteSpace(preferredName)) {
                    args.Add("name", preferredName);
                }
                return args;
            }

            if (string.Equals(candidateTool, "dnsclientx_ping", StringComparison.OrdinalIgnoreCase)) {
                if (!string.IsNullOrWhiteSpace(preferredName)) {
                    args.Add("target", preferredName);
                }
                return args;
            }
        }

        if (PackIdMatches(sourcePackId, "testimox")) {
            if (string.Equals(candidateTool, "testimox_rules_list", StringComparison.OrdinalIgnoreCase)) {
                CopyHintIfPresent(partialScopeHints, args, "search_text");
                CopyHintIfPresent(partialScopeHints, args, "rule_origin");
                CopyHintIfPresent(partialScopeHints, args, "categories");
                CopyHintIfPresent(partialScopeHints, args, "tags");
                CopyHintIfPresent(partialScopeHints, args, "source_types");
                return args;
            }

            if (string.Equals(candidateTool, "testimox_rules_run", StringComparison.OrdinalIgnoreCase)) {
                CopyHintIfPresent(partialScopeHints, args, "search_text");
                CopyHintIfPresent(partialScopeHints, args, "rule_origin");
                CopyHintIfPresent(partialScopeHints, args, "rule_names");
                CopyHintIfPresent(partialScopeHints, args, "rule_name_patterns");
                CopyHintIfPresent(partialScopeHints, args, "categories");
                CopyHintIfPresent(partialScopeHints, args, "tags");
                CopyHintIfPresent(partialScopeHints, args, "source_types");
                return args;
            }
        }

        return args;
    }

    private static void AddSchemaAwareFallbackHintArguments(
        ToolDefinition toolDefinition,
        JsonObject arguments,
        JsonObject partialScopeHints) {
        TryAddSchemaAwareStringHintArgument(toolDefinition, arguments, partialScopeHints, "domain_name", "domain_name", "dns_domain_name", "domain");
        TryAddSchemaAwareStringHintArgument(toolDefinition, arguments, partialScopeHints, "domain", "domain", "domain_name", "dns_domain_name");
        TryAddSchemaAwareStringHintArgument(toolDefinition, arguments, partialScopeHints, "dns_domain_name", "dns_domain_name", "domain_name", "domain");
        TryAddSchemaAwareStringHintArgument(toolDefinition, arguments, partialScopeHints, "forest_name", "forest_name", "forest_dns_name");
        TryAddSchemaAwareStringHintArgument(toolDefinition, arguments, partialScopeHints, "forest_dns_name", "forest_dns_name", "forest_name");
        TryAddSchemaAwareStringHintArgument(toolDefinition, arguments, partialScopeHints, "machine_name", "machine_name", "computer_name", "host");
        TryAddSchemaAwareStringHintArgument(toolDefinition, arguments, partialScopeHints, "computer_name", "computer_name", "machine_name", "host");
        TryAddSchemaAwareStringHintArgument(toolDefinition, arguments, partialScopeHints, "host", "host", "machine_name", "computer_name");
        TryAddSchemaAwareStringHintArgument(toolDefinition, arguments, partialScopeHints, "name", "name", "target", "domain_name", "domain", "host", "machine_name", "computer_name");
        TryAddSchemaAwareStringHintArgument(toolDefinition, arguments, partialScopeHints, "target", "target", "host", "name", "machine_name", "computer_name", "domain_name", "domain");
        TryAddSchemaAwareStringHintArgument(toolDefinition, arguments, partialScopeHints, "log_name", "log_name");
        TryAddSchemaAwareBooleanHintArgument(toolDefinition, arguments, partialScopeHints, "include_trusts", "include_trusts");
        TryAddSchemaAwareBooleanHintArgument(toolDefinition, arguments, partialScopeHints, "include_forest_domains", "include_forest_domains");
    }

    private static void AddSchemaAwareFallbackDefaultArguments(
        string sourcePackId,
        ToolDefinition toolDefinition,
        JsonObject arguments,
        JsonObject partialScopeHints) {
        if (PackIdMatches(sourcePackId, "active_directory")) {
            var includeTrusts = ReadHintBoolean(partialScopeHints, "include_trusts") ?? true;
            TryAddSchemaAwareStringDefaultArgument(toolDefinition, arguments, "discovery_fallback", "current_forest");
            TryAddSchemaAwareBooleanDefaultArgument(toolDefinition, arguments, "include_forest_domains", true);
            TryAddSchemaAwareBooleanDefaultArgument(toolDefinition, arguments, "include_trusts", includeTrusts);
            TryAddSchemaAwareIntegerDefaultArgument(toolDefinition, arguments, "max_domain_controllers_total", 5000);
            TryAddSchemaAwareIntegerDefaultArgument(toolDefinition, arguments, "max_domain_controllers_per_domain", 500);
            TryAddSchemaAwareIntegerDefaultArgument(toolDefinition, arguments, "max_results", 500);
            TryAddSchemaAwareIntegerDefaultArgument(toolDefinition, arguments, "max_issues", 2000);
            TryAddSchemaAwareBooleanDefaultArgument(toolDefinition, arguments, "include_dns_srv_comparison", true);
            TryAddSchemaAwareBooleanDefaultArgument(toolDefinition, arguments, "include_host_resolution", true);
            TryAddSchemaAwareBooleanDefaultArgument(toolDefinition, arguments, "include_directory_topology", true);
            return;
        }

        if (PackIdMatches(sourcePackId, "eventlog")) {
            TryAddSchemaAwareIntegerDefaultArgument(toolDefinition, arguments, "max_events", 200);
            TryAddSchemaAwareIntegerDefaultArgument(toolDefinition, arguments, "max_events_scanned", 1200);
            return;
        }

        if (PackIdMatches(sourcePackId, "system")) {
            TryAddSchemaAwareBooleanDefaultArgument(toolDefinition, arguments, "include_pending_local", false);
        }
    }

    private static void TryAddSchemaAwareStringHintArgument(
        ToolDefinition toolDefinition,
        JsonObject arguments,
        JsonObject partialScopeHints,
        string argumentName,
        params string[] hintKeys) {
        if (arguments.TryGetValue(argumentName, out _)
            || !ToolDefinitionHasInputProperty(toolDefinition, argumentName)) {
            return;
        }

        for (var i = 0; i < hintKeys.Length; i++) {
            var hintKey = (hintKeys[i] ?? string.Empty).Trim();
            if (hintKey.Length == 0) {
                continue;
            }

            var value = ReadNonEmptyHint(partialScopeHints, hintKey);
            if (string.IsNullOrWhiteSpace(value)) {
                continue;
            }

            arguments[argumentName] = JsonValue.From(value);
            return;
        }
    }

    private static void TryAddSchemaAwareBooleanHintArgument(
        ToolDefinition toolDefinition,
        JsonObject arguments,
        JsonObject partialScopeHints,
        string argumentName,
        string hintKey) {
        if (arguments.TryGetValue(argumentName, out _)
            || !ToolDefinitionHasInputProperty(toolDefinition, argumentName)) {
            return;
        }

        var value = ReadHintBoolean(partialScopeHints, hintKey);
        if (!value.HasValue) {
            return;
        }

        arguments[argumentName] = JsonValue.From(value.Value);
    }

    private static void TryAddSchemaAwareStringDefaultArgument(
        ToolDefinition toolDefinition,
        JsonObject arguments,
        string argumentName,
        string defaultValue) {
        if (arguments.TryGetValue(argumentName, out _)
            || !ToolDefinitionHasInputProperty(toolDefinition, argumentName)) {
            return;
        }

        arguments[argumentName] = JsonValue.From(defaultValue);
    }

    private static void TryAddSchemaAwareBooleanDefaultArgument(
        ToolDefinition toolDefinition,
        JsonObject arguments,
        string argumentName,
        bool defaultValue) {
        if (arguments.TryGetValue(argumentName, out _)
            || !ToolDefinitionHasInputProperty(toolDefinition, argumentName)) {
            return;
        }

        arguments[argumentName] = JsonValue.From(defaultValue);
    }

    private static void TryAddSchemaAwareIntegerDefaultArgument(
        ToolDefinition toolDefinition,
        JsonObject arguments,
        string argumentName,
        int defaultValue) {
        if (arguments.TryGetValue(argumentName, out _)
            || !ToolDefinitionHasInputProperty(toolDefinition, argumentName)) {
            return;
        }

        arguments[argumentName] = JsonValue.From(defaultValue);
    }

    private static bool TryReadPackCapabilityErrorFallbackHints(
        string sourcePackId,
        string sourceTool,
        ToolDefinition? sourceToolDefinition,
        ToolOutputDto output,
        IReadOnlyList<ToolOutputDto> toolOutputs,
        string? userRequest,
        out JsonObject hints,
        out string reason) {
        hints = new JsonObject(StringComparer.Ordinal);
        reason = "no_error_fallback_signal";

        if (!PackIdMatches(sourcePackId, "eventlog")) {
            reason = "source_pack_not_supported";
            return false;
        }

        if (!IsSourceToolEligibleForEvtxAccessDeniedFallback(sourceTool, sourceToolDefinition)) {
            reason = "source_tool_not_evtx";
            return false;
        }

        if (!LooksLikeEvtxAccessDeniedFallbackCandidate(output)) {
            reason = "evtx_access_denied_not_detected";
            return false;
        }

        TryCopyEventlogFallbackHintsFromOutput(output.Output, hints);
        var machineName = ReadNonEmptyHint(hints, "machine_name")
                          ?? ReadNonEmptyHint(hints, "computer_name");
        if (string.IsNullOrWhiteSpace(machineName)) {
            machineName = TryExtractHostHintFromUserRequest(userRequest);
        }
        if (!string.IsNullOrWhiteSpace(machineName)) {
            machineName = TryResolveHostHintFromPriorDiscoveryOutputs(machineName, toolOutputs) ?? machineName;
        }

        if (string.IsNullOrWhiteSpace(machineName)) {
            reason = "evtx_access_denied_missing_machine_hint";
            return false;
        }

        if (ReadNonEmptyHint(hints, "machine_name") is null) {
            hints.Add("machine_name", machineName);
        }
        if (ReadNonEmptyHint(hints, "log_name") is null) {
            hints.Add("log_name", "System");
        }

        reason = "evtx_access_denied_live_query_fallback";
        return true;
    }

    private static bool IsSourceToolEligibleForEvtxAccessDeniedFallback(
        string sourceTool,
        ToolDefinition? sourceToolDefinition) {
        if (!TryResolveSourceRoutingInfo(sourceTool, sourceToolDefinition, out var routingInfo)
            || !IsCrossHostFallbackReadOnlyOperation(routingInfo.Operation)) {
            return false;
        }

        var scope = (routingInfo.Scope ?? string.Empty).Trim();
        if (string.Equals(scope, "file", StringComparison.OrdinalIgnoreCase)) {
            return true;
        }

        var normalizedSourceTool = (sourceTool ?? string.Empty).Trim();
        return normalizedSourceTool.IndexOf("_evtx_", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static void CopyHintIfPresent(JsonObject source, JsonObject destination, string propertyName) {
        if (!source.TryGetValue(propertyName, out var node) || node is null) {
            return;
        }

        switch (node.Kind) {
            case IntelligenceX.Json.JsonValueKind.String: {
                    var value = (node.AsString() ?? string.Empty).Trim();
                    if (value.Length > 0) {
                        destination[propertyName] = JsonValue.From(value);
                    }
                    break;
                }
            case IntelligenceX.Json.JsonValueKind.Boolean:
                destination[propertyName] = JsonValue.From(node.AsBoolean());
                break;
            case IntelligenceX.Json.JsonValueKind.Array: {
                    var array = node.AsArray();
                    if (array is null || array.Count == 0) {
                        break;
                    }

                    var copied = new JsonArray();
                    for (var i = 0; i < array.Count; i++) {
                        var item = array[i];
                        if (item is null || item.Kind != IntelligenceX.Json.JsonValueKind.String) {
                            continue;
                        }

                        var text = (item.AsString() ?? string.Empty).Trim();
                        if (text.Length > 0) {
                            copied.Add(text);
                        }
                    }

                    if (copied.Count > 0) {
                        destination[propertyName] = JsonValue.From(copied);
                    }
                    break;
                }
        }
    }

    private static bool ShouldSkipFallbackCandidate(string candidateTool, JsonObject arguments) {
        if (!string.Equals(candidateTool, "testimox_rules_run", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        return !HasTestimoSelectionArguments(arguments);
    }

    private static bool IsHintlessSafeFallbackCandidate(string candidateTool) {
        var normalized = (candidateTool ?? string.Empty).Trim();
        return normalized.EndsWith("_pack_info", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasToolFailureSignal(ToolOutputDto output) {
        return output.Ok == false
               || !string.IsNullOrWhiteSpace(output.ErrorCode)
               || !string.IsNullOrWhiteSpace(output.Error);
    }

    private static bool HasTestimoSelectionArguments(JsonObject arguments) {
        return HasNonEmptyStringArgument(arguments, "search_text")
               || HasNonEmptyStringArrayArgument(arguments, "rule_names")
               || HasNonEmptyStringArrayArgument(arguments, "rule_name_patterns")
               || HasNonEmptyStringArrayArgument(arguments, "categories")
               || HasNonEmptyStringArrayArgument(arguments, "tags")
               || HasNonEmptyStringArrayArgument(arguments, "source_types")
               || HasNonEmptyStringArgument(arguments, "rule_origin");
    }

    private static bool HasNonEmptyStringArgument(JsonObject arguments, string name) {
        if (!arguments.TryGetValue(name, out var node) || node is null || node.Kind != IntelligenceX.Json.JsonValueKind.String) {
            return false;
        }

        return !string.IsNullOrWhiteSpace(node.AsString());
    }

    private static bool HasNonEmptyStringArrayArgument(JsonObject arguments, string name) {
        if (!arguments.TryGetValue(name, out var node) || node is null || node.Kind != IntelligenceX.Json.JsonValueKind.Array) {
            return false;
        }

        var array = node.AsArray();
        if (array is null || array.Count == 0) {
            return false;
        }

        for (var i = 0; i < array.Count; i++) {
            var value = array[i];
            if (value is null || value.Kind != IntelligenceX.Json.JsonValueKind.String) {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(value.AsString())) {
                return true;
            }
        }

        return false;
    }

    private static bool HasRequiredToolArguments(ToolDefinition toolDefinition, JsonObject arguments) {
        var required = toolDefinition.Parameters?.GetArray("required");
        if (required is null || required.Count == 0) {
            return true;
        }

        for (var i = 0; i < required.Count; i++) {
            var name = (required[i]?.AsString() ?? string.Empty).Trim();
            if (name.Length == 0) {
                continue;
            }

            if (!arguments.TryGetValue(name, out var value) || value is null || value.Kind == IntelligenceX.Json.JsonValueKind.Null) {
                return false;
            }

            if (value.Kind == IntelligenceX.Json.JsonValueKind.String && string.IsNullOrWhiteSpace(value.AsString())) {
                return false;
            }

            if (value.Kind == IntelligenceX.Json.JsonValueKind.Array && (value.AsArray()?.Count ?? 0) == 0) {
                return false;
            }
        }

        return true;
    }

    private static bool TryParseArgumentsObject(string? argumentsJson, out JsonObject arguments) {
        arguments = new JsonObject(StringComparer.Ordinal);
        var payload = (argumentsJson ?? string.Empty).Trim();
        if (payload.Length == 0 || payload[0] != '{') {
            return false;
        }

        try {
            var parsed = JsonLite.Parse(payload)?.AsObject();
            if (parsed is null) {
                return false;
            }

            arguments = parsed;
            return true;
        } catch {
            return false;
        }
    }

    private static void CopyFallbackHintsFromToolArguments(JsonObject sourceArguments, JsonObject destinationHints) {
        CopyHintIfPresent(sourceArguments, destinationHints, "domain");
        CopyHintIfPresent(sourceArguments, destinationHints, "domain_name");
        CopyHintIfPresent(sourceArguments, destinationHints, "dns_domain_name");
        CopyHintIfPresent(sourceArguments, destinationHints, "forest_name");
        CopyHintIfPresent(sourceArguments, destinationHints, "host");
        CopyHintIfPresent(sourceArguments, destinationHints, "machine_name");
        CopyHintIfPresent(sourceArguments, destinationHints, "computer_name");
        CopyHintIfPresent(sourceArguments, destinationHints, "target");
        CopyHintIfPresent(sourceArguments, destinationHints, "name");
        CopyHintIfPresent(sourceArguments, destinationHints, "domain_controller");
        CopyHintIfPresent(sourceArguments, destinationHints, "log_name");
        CopyHintIfPresent(sourceArguments, destinationHints, "include_trusts");

        CopyHintIfPresent(sourceArguments, destinationHints, "search_text");
        CopyHintIfPresent(sourceArguments, destinationHints, "rule_origin");
        CopyHintIfPresent(sourceArguments, destinationHints, "rule_names");
        CopyHintIfPresent(sourceArguments, destinationHints, "rule_name_patterns");
        CopyHintIfPresent(sourceArguments, destinationHints, "categories");
        CopyHintIfPresent(sourceArguments, destinationHints, "tags");
        CopyHintIfPresent(sourceArguments, destinationHints, "source_types");
    }

    private bool TryGetPackCapabilityFallbackContract(string packId, out PackCapabilityFallbackContract contract) {
        var aliases = GetNormalizedPackAliases(packId);
        foreach (var alias in aliases) {
            if (_packCapabilityFallbackContractsByPackId.TryGetValue(alias, out contract) && contract.FallbackTools.Length > 0) {
                return true;
            }
        }

        contract = default;
        return false;
    }

    private static bool PackIdMatches(string? actualPackId, string expectedPackId) {
        var normalizedActual = NormalizePackId(actualPackId);
        if (normalizedActual.Length == 0) {
            return false;
        }

        var aliases = GetNormalizedPackAliases(expectedPackId);
        return aliases.Contains(normalizedActual);
    }

    private static string AppendPackFallbackTelemetryMarker(string reason, string sourcePackId, string candidateTool) {
        var normalizedReason = (reason ?? string.Empty).Trim();
        if (normalizedReason.Length == 0
            || normalizedReason.IndexOf("ix:pack-fallback:v1", StringComparison.OrdinalIgnoreCase) >= 0) {
            return normalizedReason;
        }

        var normalizedSourcePack = NormalizePackId(sourcePackId);
        if (normalizedSourcePack.Length == 0) {
            normalizedSourcePack = "unknown";
        }

        var normalizedCandidateTool = (candidateTool ?? string.Empty).Trim();
        if (normalizedCandidateTool.Length == 0) {
            normalizedCandidateTool = "unknown";
        }

        var family = ResolvePackFallbackTelemetryFamily(normalizedReason);
        return normalizedReason
               + " ix:pack-fallback:v1 source_pack="
               + normalizedSourcePack
               + " target_tool="
               + normalizedCandidateTool
               + " family="
               + family;
    }

    private static string ResolvePackFallbackTelemetryFamily(string reason) {
        var normalized = (reason ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return "general";
        }

        if (normalized.IndexOf("cross_public_", StringComparison.OrdinalIgnoreCase) >= 0) {
            return "public_domain";
        }

        if (normalized.IndexOf("cross_host_", StringComparison.OrdinalIgnoreCase) >= 0) {
            return "host";
        }

        if (normalized.IndexOf("cross_system_ad_", StringComparison.OrdinalIgnoreCase) >= 0
            || normalized.IndexOf("cross_dc_discovery_first", StringComparison.OrdinalIgnoreCase) >= 0
            || normalized.IndexOf("cross_ad_", StringComparison.OrdinalIgnoreCase) >= 0
            || normalized.IndexOf("_ad_discovery", StringComparison.OrdinalIgnoreCase) >= 0) {
            return "ad_domain";
        }

        if (normalized.IndexOf("pack_contract_partial_scope_autofallback", StringComparison.OrdinalIgnoreCase) >= 0
            || normalized.IndexOf("pack_contract_failure_autofallback", StringComparison.OrdinalIgnoreCase) >= 0) {
            return "pack_contract";
        }

        return "general";
    }

    private static HashSet<string> GetNormalizedPackAliases(string packId) {
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void AddAlias(string value) {
            var normalized = NormalizePackId(value);
            if (normalized.Length > 0) {
                aliases.Add(normalized);
            }
        }

        AddAlias(packId);
        switch (NormalizePackId(packId)) {
            case "activedirectory":
                AddAlias("ad");
                AddAlias("adplayground");
                break;
            case "ad":
                AddAlias("active_directory");
                AddAlias("adplayground");
                break;
            case "adplayground":
                AddAlias("active_directory");
                AddAlias("ad");
                break;
            case "system":
                AddAlias("computerx");
                break;
            case "computerx":
                AddAlias("system");
                break;
            case "eventlog":
                AddAlias("event_log");
                break;
            case "domaindetective":
                AddAlias("domain_detective");
                break;
            case "dnsclientx":
                AddAlias("dns_client_x");
                break;
            case "testimox":
                AddAlias("testimo_x");
                break;
        }

        return aliases;
    }

    private static bool IsLikelyDomainName(string value) {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length < 3 || normalized.Length > 255 || normalized.Contains(' ', StringComparison.Ordinal)) {
            return false;
        }

        if (!normalized.Contains('.', StringComparison.Ordinal)
            || normalized.StartsWith(".", StringComparison.Ordinal)
            || normalized.EndsWith(".", StringComparison.Ordinal)) {
            return false;
        }

        for (var i = 0; i < normalized.Length; i++) {
            if (char.IsLetter(normalized[i])) {
                return true;
            }
        }

        return false;
    }

    private static bool TryReadDiscoveryPartialScopeHints(string? outputJson, out JsonObject hints, out string reason) {
        hints = new JsonObject(StringComparer.Ordinal);
        reason = "no_partial_scope_signal";

        var payload = (outputJson ?? string.Empty).Trim();
        if (payload.Length == 0 || payload[0] != '{') {
            reason = "tool_output_not_object";
            return false;
        }

        try {
            using var doc = JsonDocument.Parse(payload, ActionSelectionJsonOptions);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) {
                reason = "tool_output_not_json_object";
                return false;
            }

            var root = doc.RootElement;
            var hasSignal = false;
            if (TryReadDiscoveryStatusObject(root, out var discoveryStatus)) {
                CopyHintIfPresent(discoveryStatus, hints, "domain_name");
                CopyHintIfPresent(discoveryStatus, hints, "dns_domain_name");
                CopyHintIfPresent(discoveryStatus, hints, "forest_name");
                CopyHintIfPresent(discoveryStatus, hints, "forest_dns_name");
                CopyHintIfPresent(discoveryStatus, hints, "computer_name");
                CopyHintIfPresent(discoveryStatus, hints, "machine_name");
                CopyHintIfPresent(discoveryStatus, hints, "log_name");
                CopyHintIfPresent(discoveryStatus, hints, "include_trusts");

                if (TryReadBooleanProperty(discoveryStatus, "limited_discovery", out var limitedDiscovery) && limitedDiscovery) {
                    hasSignal = true;
                    reason = "limited_discovery";
                } else if (TryReadBooleanProperty(discoveryStatus, "truncated", out var truncated) && truncated) {
                    hasSignal = true;
                    reason = "truncated";
                } else if (TryReadPositiveIntProperty(discoveryStatus, "discovered_domain_controllers", out var discoveredDomainControllers)
                           && discoveredDomainControllers <= 1) {
                    hasSignal = true;
                    reason = "single_domain_controller";
                } else if (TryReadPositiveIntProperty(discoveryStatus, "rows", out var rows) && rows <= 1) {
                    hasSignal = true;
                    reason = "single_row";
                }
            }

            if (!hasSignal && TryReadArrayLength(root, "domain_controllers", out var domainControllerCount) && domainControllerCount <= 1) {
                hasSignal = true;
                reason = "single_domain_controller";
            }

            if (!hasSignal && TryReadArrayLength(root, "domainControllers", out domainControllerCount) && domainControllerCount <= 1) {
                hasSignal = true;
                reason = "single_domain_controller";
            }

            return hasSignal;
        } catch (JsonException) {
            reason = "tool_output_parse_failed";
            return false;
        }
    }

    private static bool LooksLikeEvtxAccessDeniedFallbackCandidate(ToolOutputDto output) {
        var errorCode = (output.ErrorCode ?? string.Empty).Trim();
        if (errorCode.Length == 0) {
            errorCode = TryReadErrorCodeFromOutputPayload(output.Output);
        }

        if (errorCode.Length == 0) {
            return false;
        }

        return errorCode.Contains("access_denied", StringComparison.OrdinalIgnoreCase)
               || errorCode.Contains("not_authorized", StringComparison.OrdinalIgnoreCase)
               || errorCode.Contains("unauthorized", StringComparison.OrdinalIgnoreCase)
               || errorCode.Contains("forbidden", StringComparison.OrdinalIgnoreCase);
    }

    private static string TryReadErrorCodeFromOutputPayload(string? outputJson) {
        var payload = (outputJson ?? string.Empty).Trim();
        if (payload.Length == 0 || payload[0] != '{') {
            return string.Empty;
        }

        try {
            using var doc = JsonDocument.Parse(payload, ActionSelectionJsonOptions);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) {
                return string.Empty;
            }

            if (TryReadStringProperty(doc.RootElement, "error_code", out var errorCode)) {
                return errorCode;
            }

            if (doc.RootElement.TryGetProperty("error", out var errorNode)
                && errorNode.ValueKind == JsonValueKind.Object
                && TryReadStringProperty(errorNode, "code", out errorCode)) {
                return errorCode;
            }

            return string.Empty;
        } catch (JsonException) {
            return string.Empty;
        }
    }

    private static bool TryReadStringProperty(JsonElement source, string propertyName, out string value) {
        value = string.Empty;
        if (!source.TryGetProperty(propertyName, out var node) || node.ValueKind != JsonValueKind.String) {
            return false;
        }

        value = (node.GetString() ?? string.Empty).Trim();
        return value.Length > 0;
    }

    private static void TryCopyEventlogFallbackHintsFromOutput(string? outputJson, JsonObject hints) {
        var payload = (outputJson ?? string.Empty).Trim();
        if (payload.Length == 0 || payload[0] != '{') {
            return;
        }

        try {
            using var doc = JsonDocument.Parse(payload, ActionSelectionJsonOptions);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) {
                return;
            }

            var root = doc.RootElement;
            CopyHintIfPresent(root, hints, "machine_name");
            CopyHintIfPresent(root, hints, "computer_name");
            CopyHintIfPresent(root, hints, "log_name");
            if (TryReadDiscoveryStatusObject(root, out var discoveryStatus)) {
                CopyHintIfPresent(discoveryStatus, hints, "machine_name");
                CopyHintIfPresent(discoveryStatus, hints, "computer_name");
                CopyHintIfPresent(discoveryStatus, hints, "log_name");
            }
        } catch (JsonException) {
            // Best-effort extraction only.
        }
    }

}
