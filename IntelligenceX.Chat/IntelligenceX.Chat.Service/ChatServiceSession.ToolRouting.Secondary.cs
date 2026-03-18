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

    private async Task<IReadOnlyList<ToolDefinition>> TrySelectToolsViaModelPlannerAsync(IntelligenceXClient client, string activeThreadId, string requestText,
        IReadOnlyList<ToolDefinition> definitions, int limit, CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(activeThreadId)
            || string.IsNullOrWhiteSpace(requestText)
            || definitions.Count == 0
            || limit <= 0) {
            return Array.Empty<ToolDefinition>();
        }

        IReadOnlyList<ToolDefinition> selected = Array.Empty<ToolDefinition>();
        Exception? plannerFailure = null;
        Exception? restoreFailure = null;
        try {
            var plannerPrompt = BuildModelPlannerPrompt(requestText, definitions, limit);
            if (plannerPrompt.Length == 0) {
                return Array.Empty<ToolDefinition>();
            }

            var plannerOptions = new ChatOptions {
                Model = _options.Model,
                Tools = null,
                ToolChoice = ToolChoice.None,
                ParallelToolCalls = false,
                Temperature = 0,
                ReasoningEffort = ReasoningEffort.Minimal,
                ReasoningSummary = ReasoningSummary.Off,
                TextVerbosity = TextVerbosity.Low,
                Instructions = """
                    You are a semantic tool router.
                    Select the most relevant tools for the user request from the provided catalog.
                    Return strict JSON only with this shape:
                    {"tool_names":["tool_a","tool_b"]}
                    Rules:
                    - Use only tool names present in the provided list.
                    - Prefer precision over recall.
                    - Return at most the requested max count.
                    - Do not add commentary or markdown.
                    """
            };

            _ = await EnsurePlannerThreadAsync(client, activeThreadId, plannerOptions.Model, cancellationToken).ConfigureAwait(false);

            var turn = await client.ChatAsync(ChatInput.FromText(plannerPrompt), plannerOptions, cancellationToken).ConfigureAwait(false);
            var plannerText = EasyChatResult.FromTurn(turn).Text ?? string.Empty;
            selected = ParsePlannerSelectedDefinitions(plannerText, definitions, limit);
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            plannerFailure = ex;
        } finally {
            try {
                await client.UseThreadAsync(activeThreadId, cancellationToken).ConfigureAwait(false);
            } catch (Exception ex) {
                restoreFailure = ex;
                ForgetPlannerThreadContext(activeThreadId);
            }
        }

        if (restoreFailure is not null) {
            Trace.TraceWarning(
                $"Tool planner failed to restore active thread '{activeThreadId}': {restoreFailure.GetType().Name}: {restoreFailure.Message}");
            return Array.Empty<ToolDefinition>();
        }

        if (plannerFailure is not null) {
            Trace.TraceWarning($"Tool planner selection failed: {plannerFailure.GetType().Name}: {plannerFailure.Message}");
            return Array.Empty<ToolDefinition>();
        }

        return selected;
    }

    private async Task<ThreadInfo> EnsurePlannerThreadAsync(IntelligenceXClient client, string activeThreadId, string? model,
        CancellationToken cancellationToken) {
        var normalizedActiveThreadId = (activeThreadId ?? string.Empty).Trim();
        if (normalizedActiveThreadId.Length == 0) {
            throw new ArgumentException("activeThreadId is required.", nameof(activeThreadId));
        }

        if (TryResolvePlannerThreadContext(normalizedActiveThreadId, out var plannerThreadId)) {
            try {
                return await client.UseThreadAsync(plannerThreadId, cancellationToken).ConfigureAwait(false);
            } catch {
                ForgetPlannerThreadContext(normalizedActiveThreadId);
            }
        }

        var plannerThread = await client.StartNewThreadAsync(
                model: model,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        RememberPlannerThreadContext(normalizedActiveThreadId, plannerThread.Id, DateTime.UtcNow.Ticks);

        // StartNewThreadAsync should set the current thread, but make the thread switch explicit to avoid
        // accidental planner prompts polluting the active conversation thread if transport semantics change.
        try {
            return await client.UseThreadAsync(plannerThread.Id, cancellationToken).ConfigureAwait(false);
        } catch {
            return plannerThread;
        }
    }

    private void RememberPlannerThreadContext(string activeThreadId, string plannerThreadId, long seenUtcTicks) {
        var normalizedActiveThreadId = (activeThreadId ?? string.Empty).Trim();
        var normalizedPlannerThreadId = (plannerThreadId ?? string.Empty).Trim();
        if (normalizedActiveThreadId.Length == 0 || normalizedPlannerThreadId.Length == 0) {
            return;
        }

        var ticks = seenUtcTicks > 0 ? seenUtcTicks : DateTime.UtcNow.Ticks;
        lock (_toolRoutingContextLock) {
            _plannerThreadIdByActiveThreadId[normalizedActiveThreadId] = normalizedPlannerThreadId;
            _plannerThreadSeenUtcTicksByActiveThreadId[normalizedActiveThreadId] = ticks;
            TrimWeightedRoutingContextsNoLock();
        }
        PersistPlannerThreadContextSnapshot(normalizedActiveThreadId, normalizedPlannerThreadId, ticks);
    }

    private void ForgetPlannerThreadContext(string activeThreadId) {
        var normalizedActiveThreadId = (activeThreadId ?? string.Empty).Trim();
        if (normalizedActiveThreadId.Length == 0) {
            return;
        }

        lock (_toolRoutingContextLock) {
            _plannerThreadIdByActiveThreadId.Remove(normalizedActiveThreadId);
            _plannerThreadSeenUtcTicksByActiveThreadId.Remove(normalizedActiveThreadId);
        }
        RemovePlannerThreadContextSnapshot(normalizedActiveThreadId);
    }

    private bool TryResolvePlannerThreadContext(string activeThreadId, out string plannerThreadId) {
        plannerThreadId = string.Empty;
        var normalizedActiveThreadId = (activeThreadId ?? string.Empty).Trim();
        if (normalizedActiveThreadId.Length == 0) {
            return false;
        }

        var nowUtc = DateTime.UtcNow;
        var nowTicks = nowUtc.Ticks;
        string? trackedPlannerThreadId = null;
        var evictSnapshot = false;

        lock (_toolRoutingContextLock) {
            if (_plannerThreadIdByActiveThreadId.TryGetValue(normalizedActiveThreadId, out var cachedPlannerThreadId)
                && !string.IsNullOrWhiteSpace(cachedPlannerThreadId)) {
                var cached = cachedPlannerThreadId.Trim();
                if (_plannerThreadSeenUtcTicksByActiveThreadId.TryGetValue(normalizedActiveThreadId, out var trackedTicks)
                    && TryGetUtcDateTimeFromTicks(trackedTicks, out var trackedSeenUtc)
                    && nowUtc - trackedSeenUtc <= PlannerThreadContextMaxAge) {
                    trackedPlannerThreadId = cached;
                } else {
                    _plannerThreadIdByActiveThreadId.Remove(normalizedActiveThreadId);
                    _plannerThreadSeenUtcTicksByActiveThreadId.Remove(normalizedActiveThreadId);
                    evictSnapshot = true;
                }
            }
        }

        if (evictSnapshot) {
            RemovePlannerThreadContextSnapshot(normalizedActiveThreadId);
        }

        if (string.IsNullOrWhiteSpace(trackedPlannerThreadId)) {
            if (!TryLoadPlannerThreadContextSnapshot(normalizedActiveThreadId, out var persistedPlannerThreadId, out _)) {
                return false;
            }

            trackedPlannerThreadId = persistedPlannerThreadId;
        }

        plannerThreadId = trackedPlannerThreadId.Trim();
        if (plannerThreadId.Length == 0) {
            RemovePlannerThreadContextSnapshot(normalizedActiveThreadId);
            return false;
        }

        lock (_toolRoutingContextLock) {
            _plannerThreadIdByActiveThreadId[normalizedActiveThreadId] = plannerThreadId;
            _plannerThreadSeenUtcTicksByActiveThreadId[normalizedActiveThreadId] = nowTicks;
            TrimWeightedRoutingContextsNoLock();
        }
        PersistPlannerThreadContextSnapshot(normalizedActiveThreadId, plannerThreadId, nowTicks);
        return true;
    }

    private IReadOnlyList<ToolDefinition> EnsureMinimumToolSelection(string userRequest, IReadOnlyList<ToolDefinition> allDefinitions,
        IReadOnlyList<ToolDefinition> initialSelected, int limit) {
        if (allDefinitions.Count == 0 || limit <= 0) {
            return Array.Empty<ToolDefinition>();
        }

        var explicitRequestedToolNames = BuildExplicitRequestedToolNameSet(userRequest);
        var selected = new List<ToolDefinition>(Math.Min(limit, allDefinitions.Count));
        var selectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < initialSelected.Count && selected.Count < limit; i++) {
            var definition = initialSelected[i];
            if (definition is null || string.IsNullOrWhiteSpace(definition.Name) || !selectedNames.Add(definition.Name)) {
                continue;
            }
            selected.Add(definition);
        }

        var minSelection = Math.Min(allDefinitions.Count, Math.Min(limit, 12));
        EnsureExplicitRequestedToolsSelected(explicitRequestedToolNames, allDefinitions, selected, selectedNames, limit);
        if (selected.Count >= minSelection) {
            return selected;
        }

        var rankedFallback = new List<(ToolDefinition Definition, double Score)>(allDefinitions.Count);
        for (var i = 0; i < allDefinitions.Count; i++) {
            var definition = allDefinitions[i];
            if (definition is null || string.IsNullOrWhiteSpace(definition.Name) || selectedNames.Contains(definition.Name)) {
                continue;
            }

            var score = 0d;
            if (IsExplicitRequestedToolMatch(definition.Name, explicitRequestedToolNames)) {
                score += 9d;
            }
            if (!string.IsNullOrWhiteSpace(userRequest)
                && userRequest.IndexOf(definition.Name, StringComparison.OrdinalIgnoreCase) >= 0) {
                score += 6d;
            }

            score += ReadToolRoutingAdjustment(definition.Name);
            rankedFallback.Add((definition, score));
        }

        rankedFallback.Sort(static (left, right) => {
            var scoreCompare = right.Score.CompareTo(left.Score);
            if (scoreCompare != 0) {
                return scoreCompare;
            }
            return StringComparer.OrdinalIgnoreCase.Compare(left.Definition.Name, right.Definition.Name);
        });

        for (var i = 0; i < rankedFallback.Count && selected.Count < minSelection; i++) {
            var definition = rankedFallback[i].Definition;
            if (selectedNames.Add(definition.Name)) {
                selected.Add(definition);
            }
        }

        EnsureExplicitRequestedToolsSelected(explicitRequestedToolNames, allDefinitions, selected, selectedNames, limit);
        return selected.Count == 0 ? allDefinitions : selected;
    }

    private static HashSet<string>? BuildExplicitRequestedToolNameSet(string userRequest) {
        var requestedToolNames = ExtractExplicitRequestedToolNames(userRequest);
        if (requestedToolNames.Length == 0) {
            return null;
        }

        var set = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < requestedToolNames.Length; i++) {
            var normalized = (requestedToolNames[i] ?? string.Empty).Trim();
            if (normalized.Length == 0) {
                continue;
            }

            set.Add(normalized);
        }

        return set.Count == 0 ? null : set;
    }

    private static bool IsExplicitRequestedToolMatch(string toolName, HashSet<string>? explicitRequestedToolNames) {
        if (explicitRequestedToolNames is null || explicitRequestedToolNames.Count == 0) {
            return false;
        }

        var normalizedToolName = NormalizeCompactToken((toolName ?? string.Empty).AsSpan());
        if (normalizedToolName.Length == 0) {
            return false;
        }

        return explicitRequestedToolNames.Contains(normalizedToolName);
    }

    private static void EnsureExplicitRequestedToolsSelected(
        HashSet<string>? explicitRequestedToolNames,
        IReadOnlyList<ToolDefinition> allDefinitions,
        IList<ToolDefinition> selected,
        ISet<string> selectedNames,
        int limit) {
        if (explicitRequestedToolNames is null
            || explicitRequestedToolNames.Count == 0
            || allDefinitions.Count == 0
            || limit <= 0) {
            return;
        }

        var explicitCandidates = new List<ToolDefinition>(Math.Min(allDefinitions.Count, 8));
        var candidateNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < allDefinitions.Count; i++) {
            var definition = allDefinitions[i];
            if (definition is null || string.IsNullOrWhiteSpace(definition.Name)) {
                continue;
            }

            var definitionName = definition.Name.Trim();
            if (!IsExplicitRequestedToolMatch(definitionName, explicitRequestedToolNames)
                || !candidateNames.Add(definitionName)) {
                continue;
            }

            explicitCandidates.Add(definition);
            if (explicitCandidates.Count >= 8) {
                break;
            }
        }

        if (explicitCandidates.Count == 0) {
            return;
        }

        for (var i = 0; i < explicitCandidates.Count; i++) {
            var candidate = explicitCandidates[i];
            var candidateName = (candidate.Name ?? string.Empty).Trim();
            if (candidateName.Length == 0 || selectedNames.Contains(candidateName)) {
                continue;
            }

            if (selected.Count < limit) {
                selected.Add(candidate);
                selectedNames.Add(candidateName);
                continue;
            }

            var replaceIndex = FindExplicitToolReplacementIndex(selected, explicitRequestedToolNames);
            if (replaceIndex < 0 || replaceIndex >= selected.Count) {
                continue;
            }

            var replacedName = (selected[replaceIndex].Name ?? string.Empty).Trim();
            selected[replaceIndex] = candidate;
            if (replacedName.Length > 0) {
                selectedNames.Remove(replacedName);
            }
            selectedNames.Add(candidateName);
        }
    }

    private static int FindExplicitToolReplacementIndex(IList<ToolDefinition> selected, HashSet<string> explicitRequestedToolNames) {
        for (var i = selected.Count - 1; i >= 0; i--) {
            var selectedName = (selected[i].Name ?? string.Empty).Trim();
            if (selectedName.Length == 0) {
                return i;
            }

            if (!IsExplicitRequestedToolMatch(selectedName, explicitRequestedToolNames)) {
                return i;
            }
        }

        return -1;
    }

    private static List<ToolRoutingInsight> BuildModelRoutingInsights(IReadOnlyList<ToolDefinition> selectedDefs, int plannedCount) {
        var list = new List<ToolRoutingInsight>(Math.Min(12, selectedDefs.Count));
        if (selectedDefs.Count == 0) {
            return list;
        }

        for (var i = 0; i < selectedDefs.Count && i < 12; i++) {
            var name = (selectedDefs[i].Name ?? string.Empty).Trim();
            if (name.Length == 0) {
                continue;
            }

            var fromPlanner = i < plannedCount;
            var confidence = i < 3 ? "high" : i < 8 ? "medium" : "low";
            var reason = fromPlanner
                ? "semantic planner selection"
                : "semantic planner backfill with routing history";
            var score = Math.Max(0.2d, 1d - (i * 0.06d));
            list.Add(new ToolRoutingInsight(
                ToolName: name,
                Confidence: confidence,
                Score: Math.Round(score, 3),
                Reason: reason,
                Strategy: ToolRoutingInsightStrategy.SemanticPlanner));
        }

        return list;
    }

    private string BuildModelPlannerPrompt(string requestText, IReadOnlyList<ToolDefinition> definitions, int limit) {
        if (definitions.Count == 0) {
            return string.Empty;
        }

        var userRequest = ExtractPrimaryUserRequest(requestText);
        _ = TryReadPlannerContextFromRequestText(requestText, out var plannerContext);
        var sb = new StringBuilder(capacity: Math.Min(64_000, 4000 + (definitions.Count * 120)));
        sb.AppendLine("Select tools for the following user request.");
        sb.AppendLine("User request:");
        sb.AppendLine(userRequest.Trim());
        if (TryReadContinuationFocusUnresolvedAskFromWorkingMemoryPrompt(requestText, out var unresolvedAsk)) {
            sb.AppendLine();
            sb.AppendLine("Current unresolved follow-up focus:");
            sb.AppendLine(unresolvedAsk);
        }
        if (plannerContext.RequiresLiveExecution || plannerContext.MissingLiveEvidence.Length > 0) {
            sb.AppendLine();
            sb.AppendLine("Execution intent:");
            sb.AppendLine(plannerContext.RequiresLiveExecution
                ? "Fresh live execution is required for this follow-up."
                : "Fresh live execution is optional for this follow-up.");
            if (plannerContext.MissingLiveEvidence.Length > 0) {
                sb.Append("Missing live evidence: ").AppendLine(plannerContext.MissingLiveEvidence);
            }
        }
        if (plannerContext.PreferredPackIds.Length > 0
            || plannerContext.PreferredToolNames.Length > 0
            || plannerContext.PreferredExecutionBackends.Length > 0) {
            sb.AppendLine();
            sb.AppendLine("Planner preferences:");
            if (plannerContext.PreferredPackIds.Length > 0) {
                sb.Append("Preferred packs: ").AppendLine(string.Join(", ", plannerContext.PreferredPackIds));
            }
            if (plannerContext.PreferredToolNames.Length > 0) {
                sb.Append("Preferred tools: ").AppendLine(string.Join(", ", plannerContext.PreferredToolNames));
            }
            if (plannerContext.StructuredNextActionSourceToolNames.Length > 0) {
                sb.Append("Structured source tools: ").AppendLine(string.Join(", ", plannerContext.StructuredNextActionSourceToolNames));
            }
            if (plannerContext.StructuredNextActionReason.Length > 0) {
                sb.Append("Structured next-action reason: ").AppendLine(plannerContext.StructuredNextActionReason);
            }
            if (plannerContext.StructuredNextActionConfidence.HasValue) {
                sb.Append("Structured next-action confidence: ")
                    .AppendLine(plannerContext.StructuredNextActionConfidence.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
            }
            if (plannerContext.PreferredExecutionBackends.Length > 0) {
                sb.Append("Preferred execution backends: ")
                    .AppendLine(string.Join(", ", plannerContext.PreferredExecutionBackends));
            }
        }
        if (plannerContext.HandoffTargetPackIds.Length > 0 || plannerContext.HandoffTargetToolNames.Length > 0) {
            sb.AppendLine();
            sb.AppendLine("Handoff targets:");
            if (plannerContext.HandoffTargetPackIds.Length > 0) {
                sb.Append("Target packs: ").AppendLine(string.Join(", ", plannerContext.HandoffTargetPackIds));
            }
            if (plannerContext.HandoffTargetToolNames.Length > 0) {
                sb.Append("Target tools: ").AppendLine(string.Join(", ", plannerContext.HandoffTargetToolNames));
            }
        }
        if (plannerContext.ContinuationSourceTool.Length > 0
            || plannerContext.ContinuationReason.Length > 0
            || plannerContext.ContinuationConfidence.Length > 0) {
            sb.AppendLine();
            sb.AppendLine("Structured continuation hint:");
            if (plannerContext.ContinuationSourceTool.Length > 0) {
                sb.Append("Source tool: ").AppendLine(plannerContext.ContinuationSourceTool);
            }
            if (plannerContext.ContinuationReason.Length > 0) {
                sb.Append("Reason: ").AppendLine(plannerContext.ContinuationReason);
            }
            if (plannerContext.ContinuationConfidence.Length > 0) {
                sb.Append("Confidence: ").AppendLine(plannerContext.ContinuationConfidence);
            }
        }
        if (plannerContext.BackgroundPreparationAllowed
            || plannerContext.BackgroundPendingReadOnlyActions > 0
            || plannerContext.BackgroundPendingUnknownActions > 0
            || plannerContext.BackgroundFollowUpClasses.Length > 0
            || plannerContext.BackgroundPriorityFocus.Length > 0
            || plannerContext.BackgroundFollowUpFocus.Length > 0
            || plannerContext.BackgroundRecentEvidenceTools.Length > 0) {
            sb.AppendLine();
            sb.AppendLine("Background preparation:");
            sb.AppendLine(plannerContext.BackgroundPreparationAllowed
                ? "Read-only follow-up preparation is allowed for this thread."
                : "Background preparation is not currently marked safe for this thread.");
            if (plannerContext.BackgroundPendingReadOnlyActions > 0) {
                sb.Append("Pending read-only actions: ")
                    .AppendLine(plannerContext.BackgroundPendingReadOnlyActions.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            if (plannerContext.BackgroundPendingUnknownActions > 0) {
                sb.Append("Pending unknown-mutability actions: ")
                    .AppendLine(plannerContext.BackgroundPendingUnknownActions.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            if (plannerContext.BackgroundFollowUpClasses.Length > 0) {
                sb.Append("Follow-up classes: ")
                    .AppendLine(string.Join(", ", plannerContext.BackgroundFollowUpClasses));
            }
            if (plannerContext.BackgroundPriorityFocus.Length > 0) {
                sb.Append("Priority focus: ")
                    .AppendLine(plannerContext.BackgroundPriorityFocus);
            }
            if (plannerContext.BackgroundFollowUpFocus.Length > 0) {
                sb.Append("Preparation focus: ").AppendLine(plannerContext.BackgroundFollowUpFocus);
            }
            if (plannerContext.BackgroundRecentEvidenceTools.Length > 0) {
                sb.Append("Recent evidence tools: ")
                    .AppendLine(string.Join(", ", plannerContext.BackgroundRecentEvidenceTools));
            }
        }
        if (plannerContext.MatchingSkills.Length > 0) {
            sb.AppendLine();
            sb.AppendLine("Matching reusable skills:");
            sb.AppendLine(string.Join(", ", plannerContext.MatchingSkills));
        }
        if (TryReadContinuationFocusCachedEvidenceReusePreferenceFromWorkingMemoryPrompt(requestText, out var preferCachedEvidenceReuse, out var cachedEvidenceReuseReason)
            && preferCachedEvidenceReuse) {
            sb.AppendLine();
            sb.AppendLine("Continuation preference:");
            sb.AppendLine("Reuse the latest fresh read-only evidence snapshot if it is still sufficient.");
            if (cachedEvidenceReuseReason.Length > 0) {
                sb.Append("Preference reason: ").AppendLine(cachedEvidenceReuseReason);
            }
        }
        var executionAvailabilitySummary = ToolExecutionAvailabilityHints.BuildSummary(definitions);
        var executionAvailabilityHints = ToolExecutionAvailabilityHints.BuildPromptHintLines(definitions);
        if (executionAvailabilitySummary.ToolCount > 0) {
            sb.AppendLine();
            sb.AppendLine("Execution locality:");
            sb.AppendLine(BuildPlannerExecutionAvailabilitySummaryLine(executionAvailabilitySummary));
            for (var hintIndex = 0; hintIndex < executionAvailabilityHints.Count; hintIndex++) {
                var hintLine = (executionAvailabilityHints[hintIndex] ?? string.Empty).Trim();
                if (hintLine.Length == 0) {
                    continue;
                }

                sb.AppendLine(hintLine);
            }
        }

        var plannerPromptHintEntries = CollectPlannerPromptHintEntries(definitions, plannerContext, _toolOrchestrationCatalog);
        var representativeExamples = ToolContractPromptExamples.BuildRepresentativeExamples(plannerPromptHintEntries);
        var crossPackTargets = ToolContractPromptExamples.BuildCrossPackTargetPackDisplayNames(plannerPromptHintEntries);
        if (representativeExamples.Count > 0 || crossPackTargets.Count > 0) {
            sb.AppendLine();
            sb.AppendLine("Contract-backed capability hints:");
            if (representativeExamples.Count > 0) {
                sb.Append("Representative live tool examples: ")
                    .AppendLine(string.Join("; ", representativeExamples));
            }

            if (crossPackTargets.Count > 0) {
                sb.AppendLine(ToolRepresentativeExamples.BuildCrossPackSummary(crossPackTargets));
            }
        }
        var hasWriteCapableTools = definitions.Any(static definition => definition.WriteGovernance?.IsWriteCapable == true);
        var hasAuthenticationAwareTools = definitions.Any(static definition => definition.Authentication?.IsAuthenticationAware == true);
        var hasProbeAwareTools = definitions.Any(static definition => definition.Authentication?.SupportsConnectivityProbe == true);
        if (hasWriteCapableTools || hasAuthenticationAwareTools || hasProbeAwareTools) {
            sb.AppendLine();
            sb.AppendLine("Contract-backed planning rules:");
            if (hasProbeAwareTools) {
                sb.AppendLine("Prefer declared probe/setup helpers before dependent remote or mutating follow-up tools.");
            }
            if (hasAuthenticationAwareTools) {
                sb.AppendLine("Treat tools marked auth-required as needing a valid runtime auth/profile context.");
            }
            if (hasWriteCapableTools) {
                sb.AppendLine("Treat tools marked write-capable as confirmation-gated follow-up actions, not default discovery steps.");
            }
        }
        sb.AppendLine();
        sb.AppendLine($"Return at most {Math.Max(1, limit)} tool names.");
        sb.AppendLine("Available tools:");

        for (var i = 0; i < definitions.Count; i++) {
            var definition = definitions[i];
            if (definition is null || string.IsNullOrWhiteSpace(definition.Name)) {
                continue;
            }

            var name = definition.Name.Trim();
            var description = (definition.Description ?? string.Empty).Trim();
            if (description.Length > 220) {
                description = description[..220].TrimEnd();
            }
            var schemaArguments = ExtractToolSchemaPropertyNames(definition, maxCount: 8, out var schemaTraits);
            var requiredArguments = ExtractToolSchemaRequiredNames(definition, maxCount: 4);
            var category = ResolvePlannerCategory(definition);
            var packId = NormalizePackId(definition.Routing?.PackId);
            var packEngineId = ResolvePlannerPackEngineId(packId);
            var packCapabilityTags = ResolvePlannerPackCapabilityTags(packId, maxCount: 3);
            var role = ResolvePlannerRole(definition);
            var domainIntentFamily = ResolveDomainIntentFamily(definition);
            var plannerTags = ExtractPlannerTags(definition, maxCount: 4);
            var traitSummary = ToolSchemaTraitProjection.BuildTraitSummary(schemaTraits);
            var authentication = definition.Authentication;
            sb.Append(i + 1).Append(". ").Append(name);
            if (description.Length > 0) {
                sb.Append(" :: ").Append(description);
            }
            if (packId.Length > 0) {
                sb.Append(" | pack: ").Append(packId);
            }
            if (packEngineId.Length > 0) {
                sb.Append(" | engine: ").Append(packEngineId);
            }
            if (role.Length > 0) {
                sb.Append(" | role: ").Append(role);
            }
            if (category.Length > 0) {
                sb.Append(" | category: ").Append(category);
            }
            if (domainIntentFamily.Length > 0) {
                sb.Append(" | family: ").Append(domainIntentFamily);
            }
            if (plannerTags.Length > 0) {
                sb.Append(" | tags: ").Append(string.Join(", ", plannerTags));
            }
            if (packCapabilityTags.Length > 0) {
                sb.Append(" | pack_traits: ").Append(string.Join(", ", packCapabilityTags));
            }
            if (requiredArguments.Length > 0) {
                sb.Append(" | required: ").Append(string.Join(", ", requiredArguments));
            }
            if (schemaArguments.Length > 0) {
                sb.Append(" | args: ").Append(string.Join(", ", schemaArguments));
            }
            if (traitSummary.Length > 0) {
                sb.Append(" | traits: ").Append(traitSummary);
            }
            if (definition.WriteGovernance?.IsWriteCapable == true) {
                sb.Append(" | write: mutating");
            }
            if (authentication?.RequiresAuthentication == true) {
                sb.Append(" | auth: required");
                var authenticationContractId = (authentication.AuthenticationContractId ?? string.Empty).Trim();
                if (authenticationContractId.Length > 0) {
                    sb.Append('(').Append(authenticationContractId).Append(')');
                }

                var authenticationArguments = authentication.GetSchemaArgumentNames();
                if (authenticationArguments.Count > 0) {
                    sb.Append(" | auth_args: ").Append(string.Join(", ", authenticationArguments));
                }
            }
            if (authentication?.SupportsConnectivityProbe == true) {
                var probeToolName = (authentication.ProbeToolName ?? string.Empty).Trim();
                if (probeToolName.Length > 0) {
                    sb.Append(" | probe: ").Append(probeToolName);
                } else {
                    sb.Append(" | probe: supported");
                }
            }
            var setupToolName = (definition.Setup?.SetupToolName ?? string.Empty).Trim();
            if (setupToolName.Length > 0) {
                sb.Append(" | setup: ").Append(setupToolName);
            }
            var recoveryToolNames = ExtractToolRecoveryHelperNames(definition, maxCount: 2);
            if (recoveryToolNames.Length > 0) {
                sb.Append(" | recovery: ").Append(string.Join(", ", recoveryToolNames));
            }
            var handoffTargets = ExtractToolHandoffTargets(definition, maxCount: 3);
            if (handoffTargets.Length > 0) {
                sb.Append(" | handoff: ").Append(string.Join(", ", handoffTargets));
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string BuildPlannerExecutionAvailabilitySummaryLine(ToolExecutionAvailabilitySummary summary) {
        if (summary.ToolCount <= 0) {
            return "Execution locality is unavailable for the current catalog.";
        }

        if (summary.IsLocalOnly) {
            return "Current candidate tools are local-only (local-only "
                   + summary.LocalOnlyTools.ToString(System.Globalization.CultureInfo.InvariantCulture)
                   + ").";
        }

        if (summary.HasMixedLocality) {
            return "Current candidate tools have mixed locality (local-only "
                   + summary.LocalOnlyTools.ToString(System.Globalization.CultureInfo.InvariantCulture)
                   + ", remote-only "
                   + summary.RemoteOnlyTools.ToString(System.Globalization.CultureInfo.InvariantCulture)
                   + ", local-or-remote "
                   + summary.LocalOrRemoteTools.ToString(System.Globalization.CultureInfo.InvariantCulture)
                   + ").";
        }

        if (summary.IsRemoteReadyOnly) {
            return "Current candidate tools are remote-ready (remote-only "
                   + summary.RemoteOnlyTools.ToString(System.Globalization.CultureInfo.InvariantCulture)
                   + ", local-or-remote "
                   + summary.LocalOrRemoteTools.ToString(System.Globalization.CultureInfo.InvariantCulture)
                   + ").";
        }

        return "Execution locality includes local-only "
               + summary.LocalOnlyTools.ToString(System.Globalization.CultureInfo.InvariantCulture)
               + ", remote-only "
               + summary.RemoteOnlyTools.ToString(System.Globalization.CultureInfo.InvariantCulture)
               + ", and local-or-remote "
               + summary.LocalOrRemoteTools.ToString(System.Globalization.CultureInfo.InvariantCulture)
               + " tools.";
    }

    private string ResolvePlannerPackEngineId(string normalizedPackId) {
        normalizedPackId = ResolveRuntimePackId(normalizedPackId);
        if (normalizedPackId.Length == 0 || !_packEngineIdsById.TryGetValue(normalizedPackId, out var engineId)) {
            return string.Empty;
        }

        return (engineId ?? string.Empty).Trim();
    }

    private string[] ResolvePlannerPackCapabilityTags(string normalizedPackId, int maxCount) {
        normalizedPackId = ResolveRuntimePackId(normalizedPackId);
        if (maxCount <= 0
            || normalizedPackId.Length == 0
            || !_packCapabilityTagsById.TryGetValue(normalizedPackId, out var capabilityTags)
            || capabilityTags is not { Length: > 0 }) {
            return Array.Empty<string>();
        }

        return capabilityTags
            .Where(static tag => !string.IsNullOrWhiteSpace(tag))
            .Take(maxCount)
            .ToArray();
    }

    private static IReadOnlyList<ToolOrchestrationCatalogEntry> CollectPlannerPromptHintEntries(
        IReadOnlyList<ToolDefinition> definitions,
        PlannerContextMetadata plannerContext,
        ToolOrchestrationCatalog? orchestrationCatalog = null) {
        if (definitions.Count == 0) {
            return Array.Empty<ToolOrchestrationCatalogEntry>();
        }

        if (orchestrationCatalog is null || orchestrationCatalog.Count == 0) {
            orchestrationCatalog = ToolOrchestrationCatalog.Build(definitions);
        }
        var preferredPackIds = new HashSet<string>(
            plannerContext.PreferredPackIds
                .Concat(plannerContext.HandoffTargetPackIds)
                .Select(static packId => NormalizePackId(packId))
                .Where(static packId => packId.Length > 0),
            StringComparer.OrdinalIgnoreCase);
        var preferredToolNames = new HashSet<string>(
            plannerContext.PreferredToolNames
                .Concat(plannerContext.HandoffTargetToolNames)
                .Where(static toolName => !string.IsNullOrWhiteSpace(toolName)),
            StringComparer.OrdinalIgnoreCase);

        var focusedEntries = new List<ToolOrchestrationCatalogEntry>(definitions.Count);
        var allEntries = new List<ToolOrchestrationCatalogEntry>(definitions.Count);
        var seenToolNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < definitions.Count; i++) {
            var toolName = (definitions[i].Name ?? string.Empty).Trim();
            if (toolName.Length == 0
                || !seenToolNames.Add(toolName)
                || !orchestrationCatalog.TryGetEntry(toolName, out var entry)
                || entry.IsPackInfoTool) {
                continue;
            }

            allEntries.Add(entry);
            if (preferredPackIds.Count == 0 && preferredToolNames.Count == 0) {
                continue;
            }

            var normalizedPackId = NormalizePackId(entry.PackId);
            if (preferredToolNames.Contains(entry.ToolName)
                || (normalizedPackId.Length > 0 && preferredPackIds.Contains(normalizedPackId))) {
                focusedEntries.Add(entry);
            }
        }

        return focusedEntries.Count > 0 ? focusedEntries : allEntries;
    }

    private static string ResolvePlannerCategory(ToolDefinition definition) {
        var category = (definition.Category ?? string.Empty).Trim();
        if (category.Length > 0) {
            return category;
        }

        return (ToolSelectionMetadata.Enrich(definition, toolType: null).Category ?? string.Empty).Trim();
    }

    private static string ResolvePlannerRole(ToolDefinition definition) {
        var role = (definition.Routing?.Role ?? string.Empty).Trim();
        if (role.Length > 0) {
            return role;
        }

        return (ToolSelectionMetadata.Enrich(definition, toolType: null).Routing?.Role ?? string.Empty).Trim();
    }

    private static string[] ExtractPlannerTags(ToolDefinition definition, int maxCount) {
        if (definition.Tags.Count == 0 || maxCount <= 0) {
            return Array.Empty<string>();
        }

        var tags = new List<string>(Math.Min(maxCount, definition.Tags.Count));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < definition.Tags.Count && tags.Count < maxCount; i++) {
            var tag = (definition.Tags[i] ?? string.Empty).Trim();
            if (tag.Length == 0 || !seen.Add(tag)) {
                continue;
            }

            tags.Add(tag);
        }

        return tags.Count == 0 ? Array.Empty<string>() : tags.ToArray();
    }

    private static IReadOnlyList<ToolDefinition> ParsePlannerSelectedDefinitions(string plannerText, IReadOnlyList<ToolDefinition> definitions, int limit) {
        if (string.IsNullOrWhiteSpace(plannerText) || definitions.Count == 0 || limit <= 0) {
            return Array.Empty<ToolDefinition>();
        }

        var byName = new Dictionary<string, ToolDefinition>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < definitions.Count; i++) {
            var definition = definitions[i];
            if (definition is null || string.IsNullOrWhiteSpace(definition.Name)) {
                continue;
            }

            var name = definition.Name.Trim();
            if (!byName.ContainsKey(name)) {
                byName.Add(name, definition);
            }
        }

        var selected = new List<ToolDefinition>(Math.Min(limit, byName.Count));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var names = ExtractPlannerToolNames(plannerText);
        for (var i = 0; i < names.Count && selected.Count < limit; i++) {
            var requestedName = names[i];
            if (string.IsNullOrWhiteSpace(requestedName)) {
                continue;
            }

            var normalized = requestedName.Trim();
            if (!byName.TryGetValue(normalized, out var definition) || !seen.Add(definition.Name)) {
                continue;
            }

            selected.Add(definition);
        }

        return selected;
    }

    private static List<string> ExtractPlannerToolNames(string plannerText) {
        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var candidates = BuildPlannerJsonCandidates(plannerText);
        for (var i = 0; i < candidates.Count; i++) {
            if (!TryExtractPlannerNamesFromJson(candidates[i], names, seen)) {
                continue;
            }

            if (names.Count > 0) {
                return names;
            }
        }

        // Fallback for non-JSON planner replies: collect tool-like identifiers.
        var matches = Regex.Matches(plannerText, @"\b[a-z][a-z0-9]*(?:_[a-z0-9]+)+\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        for (var i = 0; i < matches.Count; i++) {
            var value = (matches[i].Value ?? string.Empty).Trim();
            if (value.Length == 0 || !seen.Add(value)) {
                continue;
            }
            names.Add(value);
        }

        return names;
    }

    private static List<string> BuildPlannerJsonCandidates(string plannerText) {
        var list = new List<string>();
        var normalized = (plannerText ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return list;
        }

        list.Add(normalized);

        var fencedMatches = Regex.Matches(
            normalized,
            "```(?:json)?\\s*(?<json>[\\s\\S]*?)```",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        for (var i = 0; i < fencedMatches.Count; i++) {
            var captured = fencedMatches[i].Groups["json"].Value.Trim();
            if (captured.Length > 0) {
                list.Add(captured);
            }
        }

        AppendJsonEnvelopeCandidate(normalized, '{', '}', list);
        AppendJsonEnvelopeCandidate(normalized, '[', ']', list);
        return list;
    }

    private static void AppendJsonEnvelopeCandidate(string text, char startChar, char endChar, ICollection<string> target) {
        var start = text.IndexOf(startChar);
        var end = text.LastIndexOf(endChar);
        if (start < 0 || end <= start) {
            return;
        }

        var candidate = text.Substring(start, (end - start) + 1).Trim();
        if (candidate.Length > 1) {
            target.Add(candidate);
        }
    }

    private static bool TryExtractPlannerNamesFromJson(string candidate, ICollection<string> names, ISet<string> seen) {
        if (string.IsNullOrWhiteSpace(candidate)) {
            return false;
        }

        JsonValue? parsed;
        try {
            parsed = JsonLite.Parse(candidate);
        } catch {
            return false;
        }

        if (parsed is null) {
            return false;
        }

        var extracted = 0;
        var rootObj = parsed.AsObject();
        if (rootObj is not null) {
            extracted += AppendPlannerNamesFromObject(rootObj, names, seen);
        } else {
            var rootArray = parsed.AsArray();
            if (rootArray is not null) {
                extracted += AppendPlannerNamesFromArray(rootArray, names, seen);
            }
        }

        return extracted > 0;
    }

    private static int AppendPlannerNamesFromObject(JsonObject obj, ICollection<string> names, ISet<string> seen) {
        var added = 0;
        added += AppendPlannerNamesFromArray(obj.GetArray("tool_names"), names, seen);
        added += AppendPlannerNamesFromArray(obj.GetArray("tools"), names, seen);
        added += AppendPlannerNamesFromArray(obj.GetArray("selected"), names, seen);
        added += AppendPlannerNamesFromArray(obj.GetArray("recommended"), names, seen);

        var resultObj = obj.GetObject("result");
        if (resultObj is not null) {
            added += AppendPlannerNamesFromObject(resultObj, names, seen);
        }

        return added;
    }

    private static int AppendPlannerNamesFromArray(JsonArray? array, ICollection<string> names, ISet<string> seen) {
        if (array is null || array.Count == 0) {
            return 0;
        }

        var added = 0;
        for (var i = 0; i < array.Count; i++) {
            var item = array[i];
            var asName = item.AsString();
            if (TryAddPlannerName(asName, names, seen)) {
                added++;
                continue;
            }

            var asObj = item.AsObject();
            if (asObj is null) {
                continue;
            }

            if (TryAddPlannerName(asObj.GetString("name"), names, seen)) {
                added++;
            }
            if (TryAddPlannerName(asObj.GetString("tool"), names, seen)) {
                added++;
            }
            if (TryAddPlannerName(asObj.GetString("tool_name"), names, seen)) {
                added++;
            }
        }

        return added;
    }

    private static bool TryAddPlannerName(string? value, ICollection<string> names, ISet<string> seen) {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 0 || !seen.Add(normalized)) {
            return false;
        }

        names.Add(normalized);
        return true;
    }

    private static bool ShouldSkipWeightedRouting(string userRequest) {
        var normalized = (userRequest ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return true;
        }

        // Prefer structured action-selection contracts when present.
        if (TryParseExplicitActSelection(normalized, out _, out _)
            || TryReadActionSelectionIntent(normalized, out _, out _)
            || LooksLikeActionSelectionPayload(normalized)) {
            return true;
        }

        // Prefer structured continuation contracts when present.
        if (TryReadContinuationContractFromRequestText(normalized, out _, out _)) {
            return true;
        }

        var tokenCount = CountLetterDigitTokens(normalized, maxTokens: 6);
        var compactLength = NormalizeCompactToken(normalized.AsSpan()).Length;
        if (!LooksLikeContinuationFollowUp(normalized)
            && tokenCount > 0
            && tokenCount <= 3
            && normalized.Length <= FollowUpShapeShortCharLimit
            && !HasConflictingDomainIntentSignals(normalized)
            && !LooksLikeMixedDomainScopeRequest(normalized)) {
            return true;
        }

        if (tokenCount > 0
            && tokenCount <= 2
            && normalized.Length <= FollowUpShapeShortCharLimit
            && compactLength >= 8) {
            return true;
        }

        return false;
    }

    private static bool LooksLikeContinuationFollowUp(string userRequest) {
        return LooksLikeFollowUpShape(userRequest, ContinuationFollowUpQuestionCharLimit);
    }

    private static (bool ContinuationFollowUpTurn, bool CompactFollowUpTurn) ResolveFollowUpTurnClassification(
        bool continuationContractDetected,
        bool hasStructuredContinuationContext,
        string userRequest,
        string routedUserRequest) {
        if (continuationContractDetected) {
            var contractExpandedFollowUpTurn = !string.Equals(routedUserRequest, userRequest, StringComparison.Ordinal);
            return (contractExpandedFollowUpTurn, contractExpandedFollowUpTurn);
        }

        if (!hasStructuredContinuationContext) {
            return (false, false);
        }

        var lexicalCompactFollowUpTurn = LooksLikeContinuationFollowUp(userRequest);
        var continuationFollowUpTurn = lexicalCompactFollowUpTurn
                                       && !string.Equals(routedUserRequest, userRequest, StringComparison.Ordinal);
        var compactFollowUpTurn = continuationFollowUpTurn || lexicalCompactFollowUpTurn;
        return (continuationFollowUpTurn, compactFollowUpTurn);
    }

    private bool ShouldTreatAsPassiveCompactFollowUp(string threadId, string userRequest) {
        var normalized = (userRequest ?? string.Empty).Trim();
        if (normalized.Length == 0 || !LooksLikeContinuationFollowUp(normalized)) {
            return false;
        }

        if (ContainsQuestionSignal(normalized)) {
            return false;
        }

        if (normalized.Length > FollowUpShapeShortCharLimit) {
            return false;
        }

        if (!ContainsPassiveCompactSignalMarker(normalized)) {
            return false;
        }

        if (LooksLikeActionSelectionPayload(normalized)
            || TryParseExplicitActSelection(normalized, out _, out _)
            || TryReadActionSelectionIntent(normalized, out _, out _)) {
            return false;
        }

        if (TryParseDomainIntentMarkerSelection(normalized, DomainIntentMarker, out _)
            || TryParseDomainIntentChoiceMarkerSelection(normalized, out _)
            || TryNormalizeDomainIntentFamily(normalized, out _)
            || TryParseDomainIntentFamilyFromDomainScopePayload(normalized, out _)) {
            return false;
        }

        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length > 0 && HasFreshPendingActionsContext(normalizedThreadId)) {
            return false;
        }

        var tokenCount = CountLetterDigitTokens(normalized, maxTokens: 12);
        if (tokenCount == 0 || tokenCount > 4) {
            return false;
        }

        for (var i = 0; i < normalized.Length; i++) {
            if (char.IsDigit(normalized[i])) {
                return false;
            }
        }

        if (TryResolveDomainIntentFamilyFromUserSignals(normalized, _registry.GetDefinitions(), out _)) {
            return false;
        }

        return true;
    }

    private static bool ContainsPassiveCompactSignalMarker(string text) {
        var normalized = (text ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return false;
        }

        for (var i = 0; i < normalized.Length; i++) {
            var ch = normalized[i];
            if (char.IsLetterOrDigit(ch) || char.IsWhiteSpace(ch)) {
                continue;
            }

            if (ch == '_' || ch == '-' || ch == '/' || ch == '\\' || ch == '.') {
                continue;
            }

            if (Array.IndexOf(QuestionSignalPunctuation, ch) >= 0) {
                continue;
            }

            return true;
        }

        return false;
    }

    private sealed class ToolRoutingStats {
        public int Invocations { get; set; }
        public int Successes { get; set; }
        public int Failures { get; set; }
        public long LastUsedUtcTicks { get; set; }
        public long LastSuccessUtcTicks { get; set; }
    }

    private readonly record struct ToolScore(
        ToolDefinition Definition,
        double Score,
        bool DirectNameMatch,
        bool ExplicitToolMatch,
        int TokenHits,
        int FocusTokenHits,
        double Adjustment,
        double RemoteCapableBoost,
        double CrossPackContinuationBoost,
        double EnvironmentDiscoverBoost,
        double SetupAwareBoost,
        double ContractHelperBoost,
        double WriteFollowUpPenalty,
        double AuthFollowUpPenalty);

    private enum ToolRoutingInsightStrategy {
        Unknown = 0,
        WeightedHeuristic = 1,
        ContinuationSubset = 2,
        SemanticPlanner = 3
    }

    private readonly record struct ToolRoutingInsight(
        string ToolName,
        string Confidence,
        double Score,
        string Reason,
        ToolRoutingInsightStrategy Strategy) {
        public ToolRoutingInsight(string ToolName, string Confidence, double Score, string Reason)
            : this(ToolName, Confidence, Score, Reason, ToolRoutingInsightStrategy.Unknown) {
        }
    }

    private readonly record struct ToolRetryProfile(
        int MaxAttempts,
        int DelayBaseMs,
        bool RetryOnTimeout,
        bool RetryOnTransport,
        IReadOnlyList<string> RetryableErrorCodes,
        IReadOnlyList<string> RecoveryToolNames,
        IReadOnlyList<string> AlternateEngineIds);

    internal void RememberPlannerThreadContextForTesting(string activeThreadId, string plannerThreadId, long seenUtcTicks) {
        RememberPlannerThreadContext(activeThreadId, plannerThreadId, seenUtcTicks);
    }

    internal bool TryResolvePlannerThreadContextForTesting(string activeThreadId, out string plannerThreadId) {
        return TryResolvePlannerThreadContext(activeThreadId, out plannerThreadId);
    }

}
