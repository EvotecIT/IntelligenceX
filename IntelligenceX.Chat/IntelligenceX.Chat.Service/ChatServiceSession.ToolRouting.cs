using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using JsonValueKind = System.Text.Json.JsonValueKind;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Abstractions.Serialization;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.Json;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.OpenAI.Auth;
using IntelligenceX.OpenAI.Chat;
using IntelligenceX.OpenAI.ToolCalling;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    private const int DomainIntentClarificationMinRelevantCandidates = 3;
    private const double DomainIntentClarificationMaxDominantShare = 0.80d;
    private const double DomainIntentAffinityRetentionMinDominantShare = 0.65d;
    private const string DomainIntentFamilyAd = "ad_domain";
    private const string DomainIntentFamilyPublic = "public_domain";

    private static List<ToolRoutingInsight> BuildContinuationRoutingInsights(IReadOnlyList<ToolDefinition> selectedDefs) {
        var list = new List<ToolRoutingInsight>(selectedDefs.Count);
        for (var i = 0; i < selectedDefs.Count && i < 12; i++) {
            var name = selectedDefs[i].Name;
            if (string.IsNullOrWhiteSpace(name)) {
                continue;
            }

            list.Add(new ToolRoutingInsight(
                ToolName: name.Trim(),
                Confidence: "high",
                Score: 1d,
                Reason: "continuation follow-up reuse"));
        }

        return list;
    }

    private async Task EmitRoutingInsightsAsync(StreamWriter writer, string requestId, string threadId, IReadOnlyList<ToolRoutingInsight> insights,
        string routingStrategy, int selectedToolCount, int totalToolCount) {
        if (insights.Count == 0) {
            return;
        }

        var (selected, total) = NormalizeRoutingToolCounts(selectedToolCount, totalToolCount);
        for (var i = 0; i < insights.Count; i++) {
            var insight = insights[i];
            var insightStrategy = ResolveRoutingInsightStrategy(insight, routingStrategy);
            var payload = JsonSerializer.Serialize(new {
                confidence = insight.Confidence,
                score = insight.Score,
                reason = insight.Reason,
                strategy = insightStrategy,
                rank = i + 1,
                selectedToolCount = selected,
                totalToolCount = total
            });
            await TryWriteStatusAsync(
                    writer,
                    requestId,
                    threadId,
                    status: ChatStatusCodes.RoutingTool,
                    toolName: insight.ToolName,
                    message: payload)
                .ConfigureAwait(false);
        }
    }

    private static string ResolveRoutingStrategy(
        bool weightedToolRouting,
        bool executionContractApplies,
        bool usedContinuationSubset,
        IReadOnlyList<ToolRoutingInsight> insights,
        int selectedToolCount,
        int totalToolCount) {
        if (selectedToolCount <= 0 || totalToolCount <= 0) {
            return "no_tools";
        }

        if (!weightedToolRouting) {
            return "disabled";
        }

        if (executionContractApplies && selectedToolCount >= totalToolCount) {
            return "execution_contract_full_set";
        }

        if (usedContinuationSubset) {
            return "continuation_subset";
        }

        if (HasPlannerInsight(insights)) {
            return "semantic_planner";
        }

        if (selectedToolCount < totalToolCount) {
            return "weighted_heuristic";
        }

        return "full_toolset";
    }

    private static bool ShouldEmitRoutingTransparency(int selectedToolCount, int totalToolCount) {
        // Contract: always emit routing transparency for any non-negative state so turns remain
        // observable, then normalize counts in payload/message builders for consistency.
        return selectedToolCount >= 0
            && totalToolCount >= 0;
    }

    private static string BuildRoutingSelectionMessage(int selectedToolCount, int totalToolCount, string strategy) {
        var (selected, total) = NormalizeRoutingToolCounts(selectedToolCount, totalToolCount);

        return strategy switch {
            "execution_contract_full_set" =>
                $"Tool routing kept all {selected}/{total} tools for this explicit execution turn.",
            "continuation_subset" =>
                $"Tool routing reused continuation context and selected {selected} of {total} tools for this turn.",
            "semantic_planner" =>
                $"Tool routing used semantic planning and selected {selected} of {total} tools for this turn.",
            "weighted_heuristic" =>
                $"Tool routing used weighted relevance and selected {selected} of {total} tools for this turn.",
            "full_toolset" =>
                $"Tool routing kept the full tool set ({selected}/{total}) for this turn.",
            "disabled" =>
                $"Tool routing is disabled for this turn; using the full tool set ({selected}/{total}).",
            "no_tools" =>
                "No tools are currently available for this turn.",
            _ =>
                $"Tool routing selected {selected} of {total} tools for this turn."
        };
    }

    private static bool ShouldRequestDomainIntentClarification(
        bool weightedToolRouting,
        bool executionContractApplies,
        bool usedContinuationSubset,
        int selectedToolCount,
        int totalToolCount,
        IReadOnlyList<ToolDefinition> selectedTools) {
        if (!weightedToolRouting || executionContractApplies || usedContinuationSubset) {
            return false;
        }

        if (selectedTools is null) {
            return false;
        }

        var (selected, total) = NormalizeRoutingToolCounts(selectedToolCount, totalToolCount);
        if (selected <= 0 || total <= 0 || selected >= total || selectedTools.Count == 0) {
            return false;
        }

        var adCandidates = 0;
        var publicDomainCandidates = 0;
        for (var i = 0; i < selectedTools.Count; i++) {
            var tool = selectedTools[i];
            var toolName = (tool.Name ?? string.Empty).Trim();
            if (toolName.Length == 0) {
                continue;
            }

            var family = ResolveDomainIntentFamily(tool);
            if (string.Equals(family, DomainIntentFamilyAd, StringComparison.Ordinal)) {
                adCandidates++;
            } else if (string.Equals(family, DomainIntentFamilyPublic, StringComparison.Ordinal)) {
                publicDomainCandidates++;
            }
        }

        if (adCandidates <= 0 || publicDomainCandidates <= 0) {
            return false;
        }

        var relevantCandidates = adCandidates + publicDomainCandidates;
        if (relevantCandidates < DomainIntentClarificationMinRelevantCandidates) {
            return false;
        }

        var dominantShare = Math.Max(adCandidates, publicDomainCandidates) / (double)relevantCandidates;
        return dominantShare < DomainIntentClarificationMaxDominantShare;
    }

    private static bool IsAdDomainIntentToolName(string toolName) {
        return toolName.StartsWith("ad_", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPublicDomainIntentToolName(string toolName) {
        return toolName.StartsWith("dnsclientx_", StringComparison.OrdinalIgnoreCase)
               || toolName.StartsWith("domaindetective_", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveDomainIntentFamily(ToolDefinition definition) {
        if (definition is null) {
            return string.Empty;
        }

        var category = (definition.Category ?? string.Empty).Trim();
        if (category.Length == 0) {
            category = (ToolSelectionMetadata.Enrich(definition, toolType: null).Category ?? string.Empty).Trim();
        }

        if (string.Equals(category, "active_directory", StringComparison.OrdinalIgnoreCase)) {
            return DomainIntentFamilyAd;
        }

        if (string.Equals(category, "dns", StringComparison.OrdinalIgnoreCase)) {
            return DomainIntentFamilyPublic;
        }

        var toolName = (definition.Name ?? string.Empty).Trim();
        if (IsAdDomainIntentToolName(toolName)) {
            return DomainIntentFamilyAd;
        }

        if (IsPublicDomainIntentToolName(toolName)) {
            return DomainIntentFamilyPublic;
        }

        return string.Empty;
    }

    private string ResolveDomainIntentFamily(string toolName) {
        var normalizedToolName = (toolName ?? string.Empty).Trim();
        if (normalizedToolName.Length == 0) {
            return string.Empty;
        }

        if (_registry.TryGetDefinition(normalizedToolName, out var definition) && definition is not null) {
            var family = ResolveDomainIntentFamily(definition);
            if (family.Length > 0) {
                return family;
            }
        }

        if (IsAdDomainIntentToolName(normalizedToolName)) {
            return DomainIntentFamilyAd;
        }

        if (IsPublicDomainIntentToolName(normalizedToolName)) {
            return DomainIntentFamilyPublic;
        }

        return string.Empty;
    }

    private static string BuildDomainIntentClarificationText() {
        return "I can help with either scope, and I want to avoid running the wrong tool family.\n\n"
               + "Do you want:\n"
               + "1. Active Directory domain scope (DCs, LDAP, replication, GPO)\n"
               + "2. Public DNS/domain scope (records, MX, SPF, DMARC, NS)\n\n"
               + "Reply with `1` or `2`.";
    }

    private bool TryApplyDomainIntentAffinity(
        string threadId,
        IReadOnlyList<ToolDefinition> selectedTools,
        out IReadOnlyList<ToolDefinition> filteredTools,
        out string family,
        out int removedCount) {
        filteredTools = selectedTools;
        family = string.Empty;
        removedCount = 0;

        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0 || selectedTools is null || selectedTools.Count == 0) {
            return false;
        }

        var nowUtc = DateTime.UtcNow;
        string preferredFamily = string.Empty;
        var hasPreferredFamily = false;

        lock (_toolRoutingContextLock) {
            if (_domainIntentFamilyByThreadId.TryGetValue(normalizedThreadId, out var cachedFamily)
                && !string.IsNullOrWhiteSpace(cachedFamily)) {
                if (_domainIntentFamilySeenUtcTicks.TryGetValue(normalizedThreadId, out var seenTicks)
                    && TryGetUtcDateTimeFromTicks(seenTicks, out var seenUtc)
                    && nowUtc - seenUtc <= DomainIntentFamilyContextMaxAge) {
                    preferredFamily = cachedFamily;
                    hasPreferredFamily = true;
                    _domainIntentFamilySeenUtcTicks[normalizedThreadId] = nowUtc.Ticks;
                    TrimWeightedRoutingContextsNoLock();
                } else {
                    _domainIntentFamilyByThreadId.Remove(normalizedThreadId);
                    _domainIntentFamilySeenUtcTicks.Remove(normalizedThreadId);
                }
            }
        }

        if (!hasPreferredFamily) {
            if (!TryLoadDomainIntentFamilySnapshot(normalizedThreadId, out var snapshotFamily, out _)) {
                return false;
            }

            preferredFamily = snapshotFamily;
            hasPreferredFamily = true;
            lock (_toolRoutingContextLock) {
                _domainIntentFamilyByThreadId[normalizedThreadId] = preferredFamily;
                _domainIntentFamilySeenUtcTicks[normalizedThreadId] = nowUtc.Ticks;
                TrimWeightedRoutingContextsNoLock();
            }
        }

        var filtered = new List<ToolDefinition>(selectedTools.Count);
        for (var i = 0; i < selectedTools.Count; i++) {
            var tool = selectedTools[i];
            var toolName = (tool.Name ?? string.Empty).Trim();
            if (toolName.Length == 0) {
                continue;
            }

            var candidateFamily = ResolveDomainIntentFamily(tool);
            if (string.Equals(preferredFamily, DomainIntentFamilyAd, StringComparison.Ordinal)
                && string.Equals(candidateFamily, DomainIntentFamilyPublic, StringComparison.Ordinal)) {
                removedCount++;
                continue;
            }

            if (string.Equals(preferredFamily, DomainIntentFamilyPublic, StringComparison.Ordinal)
                && string.Equals(candidateFamily, DomainIntentFamilyAd, StringComparison.Ordinal)) {
                removedCount++;
                continue;
            }

            filtered.Add(tool);
        }

        if (removedCount <= 0 || filtered.Count == 0) {
            return false;
        }

        filteredTools = filtered;
        family = preferredFamily;
        return true;
    }

    private void RememberPreferredDomainIntentFamily(
        string threadId,
        IReadOnlyList<ToolCallDto> toolCalls,
        IReadOnlyList<ToolOutputDto> toolOutputs,
        IReadOnlyDictionary<string, bool> mutatingToolHintsByName) {
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0 || toolCalls.Count == 0 || toolOutputs.Count == 0) {
            return;
        }

        var toolNameByCallId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < toolCalls.Count; i++) {
            var call = toolCalls[i];
            var callId = (call.CallId ?? string.Empty).Trim();
            var toolName = (call.Name ?? string.Empty).Trim();
            if (callId.Length == 0 || toolName.Length == 0) {
                continue;
            }

            toolNameByCallId[callId] = toolName;
        }

        if (toolNameByCallId.Count == 0) {
            return;
        }

        var adVotes = 0;
        var publicVotes = 0;
        for (var i = 0; i < toolOutputs.Count; i++) {
            var output = toolOutputs[i];
            var callId = (output.CallId ?? string.Empty).Trim();
            if (callId.Length == 0 || !toolNameByCallId.TryGetValue(callId, out var toolName)) {
                continue;
            }

            if (mutatingToolHintsByName.TryGetValue(toolName, out var mutating) && mutating) {
                continue;
            }

            var success = output.Ok != false
                          && string.IsNullOrWhiteSpace(output.ErrorCode)
                          && string.IsNullOrWhiteSpace(output.Error);
            if (!success) {
                continue;
            }

            var family = ResolveDomainIntentFamily(toolName);
            if (string.Equals(family, DomainIntentFamilyAd, StringComparison.Ordinal)) {
                adVotes++;
            } else if (string.Equals(family, DomainIntentFamilyPublic, StringComparison.Ordinal)) {
                publicVotes++;
            }
        }

        var totalVotes = adVotes + publicVotes;
        if (totalVotes <= 0) {
            return;
        }

        var dominantVotes = Math.Max(adVotes, publicVotes);
        if (adVotes == publicVotes || dominantVotes / (double)totalVotes < DomainIntentAffinityRetentionMinDominantShare) {
            ClearPreferredDomainIntentFamily(normalizedThreadId);
            return;
        }

        var nextFamily = adVotes > publicVotes ? DomainIntentFamilyAd : DomainIntentFamilyPublic;
        var seenUtcTicks = DateTime.UtcNow.Ticks;
        lock (_toolRoutingContextLock) {
            _domainIntentFamilyByThreadId[normalizedThreadId] = nextFamily;
            _domainIntentFamilySeenUtcTicks[normalizedThreadId] = seenUtcTicks;
            TrimWeightedRoutingContextsNoLock();
        }

        PersistDomainIntentFamilySnapshot(normalizedThreadId, nextFamily, seenUtcTicks);
    }

    private void ClearPreferredDomainIntentFamily(string threadId) {
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0) {
            return;
        }

        var removed = false;
        lock (_toolRoutingContextLock) {
            removed = _domainIntentFamilyByThreadId.Remove(normalizedThreadId) || removed;
            removed = _domainIntentFamilySeenUtcTicks.Remove(normalizedThreadId) || removed;
            if (removed) {
                TrimWeightedRoutingContextsNoLock();
            }
        }

        if (removed) {
            RemoveDomainIntentFamilySnapshot(normalizedThreadId);
        }
    }

    private static string BuildRoutingMetaPayload(
        string strategy,
        bool weightedToolRouting,
        bool executionContractApplies,
        bool usedContinuationSubset,
        int selectedToolCount,
        int totalToolCount,
        int insightCount,
        bool plannerInsightsDetected,
        int? requestedMaxCandidateTools,
        int? effectiveMaxCandidateTools,
        long? effectiveContextLength,
        bool contextAwareBudgetApplied) {
        var (selected, total) = NormalizeRoutingToolCounts(selectedToolCount, totalToolCount);
        var normalizedContextLength = effectiveContextLength is > 0 ? effectiveContextLength : null;
        return JsonSerializer.Serialize(new {
            strategy = (strategy ?? string.Empty).Trim(),
            weightedToolRouting,
            executionContractApplies,
            usedContinuationSubset,
            selectedToolCount = selected,
            totalToolCount = total,
            reducedToolSet = selected > 0 && selected < total,
            insightCount = Math.Max(0, insightCount),
            plannerInsightsDetected,
            toolCandidateBudget = new {
                requested = requestedMaxCandidateTools,
                effective = effectiveMaxCandidateTools,
                contextAwareBudgetApplied,
                effectiveModelContextLength = normalizedContextLength
            }
        });
    }

    private static (int SelectedToolCount, int TotalToolCount) NormalizeRoutingToolCounts(int selectedToolCount, int totalToolCount) {
        // Keep emitted count semantics consistent for all consumers.
        var total = Math.Max(0, totalToolCount);
        var selected = Math.Clamp(selectedToolCount, 0, total);
        return (selected, total);
    }

    private static bool HasPlannerInsight(IReadOnlyList<ToolRoutingInsight> insights) {
        if (insights.Count == 0) {
            return false;
        }

        for (var i = 0; i < insights.Count; i++) {
            var reason = insights[i].Reason ?? string.Empty;
            if (reason.IndexOf("semantic planner", StringComparison.OrdinalIgnoreCase) >= 0) {
                return true;
            }
        }

        return false;
    }

    private static string ResolveRoutingInsightStrategy(ToolRoutingInsight insight, string defaultStrategy) {
        var reason = (insight.Reason ?? string.Empty).Trim();
        if (reason.Length == 0) {
            return defaultStrategy;
        }

        if (reason.IndexOf("continuation", StringComparison.OrdinalIgnoreCase) >= 0) {
            return "continuation_subset";
        }

        if (reason.IndexOf("semantic planner", StringComparison.OrdinalIgnoreCase) >= 0) {
            return "semantic_planner";
        }

        return defaultStrategy;
    }

    private bool TryGetContinuationToolSubset(string threadId, string userRequest, IReadOnlyList<ToolDefinition> allDefinitions,
        out IReadOnlyList<ToolDefinition> subset) {
        subset = Array.Empty<ToolDefinition>();
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0 || !LooksLikeContinuationFollowUp(userRequest)) {
            return false;
        }

        string[]? previousNames;
        lock (_toolRoutingContextLock) {
            if (!_lastWeightedToolNamesByThreadId.TryGetValue(normalizedThreadId, out previousNames) || previousNames.Length == 0) {
                return false;
            }

            _lastWeightedToolSubsetSeenUtcTicks[normalizedThreadId] = DateTime.UtcNow.Ticks;
            TrimWeightedRoutingContextsNoLock();
        }

        var preferred = new HashSet<string>(previousNames!, StringComparer.OrdinalIgnoreCase);
        var selected = new List<ToolDefinition>();
        for (var i = 0; i < allDefinitions.Count; i++) {
            var definition = allDefinitions[i];
            if (preferred.Contains(definition.Name)) {
                selected.Add(definition);
            }
        }

        if (selected.Count < 2) {
            return false;
        }

        subset = selected;
        return true;
    }

    private void RememberWeightedToolSubset(string threadId, IReadOnlyList<ToolDefinition> selectedDefinitions, int allToolCount) {
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0) {
            return;
        }

        lock (_toolRoutingContextLock) {
            if (selectedDefinitions.Count == 0 || selectedDefinitions.Count >= allToolCount) {
                _lastWeightedToolNamesByThreadId.Remove(normalizedThreadId);
                _lastWeightedToolSubsetSeenUtcTicks.Remove(normalizedThreadId);
                return;
            }

            var names = new List<string>(selectedDefinitions.Count);
            for (var i = 0; i < selectedDefinitions.Count && i < 64; i++) {
                var name = (selectedDefinitions[i].Name ?? string.Empty).Trim();
                if (name.Length > 0) {
                    names.Add(name);
                }
            }

            _lastWeightedToolNamesByThreadId[normalizedThreadId] = names.Count == 0 ? Array.Empty<string>() : names.ToArray();
            _lastWeightedToolSubsetSeenUtcTicks[normalizedThreadId] = DateTime.UtcNow.Ticks;
            TrimWeightedRoutingContextsNoLock();
        }
    }

    private void RememberUserIntent(string threadId, string userRequest) {
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0) {
            return;
        }

        var normalized = (userRequest ?? string.Empty).Trim();
        if (normalized.Length == 0 || LooksLikeContinuationFollowUp(normalized)) {
            return;
        }

        if (normalized.Length > 600) {
            normalized = normalized.Substring(0, 600);
        }

        lock (_toolRoutingContextLock) {
            _lastUserIntentByThreadId[normalizedThreadId] = normalized;
            _lastUserIntentSeenUtcTicks[normalizedThreadId] = DateTime.UtcNow.Ticks;
            TrimWeightedRoutingContextsNoLock();
        }
    }

    private string ExpandContinuationUserRequest(string threadId, string userRequest) {
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0) {
            return userRequest;
        }

        var raw = userRequest ?? string.Empty;
        if (TryResolvePendingActionSelection(normalizedThreadId, raw, out var resolved)) {
            return resolved;
        }

        var normalized = raw.Trim();
        if (normalized.Length == 0 || !LooksLikeContinuationFollowUp(normalized)) {
            return raw;
        }

        string? intent;
        long intentTicks;
        lock (_toolRoutingContextLock) {
            if (!_lastUserIntentByThreadId.TryGetValue(normalizedThreadId, out intent) || string.IsNullOrWhiteSpace(intent)) {
                return raw;
            }

            intentTicks = _lastUserIntentSeenUtcTicks.TryGetValue(normalizedThreadId, out var ticks) ? ticks : 0;
        }

        if (intentTicks > 0) {
            if (intentTicks < DateTime.MinValue.Ticks || intentTicks > DateTime.MaxValue.Ticks) {
                // Defensive: avoid exceptions from ticks->DateTime conversion if ticks are corrupted/out of range.
                return raw;
            }
            var age = DateTime.UtcNow - new DateTime(intentTicks, DateTimeKind.Utc);
            if (age > UserIntentContextMaxAge) {
                return raw;
            }
        }

        lock (_toolRoutingContextLock) {
            _lastUserIntentSeenUtcTicks[normalizedThreadId] = DateTime.UtcNow.Ticks;
            TrimWeightedRoutingContextsNoLock();
        }

        var expanded = $"{intent!.Trim()}\nFollow-up: {normalized}";
        return expanded.Length <= 900 ? expanded : expanded.Substring(0, 900);
    }

    private double ReadToolRoutingAdjustment(string toolName) {
        lock (_toolRoutingStatsLock) {
            if (!_toolRoutingStats.TryGetValue(toolName, out var stats)) {
                return 0d;
            }

            var score = 0d;
            if (stats.Successes > 0) {
                score += Math.Min(2.4d, stats.Successes * 0.2d);
            }
            if (stats.Failures > 0) {
                score -= Math.Min(2.4d, stats.Failures * 0.28d);
            }
            if (stats.LastSuccessUtcTicks > 0) {
                var sinceSuccess = DateTime.UtcNow - new DateTime(stats.LastSuccessUtcTicks, DateTimeKind.Utc);
                if (sinceSuccess <= TimeSpan.FromMinutes(20)) {
                    score += 0.35d;
                }
            }

            return score;
        }
    }

    private void UpdateToolRoutingStats(IReadOnlyList<ToolCall> calls, IReadOnlyList<ToolOutputDto> outputs) {
        if (calls.Count == 0 || outputs.Count == 0) {
            return;
        }

        var nameByCallId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < calls.Count; i++) {
            var call = calls[i];
            if (string.IsNullOrWhiteSpace(call.CallId) || string.IsNullOrWhiteSpace(call.Name)) {
                continue;
            }

            nameByCallId[call.CallId.Trim()] = call.Name.Trim();
        }

        if (nameByCallId.Count == 0) {
            return;
        }

        var nowTicks = DateTime.UtcNow.Ticks;
        lock (_toolRoutingStatsLock) {
            foreach (var output in outputs) {
                var normalizedOutputCallId = (output.CallId ?? string.Empty).Trim();
                if (normalizedOutputCallId.Length == 0 || !nameByCallId.TryGetValue(normalizedOutputCallId, out var toolName)) {
                    continue;
                }

                if (!_toolRoutingStats.TryGetValue(toolName, out var stats)) {
                    stats = new ToolRoutingStats();
                    _toolRoutingStats[toolName] = stats;
                }

                stats.Invocations++;
                stats.LastUsedUtcTicks = nowTicks;
                var success = output.Ok != false
                              && string.IsNullOrWhiteSpace(output.ErrorCode)
                              && string.IsNullOrWhiteSpace(output.Error);
                if (success) {
                    stats.Successes++;
                    stats.LastSuccessUtcTicks = nowTicks;
                } else {
                    stats.Failures++;
                }
            }
            TrimToolRoutingStatsNoLock();
        }
    }

    private void TrimWeightedRoutingContextsNoLock() {
        Debug.Assert(Monitor.IsEntered(_toolRoutingContextLock));

        // Weighted-tool-subset, user-intent, pending-action, structured-next-action, and planner-thread contexts share the same
        // key space (active thread id), so trim all when any grows beyond its cap.
        var weightedRemoveCount = _lastWeightedToolNamesByThreadId.Count - MaxTrackedWeightedRoutingContexts;
        var intentRemoveCount = _lastUserIntentByThreadId.Count - MaxTrackedUserIntentContexts;
        var pendingRemoveCount = _pendingActionsByThreadId.Count - MaxTrackedPendingActionContexts;
        var structuredNextActionRemoveCount = _structuredNextActionByThreadId.Count - MaxTrackedStructuredNextActionContexts;
        var plannerRemoveCount = _plannerThreadIdByActiveThreadId.Count - MaxTrackedPlannerThreadContexts;
        var domainIntentRemoveCount = _domainIntentFamilyByThreadId.Count - MaxTrackedDomainIntentFamilyContexts;
        var removeCount = Math.Max(
            Math.Max(
                Math.Max(Math.Max(weightedRemoveCount, intentRemoveCount), Math.Max(pendingRemoveCount, structuredNextActionRemoveCount)),
                Math.Max(plannerRemoveCount, domainIntentRemoveCount)),
            0);
        if (removeCount <= 0) {
            return;
        }

        // Defensive: if tick/value maps drift (missing/zero ticks), drop incomplete entries so they don't bias eviction.
        var removedInvalid = false;
        foreach (var threadId in _lastWeightedToolNamesByThreadId.Keys.ToArray()) {
            if (!_lastWeightedToolSubsetSeenUtcTicks.TryGetValue(threadId, out var ticks) || ticks <= 0) {
                _lastWeightedToolNamesByThreadId.Remove(threadId);
                _lastWeightedToolSubsetSeenUtcTicks.Remove(threadId);
                removedInvalid = true;
            }
        }
        foreach (var threadId in _lastUserIntentByThreadId.Keys.ToArray()) {
            if (!_lastUserIntentSeenUtcTicks.TryGetValue(threadId, out var ticks) || ticks <= 0) {
                _lastUserIntentByThreadId.Remove(threadId);
                _lastUserIntentSeenUtcTicks.Remove(threadId);
                removedInvalid = true;
            }
        }
        foreach (var threadId in _pendingActionsByThreadId.Keys.ToArray()) {
            if (!_pendingActionsSeenUtcTicks.TryGetValue(threadId, out var ticks) || ticks <= 0) {
                _pendingActionsByThreadId.Remove(threadId);
                _pendingActionsSeenUtcTicks.Remove(threadId);
                _pendingActionsCallToActionTokensByThreadId.Remove(threadId);
                removedInvalid = true;
            }
        }
        foreach (var threadId in _structuredNextActionByThreadId.Keys.ToArray()) {
            if (!_structuredNextActionByThreadId.TryGetValue(threadId, out var snapshot) || snapshot.SeenUtcTicks <= 0) {
                _structuredNextActionByThreadId.Remove(threadId);
                removedInvalid = true;
                continue;
            }

            if (!TryGetUtcDateTimeFromTicks(snapshot.SeenUtcTicks, out var seenUtc)) {
                _structuredNextActionByThreadId.Remove(threadId);
                removedInvalid = true;
                continue;
            }

            var age = DateTime.UtcNow - seenUtc;
            if (age > StructuredNextActionContextMaxAge) {
                _structuredNextActionByThreadId.Remove(threadId);
                removedInvalid = true;
            }
        }
        foreach (var threadId in _plannerThreadIdByActiveThreadId.Keys.ToArray()) {
            if (!_plannerThreadSeenUtcTicksByActiveThreadId.TryGetValue(threadId, out var ticks) || ticks <= 0) {
                _plannerThreadIdByActiveThreadId.Remove(threadId);
                _plannerThreadSeenUtcTicksByActiveThreadId.Remove(threadId);
                removedInvalid = true;
                continue;
            }

            var age = DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc);
            if (age > PlannerThreadContextMaxAge) {
                _plannerThreadIdByActiveThreadId.Remove(threadId);
                _plannerThreadSeenUtcTicksByActiveThreadId.Remove(threadId);
                removedInvalid = true;
            }
        }
        foreach (var threadId in _domainIntentFamilyByThreadId.Keys.ToArray()) {
            if (!_domainIntentFamilySeenUtcTicks.TryGetValue(threadId, out var ticks) || !TryGetUtcDateTimeFromTicks(ticks, out var seenUtc)) {
                _domainIntentFamilyByThreadId.Remove(threadId);
                _domainIntentFamilySeenUtcTicks.Remove(threadId);
                removedInvalid = true;
                continue;
            }

            if (DateTime.UtcNow - seenUtc > DomainIntentFamilyContextMaxAge) {
                _domainIntentFamilyByThreadId.Remove(threadId);
                _domainIntentFamilySeenUtcTicks.Remove(threadId);
                removedInvalid = true;
            }
        }
        if (removedInvalid) {
            weightedRemoveCount = _lastWeightedToolNamesByThreadId.Count - MaxTrackedWeightedRoutingContexts;
            intentRemoveCount = _lastUserIntentByThreadId.Count - MaxTrackedUserIntentContexts;
            pendingRemoveCount = _pendingActionsByThreadId.Count - MaxTrackedPendingActionContexts;
            structuredNextActionRemoveCount = _structuredNextActionByThreadId.Count - MaxTrackedStructuredNextActionContexts;
            plannerRemoveCount = _plannerThreadIdByActiveThreadId.Count - MaxTrackedPlannerThreadContexts;
            domainIntentRemoveCount = _domainIntentFamilyByThreadId.Count - MaxTrackedDomainIntentFamilyContexts;
            removeCount = Math.Max(
                Math.Max(
                    Math.Max(Math.Max(weightedRemoveCount, intentRemoveCount), Math.Max(pendingRemoveCount, structuredNextActionRemoveCount)),
                    Math.Max(plannerRemoveCount, domainIntentRemoveCount)),
                0);
            if (removeCount <= 0) {
                return;
            }
        }

        var seenThreadIds = new HashSet<string>(_lastWeightedToolNamesByThreadId.Keys, StringComparer.Ordinal);
        foreach (var threadId in _lastUserIntentByThreadId.Keys) {
            seenThreadIds.Add(threadId);
        }
        foreach (var threadId in _pendingActionsByThreadId.Keys) {
            seenThreadIds.Add(threadId);
        }
        foreach (var threadId in _structuredNextActionByThreadId.Keys) {
            seenThreadIds.Add(threadId);
        }
        foreach (var threadId in _plannerThreadIdByActiveThreadId.Keys) {
            seenThreadIds.Add(threadId);
        }
        foreach (var threadId in _domainIntentFamilyByThreadId.Keys) {
            seenThreadIds.Add(threadId);
        }

        var threadIdsToRemove = seenThreadIds
            .Select(threadId => {
                var ticks = 0L;
                if (_lastWeightedToolSubsetSeenUtcTicks.TryGetValue(threadId, out var weightedTicks) && weightedTicks > ticks) {
                    ticks = weightedTicks;
                }
                if (_lastUserIntentSeenUtcTicks.TryGetValue(threadId, out var intentTicks) && intentTicks > ticks) {
                    ticks = intentTicks;
                }
                if (_pendingActionsSeenUtcTicks.TryGetValue(threadId, out var actionTicks) && actionTicks > ticks) {
                    ticks = actionTicks;
                }
                if (_structuredNextActionByThreadId.TryGetValue(threadId, out var structuredNextAction)
                    && structuredNextAction.SeenUtcTicks > ticks) {
                    ticks = structuredNextAction.SeenUtcTicks;
                }
                if (_plannerThreadSeenUtcTicksByActiveThreadId.TryGetValue(threadId, out var plannerTicks) && plannerTicks > ticks) {
                    ticks = plannerTicks;
                }
                if (_domainIntentFamilySeenUtcTicks.TryGetValue(threadId, out var domainIntentTicks) && domainIntentTicks > ticks) {
                    ticks = domainIntentTicks;
                }
                return (ThreadId: threadId, Ticks: ticks);
            })
            .OrderBy(item => item.Ticks)
            .ThenBy(item => item.ThreadId, StringComparer.Ordinal)
            .Take(removeCount)
            .Select(item => item.ThreadId)
            .ToArray();

        foreach (var threadId in threadIdsToRemove) {
            _lastWeightedToolNamesByThreadId.Remove(threadId);
            _lastWeightedToolSubsetSeenUtcTicks.Remove(threadId);
            _lastUserIntentByThreadId.Remove(threadId);
            _lastUserIntentSeenUtcTicks.Remove(threadId);
            _pendingActionsByThreadId.Remove(threadId);
            _pendingActionsSeenUtcTicks.Remove(threadId);
            _pendingActionsCallToActionTokensByThreadId.Remove(threadId);
            _structuredNextActionByThreadId.Remove(threadId);
            _plannerThreadIdByActiveThreadId.Remove(threadId);
            _plannerThreadSeenUtcTicksByActiveThreadId.Remove(threadId);
            _domainIntentFamilyByThreadId.Remove(threadId);
            _domainIntentFamilySeenUtcTicks.Remove(threadId);
        }
    }

    private void TrimToolRoutingStatsNoLock() {
        var removeCount = _toolRoutingStats.Count - MaxTrackedToolRoutingStats;
        if (removeCount <= 0) {
            return;
        }

        var toolNamesToRemove = _toolRoutingStats
            .Select(pair => {
                var stats = pair.Value;
                var ticks = stats.LastUsedUtcTicks > 0
                    ? stats.LastUsedUtcTicks
                    : (stats.LastSuccessUtcTicks > 0 ? stats.LastSuccessUtcTicks : long.MinValue);
                return (ToolName: pair.Key, Ticks: ticks);
            })
            .OrderBy(item => item.Ticks)
            .ThenBy(item => item.ToolName, StringComparer.Ordinal)
            .Take(removeCount)
            .Select(item => item.ToolName)
            .ToArray();

        foreach (var toolName in toolNamesToRemove) {
            _toolRoutingStats.Remove(toolName);
        }
    }

    internal void SetToolRoutingStatsForTesting(IReadOnlyDictionary<string, (long LastUsedUtcTicks, long LastSuccessUtcTicks)> statsByToolName) {
        ArgumentNullException.ThrowIfNull(statsByToolName);

        lock (_toolRoutingStatsLock) {
            _toolRoutingStats.Clear();
            foreach (var pair in statsByToolName) {
                var name = (pair.Key ?? string.Empty).Trim();
                if (name.Length == 0) {
                    continue;
                }

                _toolRoutingStats[name] = new ToolRoutingStats {
                    LastUsedUtcTicks = pair.Value.LastUsedUtcTicks,
                    LastSuccessUtcTicks = pair.Value.LastSuccessUtcTicks
                };
            }
        }
    }

    internal void SetWeightedRoutingContextsForTesting(IReadOnlyDictionary<string, string[]> namesByThreadId, IReadOnlyDictionary<string, long> seenTicksByThreadId) {
        ArgumentNullException.ThrowIfNull(namesByThreadId);
        ArgumentNullException.ThrowIfNull(seenTicksByThreadId);

        lock (_toolRoutingContextLock) {
            _lastWeightedToolNamesByThreadId.Clear();
            _lastWeightedToolSubsetSeenUtcTicks.Clear();

            foreach (var pair in namesByThreadId) {
                var threadId = (pair.Key ?? string.Empty).Trim();
                if (threadId.Length == 0) {
                    continue;
                }

                var names = pair.Value ?? Array.Empty<string>();
                var namesClone = new string[names.Length];
                if (names.Length > 0) {
                    Array.Copy(names, namesClone, names.Length);
                }

                _lastWeightedToolNamesByThreadId[threadId] = namesClone;
            }

            foreach (var pair in seenTicksByThreadId) {
                var threadId = (pair.Key ?? string.Empty).Trim();
                if (threadId.Length == 0 || !_lastWeightedToolNamesByThreadId.ContainsKey(threadId)) {
                    continue;
                }

                _lastWeightedToolSubsetSeenUtcTicks[threadId] = pair.Value;
            }
        }
    }

    internal void SetPreferredDomainIntentFamilyForTesting(string threadId, string family, long? seenUtcTicks = null) {
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        var normalizedFamily = (family ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0 || normalizedFamily.Length == 0) {
            return;
        }

        lock (_toolRoutingContextLock) {
            _domainIntentFamilyByThreadId[normalizedThreadId] = normalizedFamily;
            _domainIntentFamilySeenUtcTicks[normalizedThreadId] = seenUtcTicks ?? DateTime.UtcNow.Ticks;
            TrimWeightedRoutingContextsNoLock();
        }
    }

    internal string? GetPreferredDomainIntentFamilyForTesting(string threadId) {
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0) {
            return null;
        }

        lock (_toolRoutingContextLock) {
            return _domainIntentFamilyByThreadId.TryGetValue(normalizedThreadId, out var family)
                ? family
                : null;
        }
    }

    internal bool TryApplyDomainIntentAffinityForTesting(
        string threadId,
        IReadOnlyList<ToolDefinition> selectedTools,
        out IReadOnlyList<ToolDefinition> filteredTools,
        out string family,
        out int removedCount) {
        return TryApplyDomainIntentAffinity(threadId, selectedTools, out filteredTools, out family, out removedCount);
    }

    internal void RememberPreferredDomainIntentFamilyForTesting(
        string threadId,
        IReadOnlyList<ToolCallDto> toolCalls,
        IReadOnlyList<ToolOutputDto> toolOutputs,
        IReadOnlyDictionary<string, bool> mutatingToolHintsByName) {
        RememberPreferredDomainIntentFamily(threadId, toolCalls, toolOutputs, mutatingToolHintsByName);
    }

    internal IReadOnlyCollection<string> GetTrackedToolRoutingStatNamesForTesting() {
        lock (_toolRoutingStatsLock) {
            return _toolRoutingStats.Keys.ToArray();
        }
    }

    internal IReadOnlyCollection<string> GetTrackedWeightedRoutingContextThreadIdsForTesting() {
        lock (_toolRoutingContextLock) {
            return _lastWeightedToolNamesByThreadId.Keys.ToArray();
        }
    }

    internal void TrimToolRoutingStatsForTesting() {
        lock (_toolRoutingStatsLock) {
            TrimToolRoutingStatsNoLock();
        }
    }

    internal void TrimWeightedRoutingContextsForTesting() {
        lock (_toolRoutingContextLock) {
            TrimWeightedRoutingContextsNoLock();
        }
    }

}
