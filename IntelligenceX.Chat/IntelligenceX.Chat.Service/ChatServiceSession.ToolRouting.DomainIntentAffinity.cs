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

        if (!TryNormalizeDomainIntentFamily(preferredFamily, out var normalizedFamily)) {
            return false;
        }

        var filtered = new List<ToolDefinition>(selectedTools.Count);
        for (var i = 0; i < selectedTools.Count; i++) {
            var tool = selectedTools[i];
            var toolName = (tool.Name ?? string.Empty).Trim();
            if (toolName.Length == 0) {
                continue;
            }

            var candidateFamily = ResolveDomainIntentFamily(tool);
            if (candidateFamily.Length > 0
                && !string.Equals(candidateFamily, normalizedFamily, StringComparison.Ordinal)) {
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
        bool contextAwareBudgetApplied,
        string? domainIntentSource,
        string? domainIntentFamily,
        bool weightedAmbiguityWidened,
        int? weightedAmbiguityBaselineSelection,
        int? weightedAmbiguityEffectiveSelection,
        int? weightedAmbiguityClusterSize,
        double? weightedAmbiguitySecondScoreRatio) {
        var (selected, total) = NormalizeRoutingToolCounts(selectedToolCount, totalToolCount);
        var normalizedContextLength = effectiveContextLength is > 0 ? effectiveContextLength : null;
        var normalizedDomainIntentSource = NormalizeRoutingDomainIntentSource(domainIntentSource);
        var normalizedDomainIntentFamily = TryNormalizeDomainIntentFamily(domainIntentFamily, out var parsedDomainIntentFamily)
            ? parsedDomainIntentFamily
            : string.Empty;
        var normalizedWeightedAmbiguityBaseline = weightedAmbiguityWidened && weightedAmbiguityBaselineSelection is > 0
            ? weightedAmbiguityBaselineSelection
            : null;
        var normalizedWeightedAmbiguityEffective = weightedAmbiguityWidened && weightedAmbiguityEffectiveSelection is > 0
            ? weightedAmbiguityEffectiveSelection
            : null;
        var normalizedWeightedAmbiguityCluster = weightedAmbiguityWidened && weightedAmbiguityClusterSize is > 0
            ? weightedAmbiguityClusterSize
            : null;
        var normalizedWeightedAmbiguitySecondRatio = weightedAmbiguityWidened && weightedAmbiguitySecondScoreRatio is > 0d
            ? (double?)Math.Round(weightedAmbiguitySecondScoreRatio.Value, 3)
            : null;
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
            },
            domainIntent = new {
                source = normalizedDomainIntentSource.Length > 0 ? normalizedDomainIntentSource : null,
                family = normalizedDomainIntentFamily.Length > 0 ? normalizedDomainIntentFamily : null
            },
            weightedAmbiguity = new {
                widened = weightedAmbiguityWidened,
                baselineSelection = normalizedWeightedAmbiguityBaseline,
                effectiveSelection = normalizedWeightedAmbiguityEffective,
                clusterSize = normalizedWeightedAmbiguityCluster,
                secondScoreRatio = normalizedWeightedAmbiguitySecondRatio
            }
        });
    }

    private static bool TryResolveWeightedRoutingAmbiguityTelemetry(
        IReadOnlyList<ToolRoutingInsight> insights,
        out int baselineSelection,
        out int effectiveSelection,
        out int clusterSize,
        out double secondScoreRatio) {
        baselineSelection = 0;
        effectiveSelection = 0;
        clusterSize = 0;
        secondScoreRatio = 0d;
        if (insights is null || insights.Count == 0) {
            return false;
        }

        for (var i = 0; i < insights.Count; i++) {
            var reason = (insights[i].Reason ?? string.Empty).Trim();
            if (reason.Length == 0) {
                continue;
            }

            if (!TryParseWeightedRoutingAmbiguityMarker(reason, out baselineSelection, out effectiveSelection, out clusterSize, out secondScoreRatio)) {
                continue;
            }

            return baselineSelection > 0
                   && effectiveSelection > baselineSelection
                   && clusterSize > 0
                   && secondScoreRatio > 0d;
        }

        return false;
    }

    private static bool TryParseWeightedRoutingAmbiguityMarker(
        string reason,
        out int baselineSelection,
        out int effectiveSelection,
        out int clusterSize,
        out double secondScoreRatio) {
        baselineSelection = 0;
        effectiveSelection = 0;
        clusterSize = 0;
        secondScoreRatio = 0d;

        var markerIndex = reason.IndexOf(WeightedRoutingAmbiguityMarker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0) {
            return false;
        }

        var markerTail = reason[(markerIndex + WeightedRoutingAmbiguityMarker.Length)..].Trim();
        if (markerTail.Length == 0) {
            return false;
        }

        var segments = markerTail.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var i = 0; i < segments.Length; i++) {
            var segment = segments[i];
            var separator = segment.IndexOf('=');
            if (separator <= 0 || separator >= segment.Length - 1) {
                continue;
            }

            var key = segment[..separator].Trim();
            var value = segment[(separator + 1)..].Trim().TrimEnd(',', ';');
            if (key.Length == 0 || value.Length == 0) {
                continue;
            }

            if (string.Equals(key, "baseline", StringComparison.OrdinalIgnoreCase)) {
                if (int.TryParse(value, out var parsed) && parsed > 0) {
                    baselineSelection = parsed;
                }
                continue;
            }

            if (string.Equals(key, "effective", StringComparison.OrdinalIgnoreCase)) {
                if (int.TryParse(value, out var parsed) && parsed > 0) {
                    effectiveSelection = parsed;
                }
                continue;
            }

            if (string.Equals(key, "cluster", StringComparison.OrdinalIgnoreCase)) {
                if (int.TryParse(value, out var parsed) && parsed > 0) {
                    clusterSize = parsed;
                }
                continue;
            }

            if (string.Equals(key, "second_ratio", StringComparison.OrdinalIgnoreCase)) {
                if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed) && parsed > 0d) {
                    secondScoreRatio = parsed;
                }
            }
        }

        return baselineSelection > 0
               && effectiveSelection > 0
               && clusterSize > 0
               && secondScoreRatio > 0d;
    }

    private static string NormalizeRoutingDomainIntentSource(string? source) {
        var normalized = NormalizeCompactToken((source ?? string.Empty).AsSpan());
        return normalized switch {
            "signalhint" => "signal_hint",
            "domainsignalhint" => "signal_hint",
            "affinity" => "affinity",
            "domainfamilyaffinity" => "affinity",
            _ => string.Empty
        };
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

    private static string ResolveRoutingInsightStrategyLabel(ToolRoutingInsightStrategy strategy) {
        return strategy switch {
            ToolRoutingInsightStrategy.WeightedHeuristic => "weighted_heuristic",
            ToolRoutingInsightStrategy.ContinuationSubset => "continuation_subset",
            ToolRoutingInsightStrategy.SemanticPlanner => "semantic_planner",
            _ => string.Empty
        };
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
            return ResolveRoutingInsightStrategyLabel(ToolRoutingInsightStrategy.ContinuationSubset);
        }

        if (reason.IndexOf("semantic planner", StringComparison.OrdinalIgnoreCase) >= 0) {
            return ResolveRoutingInsightStrategyLabel(ToolRoutingInsightStrategy.SemanticPlanner);
        }

        return defaultStrategy;
    }

    private static bool TryResolveRoutingInsightStrategyFromStructuredHint(ToolRoutingInsight insight, out string strategy) {
        strategy = ResolveRoutingInsightStrategyLabel(insight.Strategy);
        return strategy.Length > 0;
    }

    private bool TryGetContinuationToolSubset(string threadId, string userRequest, IReadOnlyList<ToolDefinition> allDefinitions,
        out IReadOnlyList<ToolDefinition> subset) {
        subset = Array.Empty<ToolDefinition>();
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0) {
            return false;
        }

        var hasCheckpoint = TryGetWorkingMemoryCheckpoint(normalizedThreadId, out var checkpoint);
        var compactContinuationFollowUp = LooksLikeContinuationFollowUp(userRequest);
        var focusedQuestionFollowUp = hasCheckpoint && LooksLikeWorkingMemoryFocusedQuestionFollowUp(userRequest, checkpoint);
        if (!compactContinuationFollowUp && !focusedQuestionFollowUp) {
            return false;
        }

        if (hasCheckpoint
            && compactContinuationFollowUp
            && checkpoint.PriorAnswerPlanPreferCachedEvidenceReuse
            && checkpoint.PriorAnswerPlanUnresolvedNow.Length == 0) {
            return false;
        }

        string[]? previousNames;
        long seenUtcTicks;
        List<ToolDefinition>? selected = null;
        lock (_toolRoutingContextLock) {
            _lastWeightedToolNamesByThreadId.TryGetValue(normalizedThreadId, out previousNames);
            seenUtcTicks = _lastWeightedToolSubsetSeenUtcTicks.TryGetValue(normalizedThreadId, out var ticks) ? ticks : 0;
        }

        if (previousNames is null || previousNames.Length == 0) {
            if (!TryLoadWeightedToolSubsetSnapshot(normalizedThreadId, out seenUtcTicks, out var persistedNames)
                || persistedNames.Length == 0) {
                if (!TryGetContinuationToolSubsetFromCapabilitySnapshot(
                        normalizedThreadId,
                        allDefinitions,
                        out var capabilitySubset,
                        out var capabilityToolNames,
                        out var capabilitySeenUtcTicks)
                    || capabilitySubset.Count < 2
                    || capabilityToolNames.Length < 2) {
                    return false;
                }

                selected = new List<ToolDefinition>(capabilitySubset);
                previousNames = capabilityToolNames;
                seenUtcTicks = capabilitySeenUtcTicks;
            } else {
                previousNames = persistedNames;
                lock (_toolRoutingContextLock) {
                    _lastWeightedToolNamesByThreadId[normalizedThreadId] = previousNames;
                    _lastWeightedToolSubsetSeenUtcTicks[normalizedThreadId] = seenUtcTicks;
                    TrimWeightedRoutingContextsNoLock();
                }
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

            if (!TryGetContinuationToolSubsetFromCapabilitySnapshot(
                    normalizedThreadId,
                    allDefinitions,
                    out var capabilitySubset,
                    out var capabilityToolNames,
                    out var capabilitySeenUtcTicks)
                || capabilitySubset.Count < 2
                || capabilityToolNames.Length < 2) {
                return false;
            }

            selected = new List<ToolDefinition>(capabilitySubset);
            previousNames = capabilityToolNames;
            seenUtcTicks = capabilitySeenUtcTicks;
        }

        if (selected is null) {
            var preferredNamesSet = new HashSet<string>(previousNames!, StringComparer.OrdinalIgnoreCase);
            selected = new List<ToolDefinition>();
            for (var i = 0; i < allDefinitions.Count; i++) {
                var definition = allDefinitions[i];
                if (preferredNamesSet.Contains(definition.Name)) {
                    selected.Add(definition);
                }
            }
        }

        if (selected.Count < 2) {
            if (!TryGetContinuationToolSubsetFromCapabilitySnapshot(
                    normalizedThreadId,
                    allDefinitions,
                    out var capabilitySubset,
                    out var capabilityToolNames,
                    out var capabilitySeenUtcTicks)
                || capabilitySubset.Count < 2
                || capabilityToolNames.Length < 2) {
                return false;
            }

            selected = new List<ToolDefinition>(capabilitySubset);
            previousNames = capabilityToolNames;
            seenUtcTicks = capabilitySeenUtcTicks;
        }

        if (ShouldBypassContinuationSubsetForFollowUpQuestion(userRequest, focusedQuestionFollowUp)) {
            return false;
        }

        var preferred = new HashSet<string>(previousNames!, StringComparer.OrdinalIgnoreCase);
        if (ReferencesToolOutsideContinuationSubset(userRequest, allDefinitions, preferred)) {
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

    private bool TryGetContinuationToolSubsetFromCapabilitySnapshot(
        string threadId,
        IReadOnlyList<ToolDefinition> allDefinitions,
        out IReadOnlyList<ToolDefinition> subset,
        out string[] selectedToolNames,
        out long seenUtcTicks) {
        subset = Array.Empty<ToolDefinition>();
        selectedToolNames = Array.Empty<string>();
        seenUtcTicks = 0;
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0
            || allDefinitions.Count == 0
            || !TryGetWorkingMemoryCheckpoint(normalizedThreadId, out var checkpoint)) {
            return false;
        }

        var nowUtc = DateTime.UtcNow;
        if (!TryGetUtcDateTimeFromTicks(checkpoint.SeenUtcTicks, out var checkpointSeenUtc)
            || checkpointSeenUtc > nowUtc
            || nowUtc - checkpointSeenUtc > UserIntentContextMaxAge) {
            return false;
        }

        var preferredNames = checkpoint.CapabilityHealthyToolNames.Length > 0
            ? checkpoint.CapabilityHealthyToolNames
            : checkpoint.RecentToolNames;
        if (preferredNames.Length == 0) {
            return false;
        }

        var preferredSet = new HashSet<string>(preferredNames, StringComparer.OrdinalIgnoreCase);
        var enabledPackIds = new HashSet<string>(
            checkpoint.CapabilityEnabledPackIds
                .Select(static packId => NormalizePackId(packId))
                .Where(static packId => packId.Length > 0),
            StringComparer.OrdinalIgnoreCase);
        var routingFamilies = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < checkpoint.CapabilityRoutingFamilies.Length; i++) {
            if (TryNormalizeDomainIntentFamily(checkpoint.CapabilityRoutingFamilies[i], out var normalizedFamily)) {
                routingFamilies.Add(normalizedFamily);
            }
        }
        var selected = new List<ToolDefinition>(Math.Min(8, preferredSet.Count));
        var selectedNames = new List<string>(Math.Min(8, preferredSet.Count));

        for (var i = 0; i < allDefinitions.Count; i++) {
            var definition = allDefinitions[i];
            var toolName = (definition.Name ?? string.Empty).Trim();
            if (toolName.Length == 0 || !preferredSet.Contains(toolName)) {
                continue;
            }

            if (routingFamilies.Count > 0) {
                var family = ResolveDomainIntentFamily(definition);
                if (family.Length > 0 && !routingFamilies.Contains(family)) {
                    continue;
                }
            }

            if (enabledPackIds.Count > 0) {
                var packId = ResolveToolPackIdForContinuationCapability(definition);
                if (packId.Length > 0 && !enabledPackIds.Contains(packId)) {
                    continue;
                }
            }

            selected.Add(definition);
            selectedNames.Add(toolName);
        }

        if (selected.Count < 2) {
            return false;
        }

        subset = selected;
        selectedToolNames = selectedNames.ToArray();
        seenUtcTicks = checkpoint.SeenUtcTicks;
        return true;
    }

    private string ResolveToolPackIdForContinuationCapability(ToolDefinition definition) {
        if (definition is null) {
            return string.Empty;
        }

        var routingPackId = NormalizePackId(definition.Routing?.PackId);
        if (routingPackId.Length > 0) {
            return routingPackId;
        }

        var toolName = (definition.Name ?? string.Empty).Trim();
        if (toolName.Length == 0) {
            return string.Empty;
        }

        if (_toolOrchestrationCatalog.TryGetEntry(toolName, out var catalogEntry)) {
            return NormalizePackId(catalogEntry.PackId);
        }

        return string.Empty;
    }

    private static bool ReferencesToolOutsideContinuationSubset(
        string userRequest,
        IReadOnlyList<ToolDefinition> allDefinitions,
        IReadOnlySet<string> preferredToolNames) {
        var normalizedRequest = NormalizeRequestForExplicitToolReferenceMatch(userRequest);
        if (normalizedRequest.Length == 0 || allDefinitions is null || allDefinitions.Count == 0) {
            return false;
        }

        for (var i = 0; i < allDefinitions.Count; i++) {
            var toolName = (allDefinitions[i].Name ?? string.Empty).Trim();
            if (toolName.Length == 0 || preferredToolNames.Contains(toolName)) {
                continue;
            }

            if (ContainsExplicitToolNameReference(normalizedRequest, toolName)) {
                return true;
            }
        }

        return false;
    }

    private static bool ShouldBypassContinuationSubsetForFollowUpQuestion(string userRequest, bool focusedQuestionFollowUp) {
        if (focusedQuestionFollowUp) {
            return false;
        }

        var request = NormalizeRoutingUserText((userRequest ?? string.Empty).Trim());
        if (request.Length == 0 || !ContainsQuestionSignal(request) || LooksLikeActionSelectionPayload(request)) {
            return false;
        }

        var tokenCount = CountLetterDigitTokens(request, maxTokens: 24);
        if (tokenCount == 0) {
            return false;
        }

        // Keep short acknowledgement questions (for example "go ahead?") on fast subset reuse.
        return !(tokenCount <= 2 && request.Length <= FollowUpShapeShortCharLimit);
    }

    private static string NormalizeRequestForExplicitToolReferenceMatch(string? userRequest) {
        var normalized = NormalizeRoutingUserText((userRequest ?? string.Empty).Trim());
        if (normalized.Length == 0) {
            return string.Empty;
        }

        return normalized
            .Replace("\\_", "_", StringComparison.Ordinal)
            .Replace("\\-", "-", StringComparison.Ordinal);
    }

    private static bool ContainsExplicitToolNameReference(string normalizedRequest, string toolName) {
        if (normalizedRequest.Length == 0 || string.IsNullOrWhiteSpace(toolName)) {
            return false;
        }

        var normalizedToolName = toolName.Trim();
        var start = 0;
        while (start < normalizedRequest.Length) {
            var index = normalizedRequest.IndexOf(normalizedToolName, start, StringComparison.OrdinalIgnoreCase);
            if (index < 0) {
                return false;
            }

            var hasLeftBoundary = index == 0
                                  || !IsToolNameTokenChar(normalizedRequest[index - 1]);
            var rightIndex = index + normalizedToolName.Length;
            var hasRightBoundary = rightIndex >= normalizedRequest.Length
                                   || !IsToolNameTokenChar(normalizedRequest[rightIndex]);
            if (hasLeftBoundary && hasRightBoundary) {
                return true;
            }

            start = index + normalizedToolName.Length;
        }

        return false;
    }

    private static bool IsToolNameTokenChar(char value) {
        return char.IsLetterOrDigit(value)
               || value == '_'
               || value == '-';
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

        ClearPendingActionsContext(normalizedThreadId);
        RemoveStructuredNextActionCarryover(normalizedThreadId);
        if (!TryResolveDomainIntentFamilyFromUserSignals(normalized, _registry.GetDefinitions(), out _)) {
            ClearPreferredDomainIntentFamily(normalizedThreadId);
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
