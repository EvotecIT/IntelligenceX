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

        var domainName = ReadNonEmptyHint(partialScopeHints, "domain_name")
                         ?? ReadNonEmptyHint(partialScopeHints, "dns_domain_name");
        var forestName = ReadNonEmptyHint(partialScopeHints, "forest_name")
                         ?? ReadNonEmptyHint(partialScopeHints, "forest_dns_name");
        var computerName = ReadNonEmptyHint(partialScopeHints, "computer_name")
                           ?? ReadNonEmptyHint(partialScopeHints, "machine_name");
        var logName = ReadNonEmptyHint(partialScopeHints, "log_name");
        var includeTrusts = ReadHintBoolean(partialScopeHints, "include_trusts");

        if (string.Equals(sourcePackId, NormalizePackId("active_directory"), StringComparison.OrdinalIgnoreCase)) {
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

        if (string.Equals(sourcePackId, NormalizePackId("eventlog"), StringComparison.OrdinalIgnoreCase)) {
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

        if (string.Equals(sourcePackId, NormalizePackId("system"), StringComparison.OrdinalIgnoreCase)) {
            if (!string.IsNullOrWhiteSpace(computerName)) {
                args.Add("computer_name", computerName);
            }
            if (string.Equals(candidateTool, "system_updates_installed", StringComparison.OrdinalIgnoreCase)) {
                args.Add("include_pending_local", false);
            }
        }

        return args;
    }

    private static bool TryReadPackCapabilityErrorFallbackHints(
        string sourcePackId,
        string sourceTool,
        ToolOutputDto output,
        IReadOnlyList<ToolOutputDto> toolOutputs,
        string? userRequest,
        out JsonObject hints,
        out string reason) {
        hints = new JsonObject(StringComparer.Ordinal);
        reason = "no_error_fallback_signal";

        if (!string.Equals(sourcePackId, NormalizePackId("eventlog"), StringComparison.OrdinalIgnoreCase)) {
            reason = "source_pack_not_supported";
            return false;
        }

        if (!string.Equals(sourceTool, "eventlog_evtx_find", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(sourceTool, "eventlog_evtx_query", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(sourceTool, "eventlog_evtx_stats", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(sourceTool, "eventlog_evtx_security_summary", StringComparison.OrdinalIgnoreCase)) {
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

    private static string? TryExtractHostHintFromUserRequest(string? userRequest) {
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

            var candidate = text.Substring(tokenStart, i - tokenStart);
            tokenStart = -1;
            var score = ScoreHostHintCandidate(candidate);
            if (score <= bestScore) {
                continue;
            }

            bestScore = score;
            bestCandidate = candidate;
        }

        return bestScore > 0 ? bestCandidate : null;
    }

    private static string? TryResolveHostHintFromPriorDiscoveryOutputs(string hostHint, IReadOnlyList<ToolOutputDto> toolOutputs) {
        var normalizedHint = (hostHint ?? string.Empty).Trim();
        if (normalizedHint.Length == 0 || toolOutputs.Count == 0) {
            return null;
        }

        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = toolOutputs.Count - 1; i >= 0; i--) {
            var payload = (toolOutputs[i].Output ?? string.Empty).Trim();
            if (payload.Length == 0 || payload[0] != '{') {
                continue;
            }

            try {
                using var doc = JsonDocument.Parse(payload, ActionSelectionJsonOptions);
                if (doc.RootElement.ValueKind != JsonValueKind.Object) {
                    continue;
                }

                CollectHostCandidates(doc.RootElement, candidates, depth: 0, maxDepth: 4, budget: 256);
            } catch (JsonException) {
                // Best-effort host discovery only.
            }
        }

        if (candidates.Count == 0) {
            return null;
        }

        var bestCandidate = string.Empty;
        var bestScore = 0;
        foreach (var candidate in candidates) {
            var score = ScoreHostHintMatch(normalizedHint, candidate);
            if (score <= bestScore) {
                continue;
            }

            bestScore = score;
            bestCandidate = candidate;
        }

        return bestScore > 0 ? bestCandidate : null;
    }

    private static int ScoreHostHintMatch(string hint, string candidate) {
        var normalizedHint = (hint ?? string.Empty).Trim();
        var normalizedCandidate = (candidate ?? string.Empty).Trim();
        if (normalizedHint.Length == 0 || normalizedCandidate.Length == 0) {
            return 0;
        }

        if (!IsHostLikeCandidate(normalizedCandidate)) {
            return 0;
        }

        if (string.Equals(normalizedHint, normalizedCandidate, StringComparison.OrdinalIgnoreCase)) {
            return normalizedCandidate.Contains('.', StringComparison.Ordinal) ? 8 : 6;
        }

        if (normalizedCandidate.StartsWith(normalizedHint + ".", StringComparison.OrdinalIgnoreCase)) {
            return 7;
        }

        var hintLabel = ExtractPrimaryHostLabel(normalizedHint);
        var candidateLabel = ExtractPrimaryHostLabel(normalizedCandidate);
        if (hintLabel.Length == 0 || candidateLabel.Length == 0) {
            return 0;
        }

        if (string.Equals(hintLabel, candidateLabel, StringComparison.OrdinalIgnoreCase)) {
            return normalizedCandidate.Contains('.', StringComparison.Ordinal) ? 6 : 4;
        }

        return 0;
    }

    private static string ExtractPrimaryHostLabel(string value) {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return string.Empty;
        }

        var dot = normalized.IndexOf('.', StringComparison.Ordinal);
        return dot > 0 ? normalized[..dot] : normalized;
    }

    private static void CollectHostCandidates(JsonElement node, HashSet<string> candidates, int depth, int maxDepth, int budget) {
        if (depth > maxDepth || budget <= 0) {
            return;
        }

        switch (node.ValueKind) {
            case JsonValueKind.Object:
                foreach (var property in node.EnumerateObject()) {
                    if (budget-- <= 0) {
                        return;
                    }

                    var name = property.Name;
                    if (LooksLikeHostFieldName(name)) {
                        AddHostCandidateFromNode(property.Value, candidates);
                    }

                    CollectHostCandidates(property.Value, candidates, depth + 1, maxDepth, budget);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in node.EnumerateArray()) {
                    if (budget-- <= 0) {
                        return;
                    }

                    AddHostCandidateFromNode(item, candidates);
                    CollectHostCandidates(item, candidates, depth + 1, maxDepth, budget);
                }
                break;
        }
    }

    private static bool LooksLikeHostFieldName(string name) {
        var normalized = (name ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return false;
        }

        return normalized.Equals("machine_name", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals("computer_name", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals("hostname", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals("host_name", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals("dns_host_name", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals("dnshostname", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals("server", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals("server_name", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals("domain_controller", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals("domain_controllers", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals("domainControllers", StringComparison.OrdinalIgnoreCase);
    }

    private static void AddHostCandidateFromNode(JsonElement node, HashSet<string> candidates) {
        if (node.ValueKind == JsonValueKind.String) {
            var value = (node.GetString() ?? string.Empty).Trim();
            if (IsHostLikeCandidate(value)) {
                candidates.Add(value);
            }
            return;
        }

        if (node.ValueKind != JsonValueKind.Object) {
            return;
        }

        if (node.TryGetProperty("machine_name", out var machineNameNode) && machineNameNode.ValueKind == JsonValueKind.String) {
            var value = (machineNameNode.GetString() ?? string.Empty).Trim();
            if (IsHostLikeCandidate(value)) {
                candidates.Add(value);
            }
        }
        if (node.TryGetProperty("computer_name", out var computerNameNode) && computerNameNode.ValueKind == JsonValueKind.String) {
            var value = (computerNameNode.GetString() ?? string.Empty).Trim();
            if (IsHostLikeCandidate(value)) {
                candidates.Add(value);
            }
        }
        if (node.TryGetProperty("dns_host_name", out var dnsHostNode) && dnsHostNode.ValueKind == JsonValueKind.String) {
            var value = (dnsHostNode.GetString() ?? string.Empty).Trim();
            if (IsHostLikeCandidate(value)) {
                candidates.Add(value);
            }
        }
        if (node.TryGetProperty("dNSHostName", out var dnsHostCaseNode) && dnsHostCaseNode.ValueKind == JsonValueKind.String) {
            var value = (dnsHostCaseNode.GetString() ?? string.Empty).Trim();
            if (IsHostLikeCandidate(value)) {
                candidates.Add(value);
            }
        }
    }

    private static bool IsHostLikeCandidate(string value) {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length is < 2 or > 255) {
            return false;
        }

        if (normalized.StartsWith(".", StringComparison.Ordinal)
            || normalized.EndsWith(".", StringComparison.Ordinal)
            || normalized.Contains("..", StringComparison.Ordinal)
            || normalized.Contains(' ', StringComparison.Ordinal)
            || normalized.Contains('\\', StringComparison.Ordinal)
            || normalized.Contains('/', StringComparison.Ordinal)
            || normalized.Contains('@', StringComparison.Ordinal)
            || normalized.Contains(':', StringComparison.Ordinal)) {
            return false;
        }

        var hasLetter = false;
        for (var i = 0; i < normalized.Length; i++) {
            var ch = normalized[i];
            if (char.IsLetter(ch)) {
                hasLetter = true;
                continue;
            }

            if (char.IsDigit(ch) || ch == '.' || ch == '-' || ch == '_') {
                continue;
            }

            return false;
        }

        return hasLetter;
    }

    private static int ScoreHostHintCandidate(string candidate) {
        var value = (candidate ?? string.Empty).Trim();
        if (value.Length is < 3 or > 255) {
            return 0;
        }

        if (value.StartsWith(".", StringComparison.Ordinal)
            || value.EndsWith(".", StringComparison.Ordinal)
            || value.Contains("..", StringComparison.Ordinal)) {
            return 0;
        }

        var hasLetter = false;
        var hasDigit = false;
        var hasDot = false;
        var hasDash = false;
        for (var i = 0; i < value.Length; i++) {
            var ch = value[i];
            if (char.IsLetter(ch)) {
                hasLetter = true;
                continue;
            }

            if (char.IsDigit(ch)) {
                hasDigit = true;
                continue;
            }

            if (ch == '.') {
                hasDot = true;
                continue;
            }

            if (ch == '-') {
                hasDash = true;
                continue;
            }

            if (ch == '_') {
                continue;
            }

            return 0;
        }

        if (!hasLetter) {
            return 0;
        }

        // Keep the heuristic shape-based and language-agnostic:
        // host-like candidates should look like inventory labels (digit/dot) or longer dashed ids.
        if (!hasDigit && !hasDot && !(hasDash && value.Length >= 6)) {
            return 0;
        }

        var score = 1;
        if (hasDot) {
            score += 3;
        }
        if (hasDigit) {
            score += 2;
        }
        if (hasDash) {
            score += 1;
        }
        if (value.Length >= 8) {
            score += 1;
        }

        return score;
    }

    private static bool TryReadDiscoveryStatusObject(JsonElement root, out JsonElement discoveryStatus) {
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("discovery_status", out discoveryStatus)
            && discoveryStatus.ValueKind == JsonValueKind.Object) {
            return true;
        }

        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("discoveryStatus", out discoveryStatus)
            && discoveryStatus.ValueKind == JsonValueKind.Object) {
            return true;
        }

        discoveryStatus = default;
        return false;
    }

    private static void CopyHintIfPresent(JsonElement source, JsonObject destination, string propertyName) {
        if (!source.TryGetProperty(propertyName, out var node)) {
            return;
        }

        switch (node.ValueKind) {
            case JsonValueKind.String: {
                    var value = (node.GetString() ?? string.Empty).Trim();
                    if (value.Length > 0) {
                        destination.Add(propertyName, value);
                    }
                    break;
                }
            case JsonValueKind.True:
            case JsonValueKind.False:
                destination.Add(propertyName, node.GetBoolean());
                break;
        }
    }

    private static string? ReadNonEmptyHint(JsonObject hints, string propertyName) {
        if (!hints.TryGetValue(propertyName, out var node) || node is null || node.Kind != IntelligenceX.Json.JsonValueKind.String) {
            return null;
        }

        var value = (node.AsString() ?? string.Empty).Trim();
        return value.Length == 0 ? null : value;
    }

    private static bool? ReadHintBoolean(JsonObject hints, string propertyName) {
        if (!hints.TryGetValue(propertyName, out var node) || node is null) {
            return null;
        }

        return node.Kind switch {
            IntelligenceX.Json.JsonValueKind.Boolean => node.AsBoolean(),
            _ => null
        };
    }

    private static bool TryReadPositiveIntProperty(JsonElement source, string propertyName, out int value) {
        value = 0;
        if (!source.TryGetProperty(propertyName, out var node) || node.ValueKind != JsonValueKind.Number) {
            return false;
        }

        if (!node.TryGetInt32(out value)) {
            return false;
        }

        return value > 0;
    }

    private static bool TryReadArrayLength(JsonElement source, string propertyName, out int length) {
        length = 0;
        if (!source.TryGetProperty(propertyName, out var node) || node.ValueKind != JsonValueKind.Array) {
            return false;
        }

        length = node.GetArrayLength();
        return true;
    }

    private static bool TryReadBooleanProperty(JsonElement source, string propertyName, out bool value) {
        value = false;
        if (!source.TryGetProperty(propertyName, out var node)) {
            return false;
        }

        return node.ValueKind switch {
            JsonValueKind.True => value = true,
            JsonValueKind.False => true,
            JsonValueKind.String => TryParseProtocolBoolean((node.GetString() ?? string.Empty).Trim(), out value),
            JsonValueKind.Number => node.TryGetInt64(out var numeric)
                                    && TryMapIntegerBoolean(numeric, out value),
            _ => false
        };
    }

    private static bool TryMapIntegerBoolean(long numeric, out bool value) {
        value = false;
        if (numeric == 0) {
            return true;
        }

        if (numeric == 1) {
            value = true;
            return true;
        }

        return false;
    }

    private static bool ContainsToolName(IReadOnlyList<string> tools, string toolName) {
        for (var i = 0; i < tools.Count; i++) {
            if (string.Equals(tools[i], toolName, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        return false;
    }
}
