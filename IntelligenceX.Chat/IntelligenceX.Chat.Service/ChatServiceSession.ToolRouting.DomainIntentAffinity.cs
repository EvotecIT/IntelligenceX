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
    private bool TryGetCurrentDomainIntentFamily(string threadId, out string family) {
        family = string.Empty;
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0) {
            return false;
        }

        var nowUtc = DateTime.UtcNow;
        lock (_toolRoutingContextLock) {
            if (_domainIntentFamilyByThreadId.TryGetValue(normalizedThreadId, out var cachedFamily)
                && !string.IsNullOrWhiteSpace(cachedFamily)) {
                if (_domainIntentFamilySeenUtcTicks.TryGetValue(normalizedThreadId, out var seenTicks)
                    && TryGetUtcDateTimeFromTicks(seenTicks, out var seenUtc)
                    && nowUtc - seenUtc <= DomainIntentFamilyContextMaxAge) {
                    family = cachedFamily;
                    _domainIntentFamilySeenUtcTicks[normalizedThreadId] = nowUtc.Ticks;
                    TrimWeightedRoutingContextsNoLock();
                    return true;
                }

                _domainIntentFamilyByThreadId.Remove(normalizedThreadId);
                _domainIntentFamilySeenUtcTicks.Remove(normalizedThreadId);
            }
        }

        if (!TryLoadDomainIntentFamilySnapshot(normalizedThreadId, out var snapshotFamily, out _)) {
            return false;
        }

        family = snapshotFamily;
        lock (_toolRoutingContextLock) {
            _domainIntentFamilyByThreadId[normalizedThreadId] = family;
            _domainIntentFamilySeenUtcTicks[normalizedThreadId] = nowUtc.Ticks;
            TrimWeightedRoutingContextsNoLock();
        }

        return true;
    }

    private static bool TryFilterToolsByDomainIntentFamily(
        IReadOnlyList<ToolDefinition> selectedTools,
        string preferredFamily,
        out IReadOnlyList<ToolDefinition> filteredTools,
        out int removedCount) {
        filteredTools = selectedTools;
        removedCount = 0;
        if (selectedTools is null || selectedTools.Count == 0 || string.IsNullOrWhiteSpace(preferredFamily)) {
            return false;
        }

        var normalizedFamily = string.Equals(preferredFamily, DomainIntentFamilyPublic, StringComparison.Ordinal)
            ? DomainIntentFamilyPublic
            : DomainIntentFamilyAd;
        var filtered = new List<ToolDefinition>(selectedTools.Count);
        for (var i = 0; i < selectedTools.Count; i++) {
            var tool = selectedTools[i];
            var toolName = (tool.Name ?? string.Empty).Trim();
            if (toolName.Length == 0) {
                continue;
            }

            var candidateFamily = ResolveDomainIntentFamily(tool);
            if (string.Equals(normalizedFamily, DomainIntentFamilyAd, StringComparison.Ordinal)
                && string.Equals(candidateFamily, DomainIntentFamilyPublic, StringComparison.Ordinal)) {
                removedCount++;
                continue;
            }

            if (string.Equals(normalizedFamily, DomainIntentFamilyPublic, StringComparison.Ordinal)
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
        RememberSelectedDomainIntentFamily(normalizedThreadId, nextFamily);
    }

    private void RememberSelectedDomainIntentFamily(string threadId, string family) {
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        var normalizedFamily = (family ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0 || !IsSupportedDomainIntentFamily(normalizedFamily)) {
            return;
        }

        var nowTicks = DateTime.UtcNow.Ticks;
        lock (_toolRoutingContextLock) {
            _pendingDomainIntentClarificationSeenUtcTicks.Remove(normalizedThreadId);
            _domainIntentFamilyByThreadId[normalizedThreadId] = normalizedFamily;
            _domainIntentFamilySeenUtcTicks[normalizedThreadId] = nowTicks;
            TrimWeightedRoutingContextsNoLock();
        }

        RemovePendingDomainIntentClarificationSnapshot(normalizedThreadId);
        PersistDomainIntentFamilySnapshot(normalizedThreadId, normalizedFamily, nowTicks);
    }

    private void ClearPreferredDomainIntentFamily(string threadId) {
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0) {
            return;
        }

        var removed = false;
        var removedClarification = false;
        lock (_toolRoutingContextLock) {
            removedClarification = _pendingDomainIntentClarificationSeenUtcTicks.Remove(normalizedThreadId);
            removed = _domainIntentFamilyByThreadId.Remove(normalizedThreadId) || removed;
            removed = _domainIntentFamilySeenUtcTicks.Remove(normalizedThreadId) || removed;
            if (removed || removedClarification) {
                TrimWeightedRoutingContextsNoLock();
            }
        }

        if (removed) {
            RemoveDomainIntentFamilySnapshot(normalizedThreadId);
        }
        if (removedClarification) {
            RemovePendingDomainIntentClarificationSnapshot(normalizedThreadId);
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
            if (insights[i].Strategy == ToolRoutingInsightStrategy.SemanticPlanner) {
                return true;
            }

            var reason = insights[i].Reason ?? string.Empty;
            if (reason.IndexOf("semantic planner", StringComparison.OrdinalIgnoreCase) >= 0) {
                return true;
            }
        }

        return false;
    }

    private static string ResolveRoutingInsightStrategy(ToolRoutingInsight insight, string defaultStrategy) {
        if (TryResolveRoutingInsightStrategyFromStructuredHint(insight, out var structuredStrategy)) {
            return structuredStrategy;
        }

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

    private static bool TryResolveRoutingInsightStrategyFromStructuredHint(ToolRoutingInsight insight, out string strategy) {
        strategy = insight.Strategy switch {
            ToolRoutingInsightStrategy.WeightedHeuristic => "weighted_heuristic",
            ToolRoutingInsightStrategy.ContinuationSubset => "continuation_subset",
            ToolRoutingInsightStrategy.SemanticPlanner => "semantic_planner",
            _ => string.Empty
        };
        return strategy.Length > 0;
    }

    private bool TryGetContinuationToolSubset(string threadId, string userRequest, IReadOnlyList<ToolDefinition> allDefinitions,
        out IReadOnlyList<ToolDefinition> subset) {
        subset = Array.Empty<ToolDefinition>();
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0 || !LooksLikeContinuationFollowUp(userRequest)) {
            return false;
        }

        string[]? previousNames;
        long seenUtcTicks;
        lock (_toolRoutingContextLock) {
            _lastWeightedToolNamesByThreadId.TryGetValue(normalizedThreadId, out previousNames);
            seenUtcTicks = _lastWeightedToolSubsetSeenUtcTicks.TryGetValue(normalizedThreadId, out var ticks) ? ticks : 0;
        }

        if (previousNames is null || previousNames.Length == 0) {
            if (!TryLoadWeightedToolSubsetSnapshot(normalizedThreadId, out seenUtcTicks, out var persistedNames)
                || persistedNames.Length == 0) {
                return false;
            }

            previousNames = persistedNames;
            lock (_toolRoutingContextLock) {
                _lastWeightedToolNamesByThreadId[normalizedThreadId] = previousNames;
                _lastWeightedToolSubsetSeenUtcTicks[normalizedThreadId] = seenUtcTicks;
                TrimWeightedRoutingContextsNoLock();
            }
        }

        if (!TryGetUtcDateTimeFromTicks(seenUtcTicks, out var seenUtc)
            || seenUtc > DateTime.UtcNow
            || DateTime.UtcNow - seenUtc > UserIntentContextMaxAge) {
            lock (_toolRoutingContextLock) {
                _lastWeightedToolNamesByThreadId.Remove(normalizedThreadId);
                _lastWeightedToolSubsetSeenUtcTicks.Remove(normalizedThreadId);
                TrimWeightedRoutingContextsNoLock();
            }
            RemoveWeightedToolSubsetSnapshot(normalizedThreadId);
            return false;
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

        var refreshedTicks = DateTime.UtcNow.Ticks;
        lock (_toolRoutingContextLock) {
            _lastWeightedToolSubsetSeenUtcTicks[normalizedThreadId] = refreshedTicks;
            TrimWeightedRoutingContextsNoLock();
        }
        PersistWeightedToolSubsetSnapshot(normalizedThreadId, refreshedTicks, previousNames!);

        subset = selected;
        return true;
    }

    private void RememberWeightedToolSubset(string threadId, IReadOnlyList<ToolDefinition> selectedDefinitions, int allToolCount) {
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0) {
            return;
        }

        long seenUtcTicks = 0;
        string[] namesSnapshot = Array.Empty<string>();
        var removeSnapshot = false;
        lock (_toolRoutingContextLock) {
            if (selectedDefinitions.Count == 0 || selectedDefinitions.Count >= allToolCount) {
                _lastWeightedToolNamesByThreadId.Remove(normalizedThreadId);
                _lastWeightedToolSubsetSeenUtcTicks.Remove(normalizedThreadId);
                removeSnapshot = true;
            } else {
                var names = new List<string>(selectedDefinitions.Count);
                for (var i = 0; i < selectedDefinitions.Count && i < 64; i++) {
                    var name = (selectedDefinitions[i].Name ?? string.Empty).Trim();
                    if (name.Length > 0) {
                        names.Add(name);
                    }
                }

                namesSnapshot = names.Count == 0 ? Array.Empty<string>() : names.ToArray();
                seenUtcTicks = DateTime.UtcNow.Ticks;
                _lastWeightedToolNamesByThreadId[normalizedThreadId] = namesSnapshot;
                _lastWeightedToolSubsetSeenUtcTicks[normalizedThreadId] = seenUtcTicks;
                TrimWeightedRoutingContextsNoLock();
            }
        }

        if (removeSnapshot) {
            RemoveWeightedToolSubsetSnapshot(normalizedThreadId);
            return;
        }

        if (namesSnapshot.Length > 0 && seenUtcTicks > 0) {
            PersistWeightedToolSubsetSnapshot(normalizedThreadId, seenUtcTicks, namesSnapshot);
        }
    }

    private void RememberUserIntent(string threadId, string userRequest) {
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0) {
            return;
        }

        var normalized = (userRequest ?? string.Empty).Trim();
        if (normalized.Length == 0
            || LooksLikeContinuationFollowUp(normalized)
            || LooksLikeStructuredIntentPayload(normalized)) {
            return;
        }

        if (normalized.Length > 600) {
            normalized = normalized.Substring(0, 600);
        }

        var seenUtcTicks = DateTime.UtcNow.Ticks;
        lock (_toolRoutingContextLock) {
            _lastUserIntentByThreadId[normalizedThreadId] = normalized;
            _lastUserIntentSeenUtcTicks[normalizedThreadId] = seenUtcTicks;
            TrimWeightedRoutingContextsNoLock();
        }
        PersistUserIntentSnapshot(normalizedThreadId, normalized, seenUtcTicks);
    }

}
