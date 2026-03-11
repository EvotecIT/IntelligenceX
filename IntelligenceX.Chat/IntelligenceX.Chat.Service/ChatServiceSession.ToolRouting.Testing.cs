using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.OpenAI.ToolCalling;
using IntelligenceX.Tools;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
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

    internal void PersistToolRoutingStatsForTesting() {
        PersistToolRoutingStatsSnapshot();
    }

    internal void UpdateToolRoutingStatsForTesting(IReadOnlyList<ToolCall> calls, IReadOnlyList<ToolOutputDto> outputs) {
        ArgumentNullException.ThrowIfNull(calls);
        ArgumentNullException.ThrowIfNull(outputs);
        UpdateToolRoutingStats(calls, outputs);
    }

    internal void SetCapabilitySnapshotContextForTesting(
        IReadOnlyList<ToolPackAvailabilityInfo> packAvailability,
        ToolRoutingCatalogDiagnostics routingCatalogDiagnostics,
        IReadOnlyList<ToolPluginAvailabilityInfo>? pluginAvailability = null,
        IReadOnlyList<string>? connectedRuntimeSkills = null) {
        ArgumentNullException.ThrowIfNull(packAvailability);
        ArgumentNullException.ThrowIfNull(routingCatalogDiagnostics);

        _packAvailability = packAvailability.ToArray();
        _pluginAvailability = pluginAvailability?.ToArray() ?? Array.Empty<ToolPluginAvailabilityInfo>();
        _connectedRuntimeSkillInventory = NormalizeSkillInventoryValues(connectedRuntimeSkills ?? Array.Empty<string>(), maxItems: 0);
        _connectedRuntimeSkillInventoryHydrated = _connectedRuntimeSkillInventory.Length > 0;
        _routingCatalogDiagnostics = routingCatalogDiagnostics;
    }

    internal string? BuildTurnInstructionsWithRuntimeIdentityForTesting(string resolvedModel, string? baseInstructions = null) {
        var previousInstructions = _instructions;
        _instructions = baseInstructions;
        try {
            return BuildTurnInstructionsWithRuntimeIdentity(resolvedModel);
        } finally {
            _instructions = previousInstructions;
        }
    }

    internal string[] BuildHelloStartupWarningsForTesting(Task startupToolingBootstrapTask) {
        ArgumentNullException.ThrowIfNull(startupToolingBootstrapTask);
        return BuildHelloStartupWarnings(startupToolingBootstrapTask);
    }

    internal SessionCapabilitySnapshotDto BuildRuntimeCapabilitySnapshotForTesting() {
        return BuildRuntimeCapabilitySnapshot();
    }

    internal string ExpandContinuationUserRequestForTesting(string threadId, string userRequest, bool forceContinuationFollowUp = false) {
        ArgumentNullException.ThrowIfNull(threadId);
        ArgumentNullException.ThrowIfNull(userRequest);
        return ExpandContinuationUserRequestWithOptions(threadId, userRequest, forceContinuationFollowUp);
    }

    internal bool TryGetContinuationToolSubsetForTesting(
        string threadId,
        string userRequest,
        IReadOnlyList<ToolDefinition> allDefinitions,
        out IReadOnlyList<ToolDefinition> subset) {
        ArgumentNullException.ThrowIfNull(threadId);
        ArgumentNullException.ThrowIfNull(userRequest);
        ArgumentNullException.ThrowIfNull(allDefinitions);
        return TryGetContinuationToolSubset(threadId, userRequest, allDefinitions, out subset);
    }

    internal string BuildPlannerContextAugmentedRequestForTesting(
        string threadId,
        string requestText,
        IReadOnlyList<ToolDefinition> definitions) {
        ArgumentNullException.ThrowIfNull(threadId);
        ArgumentNullException.ThrowIfNull(requestText);
        ArgumentNullException.ThrowIfNull(definitions);
        return BuildPlannerContextAugmentedRequest(threadId, requestText, definitions);
    }

    internal static bool TryReadPlannerContextFromRequestTextForTesting(
        string requestText,
        out bool requiresLiveExecution,
        out string missingLiveEvidence,
        out string[] preferredPackIds,
        out string[] preferredToolNames,
        out string[] preferredExecutionBackends,
        out string[] handoffTargetPackIds,
        out string[] handoffTargetToolNames,
        out string continuationSourceTool,
        out string continuationReason,
        out string continuationConfidence,
        out string[] matchingSkills,
        out bool allowCachedEvidenceReuse) {
        var found = TryReadPlannerContextFromRequestText(requestText, out var context);
        requiresLiveExecution = context.RequiresLiveExecution;
        missingLiveEvidence = context.MissingLiveEvidence;
        preferredPackIds = context.PreferredPackIds;
        preferredToolNames = context.PreferredToolNames;
        preferredExecutionBackends = context.PreferredExecutionBackends;
        handoffTargetPackIds = context.HandoffTargetPackIds;
        handoffTargetToolNames = context.HandoffTargetToolNames;
        continuationSourceTool = context.ContinuationSourceTool;
        continuationReason = context.ContinuationReason;
        continuationConfidence = context.ContinuationConfidence;
        matchingSkills = context.MatchingSkills;
        allowCachedEvidenceReuse = context.AllowCachedEvidenceReuse;
        return found;
    }
    internal (
        string UserRequest,
        string UserIntent,
        string RoutedUserRequest,
        bool ContinuationExpandedFromContext,
        bool HasStructuredContinuationContext,
        bool ContinuationFollowUpTurn,
        bool CompactFollowUpTurn) ResolveRoutingPreludeForTesting(string threadId, string requestText) {
        ArgumentNullException.ThrowIfNull(threadId);
        ArgumentNullException.ThrowIfNull(requestText);

        var userRequest = ExtractPrimaryUserRequest(requestText);
        var userIntent = ExtractIntentUserText(requestText);
        var continuationContractDetected = TryReadContinuationContractFromRequestText(requestText, out _, out _);
        var hasFreshPendingActionContext = HasFreshPendingActionsContext(threadId);
        RememberUserIntent(threadId, userIntent);

        var routedUserRequest = ExpandContinuationUserRequestWithOptions(
            threadId,
            userRequest,
            forceContinuationFollowUp: continuationContractDetected);
        var continuationExpandedFromContext = !string.Equals(routedUserRequest, userRequest, StringComparison.Ordinal);
        if (TryAugmentRoutedUserRequestFromWorkingMemoryCheckpoint(threadId, userRequest, routedUserRequest, out var checkpointAugmentedRequest)) {
            routedUserRequest = checkpointAugmentedRequest;
            continuationExpandedFromContext = !string.Equals(routedUserRequest, userRequest, StringComparison.Ordinal);
        }

        var hasStructuredContinuationContext = continuationContractDetected
                                              || hasFreshPendingActionContext
                                              || continuationExpandedFromContext;
        var (continuationFollowUpTurn, compactFollowUpTurn) = ResolveFollowUpTurnClassification(
            continuationContractDetected,
            hasStructuredContinuationContext,
            userRequest,
            routedUserRequest);
        return (
            userRequest,
            userIntent,
            routedUserRequest,
            continuationExpandedFromContext,
            hasStructuredContinuationContext,
            continuationFollowUpTurn,
            compactFollowUpTurn);
    }

    internal void RememberUserIntentForTesting(string threadId, string userRequest) {
        ArgumentNullException.ThrowIfNull(threadId);
        ArgumentNullException.ThrowIfNull(userRequest);
        RememberUserIntent(threadId, userRequest);
    }

    internal void RememberPendingActionsForTesting(string threadId, string assistantReply) {
        ArgumentNullException.ThrowIfNull(threadId);
        ArgumentNullException.ThrowIfNull(assistantReply);
        RememberPendingActions(threadId, assistantReply);
    }

    internal static string[] SelectCachedEvidenceAskCoverageTokensForTesting(params string[] requestTokens) {
        return SelectCachedEvidenceAskCoverageTokens(requestTokens ?? Array.Empty<string>());
    }

    internal void RememberStructuredNextActionCarryoverForTesting(
        string threadId,
        IReadOnlyList<ToolDefinition> toolDefinitions,
        IReadOnlyList<ToolCallDto> toolCalls,
        IReadOnlyList<ToolOutputDto> toolOutputs,
        IReadOnlyDictionary<string, bool>? mutatingToolHintsByName) {
        ArgumentNullException.ThrowIfNull(threadId);
        ArgumentNullException.ThrowIfNull(toolDefinitions);
        ArgumentNullException.ThrowIfNull(toolCalls);
        ArgumentNullException.ThrowIfNull(toolOutputs);
        RememberStructuredNextActionCarryover(threadId, toolDefinitions, toolCalls, toolOutputs, mutatingToolHintsByName);
    }

    internal static bool TryReadNormalizedConfidenceValueForTesting(JsonElement node, string propertyName, out double confidence) {
        return TryReadNormalizedConfidenceValue(node, propertyName, out confidence);
    }

    internal static bool TryFindNormalizedConfidenceValueForTesting(JsonElement node, int maxDepth, out double confidence) {
        return TryFindNormalizedConfidenceValue(node, maxDepth, out confidence);
    }

    internal bool HasFreshPendingActionsContextForTesting(string threadId) {
        ArgumentNullException.ThrowIfNull(threadId);
        return HasFreshPendingActionsContext(threadId);
    }

    internal double ReadToolRoutingAdjustmentForTesting(string toolName) {
        return ReadToolRoutingAdjustment(toolName);
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

    internal bool TryGetCurrentDomainIntentFamilyForTesting(string threadId, out string family) {
        return TryGetCurrentDomainIntentFamily(threadId, out family);
    }

    internal bool TryApplyDomainIntentAffinityForTesting(
        string threadId,
        IReadOnlyList<ToolDefinition> selectedTools,
        out IReadOnlyList<ToolDefinition> filteredTools,
        out string family,
        out int removedCount) {
        return TryApplyDomainIntentAffinity(threadId, selectedTools, out filteredTools, out family, out removedCount);
    }

    internal bool TryApplyDomainIntentSignalRoutingHintForTesting(
        string threadId,
        string userRequest,
        IReadOnlyList<ToolDefinition> selectedTools,
        out IReadOnlyList<ToolDefinition> filteredTools,
        out string family,
        out int removedCount) {
        return TryApplyDomainIntentSignalRoutingHint(
            threadId,
            userRequest,
            selectedTools,
            selectedTools,
            out filteredTools,
            out family,
            out removedCount);
    }

    internal bool TryApplyDomainIntentSignalRoutingHintForTesting(
        string threadId,
        string userRequest,
        IReadOnlyList<ToolDefinition> selectedTools,
        IReadOnlyList<ToolDefinition> fullCandidateTools,
        out IReadOnlyList<ToolDefinition> filteredTools,
        out string family,
        out int removedCount) {
        return TryApplyDomainIntentSignalRoutingHint(
            threadId,
            userRequest,
            selectedTools,
            fullCandidateTools,
            out filteredTools,
            out family,
            out removedCount);
    }

    internal void RememberPreferredDomainIntentFamilyForTesting(
        string threadId,
        IReadOnlyList<ToolCallDto> toolCalls,
        IReadOnlyList<ToolOutputDto> toolOutputs,
        IReadOnlyDictionary<string, bool> mutatingToolHintsByName) {
        RememberPreferredDomainIntentFamily(threadId, toolCalls, toolOutputs, mutatingToolHintsByName);
    }

    internal static bool HasConflictingDomainIntentSignalsForTesting(string userRequest) {
        return HasConflictingDomainIntentSignals(userRequest);
    }

    internal static bool HasMixedTechnicalDomainIntentSignalsForTesting(
        string userRequest,
        IReadOnlyList<ToolDefinition> availableDefinitions) {
        return HasMixedTechnicalDomainIntentSignals(userRequest, availableDefinitions);
    }

    internal static bool ShouldForceDomainIntentClarificationForConflictingSignalsForTesting(
        string userRequest,
        IReadOnlyList<ToolDefinition> allDefinitions) {
        return ShouldForceDomainIntentClarificationForConflictingSignals(userRequest, allDefinitions);
    }

    internal static bool ShouldRequestDomainIntentClarificationBeforeRoutingForTesting(
        bool weightedToolRouting,
        bool executionContractApplies,
        bool compactFollowUpTurn,
        bool hasPreferredDomainIntentFamily,
        bool hasFreshPendingActionContext,
        string userRequest,
        bool hasAdFamily,
        bool hasPublicFamily,
        IReadOnlyList<ToolDefinition> availableDefinitions) {
        return ShouldRequestDomainIntentClarificationBeforeRouting(
            weightedToolRouting,
            executionContractApplies,
            compactFollowUpTurn,
            hasPreferredDomainIntentFamily,
            hasFreshPendingActionContext,
            userRequest,
            new DomainIntentFamilyAvailability(
                HasAd: hasAdFamily,
                HasPublic: hasPublicFamily,
                Families: BuildFamilies(hasAdFamily, hasPublicFamily)),
            availableDefinitions);

        static string[] BuildFamilies(bool hasAdFamily, bool hasPublicFamily) {
            var families = new List<string>(2);
            if (hasAdFamily) {
                families.Add(DomainIntentFamilyAd);
            }
            if (hasPublicFamily) {
                families.Add(DomainIntentFamilyPublic);
            }
            return families.ToArray();
        }
    }

    internal static bool ShouldSuppressDomainIntentClarificationForCompactFollowUpForTesting(
        bool compactFollowUpTurn,
        bool hasPreferredDomainIntentFamily,
        bool hasFreshPendingActionContext,
        bool conflictingDomainSignals) {
        return ShouldSuppressDomainIntentClarificationForCompactFollowUp(
            compactFollowUpTurn,
            hasPreferredDomainIntentFamily,
            hasFreshPendingActionContext,
            conflictingDomainSignals);
    }

    internal static (bool ContinuationFollowUpTurn, bool CompactFollowUpTurn) ResolveFollowUpTurnClassificationForTesting(
        bool continuationContractDetected,
        bool hasStructuredContinuationContext,
        string userRequest,
        string routedUserRequest) {
        return ResolveFollowUpTurnClassification(
            continuationContractDetected,
            hasStructuredContinuationContext,
            userRequest,
            routedUserRequest);
    }

    internal bool LooksLikeLiveRefreshFollowUpForTesting(string threadId, string userRequest) {
        return ShouldTreatFollowUpAsLiveExecutionRequest(threadId, userRequest);
    }

    internal bool TryPreferCachedEvidenceForResolvedCompactContinuationForTesting(
        string threadId,
        string userRequest,
        TurnAnswerPlan answerPlan,
        bool toolActivityDetected,
        out string text) {
        return TryPreferCachedEvidenceForResolvedCompactContinuation(
            threadId,
            userRequest,
            answerPlan,
            toolActivityDetected,
            out text);
    }

    internal bool ResolveLiveRefreshFollowUpTurnForTesting(
        string threadId,
        bool hasStructuredContinuationContext,
        bool hasFreshThreadToolEvidence,
        string userRequest) {
        return (hasStructuredContinuationContext || hasFreshThreadToolEvidence)
               && ShouldTreatFollowUpAsLiveExecutionRequest(threadId, userRequest);
    }

    internal void RememberPendingDomainIntentClarificationRequestForTesting(string threadId) {
        RememberPendingDomainIntentClarificationRequest(threadId);
    }

    internal bool TryResolvePendingDomainIntentClarificationSelectionForTesting(string threadId, string userRequest, out string family) {
        return TryResolvePendingDomainIntentClarificationSelection(threadId, userRequest, out family);
    }

    internal bool TryResolvePendingDomainIntentClarificationSelectionForTesting(
        string threadId,
        string userRequest,
        IReadOnlyList<ToolDefinition> availableDefinitions,
        out string family) {
        return TryResolvePendingDomainIntentClarificationSelection(threadId, userRequest, availableDefinitions, out family);
    }

    internal void RememberPackPreflightToolsForTesting(string threadId, IReadOnlyList<string> toolNames, long? seenUtcTicks = null) {
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0) {
            return;
        }

        var normalizedToolNames = NormalizeDistinctStrings(
            (toolNames ?? Array.Empty<string>())
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Select(static name => name.Trim()),
            MaxRememberedPackPreflightToolNames);
        if (normalizedToolNames.Length == 0) {
            return;
        }

        var resolvedSeenUtcTicks = seenUtcTicks.GetValueOrDefault(DateTime.UtcNow.Ticks);
        lock (_toolRoutingContextLock) {
            _packPreflightToolNamesByThreadId[normalizedThreadId] = normalizedToolNames;
            _packPreflightSeenUtcTicks[normalizedThreadId] = resolvedSeenUtcTicks;
            TrimWeightedRoutingContextsNoLock();
        }

        PersistPackPreflightSnapshot(normalizedThreadId, normalizedToolNames, resolvedSeenUtcTicks);
    }

    internal string[] GetRememberedPackPreflightToolsForTesting(string threadId) {
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        return SnapshotRememberedPackPreflightTools(normalizedThreadId).OrderBy(static name => name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    internal string ResolvePackPreflightStorePathForTesting() {
        return ResolvePackPreflightStorePath();
    }

    internal void RememberHostBootstrapFailureForTesting(
        string threadId,
        string toolName,
        string failureKind,
        string? errorCode = "tool_timeout",
        string? error = "Host bootstrap tool failed.",
        long? seenUtcTicks = null) {
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        var normalizedToolName = (toolName ?? string.Empty).Trim();
        var normalizedFailureKind = NormalizeHostBootstrapFailureKind(failureKind);
        if (normalizedThreadId.Length == 0 || normalizedToolName.Length == 0 || normalizedFailureKind.Length == 0) {
            return;
        }

        PersistHostBootstrapFailureSnapshot(
            normalizedThreadId,
            new HostBootstrapFailureSnapshot(
                ToolName: normalizedToolName,
                FailureKind: normalizedFailureKind,
                ErrorCode: NormalizeHostBootstrapFailureText(errorCode, maxLength: 128),
                Error: NormalizeHostBootstrapFailureText(error, maxLength: 280),
                SeenUtcTicks: seenUtcTicks.GetValueOrDefault(DateTime.UtcNow.Ticks)));
    }

    internal string[] GetRecentHostBootstrapFailureToolNamesForTesting(string threadId) {
        return SnapshotRecentHostBootstrapFailureToolNames(threadId).OrderBy(static name => name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    internal string ResolveHostBootstrapFailureStorePathForTesting() {
        return ResolveHostBootstrapFailureStorePath();
    }

    internal void RememberAlternateEngineSuccessForTesting(
        string threadId,
        string toolName,
        string engineId,
        long? seenUtcTicks = null) {
        var output = new ToolOutputDto {
            CallId = "alt-engine-success",
            Output = """{"ok":true}""",
            Ok = true
        };
        RememberAlternateEngineOutcome(threadId, toolName, engineId, output, seenUtcTicks);
    }

    internal void RememberAlternateEngineFailureForTesting(
        string threadId,
        string toolName,
        string engineId,
        string? errorCode = "transport_unavailable",
        string? error = "Alternate engine failed.",
        long? seenUtcTicks = null) {
        var output = new ToolOutputDto {
            CallId = "alt-engine-failure",
            Output = """{"ok":false}""",
            Ok = false,
            ErrorCode = errorCode,
            Error = error,
            IsTransient = true
        };
        RememberAlternateEngineOutcome(threadId, toolName, engineId, output, seenUtcTicks);
    }

    internal string ResolveAlternateEngineHealthStorePathForTesting() {
        return ResolveAlternateEngineHealthStorePath();
    }

    internal string[] OrderAlternateEngineIdsByHealthForTesting(
        string threadId,
        string toolName,
        IReadOnlyList<string> candidateEngineIds) {
        return OrderAlternateEngineIdsByHealth(threadId, toolName, candidateEngineIds);
    }

    internal static string BuildDomainIntentClarificationTextForTesting(bool hasAdFamily, bool hasPublicFamily) {
        return BuildDomainIntentClarificationText(new DomainIntentFamilyAvailability(HasAd: hasAdFamily, HasPublic: hasPublicFamily));
    }

    internal static string BuildDomainIntentClarificationTextForTesting(
        IReadOnlyList<string> families,
        IReadOnlyDictionary<string, string>? familyActionIds) {
        var normalizedFamilies = families is null || families.Count == 0
            ? Array.Empty<string>()
            : families
                .Where(static family => TryNormalizeDomainIntentFamily(family, out _))
                .Select(static family => family.Trim())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static family => string.Equals(family, DomainIntentFamilyAd, StringComparison.Ordinal)
                        ? 0
                        : string.Equals(family, DomainIntentFamilyPublic, StringComparison.Ordinal)
                            ? 1
                            : 2)
                .ThenBy(static family => family, StringComparer.Ordinal)
                .ToArray();
        var hasAdFamily = normalizedFamilies.Contains(DomainIntentFamilyAd, StringComparer.Ordinal);
        var hasPublicFamily = normalizedFamilies.Contains(DomainIntentFamilyPublic, StringComparer.Ordinal);
        var availability = new DomainIntentFamilyAvailability(
            HasAd: hasAdFamily,
            HasPublic: hasPublicFamily,
            Families: normalizedFamilies);
        var actionCatalog = new DomainIntentActionCatalog(FamilyActionIds: familyActionIds);
        return BuildDomainIntentClarificationText(availability, actionCatalog);
    }

    internal static string BuildDomainIntentClarificationVisibleTextForTesting(bool hasAdFamily, bool hasPublicFamily) {
        return BuildDomainIntentClarificationVisibleText(new DomainIntentFamilyAvailability(HasAd: hasAdFamily, HasPublic: hasPublicFamily));
    }

    internal static string BuildDomainIntentClarificationVisibleTextForTesting(
        string userRequest,
        bool hasAdFamily,
        bool hasPublicFamily) {
        return BuildDomainIntentClarificationVisibleText(
            userRequest,
            new DomainIntentFamilyAvailability(HasAd: hasAdFamily, HasPublic: hasPublicFamily),
            BuildDefaultDomainIntentActionCatalog());
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
