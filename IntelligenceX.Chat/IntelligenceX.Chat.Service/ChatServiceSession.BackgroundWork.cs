using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using JsonValueKind = System.Text.Json.JsonValueKind;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.Json;
using IntelligenceX.Tools;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    private const int MaxBackgroundWorkItems = 6;
    private const int MaxBackgroundWorkEvidenceTools = 4;
    private const string BackgroundWorkStateQueued = "queued";
    private const string BackgroundWorkStateReady = "ready";
    private const string BackgroundWorkStateRunning = "running";
    private const string BackgroundWorkStateCompleted = "completed";
    private const string BackgroundWorkKindPendingAction = "pending_action";
    private const string BackgroundWorkKindToolHandoff = "tool_handoff";
    private const string BackgroundWorkMutabilityReadOnly = "read_only";
    private const string BackgroundWorkMutabilityUnknown = "unknown";
    private static readonly TimeSpan BackgroundWorkClaimLeaseDuration = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan BackgroundWorkRetryCooldown = TimeSpan.FromSeconds(30);

    internal readonly record struct ThreadBackgroundWorkItem(
        string Id,
        string Title,
        string Request,
        string State,
        string[] EvidenceToolNames,
        string Kind,
        string Mutability,
        string SourceToolName,
        string SourceCallId,
        string TargetPackId,
        string TargetToolName,
        string FollowUpKind,
        int FollowUpPriority,
        string PreparedArgumentsJson,
        string ResultReference,
        int ExecutionAttemptCount,
        string LastExecutionCallId,
        long LastExecutionStartedUtcTicks,
        long LastExecutionFinishedUtcTicks,
        long LeaseExpiresUtcTicks,
        long CreatedUtcTicks,
        long UpdatedUtcTicks);

    internal readonly record struct ThreadBackgroundWorkSnapshot(
        int QueuedCount,
        int ReadyCount,
        int RunningCount,
        int CompletedCount,
        int PendingReadOnlyCount,
        int PendingUnknownCount,
        string[] RecentEvidenceTools,
        ThreadBackgroundWorkItem[] Items);

    private readonly record struct BackgroundWorkReplayCandidate(
        string ItemId,
        ToolCall ToolCall,
        ThreadBackgroundWorkItem Item,
        string Reason);

    private readonly record struct BackgroundWorkFollowUpSummary(
        string[] FollowUpKinds,
        string PriorityFocus);

    private ThreadBackgroundWorkSnapshot ResolveThreadBackgroundWorkSnapshot(string threadId) {
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0) {
            return EmptyBackgroundWorkSnapshot();
        }

        TryGetRememberedThreadBackgroundWorkSnapshot(normalizedThreadId, out var rememberedSnapshot);
        rememberedSnapshot = NormalizeThreadBackgroundWorkLeases(normalizedThreadId, rememberedSnapshot);
        if (!HasFreshPendingActionsContext(normalizedThreadId)) {
            return !IsEmptyBackgroundWorkSnapshot(rememberedSnapshot)
                ? rememberedSnapshot
                : EmptyBackgroundWorkSnapshot();
        }

        var liveSnapshot = BuildLiveThreadBackgroundWorkSnapshot(normalizedThreadId);
        var mergedSnapshot = MergeThreadBackgroundWorkSnapshots(rememberedSnapshot, liveSnapshot);
        mergedSnapshot = NormalizeThreadBackgroundWorkLeases(normalizedThreadId, mergedSnapshot);
        if (IsEmptyBackgroundWorkSnapshot(mergedSnapshot)) {
            ClearRememberedThreadBackgroundWork(normalizedThreadId);
            return mergedSnapshot;
        }

        RememberThreadBackgroundWorkSnapshot(normalizedThreadId, mergedSnapshot, DateTime.UtcNow.Ticks);
        return mergedSnapshot;
    }

    private ThreadBackgroundWorkSnapshot BuildLiveThreadBackgroundWorkSnapshot(string normalizedThreadId) {
        PendingAction[] actions;
        lock (_toolRoutingContextLock) {
            if (!_pendingActionsByThreadId.TryGetValue(normalizedThreadId, out var cachedActions) || cachedActions is not { Length: > 0 }) {
                return EmptyBackgroundWorkSnapshot();
            }

            actions = cachedActions.ToArray();
        }

        var recentEvidenceTools = CollectBackgroundWorkRecentEvidenceTools(normalizedThreadId);
        var hasRecentEvidence = recentEvidenceTools.Length > 0;
        var items = new List<ThreadBackgroundWorkItem>(Math.Min(MaxBackgroundWorkItems, actions.Length));
        var nowTicks = DateTime.UtcNow.Ticks;

        for (var i = 0; i < actions.Length; i++) {
            var action = actions[i];
            if (action.Mutability == ActionMutability.Mutating) {
                continue;
            }

            var id = (action.Id ?? string.Empty).Trim();
            if (id.Length == 0) {
                continue;
            }

            var title = (action.Title ?? string.Empty).Trim();
            var request = NormalizeWorkingMemoryAnswerPlanFocus(string.IsNullOrWhiteSpace(action.Request) ? title : action.Request);
            var state = action.Mutability == ActionMutability.ReadOnly && hasRecentEvidence
                ? BackgroundWorkStateReady
                : BackgroundWorkStateQueued;
            items.Add(CreatePendingActionBackgroundWorkItem(
                actionId: id,
                title: title,
                request: request,
                state: state,
                evidenceToolNames: string.Equals(state, BackgroundWorkStateReady, StringComparison.OrdinalIgnoreCase)
                    ? recentEvidenceTools
                    : Array.Empty<string>(),
                mutability: action.Mutability == ActionMutability.ReadOnly
                    ? BackgroundWorkMutabilityReadOnly
                    : BackgroundWorkMutabilityUnknown,
                createdUtcTicks: nowTicks,
                updatedUtcTicks: nowTicks));
        }

        return BuildBackgroundWorkSnapshotFromItems(items, recentEvidenceTools);
    }

    private static ThreadBackgroundWorkItem CreatePendingActionBackgroundWorkItem(
        string actionId,
        string title,
        string request,
        string state,
        IReadOnlyList<string> evidenceToolNames,
        string mutability,
        long createdUtcTicks,
        long updatedUtcTicks) {
        return new ThreadBackgroundWorkItem(
            Id: "pending_action:" + NormalizeBackgroundWorkToken(actionId),
            Title: (title ?? string.Empty).Trim(),
            Request: NormalizeWorkingMemoryAnswerPlanFocus(request),
            State: NormalizeBackgroundWorkState(state),
            EvidenceToolNames: NormalizeBackgroundWorkToolNames(evidenceToolNames),
            Kind: BackgroundWorkKindPendingAction,
            Mutability: NormalizeBackgroundWorkMutability(mutability),
            SourceToolName: string.Empty,
            SourceCallId: string.Empty,
            TargetPackId: string.Empty,
            TargetToolName: string.Empty,
            FollowUpKind: string.Empty,
            FollowUpPriority: 0,
            PreparedArgumentsJson: "{}",
            ResultReference: string.Empty,
            ExecutionAttemptCount: 0,
            LastExecutionCallId: string.Empty,
            LastExecutionStartedUtcTicks: 0,
            LastExecutionFinishedUtcTicks: 0,
            LeaseExpiresUtcTicks: 0,
            CreatedUtcTicks: createdUtcTicks,
            UpdatedUtcTicks: updatedUtcTicks);
    }

    private void RememberToolHandoffBackgroundWork(
        string threadId,
        IReadOnlyList<ToolDefinition> toolDefinitions,
        IReadOnlyList<ToolCallDto> toolCalls,
        IReadOnlyList<ToolOutputDto> toolOutputs) {
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0 || toolCalls.Count == 0 || toolOutputs.Count == 0) {
            return;
        }

        var successfulOutputsByCallId = BuildLatestSuccessfulToolOutputsByCallId(toolOutputs);
        if (successfulOutputsByCallId.Count == 0) {
            return;
        }

        var nowTicks = DateTime.UtcNow.Ticks;
        var seededItems = new List<ThreadBackgroundWorkItem>();
        for (var i = 0; i < toolCalls.Count; i++) {
            var call = toolCalls[i];
            var callId = (call.CallId ?? string.Empty).Trim();
            var toolName = (call.Name ?? string.Empty).Trim();
            if (callId.Length == 0
                || toolName.Length == 0
                || !successfulOutputsByCallId.TryGetValue(callId, out var output)
                || !_toolOrchestrationCatalog.TryGetEntry(toolName, out var orchestrationEntry)
                || !orchestrationEntry.IsHandoffAware
                || orchestrationEntry.HandoffEdges.Count == 0
                || !TryGetToolDefinitionByName(toolDefinitions, toolName, out var toolDefinition)
                || ShouldSkipToolHandoffBackgroundWorkSeed(toolDefinition, output)) {
                continue;
            }

            for (var edgeIndex = 0; edgeIndex < orchestrationEntry.HandoffEdges.Count; edgeIndex++) {
                foreach (var item in CreateToolHandoffBackgroundWorkItems(
                             sourceToolName: toolName,
                             sourceCallId: callId,
                             callArgumentsJson: call.ArgumentsJson,
                             output: output,
                             edge: orchestrationEntry.HandoffEdges[edgeIndex],
                             createdUtcTicks: nowTicks,
                             updatedUtcTicks: nowTicks)) {
                    seededItems.Add(item);
                }
            }
        }

        if (seededItems.Count == 0) {
            return;
        }

        TryGetRememberedThreadBackgroundWorkSnapshot(normalizedThreadId, out var rememberedSnapshot);
        var seededSnapshot = BuildBackgroundWorkSnapshotFromItems(
            seededItems,
            seededItems.Select(static item => item.SourceToolName).Where(static value => !string.IsNullOrWhiteSpace(value)).ToArray());
        var mergedSnapshot = MergeThreadBackgroundWorkSnapshots(rememberedSnapshot, seededSnapshot);
        RememberThreadBackgroundWorkSnapshot(normalizedThreadId, mergedSnapshot, nowTicks);
    }

    private bool TryBuildReadyBackgroundWorkToolCall(
        string threadId,
        string userRequest,
        IReadOnlyList<ToolDefinition> toolDefinitions,
        IReadOnlyDictionary<string, bool>? mutatingToolHintsByName,
        out ToolCall toolCall,
        out string itemId,
        out string reason) {
        toolCall = null!;
        itemId = string.Empty;

        if (!TryBuildReadyBackgroundWorkReplayCandidateCore(
                threadId,
                userRequest,
                toolDefinitions,
                mutatingToolHintsByName,
                claimItem: true,
                out var candidate,
                out reason)) {
            return false;
        }

        toolCall = candidate.ToolCall;
        itemId = candidate.ItemId;
        reason = candidate.Reason;
        return true;
    }

    private bool TryPreviewReadyBackgroundWorkReplayCandidate(
        string threadId,
        string userRequest,
        IReadOnlyList<ToolDefinition> toolDefinitions,
        IReadOnlyDictionary<string, bool>? mutatingToolHintsByName,
        out BackgroundWorkReplayCandidate candidate,
        out string reason) {
        return TryBuildReadyBackgroundWorkReplayCandidateCore(
            threadId,
            userRequest,
            toolDefinitions,
            mutatingToolHintsByName,
            claimItem: false,
            out candidate,
            out reason);
    }

    private bool TryBuildReadyBackgroundWorkReplayCandidateCore(
        string threadId,
        string userRequest,
        IReadOnlyList<ToolDefinition> toolDefinitions,
        IReadOnlyDictionary<string, bool>? mutatingToolHintsByName,
        bool claimItem,
        out BackgroundWorkReplayCandidate candidate,
        out string reason) {
        candidate = default;
        reason = "background_work_missing";

        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0) {
            reason = "background_work_missing_thread";
            return false;
        }

        if (!TryGetRememberedThreadBackgroundWorkSnapshot(normalizedThreadId, out var snapshot)
            || snapshot.Items.Length == 0) {
            reason = "background_work_snapshot_missing";
            return false;
        }

        var readyCandidateIndexes = new List<int>(snapshot.Items.Length);
        for (var i = 0; i < snapshot.Items.Length; i++) {
            var item = snapshot.Items[i];
            if (string.Equals(item.State, BackgroundWorkStateReady, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.Kind, BackgroundWorkKindToolHandoff, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(item.TargetToolName)) {
                readyCandidateIndexes.Add(i);
            }
        }

        if (readyCandidateIndexes.Count == 0) {
            reason = "background_work_no_ready_handoffs";
            return false;
        }

        readyCandidateIndexes.Sort((leftIndex, rightIndex) =>
            CompareBackgroundWorkReplayPriority(snapshot.Items[leftIndex], snapshot.Items[rightIndex]));

        var nowUtcTicks = DateTime.UtcNow.Ticks;
        for (var candidateIndex = 0; candidateIndex < readyCandidateIndexes.Count; candidateIndex++) {
            var item = snapshot.Items[readyCandidateIndexes[candidateIndex]];
            if (!IsBackgroundWorkItemRetryEligibleNow(item, nowUtcTicks, out var retryEligibilityReason)) {
                reason = retryEligibilityReason;
                continue;
            }

            if (!TryGetToolDefinitionByName(toolDefinitions, item.TargetToolName, out var toolDefinition)) {
                reason = "background_work_target_tool_not_available";
                continue;
            }

            var declaredMutability = string.Equals(item.Mutability, BackgroundWorkMutabilityReadOnly, StringComparison.OrdinalIgnoreCase)
                ? ActionMutability.ReadOnly
                : ActionMutability.Unknown;
            var resolvedMutability = ResolveStructuredNextActionMutability(
                declaredMutability,
                item.TargetToolName,
                toolDefinition,
                mutatingToolHintsByName);
            if (resolvedMutability != ActionMutability.ReadOnly) {
                reason = resolvedMutability == ActionMutability.Mutating
                    ? "background_work_target_mutating_not_autorun"
                    : "background_work_target_mutability_unknown";
                continue;
            }

            if (!TryParseStructuredNextActionArguments(
                    item.PreparedArgumentsJson,
                    toolDefinition,
                    out var normalizedArguments,
                    out var argumentReason)) {
                reason = "background_work_" + argumentReason;
                continue;
            }

            if (HasCarryoverHostHintMismatch(userRequest, normalizedArguments)) {
                reason = "background_work_host_hint_mismatch";
                continue;
            }

            if (ShouldBlockSingleHostStructuredReplayForScopeShift(
                    normalizedThreadId,
                    userRequest,
                    normalizedArguments)) {
                reason = "background_work_scope_shift_requires_fresh_plan";
                continue;
            }

            var callId = BuildHostGeneratedToolCallId("host_background_work", item.TargetToolName);
            if (claimItem
                && !TrySetThreadBackgroundWorkItemState(normalizedThreadId, item.Id, BackgroundWorkStateRunning, executionCallId: callId)) {
                reason = "background_work_claim_failed";
                continue;
            }

            var serializedArguments = JsonLite.Serialize(normalizedArguments);
            var raw = new JsonObject()
                .Add("type", "tool_call")
                .Add("call_id", callId)
                .Add("name", item.TargetToolName)
                .Add("arguments", serializedArguments);
            candidate = new BackgroundWorkReplayCandidate(
                ItemId: item.Id,
                ToolCall: new ToolCall(
                    callId: callId,
                    name: item.TargetToolName,
                    input: serializedArguments,
                    arguments: normalizedArguments,
                    raw: raw),
                Item: item,
                Reason: "background_work_ready_readonly_autorun");
            reason = candidate.Reason;
            return true;
        }

        return false;
    }

    private static int CompareBackgroundWorkReplayPriority(ThreadBackgroundWorkItem left, ThreadBackgroundWorkItem right) {
        var followUpPriorityComparison = ToolHandoffFollowUpPriorities.Normalize(right.FollowUpPriority)
            .CompareTo(ToolHandoffFollowUpPriorities.Normalize(left.FollowUpPriority));
        if (followUpPriorityComparison != 0) {
            return followUpPriorityComparison;
        }

        var followUpKindComparison = GetBackgroundWorkFollowUpKindSortOrder(left.FollowUpKind)
            .CompareTo(GetBackgroundWorkFollowUpKindSortOrder(right.FollowUpKind));
        if (followUpKindComparison != 0) {
            return followUpKindComparison;
        }

        var attemptComparison = Math.Max(0, left.ExecutionAttemptCount).CompareTo(Math.Max(0, right.ExecutionAttemptCount));
        if (attemptComparison != 0) {
            return attemptComparison;
        }

        var updatedComparison = ResolveBackgroundWorkReplayPriorityTicks(left).CompareTo(ResolveBackgroundWorkReplayPriorityTicks(right));
        if (updatedComparison != 0) {
            return updatedComparison;
        }

        var createdComparison = left.CreatedUtcTicks.CompareTo(right.CreatedUtcTicks);
        if (createdComparison != 0) {
            return createdComparison;
        }

        return StringComparer.OrdinalIgnoreCase.Compare(left.Id, right.Id);
    }

    private static long ResolveBackgroundWorkReplayPriorityTicks(ThreadBackgroundWorkItem item) {
        if (item.UpdatedUtcTicks > 0) {
            return item.UpdatedUtcTicks;
        }

        if (item.CreatedUtcTicks > 0) {
            return item.CreatedUtcTicks;
        }

        return long.MaxValue;
    }

    private static bool IsBackgroundWorkItemRetryEligibleNow(ThreadBackgroundWorkItem item, long nowUtcTicks, out string reason) {
        reason = string.Empty;
        if (Math.Max(0, item.ExecutionAttemptCount) <= 0 || item.LastExecutionFinishedUtcTicks <= 0) {
            return true;
        }

        if (nowUtcTicks <= item.LastExecutionFinishedUtcTicks) {
            reason = "background_work_retry_cooldown_active";
            return false;
        }

        var elapsedTicks = nowUtcTicks - item.LastExecutionFinishedUtcTicks;
        if (elapsedTicks < BackgroundWorkRetryCooldown.Ticks) {
            reason = "background_work_retry_cooldown_active";
            return false;
        }

        return true;
    }

    private static int GetBackgroundWorkFollowUpKindSortOrder(string? kind) {
        var normalized = ToolHandoffFollowUpKinds.Normalize(kind);
        return normalized switch {
            ToolHandoffFollowUpKinds.Verification => 0,
            ToolHandoffFollowUpKinds.Investigation => 1,
            ToolHandoffFollowUpKinds.Normalization => 2,
            ToolHandoffFollowUpKinds.Enrichment => 3,
            _ => 4
        };
    }

    private static BackgroundWorkFollowUpSummary BuildBackgroundWorkFollowUpSummary(IEnumerable<ThreadBackgroundWorkItem> items) {
        var normalizedItems = (items ?? Array.Empty<ThreadBackgroundWorkItem>())
            .Where(static item => !string.Equals(item.State, BackgroundWorkStateCompleted, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (normalizedItems.Length == 0) {
            return new BackgroundWorkFollowUpSummary(Array.Empty<string>(), string.Empty);
        }

        var followUpKinds = normalizedItems
            .Select(static item => ToolHandoffFollowUpKinds.Normalize(item.FollowUpKind))
            .Where(static kind => kind.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static kind => GetBackgroundWorkFollowUpKindSortOrder(kind))
            .Take(3)
            .ToArray();

        var priorityItem = normalizedItems
            .Where(static item => item.FollowUpPriority > 0 || ToolHandoffFollowUpKinds.Normalize(item.FollowUpKind).Length > 0)
            .OrderByDescending(static item => ToolHandoffFollowUpPriorities.Normalize(item.FollowUpPriority))
            .ThenBy(static item => GetBackgroundWorkFollowUpKindSortOrder(item.FollowUpKind))
            .ThenBy(static item => Math.Max(0, item.ExecutionAttemptCount))
            .ThenBy(static item => ResolveBackgroundWorkReplayPriorityTicks(item))
            .ThenBy(static item => item.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(priorityItem.Id)) {
            priorityItem = normalizedItems
                .OrderByDescending(static item => ToolHandoffFollowUpPriorities.Normalize(item.FollowUpPriority))
                .ThenBy(static item => GetBackgroundWorkFollowUpKindSortOrder(item.FollowUpKind))
                .ThenBy(static item => Math.Max(0, item.ExecutionAttemptCount))
                .ThenBy(static item => ResolveBackgroundWorkReplayPriorityTicks(item))
                .ThenBy(static item => item.Id, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }

        return new BackgroundWorkFollowUpSummary(
            FollowUpKinds: followUpKinds,
            PriorityFocus: BuildBackgroundWorkPriorityFocus(priorityItem));
    }

    private static string BuildBackgroundWorkPriorityFocus(ThreadBackgroundWorkItem item) {
        if (string.IsNullOrWhiteSpace(item.Id)) {
            return string.Empty;
        }

        var kind = ToolHandoffFollowUpKinds.Normalize(item.FollowUpKind);
        var priority = DescribeBackgroundWorkPriority(item.FollowUpPriority);
        if (priority.Length > 0 && kind.Length > 0) {
            return priority + " " + kind;
        }

        if (kind.Length > 0) {
            return kind;
        }

        return priority;
    }

    private static string DescribeBackgroundWorkPriority(int priority) {
        var normalized = ToolHandoffFollowUpPriorities.Normalize(priority);
        return normalized switch {
            >= ToolHandoffFollowUpPriorities.Critical => "critical",
            >= ToolHandoffFollowUpPriorities.High => "high",
            >= ToolHandoffFollowUpPriorities.Normal => "normal",
            >= ToolHandoffFollowUpPriorities.Low => "low",
            _ => string.Empty
        };
    }

    private static Dictionary<string, ToolOutputDto> BuildLatestSuccessfulToolOutputsByCallId(IReadOnlyList<ToolOutputDto> outputs) {
        var byCallId = new Dictionary<string, ToolOutputDto>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < outputs.Count; i++) {
            var output = outputs[i];
            var callId = (output.CallId ?? string.Empty).Trim();
            if (callId.Length == 0 || output.Ok != true) {
                continue;
            }

            byCallId[callId] = output;
        }

        return byCallId;
    }

    private static bool ShouldSkipToolHandoffBackgroundWorkSeed(ToolDefinition toolDefinition, ToolOutputDto output) {
        if (toolDefinition.WriteGovernance?.IsWriteCapable != true) {
            return false;
        }

        return !TryReadToolOutputMetaBoolean(output, "write_applied", out var writeApplied) || !writeApplied;
    }

    private static bool TryReadToolOutputMetaBoolean(ToolOutputDto output, string propertyName, out bool value) {
        value = false;
        if (TryReadBooleanPropertyFromJson(output.MetaJson, propertyName, out value)) {
            return true;
        }

        if (!TryParseJsonObject(output.Output, out var root)) {
            return false;
        }

        if (TryReadBooleanProperty(root, propertyName, out value)) {
            return true;
        }

        return root.TryGetProperty("meta", out var metaNode)
               && metaNode.ValueKind == JsonValueKind.Object
               && TryReadBooleanProperty(metaNode, propertyName, out value);
    }

    private IEnumerable<ThreadBackgroundWorkItem> CreateToolHandoffBackgroundWorkItems(
        string sourceToolName,
        string sourceCallId,
        string? callArgumentsJson,
        ToolOutputDto output,
        ToolOrchestrationHandoffEdge edge,
        long createdUtcTicks,
        long updatedUtcTicks) {
        var targetPackId = (edge.TargetPackId ?? string.Empty).Trim();
        var targetToolName = (edge.TargetToolName ?? string.Empty).Trim();
        if (targetToolName.Length == 0) {
            yield break;
        }

        var valuesByTargetArgument = ExtractBackgroundWorkBindingValuesByTargetArgument(
            callArgumentsJson,
            output.Output,
            edge.BindingPairs);
        foreach (var pair in valuesByTargetArgument) {
            for (var i = 0; i < pair.Value.Length; i++) {
                var value = pair.Value[i];
                if (string.IsNullOrWhiteSpace(value)) {
                    continue;
                }

                yield return new ThreadBackgroundWorkItem(
                    Id: BuildToolHandoffBackgroundWorkItemId(sourceToolName, targetToolName, pair.Key, value),
                    Title: BuildToolHandoffBackgroundWorkTitle(targetToolName, value),
                    Request: BuildToolHandoffBackgroundWorkRequest(targetToolName, pair.Key, value, sourceToolName),
                    State: BackgroundWorkStateReady,
                    EvidenceToolNames: NormalizeBackgroundWorkToolNames(new[] { sourceToolName }),
                    Kind: BackgroundWorkKindToolHandoff,
                    Mutability: BackgroundWorkMutabilityReadOnly,
                    SourceToolName: NormalizeToolNameForAnswerPlan(sourceToolName),
                    SourceCallId: sourceCallId,
                    TargetPackId: targetPackId,
                    TargetToolName: NormalizeToolNameForAnswerPlan(targetToolName),
                    FollowUpKind: ToolHandoffFollowUpKinds.Normalize(edge.FollowUpKind),
                    FollowUpPriority: ToolHandoffFollowUpPriorities.Normalize(edge.FollowUpPriority),
                    PreparedArgumentsJson: BuildBackgroundWorkPreparedArgumentsJson(pair.Key, value),
                    ResultReference: BuildBackgroundWorkResultReference(sourceToolName, sourceCallId, targetToolName, pair.Key, value),
                    ExecutionAttemptCount: 0,
                    LastExecutionCallId: string.Empty,
                    LastExecutionStartedUtcTicks: 0,
                    LastExecutionFinishedUtcTicks: 0,
                    LeaseExpiresUtcTicks: 0,
                    CreatedUtcTicks: createdUtcTicks,
                    UpdatedUtcTicks: updatedUtcTicks);
            }
        }
    }

    private static Dictionary<string, string[]> ExtractBackgroundWorkBindingValuesByTargetArgument(
        string? callArgumentsJson,
        string outputJson,
        IReadOnlyList<string> bindingPairs) {
        var valuesByTargetArgument = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var roots = new List<JsonElement>(capacity: 2);
        if (TryParseJsonObject(outputJson, out var outputRoot)) {
            roots.Add(outputRoot);
        }
        if (TryParseJsonObject(callArgumentsJson, out var argumentsRoot)) {
            roots.Add(argumentsRoot);
        }

        for (var i = 0; i < bindingPairs.Count; i++) {
            if (!TryParseBackgroundWorkBindingPair(bindingPairs[i], out var sourceField, out var targetArgument)) {
                continue;
            }

            for (var rootIndex = 0; rootIndex < roots.Count; rootIndex++) {
                var candidates = ExtractBackgroundWorkFieldValues(roots[rootIndex], sourceField);
                for (var candidateIndex = 0; candidateIndex < candidates.Length; candidateIndex++) {
                    if (!valuesByTargetArgument.TryGetValue(targetArgument, out var values)) {
                        values = new List<string>();
                        valuesByTargetArgument[targetArgument] = values;
                    }

                    if (!values.Contains(candidates[candidateIndex], StringComparer.OrdinalIgnoreCase)) {
                        values.Add(candidates[candidateIndex]);
                    }
                }
            }
        }

        return valuesByTargetArgument
            .Where(static pair => pair.Value.Count > 0)
            .ToDictionary(
                static pair => pair.Key,
                static pair => pair.Value.Take(MaxBackgroundWorkItems).ToArray(),
                StringComparer.OrdinalIgnoreCase);
    }

    private static bool TryParseBackgroundWorkBindingPair(string bindingPair, out string sourceField, out string targetArgument) {
        sourceField = string.Empty;
        targetArgument = string.Empty;
        var normalized = (bindingPair ?? string.Empty).Trim();
        var separator = normalized.IndexOf("->", StringComparison.Ordinal);
        if (separator <= 0 || separator >= normalized.Length - 2) {
            return false;
        }

        sourceField = NormalizeBackgroundWorkToken(normalized[..separator]);
        targetArgument = NormalizeBackgroundWorkToken(normalized[(separator + 2)..]);
        return sourceField.Length > 0 && targetArgument.Length > 0;
    }

    private static string[] ExtractBackgroundWorkFieldValues(JsonElement root, string sourceField) {
        if (root.ValueKind != JsonValueKind.Object) {
            return Array.Empty<string>();
        }

        var values = new List<string>();
        foreach (var candidateField in EnumerateBackgroundWorkFieldCandidates(sourceField)) {
            if (!TryGetBackgroundWorkProperty(root, candidateField, out var value)) {
                continue;
            }

            AppendBackgroundWorkJsonValues(value, values);
        }

        return values.Count == 0
            ? Array.Empty<string>()
            : values
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxBackgroundWorkItems)
                .ToArray();
    }

    private static IEnumerable<string> EnumerateBackgroundWorkFieldCandidates(string sourceField) {
        var normalizedField = NormalizeBackgroundWorkToken(sourceField);
        if (normalizedField.Length == 0) {
            yield break;
        }

        yield return normalizedField;

        var lastDotIndex = normalizedField.LastIndexOf('.', normalizedField.Length - 1);
        if (lastDotIndex >= 0 && lastDotIndex < normalizedField.Length - 1) {
            var leaf = normalizedField[(lastDotIndex + 1)..];
            if (leaf.Length > 0 && !string.Equals(leaf, normalizedField, StringComparison.OrdinalIgnoreCase)) {
                yield return leaf;
            }
        }
    }

    private static bool TryGetBackgroundWorkProperty(JsonElement root, string fieldName, out JsonElement value) {
        value = default;
        if (root.ValueKind != JsonValueKind.Object) {
            return false;
        }

        foreach (var property in root.EnumerateObject()) {
            if (!string.Equals(NormalizeBackgroundWorkToken(property.Name), fieldName, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            value = property.Value;
            return true;
        }

        return false;
    }

    private ThreadBackgroundWorkSnapshot NormalizeThreadBackgroundWorkLeases(string threadId, ThreadBackgroundWorkSnapshot snapshot) {
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0 || snapshot.Items.Length == 0) {
            return snapshot;
        }

        var nowTicks = DateTime.UtcNow.Ticks;
        if (!TryNormalizeThreadBackgroundWorkLeases(snapshot, nowTicks, out var normalizedSnapshot)) {
            return snapshot;
        }

        RememberThreadBackgroundWorkSnapshot(normalizedThreadId, normalizedSnapshot, nowTicks);
        return normalizedSnapshot;
    }

    private static bool TryNormalizeThreadBackgroundWorkLeases(
        ThreadBackgroundWorkSnapshot snapshot,
        long nowUtcTicks,
        out ThreadBackgroundWorkSnapshot normalizedSnapshot) {
        normalizedSnapshot = snapshot;
        if (snapshot.Items.Length == 0 || nowUtcTicks <= 0) {
            return false;
        }

        var changed = false;
        var items = new ThreadBackgroundWorkItem[snapshot.Items.Length];
        for (var i = 0; i < snapshot.Items.Length; i++) {
            var item = snapshot.Items[i];
            if (string.Equals(item.State, BackgroundWorkStateRunning, StringComparison.OrdinalIgnoreCase)
                && item.LeaseExpiresUtcTicks > 0
                && nowUtcTicks >= item.LeaseExpiresUtcTicks) {
                items[i] = item with {
                    State = BackgroundWorkStateReady,
                    LeaseExpiresUtcTicks = 0,
                    UpdatedUtcTicks = nowUtcTicks
                };
                changed = true;
                continue;
            }

            items[i] = item;
        }

        if (!changed) {
            return false;
        }

        normalizedSnapshot = BuildBackgroundWorkSnapshotFromItems(items, snapshot.RecentEvidenceTools);
        return true;
    }

    private static void AppendBackgroundWorkJsonValues(JsonElement value, ICollection<string> results) {
        switch (value.ValueKind) {
            case JsonValueKind.String:
                AddBackgroundWorkValue(results, value.GetString());
                break;
            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
                AddBackgroundWorkValue(results, value.GetRawText());
                break;
            case JsonValueKind.Array:
                foreach (var item in value.EnumerateArray()) {
                    AppendBackgroundWorkJsonValues(item, results);
                }
                break;
        }
    }

    private static void AddBackgroundWorkValue(ICollection<string> results, string? value) {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return;
        }

        results.Add(normalized);
    }

    private static bool TryParseJsonObject(string? json, out JsonElement root) {
        root = default;
        var normalized = (json ?? string.Empty).Trim();
        if (normalized.Length == 0 || normalized[0] != '{') {
            return false;
        }

        try {
            using var document = JsonDocument.Parse(normalized);
            if (document.RootElement.ValueKind != JsonValueKind.Object) {
                return false;
            }

            root = document.RootElement.Clone();
            return true;
        } catch (JsonException) {
            return false;
        }
    }

    private static bool TryReadBooleanPropertyFromJson(string? json, string propertyName, out bool value) {
        value = false;
        return TryParseJsonObject(json, out var root) && TryReadBooleanProperty(root, propertyName, out value);
    }

    private static bool TryReadBooleanProperty(JsonElement root, string propertyName, out bool value) {
        value = false;
        if (root.ValueKind != JsonValueKind.Object) {
            return false;
        }

        var normalizedPropertyName = NormalizeBackgroundWorkToken(propertyName);
        foreach (var property in root.EnumerateObject()) {
            if (!string.Equals(NormalizeBackgroundWorkToken(property.Name), normalizedPropertyName, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            if (property.Value.ValueKind == JsonValueKind.True) {
                value = true;
                return true;
            }

            if (property.Value.ValueKind == JsonValueKind.False) {
                value = false;
                return true;
            }
        }

        return false;
    }

    private static string BuildToolHandoffBackgroundWorkItemId(
        string sourceToolName,
        string targetToolName,
        string targetArgument,
        string value) {
        return string.Join(
            ":",
            "handoff",
            NormalizeBackgroundWorkToken(sourceToolName),
            NormalizeBackgroundWorkToken(targetToolName),
            NormalizeBackgroundWorkToken(targetArgument),
            NormalizeBackgroundWorkToken(value));
    }

    private static string BuildToolHandoffBackgroundWorkTitle(string targetToolName, string value) {
        var normalizedToolName = NormalizeToolNameForAnswerPlan(targetToolName);
        var normalizedValue = (value ?? string.Empty).Trim();
        return normalizedValue.Length == 0
            ? "Prepared " + normalizedToolName + " follow-up"
            : "Prepared " + normalizedToolName + " follow-up for " + normalizedValue;
    }

    private static string BuildToolHandoffBackgroundWorkRequest(string targetToolName, string targetArgument, string value, string sourceToolName) {
        return "Run "
               + NormalizeToolNameForAnswerPlan(targetToolName)
               + " with "
               + NormalizeBackgroundWorkToken(targetArgument)
               + "="
               + (value ?? string.Empty).Trim()
               + " using the latest "
               + NormalizeToolNameForAnswerPlan(sourceToolName)
               + " evidence.";
    }

    private static string BuildBackgroundWorkPreparedArgumentsJson(string targetArgument, string value) {
        return JsonSerializer.Serialize(new Dictionary<string, string>(StringComparer.Ordinal) {
            [(targetArgument ?? string.Empty).Trim()] = (value ?? string.Empty).Trim()
        });
    }

    private static string BuildBackgroundWorkResultReference(
        string sourceToolName,
        string sourceCallId,
        string targetToolName,
        string targetArgument,
        string value) {
        return string.Join(
            ";",
            "source_tool=" + NormalizeToolNameForAnswerPlan(sourceToolName),
            "source_call_id=" + (sourceCallId ?? string.Empty).Trim(),
            "target_tool=" + NormalizeToolNameForAnswerPlan(targetToolName),
            "target_argument=" + NormalizeBackgroundWorkToken(targetArgument),
            "target_value=" + (value ?? string.Empty).Trim());
    }

    private static ThreadBackgroundWorkSnapshot MergeThreadBackgroundWorkSnapshots(params ThreadBackgroundWorkSnapshot[] snapshots) {
        if (snapshots is null || snapshots.Length == 0) {
            return EmptyBackgroundWorkSnapshot();
        }

        var itemsById = new Dictionary<string, ThreadBackgroundWorkItem>(StringComparer.OrdinalIgnoreCase);
        var recentEvidenceTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var snapshotIndex = 0; snapshotIndex < snapshots.Length; snapshotIndex++) {
            var snapshot = snapshots[snapshotIndex];
            if (snapshot.RecentEvidenceTools is { Length: > 0 }) {
                for (var toolIndex = 0; toolIndex < snapshot.RecentEvidenceTools.Length; toolIndex++) {
                    var toolName = NormalizeToolNameForAnswerPlan(snapshot.RecentEvidenceTools[toolIndex]);
                    if (toolName.Length > 0) {
                        recentEvidenceTools.Add(toolName);
                    }
                }
            }

            if (snapshot.Items is not { Length: > 0 }) {
                continue;
            }

            for (var itemIndex = 0; itemIndex < snapshot.Items.Length; itemIndex++) {
                var item = NormalizeThreadBackgroundWorkItem(snapshot.Items[itemIndex]);
                if (item.Id.Length == 0) {
                    continue;
                }

                if (!itemsById.TryGetValue(item.Id, out var existing) || item.UpdatedUtcTicks >= existing.UpdatedUtcTicks) {
                    itemsById[item.Id] = item;
                }
            }
        }

        return BuildBackgroundWorkSnapshotFromItems(itemsById.Values, recentEvidenceTools);
    }

    private static ThreadBackgroundWorkSnapshot BuildBackgroundWorkSnapshotFromItems(
        IEnumerable<ThreadBackgroundWorkItem> items,
        IEnumerable<string>? recentEvidenceTools) {
        var normalizedItems = (items ?? Array.Empty<ThreadBackgroundWorkItem>())
            .Where(static item => !string.IsNullOrWhiteSpace(item.Id))
            .Select(static item => NormalizeThreadBackgroundWorkItem(item))
            .OrderBy(static item => GetBackgroundWorkStateSortOrder(item.State))
            .ThenByDescending(static item => item.UpdatedUtcTicks)
            .ThenBy(static item => item.Id, StringComparer.OrdinalIgnoreCase)
            .Take(MaxBackgroundWorkItems)
            .ToArray();
        if (normalizedItems.Length == 0) {
            return EmptyBackgroundWorkSnapshot();
        }

        var queuedCount = normalizedItems.Count(static item => string.Equals(item.State, BackgroundWorkStateQueued, StringComparison.OrdinalIgnoreCase));
        var readyCount = normalizedItems.Count(static item => string.Equals(item.State, BackgroundWorkStateReady, StringComparison.OrdinalIgnoreCase));
        var runningCount = normalizedItems.Count(static item => string.Equals(item.State, BackgroundWorkStateRunning, StringComparison.OrdinalIgnoreCase));
        var completedCount = normalizedItems.Count(static item => string.Equals(item.State, BackgroundWorkStateCompleted, StringComparison.OrdinalIgnoreCase));
        var pendingReadOnlyCount = normalizedItems.Count(static item => string.Equals(item.Mutability, BackgroundWorkMutabilityReadOnly, StringComparison.OrdinalIgnoreCase));
        var pendingUnknownCount = normalizedItems.Length - pendingReadOnlyCount;

        return new ThreadBackgroundWorkSnapshot(
            QueuedCount: queuedCount,
            ReadyCount: readyCount,
            RunningCount: runningCount,
            CompletedCount: completedCount,
            PendingReadOnlyCount: pendingReadOnlyCount,
            PendingUnknownCount: pendingUnknownCount,
            RecentEvidenceTools: NormalizeBackgroundWorkToolNames(recentEvidenceTools),
            Items: normalizedItems);
    }

    private static ThreadBackgroundWorkItem NormalizeThreadBackgroundWorkItem(ThreadBackgroundWorkItem item) {
        return item with {
            Id = (item.Id ?? string.Empty).Trim(),
            Title = (item.Title ?? string.Empty).Trim(),
            Request = NormalizeWorkingMemoryAnswerPlanFocus(item.Request),
            State = NormalizeBackgroundWorkState(item.State),
            EvidenceToolNames = NormalizeBackgroundWorkToolNames(item.EvidenceToolNames),
            Kind = NormalizeBackgroundWorkKind(item.Kind),
            Mutability = NormalizeBackgroundWorkMutability(item.Mutability),
            SourceToolName = NormalizeToolNameForAnswerPlan(item.SourceToolName),
            SourceCallId = (item.SourceCallId ?? string.Empty).Trim(),
            TargetPackId = (item.TargetPackId ?? string.Empty).Trim(),
            TargetToolName = NormalizeToolNameForAnswerPlan(item.TargetToolName),
            FollowUpKind = ToolHandoffFollowUpKinds.Normalize(item.FollowUpKind),
            FollowUpPriority = ToolHandoffFollowUpPriorities.Normalize(item.FollowUpPriority),
            PreparedArgumentsJson = NormalizeBackgroundWorkArgumentsJson(item.PreparedArgumentsJson),
            ResultReference = (item.ResultReference ?? string.Empty).Trim(),
            ExecutionAttemptCount = Math.Max(0, item.ExecutionAttemptCount),
            LastExecutionCallId = (item.LastExecutionCallId ?? string.Empty).Trim(),
            LastExecutionStartedUtcTicks = Math.Max(0, item.LastExecutionStartedUtcTicks),
            LastExecutionFinishedUtcTicks = Math.Max(0, item.LastExecutionFinishedUtcTicks),
            LeaseExpiresUtcTicks = Math.Max(0, item.LeaseExpiresUtcTicks),
            CreatedUtcTicks = item.CreatedUtcTicks,
            UpdatedUtcTicks = item.UpdatedUtcTicks > 0 ? item.UpdatedUtcTicks : item.CreatedUtcTicks
        };
    }

    private static int GetBackgroundWorkStateSortOrder(string? state) {
        var normalizedState = NormalizeBackgroundWorkState(state);
        if (string.Equals(normalizedState, BackgroundWorkStateRunning, StringComparison.OrdinalIgnoreCase)) {
            return 0;
        }

        if (string.Equals(normalizedState, BackgroundWorkStateReady, StringComparison.OrdinalIgnoreCase)) {
            return 1;
        }

        if (string.Equals(normalizedState, BackgroundWorkStateQueued, StringComparison.OrdinalIgnoreCase)) {
            return 2;
        }

        return 3;
    }

    private static bool IsEmptyBackgroundWorkSnapshot(ThreadBackgroundWorkSnapshot snapshot) {
        return snapshot.QueuedCount <= 0
               && snapshot.ReadyCount <= 0
               && snapshot.RunningCount <= 0
               && snapshot.CompletedCount <= 0
               && snapshot.PendingReadOnlyCount <= 0
               && snapshot.PendingUnknownCount <= 0
               && snapshot.RecentEvidenceTools.Length == 0
               && snapshot.Items.Length == 0;
    }

    private static ThreadBackgroundWorkSnapshot EmptyBackgroundWorkSnapshot() {
        return new ThreadBackgroundWorkSnapshot(
            QueuedCount: 0,
            ReadyCount: 0,
            RunningCount: 0,
            CompletedCount: 0,
            PendingReadOnlyCount: 0,
            PendingUnknownCount: 0,
            RecentEvidenceTools: Array.Empty<string>(),
            Items: Array.Empty<ThreadBackgroundWorkItem>());
    }

    private string[] CollectBackgroundWorkRecentEvidenceTools(string normalizedThreadId) {
        if (normalizedThreadId.Length == 0) {
            return Array.Empty<string>();
        }

        TryHydrateThreadToolEvidenceFromSnapshot(normalizedThreadId);

        lock (_threadToolEvidenceLock) {
            if (!_threadToolEvidenceByThreadId.TryGetValue(normalizedThreadId, out var bySignature) || bySignature.Count == 0) {
                return Array.Empty<string>();
            }

            var nowUtc = DateTime.UtcNow;
            return bySignature.Values
                .Where(static entry => !string.IsNullOrWhiteSpace(entry.ToolName))
                .Where(entry => TryGetUtcDateTimeFromTicks(entry.SeenUtcTicks, out var seenUtc)
                                && seenUtc <= nowUtc
                                && nowUtc - seenUtc <= ThreadToolEvidenceContextMaxAge)
                .OrderByDescending(static entry => entry.SeenUtcTicks)
                .Select(static entry => NormalizeToolNameForAnswerPlan(entry.ToolName))
                .Where(static toolName => toolName.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxBackgroundWorkEvidenceTools)
                .ToArray();
        }
    }

    private async Task RememberPendingActionsAndEmitBackgroundWorkStatusAsync(
        StreamWriter writer,
        string requestId,
        string threadId,
        string assistantText) {
        RememberPendingActions(threadId, assistantText);

        var snapshot = ResolveThreadBackgroundWorkSnapshot(threadId);
        if (snapshot.QueuedCount <= 0 && snapshot.ReadyCount <= 0) {
            return;
        }

        if (snapshot.QueuedCount > 0) {
            await TryWriteStatusAsync(
                    writer,
                    requestId,
                    threadId,
                    status: ChatStatusCodes.BackgroundWorkQueued,
                    message: BuildBackgroundWorkQueuedStatusMessage(snapshot.QueuedCount))
                .ConfigureAwait(false);
        }

        if (snapshot.ReadyCount > 0) {
            await TryWriteStatusAsync(
                    writer,
                    requestId,
                    threadId,
                    status: ChatStatusCodes.BackgroundWorkReady,
                    message: BuildBackgroundWorkReadyStatusMessage(snapshot.ReadyCount, snapshot.RecentEvidenceTools, snapshot.Items))
                .ConfigureAwait(false);
        }
    }

    private bool TrySetThreadBackgroundWorkItemState(
        string threadId,
        string itemId,
        string state,
        string? resultReference = null,
        string? executionCallId = null) {
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        var normalizedItemId = (itemId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0 || normalizedItemId.Length == 0) {
            return false;
        }

        if (!TryGetRememberedThreadBackgroundWorkSnapshot(normalizedThreadId, out var snapshot)
            || snapshot.Items.Length == 0) {
            return false;
        }

        var changed = false;
        var updatedTicks = DateTime.UtcNow.Ticks;
        var items = new ThreadBackgroundWorkItem[snapshot.Items.Length];
        for (var i = 0; i < snapshot.Items.Length; i++) {
            var current = snapshot.Items[i];
            if (!string.Equals(current.Id, normalizedItemId, StringComparison.OrdinalIgnoreCase)) {
                items[i] = current;
                continue;
            }

            var nextState = NormalizeBackgroundWorkState(state);
            string nextResultReference = string.IsNullOrWhiteSpace(resultReference)
                ? (current.ResultReference ?? string.Empty)
                : resultReference.Trim();
            var nextExecutionCallId = string.IsNullOrWhiteSpace(executionCallId)
                ? (current.LastExecutionCallId ?? string.Empty)
                : executionCallId.Trim();
            var nextExecutionAttemptCount = Math.Max(0, current.ExecutionAttemptCount);
            var nextExecutionStartedUtcTicks = current.LastExecutionStartedUtcTicks;
            var nextExecutionFinishedUtcTicks = current.LastExecutionFinishedUtcTicks;
            var nextLeaseExpiresUtcTicks = current.LeaseExpiresUtcTicks;
            if (string.Equals(nextState, BackgroundWorkStateRunning, StringComparison.OrdinalIgnoreCase)) {
                nextExecutionAttemptCount++;
                nextExecutionStartedUtcTicks = updatedTicks;
                nextExecutionFinishedUtcTicks = 0;
                nextLeaseExpiresUtcTicks = updatedTicks + BackgroundWorkClaimLeaseDuration.Ticks;
            } else if (string.Equals(nextState, BackgroundWorkStateReady, StringComparison.OrdinalIgnoreCase)
                       || string.Equals(nextState, BackgroundWorkStateCompleted, StringComparison.OrdinalIgnoreCase)) {
                nextExecutionFinishedUtcTicks = updatedTicks;
                nextLeaseExpiresUtcTicks = 0;
            }

            if (string.Equals(current.State, nextState, StringComparison.OrdinalIgnoreCase)
                && string.Equals(current.ResultReference ?? string.Empty, nextResultReference ?? string.Empty, StringComparison.Ordinal)
                && current.ExecutionAttemptCount == nextExecutionAttemptCount
                && string.Equals(current.LastExecutionCallId ?? string.Empty, nextExecutionCallId ?? string.Empty, StringComparison.Ordinal)
                && current.LastExecutionStartedUtcTicks == nextExecutionStartedUtcTicks
                && current.LastExecutionFinishedUtcTicks == nextExecutionFinishedUtcTicks
                && current.LeaseExpiresUtcTicks == nextLeaseExpiresUtcTicks) {
                items[i] = current;
                continue;
            }

            items[i] = current with {
                State = nextState,
                ResultReference = nextResultReference!,
                ExecutionAttemptCount = nextExecutionAttemptCount,
                LastExecutionCallId = nextExecutionCallId!,
                LastExecutionStartedUtcTicks = nextExecutionStartedUtcTicks,
                LastExecutionFinishedUtcTicks = nextExecutionFinishedUtcTicks,
                LeaseExpiresUtcTicks = nextLeaseExpiresUtcTicks,
                UpdatedUtcTicks = updatedTicks
            };
            changed = true;
        }

        if (!changed) {
            return false;
        }

        var updatedSnapshot = BuildBackgroundWorkSnapshotFromItems(items, snapshot.RecentEvidenceTools);
        RememberThreadBackgroundWorkSnapshot(normalizedThreadId, updatedSnapshot, updatedTicks);
        return true;
    }

    private void RememberBackgroundWorkExecutionOutcome(
        string threadId,
        string itemId,
        string toolCallId,
        IReadOnlyList<ToolOutputDto> outputs) {
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        var normalizedItemId = (itemId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0 || normalizedItemId.Length == 0 || outputs.Count == 0) {
            return;
        }

        if (!TryGetThreadBackgroundWorkItem(normalizedThreadId, normalizedItemId, out var item)) {
            return;
        }

        var output = outputs[0];
        var resultReference = BuildBackgroundWorkExecutionOutcomeReference(
            existingResultReference: item.ResultReference,
            toolCallId: toolCallId,
            output: output);
        var nextState = output.Ok == true ? BackgroundWorkStateCompleted : BackgroundWorkStateReady;
        _ = TrySetThreadBackgroundWorkItemState(normalizedThreadId, normalizedItemId, nextState, resultReference, toolCallId);
    }

    private bool TrySetThreadBackgroundWorkLeaseExpiry(string threadId, string itemId, long leaseExpiresUtcTicks) {
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        var normalizedItemId = (itemId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0 || normalizedItemId.Length == 0) {
            return false;
        }

        if (!TryGetRememberedThreadBackgroundWorkSnapshot(normalizedThreadId, out var snapshot)
            || snapshot.Items.Length == 0) {
            return false;
        }

        var changed = false;
        var updatedTicks = DateTime.UtcNow.Ticks;
        var items = new ThreadBackgroundWorkItem[snapshot.Items.Length];
        for (var i = 0; i < snapshot.Items.Length; i++) {
            var current = snapshot.Items[i];
            if (!string.Equals(current.Id, normalizedItemId, StringComparison.OrdinalIgnoreCase)) {
                items[i] = current;
                continue;
            }

            var nextLeaseExpiresUtcTicks = Math.Max(0, leaseExpiresUtcTicks);
            if (current.LeaseExpiresUtcTicks == nextLeaseExpiresUtcTicks) {
                items[i] = current;
                continue;
            }

            items[i] = current with {
                LeaseExpiresUtcTicks = nextLeaseExpiresUtcTicks,
                UpdatedUtcTicks = updatedTicks
            };
            changed = true;
        }

        if (!changed) {
            return false;
        }

        var updatedSnapshot = BuildBackgroundWorkSnapshotFromItems(items, snapshot.RecentEvidenceTools);
        RememberThreadBackgroundWorkSnapshot(normalizedThreadId, updatedSnapshot, updatedTicks);
        return true;
    }

    private bool TryGetThreadBackgroundWorkItem(string threadId, string itemId, out ThreadBackgroundWorkItem item) {
        item = default;
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        var normalizedItemId = (itemId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0 || normalizedItemId.Length == 0) {
            return false;
        }

        if (!TryGetRememberedThreadBackgroundWorkSnapshot(normalizedThreadId, out var snapshot)
            || snapshot.Items.Length == 0) {
            return false;
        }

        for (var i = 0; i < snapshot.Items.Length; i++) {
            if (!string.Equals(snapshot.Items[i].Id, normalizedItemId, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            item = snapshot.Items[i];
            return true;
        }

        return false;
    }

    private static string BuildBackgroundWorkQueuedStatusMessage(int queuedCount) {
        var boundedCount = Math.Max(0, queuedCount);
        return boundedCount == 1
            ? "Queued 1 safe follow-up item for background preparation."
            : "Queued " + boundedCount.ToString(System.Globalization.CultureInfo.InvariantCulture)
              + " safe follow-up items for background preparation.";
    }

    private static string BuildBackgroundWorkReadyStatusMessage(
        int readyCount,
        IReadOnlyList<string> recentEvidenceTools,
        IReadOnlyList<ThreadBackgroundWorkItem>? items = null) {
        var boundedCount = Math.Max(0, readyCount);
        var message = boundedCount == 1
            ? "Prepared 1 read-only follow-up item from recent evidence."
            : "Prepared " + boundedCount.ToString(System.Globalization.CultureInfo.InvariantCulture)
              + " read-only follow-up items from recent evidence.";
        if (recentEvidenceTools is null || recentEvidenceTools.Count == 0) {
            return message;
        }

        var normalizedTools = recentEvidenceTools
            .Where(static toolName => !string.IsNullOrWhiteSpace(toolName))
            .Take(MaxBackgroundWorkEvidenceTools)
            .ToArray();
        if (normalizedTools.Length == 0) {
            var followUpSummary = BuildBackgroundWorkFollowUpSummary(items ?? Array.Empty<ThreadBackgroundWorkItem>());
            if (followUpSummary.PriorityFocus.Length > 0) {
                return message + " Priority: " + followUpSummary.PriorityFocus + ".";
            }

            return message;
        }

        message += " Evidence: " + string.Join(", ", normalizedTools) + ".";
        var summary = BuildBackgroundWorkFollowUpSummary(items ?? Array.Empty<ThreadBackgroundWorkItem>());
        if (summary.PriorityFocus.Length > 0) {
            message += " Priority: " + summary.PriorityFocus + ".";
        }

        return message;
    }

    private static string BuildBackgroundWorkRunningStatusMessage(int runningCount) {
        return BuildBackgroundWorkRunningStatusMessage(runningCount, items: null);
    }

    private static string BuildBackgroundWorkRunningStatusMessage(
        int runningCount,
        IReadOnlyList<ThreadBackgroundWorkItem>? items) {
        var boundedCount = Math.Max(0, runningCount);
        var message = boundedCount == 1
            ? "Background follow-up preparation started."
            : "Background preparation started for "
              + boundedCount.ToString(System.Globalization.CultureInfo.InvariantCulture)
              + " follow-up items.";
        var focusSuffix = BuildBackgroundWorkFocusSuffix(items);
        return focusSuffix.Length == 0
            ? message
            : message + focusSuffix;
    }

    private static string BuildBackgroundWorkCompletedStatusMessage(int completedCount) {
        return BuildBackgroundWorkCompletedStatusMessage(completedCount, items: null);
    }

    private static string BuildBackgroundWorkCompletedStatusMessage(
        int completedCount,
        IReadOnlyList<ThreadBackgroundWorkItem>? items) {
        var boundedCount = Math.Max(0, completedCount);
        var message = boundedCount == 1
            ? "Background follow-up item completed."
            : "Completed "
              + boundedCount.ToString(System.Globalization.CultureInfo.InvariantCulture)
              + " background follow-up items.";
        var focusSuffix = BuildBackgroundWorkFocusSuffix(items);
        return focusSuffix.Length == 0
            ? message
            : message + focusSuffix;
    }

    private static string BuildBackgroundWorkFocusSuffix(IReadOnlyList<ThreadBackgroundWorkItem>? items) {
        if (items is null || items.Count == 0) {
            return string.Empty;
        }

        var summary = BuildBackgroundWorkFollowUpSummary(items);
        if (summary.PriorityFocus.Length > 0) {
            return " Focus: " + summary.PriorityFocus + ".";
        }

        if (summary.FollowUpKinds.Length == 0) {
            return string.Empty;
        }

        return " Focus: " + string.Join(", ", summary.FollowUpKinds) + ".";
    }

    private static string BuildBackgroundWorkExecutionOutcomeReference(
        string? existingResultReference,
        string toolCallId,
        ToolOutputDto output) {
        var segments = new List<string>();
        var existing = (existingResultReference ?? string.Empty).Trim();
        if (existing.Length > 0) {
            segments.Add(existing);
        }

        segments.Add("execution_call_id=" + (toolCallId ?? string.Empty).Trim());
        segments.Add("execution_status=" + (output.Ok == true ? "completed" : "failed"));
        if (!string.IsNullOrWhiteSpace(output.ErrorCode)) {
            segments.Add("execution_error_code=" + output.ErrorCode.Trim());
        }

        return string.Join(";", segments);
    }

    private void RememberThreadBackgroundWorkSnapshot(
        string threadId,
        ThreadBackgroundWorkSnapshot snapshot,
        long seenUtcTicks) {
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0 || seenUtcTicks <= 0 || IsEmptyBackgroundWorkSnapshot(snapshot)) {
            ClearRememberedThreadBackgroundWork(normalizedThreadId);
            return;
        }

        lock (_threadBackgroundWorkLock) {
            _threadBackgroundWorkByThreadId[normalizedThreadId] = snapshot;
            _threadBackgroundWorkSeenUtcTicksByThreadId[normalizedThreadId] = seenUtcTicks;
            TrimThreadBackgroundWorkContextsNoLock();
        }

        PersistThreadBackgroundWorkSnapshot(normalizedThreadId, snapshot, seenUtcTicks);
    }

    private bool TryGetRememberedThreadBackgroundWorkSnapshot(string threadId, out ThreadBackgroundWorkSnapshot snapshot) {
        snapshot = EmptyBackgroundWorkSnapshot();
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0) {
            return false;
        }

        var foundCachedSnapshot = false;
        lock (_threadBackgroundWorkLock) {
            if (TryGetRememberedThreadBackgroundWorkSnapshotNoLock(normalizedThreadId, out snapshot)) {
                foundCachedSnapshot = true;
            }
        }
        if (foundCachedSnapshot) {
            snapshot = NormalizeThreadBackgroundWorkLeases(normalizedThreadId, snapshot);
            return true;
        }

        if (!TryLoadThreadBackgroundWorkSnapshot(normalizedThreadId, out var persistedSnapshot, out var persistedSeenUtcTicks)
            || IsEmptyBackgroundWorkSnapshot(persistedSnapshot)) {
            return false;
        }

        var foundSecondCachedSnapshot = false;
        lock (_threadBackgroundWorkLock) {
            if (TryGetRememberedThreadBackgroundWorkSnapshotNoLock(normalizedThreadId, out snapshot)) {
                foundSecondCachedSnapshot = true;
            } else {
                _threadBackgroundWorkByThreadId[normalizedThreadId] = persistedSnapshot;
                _threadBackgroundWorkSeenUtcTicksByThreadId[normalizedThreadId] = persistedSeenUtcTicks;
                TrimThreadBackgroundWorkContextsNoLock();
                snapshot = persistedSnapshot;
            }
        }

        snapshot = NormalizeThreadBackgroundWorkLeases(normalizedThreadId, snapshot);
        return foundSecondCachedSnapshot || !IsEmptyBackgroundWorkSnapshot(snapshot);
    }

    private bool TryGetRememberedThreadBackgroundWorkSnapshotNoLock(string normalizedThreadId, out ThreadBackgroundWorkSnapshot snapshot) {
        snapshot = EmptyBackgroundWorkSnapshot();
        if (!_threadBackgroundWorkByThreadId.TryGetValue(normalizedThreadId, out var cachedSnapshot)
            || !_threadBackgroundWorkSeenUtcTicksByThreadId.TryGetValue(normalizedThreadId, out var seenUtcTicks)
            || seenUtcTicks <= 0
            || !TryGetUtcDateTimeFromTicks(seenUtcTicks, out var seenUtc)) {
            return false;
        }

        var nowUtc = DateTime.UtcNow;
        if (seenUtc > nowUtc || nowUtc - seenUtc > ThreadBackgroundWorkContextMaxAge || IsEmptyBackgroundWorkSnapshot(cachedSnapshot)) {
            _threadBackgroundWorkByThreadId.Remove(normalizedThreadId);
            _threadBackgroundWorkSeenUtcTicksByThreadId.Remove(normalizedThreadId);
            PersistThreadBackgroundWorkSnapshot(normalizedThreadId, EmptyBackgroundWorkSnapshot(), seenUtcTicks: 0);
            return false;
        }

        snapshot = cachedSnapshot;
        return true;
    }

    private void ClearRememberedThreadBackgroundWork(string threadId) {
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0) {
            return;
        }

        lock (_threadBackgroundWorkLock) {
            _threadBackgroundWorkByThreadId.Remove(normalizedThreadId);
            _threadBackgroundWorkSeenUtcTicksByThreadId.Remove(normalizedThreadId);
        }

        PersistThreadBackgroundWorkSnapshot(normalizedThreadId, EmptyBackgroundWorkSnapshot(), seenUtcTicks: 0);
    }

    private void ClearThreadBackgroundWorkSnapshots() {
        lock (_threadBackgroundWorkLock) {
            _threadBackgroundWorkByThreadId.Clear();
            _threadBackgroundWorkSeenUtcTicksByThreadId.Clear();
        }

        ClearBackgroundWorkSnapshots();
    }

    private void TrimThreadBackgroundWorkContextsNoLock() {
        if (_threadBackgroundWorkByThreadId.Count <= MaxTrackedThreadBackgroundWorkContexts) {
            return;
        }

        var removeCount = _threadBackgroundWorkByThreadId.Count - MaxTrackedThreadBackgroundWorkContexts;
        if (removeCount <= 0) {
            return;
        }

        var threadIdsToRemove = _threadBackgroundWorkSeenUtcTicksByThreadId
            .OrderBy(static pair => pair.Value)
            .ThenBy(static pair => pair.Key, StringComparer.Ordinal)
            .Take(removeCount)
            .Select(static pair => pair.Key)
            .ToArray();
        for (var i = 0; i < threadIdsToRemove.Length; i++) {
            _threadBackgroundWorkByThreadId.Remove(threadIdsToRemove[i]);
            _threadBackgroundWorkSeenUtcTicksByThreadId.Remove(threadIdsToRemove[i]);
        }
    }

    private static string NormalizeBackgroundWorkKind(string? kind) {
        return string.Equals((kind ?? string.Empty).Trim(), BackgroundWorkKindToolHandoff, StringComparison.OrdinalIgnoreCase)
            ? BackgroundWorkKindToolHandoff
            : BackgroundWorkKindPendingAction;
    }

    private static string NormalizeBackgroundWorkMutability(string? mutability) {
        return string.Equals((mutability ?? string.Empty).Trim(), BackgroundWorkMutabilityUnknown, StringComparison.OrdinalIgnoreCase)
            ? BackgroundWorkMutabilityUnknown
            : BackgroundWorkMutabilityReadOnly;
    }

    private static string[] NormalizeBackgroundWorkToolNames(IEnumerable<string>? values) {
        return (values ?? Array.Empty<string>())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => NormalizeToolNameForAnswerPlan(value))
            .Where(static value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxBackgroundWorkEvidenceTools)
            .ToArray();
    }

    private static string NormalizeBackgroundWorkArgumentsJson(string? argumentsJson) {
        var normalized = (argumentsJson ?? string.Empty).Trim();
        return normalized.Length == 0 ? "{}" : normalized;
    }

    private static string NormalizeBackgroundWorkState(string? state) {
        var normalized = (state ?? string.Empty).Trim();
        if (string.Equals(normalized, BackgroundWorkStateReady, StringComparison.OrdinalIgnoreCase)) {
            return BackgroundWorkStateReady;
        }

        if (string.Equals(normalized, BackgroundWorkStateRunning, StringComparison.OrdinalIgnoreCase)) {
            return BackgroundWorkStateRunning;
        }

        if (string.Equals(normalized, BackgroundWorkStateCompleted, StringComparison.OrdinalIgnoreCase)) {
            return BackgroundWorkStateCompleted;
        }

        return BackgroundWorkStateQueued;
    }

    private static string NormalizeBackgroundWorkToken(string? value) {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0) {
            return string.Empty;
        }

        return normalized
            .Replace("[]", string.Empty, StringComparison.Ordinal)
            .Replace("\\", "_", StringComparison.Ordinal)
            .Replace("/", "_", StringComparison.Ordinal)
            .Replace(":", "_", StringComparison.Ordinal)
            .Replace(".", "_", StringComparison.Ordinal)
            .Replace(" ", "_", StringComparison.Ordinal)
            .Replace(",", "_", StringComparison.Ordinal);
    }
}
