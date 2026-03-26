using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.OpenAI.Chat;
using IntelligenceX.OpenAI.ToolCalling;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    private const string PlannerContextMarker = "ix:planner-context:v1";
    private const int MaxPlannerContextPackIds = 8;
    private const int MaxPlannerContextToolNames = 8;
    private const int MaxPlannerContextDeferredWorkCapabilityIds = 6;
    private const int MaxPlannerContextSkills = 6;
    private const int MaxPlannerContextHandoffTargets = 8;
    private const int MaxPlannerContextSourceTools = 4;
    private const int MaxPlannerContextBackgroundFocusChars = 320;

    private readonly record struct PlannerContextMetadata(
        bool RequiresLiveExecution,
        string MissingLiveEvidence,
        string[] PreferredPackIds,
        string[] PreferredToolNames,
        string[] PreferredDeferredWorkCapabilityIds,
        string[] StructuredNextActionSourceToolNames,
        string StructuredNextActionReason,
        double? StructuredNextActionConfidence,
        string[] PreferredExecutionBackends,
        string[] HandoffTargetPackIds,
        string[] HandoffTargetToolNames,
        string ContinuationSourceTool,
        string ContinuationReason,
        string ContinuationConfidence,
        bool BackgroundPreparationAllowed,
        int BackgroundPendingReadOnlyActions,
        int BackgroundPendingUnknownActions,
        string[] BackgroundFollowUpClasses,
        string BackgroundPriorityFocus,
        string BackgroundFollowUpFocus,
        string[] BackgroundRecentEvidenceTools,
        string[] MatchingSkills,
        bool AllowCachedEvidenceReuse);

    private readonly record struct PlannerBackgroundPreparationHints(
        bool PreparationAllowed,
        int PendingReadOnlyActions,
        int PendingUnknownActions,
        string[] FollowUpClasses,
        string PriorityFocus,
        string FollowUpFocus,
        string[] RecentEvidenceTools);

    private string BuildPlannerContextAugmentedRequest(string threadId, string requestText, IReadOnlyList<ToolDefinition> definitions) {
        var normalizedRequest = (requestText ?? string.Empty).Trim();
        if (normalizedRequest.Length == 0
            || normalizedRequest.IndexOf(PlannerContextMarker, StringComparison.OrdinalIgnoreCase) >= 0) {
            return normalizedRequest;
        }

        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0) {
            return normalizedRequest;
        }

        var hasCheckpoint = TryGetWorkingMemoryCheckpoint(normalizedThreadId, out var checkpoint);
        var (
            structuredPreferredPackIds,
            structuredPreferredToolNames,
            structuredSourceToolNames,
            structuredNextActionReason,
            structuredNextActionConfidence,
            structuredHandoffTargetPackIds,
            structuredHandoffTargetToolNames,
            continuationSourceTool,
            continuationReason,
            continuationConfidence) = ResolvePlannerStructuredNextActionHints(normalizedThreadId, definitions);
        var deferredPreferenceHints = ResolveDeferredToolPreferenceHints(
            normalizedRequest,
            options: null,
            maxPreferredPackIds: MaxPlannerContextPackIds,
            maxPreferredToolNames: MaxPlannerContextToolNames);
        var backgroundHints = ResolvePlannerBackgroundPreparationHints(normalizedThreadId);
        if (!hasCheckpoint
            && structuredPreferredPackIds.Length == 0
            && structuredPreferredToolNames.Length == 0
            && deferredPreferenceHints.PreferredPackIds.Length == 0
            && deferredPreferenceHints.PreferredToolNames.Length == 0
            && structuredSourceToolNames.Length == 0
            && structuredNextActionReason.Length == 0
            && !structuredNextActionConfidence.HasValue
            && structuredHandoffTargetPackIds.Length == 0
            && structuredHandoffTargetToolNames.Length == 0
            && continuationSourceTool.Length == 0
            && continuationReason.Length == 0
            && continuationConfidence.Length == 0
            && !backgroundHints.PreparationAllowed
            && backgroundHints.PendingReadOnlyActions <= 0
            && backgroundHints.PendingUnknownActions <= 0
            && backgroundHints.FollowUpClasses.Length == 0
            && backgroundHints.PriorityFocus.Length == 0
            && backgroundHints.FollowUpFocus.Length == 0
            && backgroundHints.RecentEvidenceTools.Length == 0) {
            return normalizedRequest;
        }

        var preferredPackIds = NormalizeDistinctStrings(
            (hasCheckpoint ? checkpoint.PriorAnswerPlanPreferredPackIds : Array.Empty<string>())
            .Concat(structuredPreferredPackIds)
            .Concat(deferredPreferenceHints.PreferredPackIds)
            .Select(static packId => NormalizePackId(packId))
            .Where(static packId => packId.Length > 0),
            MaxPlannerContextPackIds);
        var preferredToolNames = NormalizeDistinctStrings(
            (hasCheckpoint ? checkpoint.PriorAnswerPlanPreferredToolNames : Array.Empty<string>())
            .Concat(structuredPreferredToolNames)
            .Concat(deferredPreferenceHints.PreferredToolNames),
            MaxPlannerContextToolNames);
        var preferredDeferredWorkCapabilityIds = NormalizeDistinctStrings(
            (hasCheckpoint ? checkpoint.PriorAnswerPlanPreferredDeferredWorkCapabilityIds : Array.Empty<string>())
            .Select(static capabilityId => NormalizeDeferredWorkCapabilityId(capabilityId))
            .Where(static capabilityId => capabilityId.Length > 0),
            MaxPlannerContextDeferredWorkCapabilityIds);
        var sourceToolNames = NormalizeDistinctStrings(structuredSourceToolNames, MaxPlannerContextSourceTools);
        var preferredExecutionBackends = CollectThreadToolExecutionBackendHints(
            normalizedThreadId,
            preferredToolNames,
            hasCheckpoint ? checkpoint.RecentToolNames : Array.Empty<string>());

        var handoffSourceToolNames = NormalizeDistinctStrings(
            (hasCheckpoint ? checkpoint.PriorAnswerPlanPreferredToolNames : Array.Empty<string>())
            .Concat(hasCheckpoint ? checkpoint.RecentToolNames : Array.Empty<string>())
            .Concat(hasCheckpoint ? checkpoint.CapabilityHealthyToolNames : Array.Empty<string>())
            .Concat(structuredPreferredToolNames),
            MaxPlannerContextToolNames);
        var (derivedHandoffTargetPackIds, derivedHandoffTargetToolNames) = CollectPlannerHandoffTargets(handoffSourceToolNames);
        var handoffTargetPackIds = NormalizeDistinctStrings(
            derivedHandoffTargetPackIds.Concat(structuredHandoffTargetPackIds),
            MaxPlannerContextHandoffTargets);
        var handoffTargetToolNames = NormalizeDistinctStrings(
            derivedHandoffTargetToolNames.Concat(structuredHandoffTargetToolNames),
            MaxPlannerContextHandoffTargets);

        var matchingSkills = ResolvePlannerMatchingSkills(
            normalizedRequest,
            checkpoint,
            preferredPackIds,
            preferredToolNames,
            preferredDeferredWorkCapabilityIds,
            sourceToolNames,
            structuredNextActionReason,
            backgroundHints.FollowUpClasses,
            backgroundHints.PriorityFocus,
            handoffTargetPackIds,
            handoffTargetToolNames);

        var context = new PlannerContextMetadata(
            RequiresLiveExecution: hasCheckpoint && checkpoint.PriorAnswerPlanRequiresLiveExecution,
            MissingLiveEvidence: hasCheckpoint ? checkpoint.PriorAnswerPlanMissingLiveEvidence : string.Empty,
            PreferredPackIds: preferredPackIds,
            PreferredToolNames: preferredToolNames,
            PreferredDeferredWorkCapabilityIds: preferredDeferredWorkCapabilityIds,
            StructuredNextActionSourceToolNames: sourceToolNames,
            StructuredNextActionReason: structuredNextActionReason,
            StructuredNextActionConfidence: structuredNextActionConfidence,
            PreferredExecutionBackends: preferredExecutionBackends,
            HandoffTargetPackIds: handoffTargetPackIds,
            HandoffTargetToolNames: handoffTargetToolNames,
            ContinuationSourceTool: continuationSourceTool,
            ContinuationReason: continuationReason,
            ContinuationConfidence: continuationConfidence,
            BackgroundPreparationAllowed: backgroundHints.PreparationAllowed,
            BackgroundPendingReadOnlyActions: backgroundHints.PendingReadOnlyActions,
            BackgroundPendingUnknownActions: backgroundHints.PendingUnknownActions,
            BackgroundFollowUpClasses: backgroundHints.FollowUpClasses,
            BackgroundPriorityFocus: backgroundHints.PriorityFocus,
            BackgroundFollowUpFocus: backgroundHints.FollowUpFocus,
            BackgroundRecentEvidenceTools: backgroundHints.RecentEvidenceTools,
            MatchingSkills: matchingSkills,
            AllowCachedEvidenceReuse: hasCheckpoint && checkpoint.PriorAnswerPlanAllowCachedEvidenceReuse);
        if (!HasPlannerContextSignals(context)) {
            return normalizedRequest;
        }

        return BuildPlannerContextText(context, normalizedRequest);
    }

    private void AppendToolRoundReplayPlannerContextIfNeeded(
        ChatInput replayInput,
        string threadId,
        string requestText,
        IReadOnlyList<ToolDefinition> currentVisibleDefinitions,
        IReadOnlyList<ToolCall> executedCalls) {
        ArgumentNullException.ThrowIfNull(replayInput);
        ArgumentNullException.ThrowIfNull(currentVisibleDefinitions);
        ArgumentNullException.ThrowIfNull(executedCalls);

        if (!TryBuildToolRoundReplayPlannerContextText(
                threadId,
                requestText,
                currentVisibleDefinitions,
                executedCalls,
                out var plannerContextText)) {
            return;
        }

        replayInput.AddText(plannerContextText);
    }

    private bool TryBuildToolRoundReplayPlannerContextText(
        string threadId,
        string requestText,
        IReadOnlyList<ToolDefinition> currentVisibleDefinitions,
        IReadOnlyList<ToolCall> executedCalls,
        out string plannerContextText) {
        ArgumentNullException.ThrowIfNull(currentVisibleDefinitions);
        ArgumentNullException.ThrowIfNull(executedCalls);

        plannerContextText = string.Empty;
        var context = BuildToolRoundReplayPlannerContextMetadata(
            threadId,
            requestText,
            currentVisibleDefinitions,
            executedCalls);
        if (!HasPlannerContextSignals(context)) {
            return false;
        }

        plannerContextText = BuildPlannerContextText(context);
        return plannerContextText.Length > 0;
    }

    private PlannerContextMetadata BuildToolRoundReplayPlannerContextMetadata(
        string threadId,
        string requestText,
        IReadOnlyList<ToolDefinition> currentVisibleDefinitions,
        IReadOnlyList<ToolCall> executedCalls) {
        if (currentVisibleDefinitions.Count == 0 || executedCalls.Count == 0) {
            return default;
        }

        var sourceToolNames = NormalizeDistinctStrings(
            executedCalls
                .Select(static call => NormalizeToolNameForAnswerPlan(call.Name))
                .Where(static toolName => toolName.Length > 0),
            MaxPlannerContextSourceTools);
        if (sourceToolNames.Length == 0) {
            return default;
        }

        var sourcePackIds = NormalizeDistinctStrings(
            sourceToolNames
                .Select(toolName =>
                    TryGetToolDefinitionByName(currentVisibleDefinitions, toolName, out var definition)
                        ? ResolveToolPackId(definition, _toolOrchestrationCatalog)
                        : string.Empty)
                .Select(static packId => NormalizePackId(packId))
                .Where(static packId => packId.Length > 0),
            MaxPlannerContextPackIds);
        var (handoffTargetPackIds, handoffTargetToolNames) = CollectPlannerHandoffTargets(sourceToolNames);
        var preferredPackIds = handoffTargetPackIds.Length > 0 ? handoffTargetPackIds : sourcePackIds;
        var preferredToolNames = handoffTargetToolNames.Length > 0
            ? handoffTargetToolNames
            : NormalizeDistinctStrings(sourceToolNames, MaxPlannerContextToolNames);
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        var preferredExecutionBackends = normalizedThreadId.Length == 0
            ? Array.Empty<string>()
            : CollectThreadToolExecutionBackendHints(normalizedThreadId, preferredToolNames, sourceToolNames);
        var continuationSourceTool = NormalizeToolNameForAnswerPlan(executedCalls[executedCalls.Count - 1].Name);
        var structuredNextActionReason = handoffTargetPackIds.Length > 0 || handoffTargetToolNames.Length > 0
            ? "continue with the declared handoff target from the prior tool result"
            : "continue with the active tool scope from the prior tool result";
        WorkingMemoryCheckpoint checkpoint = default;
        if (normalizedThreadId.Length > 0) {
            TryGetWorkingMemoryCheckpoint(normalizedThreadId, out checkpoint);
        }

        var matchingSkills = ResolvePlannerMatchingSkills(
            requestText,
            checkpoint,
            preferredPackIds,
            preferredToolNames,
            Array.Empty<string>(),
            sourceToolNames,
            structuredNextActionReason,
            Array.Empty<string>(),
            string.Empty,
            handoffTargetPackIds,
            handoffTargetToolNames);
        return new PlannerContextMetadata(
            RequiresLiveExecution: false,
            MissingLiveEvidence: string.Empty,
            PreferredPackIds: preferredPackIds,
            PreferredToolNames: preferredToolNames,
            PreferredDeferredWorkCapabilityIds: Array.Empty<string>(),
            StructuredNextActionSourceToolNames: sourceToolNames,
            StructuredNextActionReason: structuredNextActionReason,
            StructuredNextActionConfidence: null,
            PreferredExecutionBackends: preferredExecutionBackends,
            HandoffTargetPackIds: handoffTargetPackIds,
            HandoffTargetToolNames: handoffTargetToolNames,
            ContinuationSourceTool: continuationSourceTool,
            ContinuationReason: structuredNextActionReason,
            ContinuationConfidence: string.Empty,
            BackgroundPreparationAllowed: false,
            BackgroundPendingReadOnlyActions: 0,
            BackgroundPendingUnknownActions: 0,
            BackgroundFollowUpClasses: Array.Empty<string>(),
            BackgroundPriorityFocus: string.Empty,
            BackgroundFollowUpFocus: string.Empty,
            BackgroundRecentEvidenceTools: Array.Empty<string>(),
            MatchingSkills: matchingSkills,
            AllowCachedEvidenceReuse: false);
    }

    private (
        string[] PreferredPackIds,
        string[] PreferredToolNames,
        string[] SourceToolNames,
        string StructuredNextActionReason,
        double? StructuredNextActionConfidence,
        string[] HandoffTargetPackIds,
        string[] HandoffTargetToolNames,
        string ContinuationSourceTool,
        string ContinuationReason,
        string ContinuationConfidence)
        ResolvePlannerStructuredNextActionHints(string normalizedThreadId, IReadOnlyList<ToolDefinition> definitions) {
        if (normalizedThreadId.Length == 0
            || definitions is null
            || definitions.Count == 0
            || !TryGetStructuredNextActionCarryover(normalizedThreadId, out var snapshot, out _)
            || snapshot.Mutability == ActionMutability.Mutating
            || !TryGetToolDefinitionByName(definitions, snapshot.ToolName, out var toolDefinition)) {
            return (Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), string.Empty, null, Array.Empty<string>(), Array.Empty<string>(), string.Empty, string.Empty, string.Empty);
        }

        var preferredToolNames = NormalizeDistinctStrings(new[] { snapshot.ToolName }, MaxPlannerContextToolNames);
        var packId = ResolveToolPackId(toolDefinition, _toolOrchestrationCatalog);
        var preferredPackIds = packId.Length == 0
            ? Array.Empty<string>()
            : NormalizeDistinctStrings(new[] { packId }, MaxPlannerContextPackIds);
        var sourceToolNames = NormalizeDistinctStrings(
            new[] { (snapshot.SourceToolName ?? string.Empty).Trim() },
            MaxPlannerContextSourceTools);
        var (handoffTargetPackIds, handoffTargetToolNames) = CollectPlannerHandoffTargets(preferredToolNames);
        return (
            preferredPackIds,
            preferredToolNames,
            sourceToolNames,
            NormalizeStructuredNextActionReason(snapshot.Reason),
            NormalizeStructuredNextActionConfidence(snapshot.Confidence),
            handoffTargetPackIds,
            handoffTargetToolNames,
            NormalizeToolNameForAnswerPlan(snapshot.SourceToolName),
            NormalizeWorkingMemoryAnswerPlanFocus(snapshot.Reason),
            NormalizeContinuationConfidence(snapshot.Confidence));
    }

    private PlannerBackgroundPreparationHints ResolvePlannerBackgroundPreparationHints(string normalizedThreadId) {
        if (normalizedThreadId.Length == 0) {
            return new PlannerBackgroundPreparationHints(
                PreparationAllowed: false,
                PendingReadOnlyActions: 0,
                PendingUnknownActions: 0,
                FollowUpClasses: Array.Empty<string>(),
                PriorityFocus: string.Empty,
                FollowUpFocus: string.Empty,
                RecentEvidenceTools: Array.Empty<string>());
        }

        var backgroundWork = ResolveThreadBackgroundWorkSnapshot(normalizedThreadId);
        if (backgroundWork.QueuedCount <= 0 && backgroundWork.ReadyCount <= 0) {
            return new PlannerBackgroundPreparationHints(
                PreparationAllowed: false,
                PendingReadOnlyActions: 0,
                PendingUnknownActions: 0,
                FollowUpClasses: Array.Empty<string>(),
                PriorityFocus: string.Empty,
                FollowUpFocus: string.Empty,
                RecentEvidenceTools: Array.Empty<string>());
        }

        var activeItems = backgroundWork.Items
            .Where(static item => !string.Equals(item.State, BackgroundWorkStateCompleted, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var followUpSummary = BuildBackgroundWorkFollowUpSummary(activeItems);
        var focusItems = activeItems
            .Select(static item => NormalizeWorkingMemoryAnswerPlanFocus(item.Request))
            .Where(static focus => focus.Length > 0)
            .Take(3)
            .ToArray();
        var followUpFocus = focusItems.Length == 0
            ? string.Empty
            : string.Join("; ", focusItems);
        if (followUpFocus.Length > MaxPlannerContextBackgroundFocusChars) {
            followUpFocus = followUpFocus[..MaxPlannerContextBackgroundFocusChars].TrimEnd();
        }

        return new PlannerBackgroundPreparationHints(
            PreparationAllowed: backgroundWork.ReadyCount > 0 || backgroundWork.QueuedCount > 0,
            PendingReadOnlyActions: backgroundWork.PendingReadOnlyCount,
            PendingUnknownActions: backgroundWork.PendingUnknownCount,
            FollowUpClasses: followUpSummary.FollowUpKinds,
            PriorityFocus: followUpSummary.PriorityFocus,
            FollowUpFocus: followUpFocus,
            RecentEvidenceTools: backgroundWork.RecentEvidenceTools);
    }

    private (string[] TargetPackIds, string[] TargetToolNames) CollectPlannerHandoffTargets(IReadOnlyList<string> sourceToolNames) {
        if (sourceToolNames is null || sourceToolNames.Count == 0 || _toolOrchestrationCatalog is null) {
            return (Array.Empty<string>(), Array.Empty<string>());
        }

        var targetPackIds = new List<string>();
        var targetToolNames = new List<string>();
        for (var i = 0; i < sourceToolNames.Count; i++) {
            var sourceToolName = (sourceToolNames[i] ?? string.Empty).Trim();
            if (sourceToolName.Length == 0 || !_toolOrchestrationCatalog.TryGetEntry(sourceToolName, out var entry)) {
                continue;
            }

            for (var e = 0; e < entry.HandoffEdges.Count; e++) {
                var edge = entry.HandoffEdges[e];
                var targetPackId = NormalizePackId(edge.TargetPackId);
                if (targetPackId.Length > 0) {
                    targetPackIds.Add(targetPackId);
                }

                var targetToolName = NormalizeToolNameForAnswerPlan(edge.TargetToolName);
                if (targetToolName.Length > 0) {
                    targetToolNames.Add(targetToolName);
                }
            }
        }

        return (
            NormalizeDistinctStrings(targetPackIds, MaxPlannerContextHandoffTargets),
            NormalizeDistinctStrings(targetToolNames, MaxPlannerContextHandoffTargets));
    }

    private string[] ResolvePlannerMatchingSkills(
        string requestText,
        WorkingMemoryCheckpoint checkpoint,
        IReadOnlyList<string> preferredPackIds,
        IReadOnlyList<string> preferredToolNames,
        IReadOnlyList<string> preferredDeferredWorkCapabilityIds,
        IReadOnlyList<string> sourceToolNames,
        string structuredNextActionReason,
        IReadOnlyList<string> backgroundFollowUpClasses,
        string backgroundPriorityFocus,
        IReadOnlyList<string> handoffTargetPackIds,
        IReadOnlyList<string> handoffTargetToolNames) {
        var skillInventory = NormalizeSkillInventoryValues(
            (checkpoint.CapabilitySkills ?? Array.Empty<string>())
            .Concat(_connectedRuntimeSkillInventory ?? Array.Empty<string>()),
            maxItems: 0);
        if (skillInventory.Length == 0) {
            return Array.Empty<string>();
        }

        var requestTokens = new HashSet<string>(
            TokenizeRoutingTokens(ExtractPrimaryUserRequest(requestText), maxTokens: 12),
            StringComparer.OrdinalIgnoreCase);
        AddPlannerTokenSeeds(requestTokens, preferredPackIds);
        AddPlannerTokenSeeds(requestTokens, preferredToolNames);
        AddPlannerTokenSeeds(requestTokens, preferredDeferredWorkCapabilityIds);
        AddPlannerTokenSeeds(requestTokens, sourceToolNames);
        AddPlannerTokenSeeds(requestTokens, backgroundFollowUpClasses);
        AddPlannerTokenSeeds(requestTokens, handoffTargetPackIds);
        AddPlannerTokenSeeds(requestTokens, handoffTargetToolNames);
        AddPlannerReasonTokens(requestTokens, structuredNextActionReason);
        AddPlannerReasonTokens(requestTokens, backgroundPriorityFocus);
        if (checkpoint.DomainIntentFamily.Length > 0) {
            requestTokens.Add(checkpoint.DomainIntentFamily);
        }

        var candidates = new List<(string Skill, int Score)>(skillInventory.Length);
        for (var i = 0; i < skillInventory.Length; i++) {
            var skill = (skillInventory[i] ?? string.Empty).Trim();
            if (skill.Length == 0) {
                continue;
            }

            var score = 0;
            score += CountPlannerSkillMatches(skill, preferredPackIds) * 3;
            score += CountPlannerSkillMatches(skill, handoffTargetPackIds) * 2;
            score += CountPlannerSkillMatches(skill, preferredToolNames) * 3;
            score += CountPlannerSkillMatches(skill, preferredDeferredWorkCapabilityIds) * 3;
            score += CountPlannerSkillMatches(skill, sourceToolNames);
            score += CountPlannerSkillMatches(skill, handoffTargetToolNames) * 2;
            if (checkpoint.DomainIntentFamily.Length > 0
                && skill.IndexOf(checkpoint.DomainIntentFamily, StringComparison.OrdinalIgnoreCase) >= 0) {
                score += 2;
            }

            foreach (var token in requestTokens) {
                if (token.Length == 0) {
                    continue;
                }

                if (skill.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0) {
                    score++;
                }
            }

            if (score > 0) {
                candidates.Add((skill, score));
            }
        }

        return candidates
            .OrderByDescending(static candidate => candidate.Score)
            .ThenBy(static candidate => candidate.Skill, StringComparer.OrdinalIgnoreCase)
            .Select(static candidate => candidate.Skill)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxPlannerContextSkills)
            .ToArray();
    }

    private static int CountPlannerSkillMatches(string skill, IReadOnlyList<string> seeds) {
        if (seeds is null || seeds.Count == 0) {
            return 0;
        }

        var matches = 0;
        for (var i = 0; i < seeds.Count; i++) {
            var seed = (seeds[i] ?? string.Empty).Trim();
            if (seed.Length == 0) {
                continue;
            }

            if (skill.IndexOf(seed, StringComparison.OrdinalIgnoreCase) >= 0) {
                matches++;
            }
        }

        return matches;
    }

    private static void AddPlannerTokenSeeds(ISet<string> tokens, IReadOnlyList<string> values) {
        if (tokens is null || values is null || values.Count == 0) {
            return;
        }

        for (var i = 0; i < values.Count; i++) {
            var value = (values[i] ?? string.Empty).Trim();
            if (value.Length == 0) {
                continue;
            }

            tokens.Add(value);
            var normalizedTokens = TokenizeRoutingTokens(value, maxTokens: 6);
            for (var t = 0; t < normalizedTokens.Length; t++) {
                tokens.Add(normalizedTokens[t]);
            }
        }
    }

    private static void AddPlannerReasonTokens(ISet<string> tokens, string value) {
        var normalized = (value ?? string.Empty).Trim();
        if (tokens is null || normalized.Length == 0) {
            return;
        }

        tokens.Add(normalized);
        var reasonTokens = TokenizeRoutingTokens(normalized, maxTokens: 8);
        for (var i = 0; i < reasonTokens.Length; i++) {
            tokens.Add(reasonTokens[i]);
        }
    }

    private static bool TryReadPlannerContextFromRequestText(string requestText, out PlannerContextMetadata context) {
        context = new PlannerContextMetadata(
            RequiresLiveExecution: false,
            MissingLiveEvidence: string.Empty,
            PreferredPackIds: Array.Empty<string>(),
            PreferredToolNames: Array.Empty<string>(),
            PreferredDeferredWorkCapabilityIds: Array.Empty<string>(),
            StructuredNextActionSourceToolNames: Array.Empty<string>(),
            StructuredNextActionReason: string.Empty,
            StructuredNextActionConfidence: null,
            PreferredExecutionBackends: Array.Empty<string>(),
            HandoffTargetPackIds: Array.Empty<string>(),
            HandoffTargetToolNames: Array.Empty<string>(),
            ContinuationSourceTool: string.Empty,
            ContinuationReason: string.Empty,
            ContinuationConfidence: string.Empty,
            BackgroundPreparationAllowed: false,
            BackgroundPendingReadOnlyActions: 0,
            BackgroundPendingUnknownActions: 0,
            BackgroundFollowUpClasses: Array.Empty<string>(),
            BackgroundPriorityFocus: string.Empty,
            BackgroundFollowUpFocus: string.Empty,
            BackgroundRecentEvidenceTools: Array.Empty<string>(),
            MatchingSkills: Array.Empty<string>(),
            AllowCachedEvidenceReuse: false);
        var raw = requestText ?? string.Empty;
        if (raw.IndexOf(PlannerContextMarker, StringComparison.OrdinalIgnoreCase) < 0) {
            return false;
        }

        var requiresLiveExecution = false;
        var missingLiveEvidence = string.Empty;
        var preferredPackIds = Array.Empty<string>();
        var preferredToolNames = Array.Empty<string>();
        var preferredDeferredWorkCapabilityIds = Array.Empty<string>();
        var structuredNextActionSourceToolNames = Array.Empty<string>();
        var structuredNextActionReason = string.Empty;
        double? structuredNextActionConfidence = null;
        var preferredExecutionBackends = Array.Empty<string>();
        var handoffTargetPackIds = Array.Empty<string>();
        var handoffTargetToolNames = Array.Empty<string>();
        var continuationSourceTool = string.Empty;
        var continuationReason = string.Empty;
        var continuationConfidence = string.Empty;
        var backgroundPreparationAllowed = false;
        var backgroundPendingReadOnlyActions = 0;
        var backgroundPendingUnknownActions = 0;
        var backgroundFollowUpClasses = Array.Empty<string>();
        var backgroundPriorityFocus = string.Empty;
        var backgroundFollowUpFocus = string.Empty;
        var backgroundRecentEvidenceTools = Array.Empty<string>();
        var matchingSkills = Array.Empty<string>();
        var allowCachedEvidenceReuse = false;
        var sawMarker = false;
        var parsedAnyStructuredValue = false;

        using var reader = new StringReader(raw);
        while (reader.ReadLine() is { } line) {
            var trimmed = line.Trim();
            if (!sawMarker) {
                if (trimmed.IndexOf(PlannerContextMarker, StringComparison.OrdinalIgnoreCase) >= 0) {
                    sawMarker = true;
                }

                continue;
            }

            if (trimmed.Length == 0) {
                if (parsedAnyStructuredValue) {
                    break;
                }

                continue;
            }

            if (LooksLikeStructuredSectionHeader(trimmed)) {
                if (parsedAnyStructuredValue) {
                    break;
                }

                continue;
            }

            if (TryParseStructuredBooleanLine(trimmed, "requires_live_execution", out var parsedRequiresLiveExecution)) {
                requiresLiveExecution = parsedRequiresLiveExecution;
                parsedAnyStructuredValue = true;
                continue;
            }

            if (TryParseStructuredBooleanLine(trimmed, "allow_cached_evidence_reuse", out var parsedAllowCachedEvidenceReuse)) {
                allowCachedEvidenceReuse = parsedAllowCachedEvidenceReuse;
                parsedAnyStructuredValue = true;
                continue;
            }

            if (TryParseStructuredKeyValueLine(trimmed, "missing_live_evidence", out var missingLiveEvidenceValue)) {
                missingLiveEvidence = NormalizeWorkingMemoryAnswerPlanFocus(missingLiveEvidenceValue.ToString());
                parsedAnyStructuredValue = true;
                continue;
            }

            if (TryParseStructuredKeyValueLine(trimmed, "preferred_pack_ids", out var preferredPackIdsValue)) {
                preferredPackIds = NormalizeStructuredMetadataCsv(preferredPackIdsValue, NormalizePackId, MaxPlannerContextPackIds);
                parsedAnyStructuredValue = true;
                continue;
            }

            if (TryParseStructuredKeyValueLine(trimmed, "preferred_tool_names", out var preferredToolNamesValue)) {
                preferredToolNames = NormalizeStructuredMetadataCsv(
                    preferredToolNamesValue,
                    static value => NormalizeToolNameForAnswerPlan(value),
                    MaxPlannerContextToolNames);
                parsedAnyStructuredValue = true;
                continue;
            }

            if (TryParseStructuredKeyValueLine(trimmed, "preferred_deferred_work_capability_ids", out var preferredDeferredWorkCapabilityIdsValue)) {
                preferredDeferredWorkCapabilityIds = NormalizeStructuredMetadataCsv(
                    preferredDeferredWorkCapabilityIdsValue,
                    static value => NormalizeDeferredWorkCapabilityId(value),
                    MaxPlannerContextDeferredWorkCapabilityIds);
                parsedAnyStructuredValue = true;
                continue;
            }

            if (TryParseStructuredKeyValueLine(trimmed, "structured_next_action_source_tools", out var structuredSourceToolsValue)) {
                structuredNextActionSourceToolNames = NormalizeStructuredMetadataCsv(
                    structuredSourceToolsValue,
                    static value => NormalizeToolNameForAnswerPlan(value),
                    MaxPlannerContextSourceTools);
                continue;
            }

            if (TryParseStructuredKeyValueLine(trimmed, "structured_next_action_reason", out var structuredReasonValue)) {
                structuredNextActionReason = NormalizeStructuredNextActionReason(structuredReasonValue.ToString());
                continue;
            }

            if (TryParseStructuredKeyValueLine(trimmed, "structured_next_action_confidence", out var structuredConfidenceValue)
                && double.TryParse(
                    structuredConfidenceValue.ToString(),
                    System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowThousands,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var parsedStructuredConfidence)) {
                structuredNextActionConfidence = NormalizeStructuredNextActionConfidence(parsedStructuredConfidence);
                continue;
            }

            if (TryParseStructuredKeyValueLine(trimmed, "preferred_execution_backends", out var preferredExecutionBackendsValue)) {
                preferredExecutionBackends = NormalizeStructuredMetadataCsv(
                    preferredExecutionBackendsValue,
                    static value => NormalizeToolExecutionBackendHint(value),
                    MaxToolExecutionBackendHints);
                continue;
            }

            if (TryParseStructuredKeyValueLine(trimmed, "handoff_target_pack_ids", out var handoffTargetPackIdsValue)) {
                handoffTargetPackIds = NormalizeStructuredMetadataCsv(
                    handoffTargetPackIdsValue,
                    NormalizePackId,
                    MaxPlannerContextHandoffTargets);
                parsedAnyStructuredValue = true;
                continue;
            }

            if (TryParseStructuredKeyValueLine(trimmed, "handoff_target_tool_names", out var handoffTargetToolNamesValue)) {
                handoffTargetToolNames = NormalizeStructuredMetadataCsv(
                    handoffTargetToolNamesValue,
                    static value => NormalizeToolNameForAnswerPlan(value),
                    MaxPlannerContextHandoffTargets);
                parsedAnyStructuredValue = true;
                continue;
            }

            if (TryParseStructuredKeyValueLine(trimmed, "continuation_source_tool", out var continuationSourceToolValue)) {
                continuationSourceTool = NormalizeToolNameForAnswerPlan(continuationSourceToolValue.ToString());
                continue;
            }

            if (TryParseStructuredKeyValueLine(trimmed, "continuation_reason", out var continuationReasonValue)) {
                continuationReason = NormalizeWorkingMemoryAnswerPlanFocus(continuationReasonValue.ToString());
                continue;
            }

            if (TryParseStructuredKeyValueLine(trimmed, "continuation_confidence", out var continuationConfidenceValue)) {
                continuationConfidence = NormalizeContinuationConfidence(continuationConfidenceValue.ToString());
                continue;
            }

            if (TryParseStructuredBooleanLine(trimmed, "background_preparation_allowed", out var parsedBackgroundPreparationAllowed)) {
                backgroundPreparationAllowed = parsedBackgroundPreparationAllowed;
                parsedAnyStructuredValue = true;
                continue;
            }

            if (TryParseStructuredKeyValueLine(trimmed, "background_pending_read_only_actions", out var backgroundPendingReadOnlyActionsValue)
                && int.TryParse(
                    backgroundPendingReadOnlyActionsValue.ToString(),
                    System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var parsedBackgroundPendingReadOnlyActions)) {
                backgroundPendingReadOnlyActions = Math.Max(0, parsedBackgroundPendingReadOnlyActions);
                parsedAnyStructuredValue = true;
                continue;
            }

            if (TryParseStructuredKeyValueLine(trimmed, "background_pending_unknown_actions", out var backgroundPendingUnknownActionsValue)
                && int.TryParse(
                    backgroundPendingUnknownActionsValue.ToString(),
                    System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var parsedBackgroundPendingUnknownActions)) {
                backgroundPendingUnknownActions = Math.Max(0, parsedBackgroundPendingUnknownActions);
                parsedAnyStructuredValue = true;
                continue;
            }

            if (TryParseStructuredKeyValueLine(trimmed, "background_follow_up_focus", out var backgroundFollowUpFocusValue)) {
                backgroundFollowUpFocus = NormalizeWorkingMemoryAnswerPlanFocus(backgroundFollowUpFocusValue.ToString());
                parsedAnyStructuredValue = true;
                continue;
            }

            if (TryParseStructuredKeyValueLine(trimmed, "background_follow_up_classes", out var backgroundFollowUpClassesValue)) {
                backgroundFollowUpClasses = NormalizeStructuredMetadataCsv(
                    backgroundFollowUpClassesValue,
                    static value => ToolHandoffFollowUpKinds.Normalize(value),
                    maxItems: 4);
                parsedAnyStructuredValue = true;
                continue;
            }

            if (TryParseStructuredKeyValueLine(trimmed, "background_priority_focus", out var backgroundPriorityFocusValue)) {
                backgroundPriorityFocus = NormalizeWorkingMemoryAnswerPlanFocus(backgroundPriorityFocusValue.ToString());
                parsedAnyStructuredValue = true;
                continue;
            }

            if (TryParseStructuredKeyValueLine(trimmed, "background_recent_evidence_tools", out var backgroundRecentEvidenceToolsValue)) {
                backgroundRecentEvidenceTools = NormalizeStructuredMetadataCsv(
                    backgroundRecentEvidenceToolsValue,
                    static value => NormalizeToolNameForAnswerPlan(value),
                    MaxPlannerContextSourceTools);
                parsedAnyStructuredValue = true;
                continue;
            }

            if (TryParseStructuredKeyValueLine(trimmed, "matching_skills", out var matchingSkillsValue)) {
                matchingSkills = NormalizeStructuredMetadataCsv(
                    matchingSkillsValue,
                    static value => NormalizeSkillSnapshotValue(value),
                    MaxPlannerContextSkills);
                parsedAnyStructuredValue = true;
                continue;
            }

            if (parsedAnyStructuredValue) {
                break;
            }
        }

        context = new PlannerContextMetadata(
            RequiresLiveExecution: requiresLiveExecution,
            MissingLiveEvidence: missingLiveEvidence,
            PreferredPackIds: preferredPackIds,
            PreferredToolNames: preferredToolNames,
            PreferredDeferredWorkCapabilityIds: preferredDeferredWorkCapabilityIds,
            StructuredNextActionSourceToolNames: structuredNextActionSourceToolNames,
            StructuredNextActionReason: structuredNextActionReason,
            StructuredNextActionConfidence: structuredNextActionConfidence,
            PreferredExecutionBackends: preferredExecutionBackends,
            HandoffTargetPackIds: handoffTargetPackIds,
            HandoffTargetToolNames: handoffTargetToolNames,
            ContinuationSourceTool: continuationSourceTool,
            ContinuationReason: continuationReason,
            ContinuationConfidence: continuationConfidence,
            BackgroundPreparationAllowed: backgroundPreparationAllowed,
            BackgroundPendingReadOnlyActions: backgroundPendingReadOnlyActions,
            BackgroundPendingUnknownActions: backgroundPendingUnknownActions,
            BackgroundFollowUpClasses: backgroundFollowUpClasses,
            BackgroundPriorityFocus: backgroundPriorityFocus,
            BackgroundFollowUpFocus: backgroundFollowUpFocus,
            BackgroundRecentEvidenceTools: backgroundRecentEvidenceTools,
            MatchingSkills: matchingSkills,
            AllowCachedEvidenceReuse: allowCachedEvidenceReuse);
        return sawMarker && parsedAnyStructuredValue;
    }

    private static bool HasPlannerContextSignals(PlannerContextMetadata context) {
        return context.RequiresLiveExecution
               || context.MissingLiveEvidence.Length > 0
               || context.PreferredPackIds.Length > 0
               || context.PreferredToolNames.Length > 0
               || context.PreferredDeferredWorkCapabilityIds.Length > 0
               || context.StructuredNextActionSourceToolNames.Length > 0
               || context.StructuredNextActionReason.Length > 0
               || context.StructuredNextActionConfidence.HasValue
               || context.PreferredExecutionBackends.Length > 0
               || context.HandoffTargetPackIds.Length > 0
               || context.HandoffTargetToolNames.Length > 0
               || context.ContinuationSourceTool.Length > 0
               || context.ContinuationReason.Length > 0
               || context.ContinuationConfidence.Length > 0
               || context.BackgroundPreparationAllowed
               || context.BackgroundPendingReadOnlyActions > 0
               || context.BackgroundPendingUnknownActions > 0
               || context.BackgroundFollowUpClasses.Length > 0
               || context.BackgroundPriorityFocus.Length > 0
               || context.BackgroundFollowUpFocus.Length > 0
               || context.BackgroundRecentEvidenceTools.Length > 0
               || context.MatchingSkills.Length > 0
               || context.AllowCachedEvidenceReuse;
    }

    private static string BuildPlannerContextText(PlannerContextMetadata context, string? requestText = null) {
        if (!HasPlannerContextSignals(context)) {
            return string.Empty;
        }

        var normalizedRequest = (requestText ?? string.Empty).Trim();
        var builder = new StringBuilder((normalizedRequest.Length > 0 ? normalizedRequest.Length : 0) + 512);
        if (normalizedRequest.Length > 0) {
            builder.AppendLine(normalizedRequest);
            builder.AppendLine();
        }

        builder.AppendLine("[Planner context]");
        builder.AppendLine(PlannerContextMarker);
        builder.Append("requires_live_execution: ")
            .AppendLine(context.RequiresLiveExecution ? "true" : "false");
        if (context.MissingLiveEvidence.Length > 0) {
            builder.Append("missing_live_evidence: ")
                .AppendLine(context.MissingLiveEvidence);
        }

        if (context.PreferredPackIds.Length > 0) {
            builder.Append("preferred_pack_ids: ")
                .AppendLine(string.Join(", ", context.PreferredPackIds));
        }

        if (context.PreferredToolNames.Length > 0) {
            builder.Append("preferred_tool_names: ")
                .AppendLine(string.Join(", ", context.PreferredToolNames));
        }

        if (context.PreferredDeferredWorkCapabilityIds.Length > 0) {
            builder.Append("preferred_deferred_work_capability_ids: ")
                .AppendLine(string.Join(", ", context.PreferredDeferredWorkCapabilityIds));
        }

        if (context.StructuredNextActionSourceToolNames.Length > 0) {
            builder.Append("structured_next_action_source_tools: ")
                .AppendLine(string.Join(", ", context.StructuredNextActionSourceToolNames));
        }

        if (context.StructuredNextActionReason.Length > 0) {
            builder.Append("structured_next_action_reason: ")
                .AppendLine(context.StructuredNextActionReason);
        }

        if (context.StructuredNextActionConfidence.HasValue) {
            builder.Append("structured_next_action_confidence: ")
                .AppendLine(context.StructuredNextActionConfidence.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
        }

        if (context.PreferredExecutionBackends.Length > 0) {
            builder.Append("preferred_execution_backends: ")
                .AppendLine(string.Join(", ", context.PreferredExecutionBackends));
        }

        if (context.HandoffTargetPackIds.Length > 0) {
            builder.Append("handoff_target_pack_ids: ")
                .AppendLine(string.Join(", ", context.HandoffTargetPackIds));
        }

        if (context.HandoffTargetToolNames.Length > 0) {
            builder.Append("handoff_target_tool_names: ")
                .AppendLine(string.Join(", ", context.HandoffTargetToolNames));
        }

        if (context.ContinuationSourceTool.Length > 0) {
            builder.Append("continuation_source_tool: ")
                .AppendLine(context.ContinuationSourceTool);
        }

        if (context.ContinuationReason.Length > 0) {
            builder.Append("continuation_reason: ")
                .AppendLine(context.ContinuationReason);
        }

        if (context.ContinuationConfidence.Length > 0) {
            builder.Append("continuation_confidence: ")
                .AppendLine(context.ContinuationConfidence);
        }

        if (context.BackgroundPreparationAllowed) {
            builder.AppendLine("background_preparation_allowed: true");
        }

        if (context.BackgroundPendingReadOnlyActions > 0) {
            builder.Append("background_pending_read_only_actions: ")
                .AppendLine(context.BackgroundPendingReadOnlyActions.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (context.BackgroundPendingUnknownActions > 0) {
            builder.Append("background_pending_unknown_actions: ")
                .AppendLine(context.BackgroundPendingUnknownActions.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (context.BackgroundFollowUpClasses.Length > 0) {
            builder.Append("background_follow_up_classes: ")
                .AppendLine(string.Join(", ", context.BackgroundFollowUpClasses));
        }

        if (context.BackgroundPriorityFocus.Length > 0) {
            builder.Append("background_priority_focus: ")
                .AppendLine(context.BackgroundPriorityFocus);
        }

        if (context.BackgroundFollowUpFocus.Length > 0) {
            builder.Append("background_follow_up_focus: ")
                .AppendLine(context.BackgroundFollowUpFocus);
        }

        if (context.BackgroundRecentEvidenceTools.Length > 0) {
            builder.Append("background_recent_evidence_tools: ")
                .AppendLine(string.Join(", ", context.BackgroundRecentEvidenceTools));
        }

        if (context.MatchingSkills.Length > 0) {
            builder.Append("matching_skills: ")
                .AppendLine(string.Join(", ", context.MatchingSkills));
        }

        builder.Append("allow_cached_evidence_reuse: ")
            .AppendLine(context.AllowCachedEvidenceReuse ? "true" : "false");
        return builder.ToString().TrimEnd();
    }

    private static string NormalizeDeferredWorkCapabilityId(string? value) {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return string.Empty;
        }

        if (ToolPackCapabilityTags.TryGetDeferredCapabilityId(normalized, out var capabilityId)) {
            return capabilityId;
        }

        var capabilityTag = ToolPackCapabilityTags.CreateDeferredCapabilityTag(normalized);
        if (capabilityTag.Length == 0) {
            return string.Empty;
        }

        return ToolPackCapabilityTags.TryGetDeferredCapabilityId(capabilityTag, out capabilityId)
            ? capabilityId
            : string.Empty;
    }

    private static string[] ReadRememberedToolExecutionBackendHintsFromRequestText(string requestText) {
        var raw = requestText ?? string.Empty;
        if (raw.Length == 0
            || (raw.IndexOf("preferred_execution_backends", StringComparison.OrdinalIgnoreCase) < 0
                && raw.IndexOf("recent_tool_execution_backends", StringComparison.OrdinalIgnoreCase) < 0)) {
            return Array.Empty<string>();
        }

        var rememberedHints = new List<string>(MaxToolExecutionBackendHints * 2);
        using var reader = new StringReader(raw);
        while (reader.ReadLine() is { } line) {
            var trimmed = line.Trim();
            if (trimmed.Length == 0) {
                continue;
            }

            if (TryParseStructuredKeyValueLine(trimmed, "preferred_execution_backends", out var hintsValue)
                || TryParseStructuredKeyValueLine(trimmed, "recent_tool_execution_backends", out hintsValue)) {
                rememberedHints.AddRange(NormalizeStructuredMetadataCsv(
                    hintsValue,
                    static value => NormalizeToolExecutionBackendHint(value),
                    MaxToolExecutionBackendHints));
            }
        }

        return NormalizeDistinctStrings(rememberedHints, MaxToolExecutionBackendHints);
    }

    private static string NormalizeContinuationConfidence(string? value) {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return string.Empty;
        }

        if (normalized.Equals("high", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("medium", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("low", StringComparison.OrdinalIgnoreCase)) {
            return normalized.ToLowerInvariant();
        }

        return string.Empty;
    }

    private static string NormalizeContinuationConfidence(double? value) {
        if (!value.HasValue || double.IsNaN(value.Value) || double.IsInfinity(value.Value)) {
            return string.Empty;
        }

        var normalized = Math.Clamp(value.Value, 0d, 1d);
        return normalized >= 0.72d
            ? "high"
            : normalized >= 0.45d
                ? "medium"
                : "low";
    }
}
