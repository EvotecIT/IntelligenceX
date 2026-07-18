using System;
using System.Collections.Generic;
using System.Linq;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.OpenAI;

namespace IntelligenceX.Chat.App.Conversation;

/// <summary>
/// Three-way merges the shared profile, memory, and conversation fields written by both desktop shells.
/// </summary>
internal static class DesktopChatStateMerger {
    internal static bool SharedStateEquals(ChatAppState left, ChatAppState right) {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return string.Equals(left.UserName, right.UserName, StringComparison.Ordinal)
               && string.Equals(left.AssistantPersona, right.AssistantPersona, StringComparison.Ordinal)
               && string.Equals(left.ThemePreset, right.ThemePreset, StringComparison.OrdinalIgnoreCase)
               && left.OnboardingCompleted == right.OnboardingCompleted
               && left.PersistentMemoryEnabled == right.PersistentMemoryEnabled
               && string.Equals(left.ActiveConversationId, right.ActiveConversationId, StringComparison.OrdinalIgnoreCase)
               && SequenceEqual(left.MemoryFacts, right.MemoryFacts, MemoryFactEquals)
               && SequenceEqual(
                   left.Conversations,
                   right.Conversations,
                   DesktopChatConversationStateMerger.ConversationEquals);
    }

    internal static bool RuntimeAndPreferenceStateEquals(ChatAppState left, ChatAppState right) {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return string.Equals(left.LocalProviderTransport, right.LocalProviderTransport, StringComparison.OrdinalIgnoreCase)
               && left.LocalProviderRuntimeOverrideActive == right.LocalProviderRuntimeOverrideActive
               && string.Equals(left.LocalProviderBaseUrl, right.LocalProviderBaseUrl, StringComparison.OrdinalIgnoreCase)
               && string.Equals(left.LocalProviderModel, right.LocalProviderModel, StringComparison.Ordinal)
               && string.Equals(left.LocalProviderOpenAIAuthMode, right.LocalProviderOpenAIAuthMode, StringComparison.OrdinalIgnoreCase)
               && string.Equals(left.LocalProviderOpenAIBasicUsername, right.LocalProviderOpenAIBasicUsername, StringComparison.Ordinal)
               && string.Equals(left.LocalProviderOpenAIAccountId, right.LocalProviderOpenAIAccountId, StringComparison.Ordinal)
               && left.ActiveNativeAccountSlot == right.ActiveNativeAccountSlot
               && string.Equals(left.NativeAccountSlot1, right.NativeAccountSlot1, StringComparison.Ordinal)
               && string.Equals(left.NativeAccountSlot2, right.NativeAccountSlot2, StringComparison.Ordinal)
               && string.Equals(left.NativeAccountSlot3, right.NativeAccountSlot3, StringComparison.Ordinal)
               && SequenceEqual(left.NativeAccountSlots, right.NativeAccountSlots, StringComparer.Ordinal.Equals)
               && string.Equals(left.LocalProviderReasoningEffort, right.LocalProviderReasoningEffort, StringComparison.Ordinal)
               && string.Equals(left.LocalProviderReasoningSummary, right.LocalProviderReasoningSummary, StringComparison.Ordinal)
               && string.Equals(left.LocalProviderTextVerbosity, right.LocalProviderTextVerbosity, StringComparison.Ordinal)
               && left.LocalProviderTemperature == right.LocalProviderTemperature
               && left.LocalProviderImageGenerationEnabled == right.LocalProviderImageGenerationEnabled
               && left.LocalProviderImageGenerationOverrideActive == right.LocalProviderImageGenerationOverrideActive
               && string.Equals(left.LocalProviderImageGenerationQuality, right.LocalProviderImageGenerationQuality, StringComparison.Ordinal)
               && string.Equals(left.LocalProviderImageGenerationSize, right.LocalProviderImageGenerationSize, StringComparison.Ordinal)
               && string.Equals(left.LocalProviderImageGenerationOutputFormat, right.LocalProviderImageGenerationOutputFormat, StringComparison.Ordinal)
               && left.LocalProviderImageGenerationOutputCompression == right.LocalProviderImageGenerationOutputCompression
               && string.Equals(left.LocalProviderImageGenerationBackground, right.LocalProviderImageGenerationBackground, StringComparison.Ordinal)
               && string.Equals(left.LocalProviderImageGenerationOutputDirectory, right.LocalProviderImageGenerationOutputDirectory, StringComparison.OrdinalIgnoreCase)
               && string.Equals(left.TimestampMode, right.TimestampMode, StringComparison.OrdinalIgnoreCase)
               && left.AutonomyMaxToolRounds == right.AutonomyMaxToolRounds
               && left.AutonomyParallelTools == right.AutonomyParallelTools
               && left.AutonomyTurnTimeoutSeconds == right.AutonomyTurnTimeoutSeconds
               && left.AutonomyToolTimeoutSeconds == right.AutonomyToolTimeoutSeconds
               && left.AutonomyWeightedToolRouting == right.AutonomyWeightedToolRouting
               && left.AutonomyMaxCandidateTools == right.AutonomyMaxCandidateTools
               && left.AutonomyPlanExecuteReviewLoop == right.AutonomyPlanExecuteReviewLoop
               && left.AutonomyMaxReviewPasses == right.AutonomyMaxReviewPasses
               && left.AutonomyModelHeartbeatSeconds == right.AutonomyModelHeartbeatSeconds
               && string.Equals(left.ExportSaveMode, right.ExportSaveMode, StringComparison.OrdinalIgnoreCase)
               && string.Equals(left.ExportDefaultFormat, right.ExportDefaultFormat, StringComparison.OrdinalIgnoreCase)
               && string.Equals(left.ExportVisualThemeMode, right.ExportVisualThemeMode, StringComparison.OrdinalIgnoreCase)
               && left.ExportDocxVisualMaxWidthPx == right.ExportDocxVisualMaxWidthPx
               && string.Equals(left.ExportLastDirectory, right.ExportLastDirectory, StringComparison.OrdinalIgnoreCase)
               && left.QueueAutoDispatchEnabled == right.QueueAutoDispatchEnabled
               && left.ProactiveModeEnabled == right.ProactiveModeEnabled
               && left.ShowAssistantTurnTrace == right.ShowAssistantTurnTrace
               && left.ShowAssistantDraftBubbles == right.ShowAssistantDraftBubbles
               && ModelCatalogContentEquals(left, right)
               && SequenceEqual(left.DisabledTools, right.DisabledTools, StringComparer.Ordinal.Equals);
    }

    /// <summary>
    /// Compares live queue state that must not be silently adopted as a persistence baseline by another window.
    /// </summary>
    internal static bool LiveOperationalStateEquals(ChatAppState left, ChatAppState right) {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return SequenceEqual(left.PendingTurns, right.PendingTurns, QueuedTurnEquals)
               && SequenceEqual(left.QueuedTurnsAfterLogin, right.QueuedTurnsAfterLogin, QueuedTurnEquals);
    }

    internal static ChatAppState MergeLegacySnapshot(
        ChatAppState local,
        ChatAppState? baseline,
        ChatAppState? latest) {
        ArgumentNullException.ThrowIfNull(local);
        if (baseline is null || latest is null) {
            return local;
        }

        MergeRuntimeAndPreferenceState(local, baseline, latest);
        local.UserName = Resolve(local.UserName, baseline.UserName, latest.UserName, StringComparer.Ordinal);
        local.AssistantPersona = Resolve(
            local.AssistantPersona,
            baseline.AssistantPersona,
            latest.AssistantPersona,
            StringComparer.Ordinal);
        local.ThemePreset = Resolve(
            local.ThemePreset,
            baseline.ThemePreset,
            latest.ThemePreset,
            StringComparer.OrdinalIgnoreCase) ?? Theming.ThemeContract.DefaultPreset;
        local.OnboardingCompleted = Resolve(
            local.OnboardingCompleted,
            baseline.OnboardingCompleted,
            latest.OnboardingCompleted);
        local.PersistentMemoryEnabled = Resolve(
            local.PersistentMemoryEnabled,
            baseline.PersistentMemoryEnabled,
            latest.PersistentMemoryEnabled);
        local.MemoryFacts = MergeMemoryFacts(local.MemoryFacts, baseline.MemoryFacts, latest.MemoryFacts);
        local.Conversations = DesktopChatConversationStateMerger.MergeConversations(
            local.Conversations,
            baseline.Conversations,
            latest.Conversations);
        local.PendingTurns = MergeQueuedTurns(local.PendingTurns, baseline.PendingTurns, latest.PendingTurns);
        local.QueuedTurnsAfterLogin = MergeQueuedTurns(
            local.QueuedTurnsAfterLogin,
            baseline.QueuedTurnsAfterLogin,
            latest.QueuedTurnsAfterLogin);
        local.ActiveConversationId = Resolve(
            local.ActiveConversationId,
            baseline.ActiveConversationId,
            latest.ActiveConversationId,
            StringComparer.OrdinalIgnoreCase);

        var active = local.Conversations.FirstOrDefault(conversation =>
            string.Equals(conversation.Id, local.ActiveConversationId, StringComparison.OrdinalIgnoreCase));
        if (active is null) {
            active = local.Conversations.FirstOrDefault(conversation =>
                         !string.Equals(conversation.Id, "chat-system", StringComparison.OrdinalIgnoreCase))
                     ?? local.Conversations.FirstOrDefault();
            local.ActiveConversationId = active?.Id;
        }

        if (active is not null) {
            local.ThreadId = active.ThreadId;
            local.Messages = active.Messages
                .Select(DesktopChatConversationStateMerger.CloneMessage)
                .ToList();
        } else {
            local.ThreadId = Resolve(
                local.ThreadId,
                baseline.ThreadId,
                latest.ThreadId,
                StringComparer.Ordinal);
            local.Messages = DesktopChatConversationStateMerger.MergeMessages(
                local.Messages,
                baseline.Messages,
                latest.Messages);
        }

        return local;
    }

    private static void MergeRuntimeAndPreferenceState(ChatAppState local, ChatAppState baseline, ChatAppState latest) {
        local.LocalProviderTransport = Resolve(
            local.LocalProviderTransport,
            baseline.LocalProviderTransport,
            latest.LocalProviderTransport,
            StringComparer.OrdinalIgnoreCase) ?? "native";
        local.LocalProviderRuntimeOverrideActive = Resolve(
            local.LocalProviderRuntimeOverrideActive,
            baseline.LocalProviderRuntimeOverrideActive,
            latest.LocalProviderRuntimeOverrideActive);
        local.LocalProviderRuntimeOverrideActiveWasPresent = Resolve(
            local.LocalProviderRuntimeOverrideActiveWasPresent,
            baseline.LocalProviderRuntimeOverrideActiveWasPresent,
            latest.LocalProviderRuntimeOverrideActiveWasPresent);
        local.LocalProviderBaseUrl = Resolve(
            local.LocalProviderBaseUrl,
            baseline.LocalProviderBaseUrl,
            latest.LocalProviderBaseUrl,
            StringComparer.OrdinalIgnoreCase);
        local.LocalProviderModel = Resolve(
            local.LocalProviderModel,
            baseline.LocalProviderModel,
            latest.LocalProviderModel,
            StringComparer.Ordinal) ?? OpenAIModelCatalog.DefaultModel;
        local.LocalProviderOpenAIAuthMode = Resolve(
            local.LocalProviderOpenAIAuthMode,
            baseline.LocalProviderOpenAIAuthMode,
            latest.LocalProviderOpenAIAuthMode,
            StringComparer.OrdinalIgnoreCase) ?? "bearer";
        local.LocalProviderOpenAIBasicUsername = Resolve(
            local.LocalProviderOpenAIBasicUsername,
            baseline.LocalProviderOpenAIBasicUsername,
            latest.LocalProviderOpenAIBasicUsername,
            StringComparer.Ordinal) ?? string.Empty;
        local.LocalProviderOpenAIAccountId = Resolve(
            local.LocalProviderOpenAIAccountId,
            baseline.LocalProviderOpenAIAccountId,
            latest.LocalProviderOpenAIAccountId,
            StringComparer.Ordinal) ?? string.Empty;
        local.ActiveNativeAccountSlot = ResolveValue(
            local.ActiveNativeAccountSlot,
            baseline.ActiveNativeAccountSlot,
            latest.ActiveNativeAccountSlot);
        local.NativeAccountSlot1 = Resolve(local.NativeAccountSlot1, baseline.NativeAccountSlot1, latest.NativeAccountSlot1, StringComparer.Ordinal) ?? string.Empty;
        local.NativeAccountSlot2 = Resolve(local.NativeAccountSlot2, baseline.NativeAccountSlot2, latest.NativeAccountSlot2, StringComparer.Ordinal) ?? string.Empty;
        local.NativeAccountSlot3 = Resolve(local.NativeAccountSlot3, baseline.NativeAccountSlot3, latest.NativeAccountSlot3, StringComparer.Ordinal) ?? string.Empty;
        local.NativeAccountSlots = ResolveStringList(local.NativeAccountSlots, baseline.NativeAccountSlots, latest.NativeAccountSlots);
        local.LocalProviderReasoningEffort = Resolve(local.LocalProviderReasoningEffort, baseline.LocalProviderReasoningEffort, latest.LocalProviderReasoningEffort, StringComparer.Ordinal) ?? string.Empty;
        local.LocalProviderReasoningSummary = Resolve(local.LocalProviderReasoningSummary, baseline.LocalProviderReasoningSummary, latest.LocalProviderReasoningSummary, StringComparer.Ordinal) ?? string.Empty;
        local.LocalProviderTextVerbosity = Resolve(local.LocalProviderTextVerbosity, baseline.LocalProviderTextVerbosity, latest.LocalProviderTextVerbosity, StringComparer.Ordinal) ?? string.Empty;
        local.LocalProviderTemperature = ResolveValue(local.LocalProviderTemperature, baseline.LocalProviderTemperature, latest.LocalProviderTemperature);
        local.LocalProviderImageGenerationEnabled = Resolve(local.LocalProviderImageGenerationEnabled, baseline.LocalProviderImageGenerationEnabled, latest.LocalProviderImageGenerationEnabled);
        local.LocalProviderImageGenerationOverrideActive = Resolve(local.LocalProviderImageGenerationOverrideActive, baseline.LocalProviderImageGenerationOverrideActive, latest.LocalProviderImageGenerationOverrideActive);
        local.LocalProviderImageGenerationOverrideActiveWasPresent = Resolve(local.LocalProviderImageGenerationOverrideActiveWasPresent, baseline.LocalProviderImageGenerationOverrideActiveWasPresent, latest.LocalProviderImageGenerationOverrideActiveWasPresent);
        local.LocalProviderImageGenerationQuality = Resolve(local.LocalProviderImageGenerationQuality, baseline.LocalProviderImageGenerationQuality, latest.LocalProviderImageGenerationQuality, StringComparer.Ordinal) ?? string.Empty;
        local.LocalProviderImageGenerationSize = Resolve(local.LocalProviderImageGenerationSize, baseline.LocalProviderImageGenerationSize, latest.LocalProviderImageGenerationSize, StringComparer.Ordinal) ?? string.Empty;
        local.LocalProviderImageGenerationOutputFormat = Resolve(local.LocalProviderImageGenerationOutputFormat, baseline.LocalProviderImageGenerationOutputFormat, latest.LocalProviderImageGenerationOutputFormat, StringComparer.Ordinal) ?? string.Empty;
        local.LocalProviderImageGenerationOutputCompression = ResolveValue(local.LocalProviderImageGenerationOutputCompression, baseline.LocalProviderImageGenerationOutputCompression, latest.LocalProviderImageGenerationOutputCompression);
        local.LocalProviderImageGenerationBackground = Resolve(local.LocalProviderImageGenerationBackground, baseline.LocalProviderImageGenerationBackground, latest.LocalProviderImageGenerationBackground, StringComparer.Ordinal) ?? string.Empty;
        local.LocalProviderImageGenerationOutputDirectory = Resolve(local.LocalProviderImageGenerationOutputDirectory, baseline.LocalProviderImageGenerationOutputDirectory, latest.LocalProviderImageGenerationOutputDirectory, StringComparer.OrdinalIgnoreCase) ?? string.Empty;
        local.TimestampMode = Resolve(local.TimestampMode, baseline.TimestampMode, latest.TimestampMode, StringComparer.OrdinalIgnoreCase) ?? "seconds";
        local.AutonomyMaxToolRounds = ResolveValue(local.AutonomyMaxToolRounds, baseline.AutonomyMaxToolRounds, latest.AutonomyMaxToolRounds);
        local.AutonomyParallelTools = ResolveValue(local.AutonomyParallelTools, baseline.AutonomyParallelTools, latest.AutonomyParallelTools);
        local.AutonomyTurnTimeoutSeconds = ResolveValue(local.AutonomyTurnTimeoutSeconds, baseline.AutonomyTurnTimeoutSeconds, latest.AutonomyTurnTimeoutSeconds);
        local.AutonomyToolTimeoutSeconds = ResolveValue(local.AutonomyToolTimeoutSeconds, baseline.AutonomyToolTimeoutSeconds, latest.AutonomyToolTimeoutSeconds);
        local.AutonomyWeightedToolRouting = ResolveValue(local.AutonomyWeightedToolRouting, baseline.AutonomyWeightedToolRouting, latest.AutonomyWeightedToolRouting);
        local.AutonomyMaxCandidateTools = ResolveValue(local.AutonomyMaxCandidateTools, baseline.AutonomyMaxCandidateTools, latest.AutonomyMaxCandidateTools);
        local.AutonomyPlanExecuteReviewLoop = ResolveValue(local.AutonomyPlanExecuteReviewLoop, baseline.AutonomyPlanExecuteReviewLoop, latest.AutonomyPlanExecuteReviewLoop);
        local.AutonomyMaxReviewPasses = ResolveValue(local.AutonomyMaxReviewPasses, baseline.AutonomyMaxReviewPasses, latest.AutonomyMaxReviewPasses);
        local.AutonomyModelHeartbeatSeconds = ResolveValue(local.AutonomyModelHeartbeatSeconds, baseline.AutonomyModelHeartbeatSeconds, latest.AutonomyModelHeartbeatSeconds);
        local.ExportSaveMode = Resolve(local.ExportSaveMode, baseline.ExportSaveMode, latest.ExportSaveMode, StringComparer.OrdinalIgnoreCase) ?? ExportPreferencesContract.DefaultSaveMode;
        local.ExportDefaultFormat = Resolve(local.ExportDefaultFormat, baseline.ExportDefaultFormat, latest.ExportDefaultFormat, StringComparer.OrdinalIgnoreCase) ?? ExportPreferencesContract.DefaultFormat;
        local.ExportVisualThemeMode = Resolve(local.ExportVisualThemeMode, baseline.ExportVisualThemeMode, latest.ExportVisualThemeMode, StringComparer.OrdinalIgnoreCase) ?? ExportPreferencesContract.DefaultVisualThemeMode;
        local.ExportDocxVisualMaxWidthPx = ResolveValue(local.ExportDocxVisualMaxWidthPx, baseline.ExportDocxVisualMaxWidthPx, latest.ExportDocxVisualMaxWidthPx);
        local.ExportLastDirectory = Resolve(local.ExportLastDirectory, baseline.ExportLastDirectory, latest.ExportLastDirectory, StringComparer.OrdinalIgnoreCase);
        local.QueueAutoDispatchEnabled = Resolve(local.QueueAutoDispatchEnabled, baseline.QueueAutoDispatchEnabled, latest.QueueAutoDispatchEnabled);
        local.ProactiveModeEnabled = Resolve(local.ProactiveModeEnabled, baseline.ProactiveModeEnabled, latest.ProactiveModeEnabled);
        local.ShowAssistantTurnTrace = Resolve(local.ShowAssistantTurnTrace, baseline.ShowAssistantTurnTrace, latest.ShowAssistantTurnTrace);
        local.ShowAssistantDraftBubbles = Resolve(local.ShowAssistantDraftBubbles, baseline.ShowAssistantDraftBubbles, latest.ShowAssistantDraftBubbles);
        MergeModelCatalogState(local, baseline, latest);
        local.DisabledTools = ResolveStringList(local.DisabledTools, baseline.DisabledTools, latest.DisabledTools);
    }

    private static List<ChatMemoryFactState> MergeMemoryFacts(
        IReadOnlyList<ChatMemoryFactState>? local,
        IReadOnlyList<ChatMemoryFactState>? baseline,
        IReadOnlyList<ChatMemoryFactState>? latest) {
        var localByKey = IndexMemory(local);
        var baselineByKey = IndexMemory(baseline);
        var latestByKey = IndexMemory(latest);
        var keys = latestByKey.Keys
            .Concat(localByKey.Keys)
            .Concat(baselineByKey.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        var merged = new List<ChatMemoryFactState>();
        foreach (var key in keys) {
            localByKey.TryGetValue(key, out var localValue);
            baselineByKey.TryGetValue(key, out var baselineValue);
            latestByKey.TryGetValue(key, out var latestValue);
            var resolved = ResolveEntity(localValue, baselineValue, latestValue, MemoryFactEquals, CloneMemoryFact);
            if (resolved is not null) {
                merged.Add(resolved);
            }
        }

        return merged
            .OrderByDescending(static fact => fact.UpdatedUtc)
            .Take(120)
            .ToList();
    }

    private static T? ResolveEntity<T>(
        T? local,
        T? baseline,
        T? latest,
        Func<T, T, bool> equals,
        Func<T, T> clone)
        where T : class {
        if (baseline is null) {
            return local is null ? latest is null ? null : clone(latest) : clone(local);
        }
        if (local is null) {
            return latest is null || equals(latest, baseline) ? null : clone(latest);
        }
        if (latest is null) {
            return equals(local, baseline) ? null : clone(local);
        }
        if (equals(local, baseline)) {
            return clone(latest);
        }
        return clone(local);
    }

    private static string? Resolve(
        string? local,
        string? baseline,
        string? latest,
        StringComparer comparer) {
        if (comparer.Equals(local, baseline)) {
            return latest;
        }
        if (comparer.Equals(latest, baseline) || comparer.Equals(local, latest)) {
            return local;
        }
        return local;
    }

    private static bool Resolve(bool local, bool baseline, bool latest) =>
        local == baseline ? latest : local;

    private static T ResolveValue<T>(T local, T baseline, T latest) =>
        EqualityComparer<T>.Default.Equals(local, baseline) ? latest : local;

    private static List<string> ResolveStringList(
        IReadOnlyList<string>? local,
        IReadOnlyList<string>? baseline,
        IReadOnlyList<string>? latest) {
        var localValues = local ?? Array.Empty<string>();
        var baselineValues = baseline ?? Array.Empty<string>();
        var resolved = SequenceEqual(localValues, baselineValues, StringComparer.Ordinal.Equals)
            ? latest ?? Array.Empty<string>()
            : localValues;
        return resolved.ToList();
    }

    private static void MergeModelCatalogState(ChatAppState local, ChatAppState baseline, ChatAppState latest) {
        var source = ModelCatalogContentEquals(local, baseline) ? latest : local;
        local.CachedModelsTransport = source.CachedModelsTransport;
        local.CachedModelsBaseUrl = source.CachedModelsBaseUrl;
        local.CachedModels = (source.CachedModels ?? new List<ModelInfoDto>())
            .Select(CloneModelInfo)
            .ToList();
        local.CachedFavoriteModels = (source.CachedFavoriteModels ?? new List<string>()).ToList();
        local.CachedRecentModels = (source.CachedRecentModels ?? new List<string>()).ToList();
        local.CachedModelListIsStale = source.CachedModelListIsStale;
        local.CachedModelListWarning = source.CachedModelListWarning;
        local.CachedModelsUpdatedUtc = source.CachedModelsUpdatedUtc;
    }

    private static List<ChatQueuedTurnState> MergeQueuedTurns(
        IReadOnlyList<ChatQueuedTurnState>? local,
        IReadOnlyList<ChatQueuedTurnState>? baseline,
        IReadOnlyList<ChatQueuedTurnState>? latest) {
        var localValues = local ?? Array.Empty<ChatQueuedTurnState>();
        var baselineValues = baseline ?? Array.Empty<ChatQueuedTurnState>();
        var latestValues = latest ?? Array.Empty<ChatQueuedTurnState>();
        if (SequenceEqual(localValues, baselineValues, QueuedTurnEquals)) {
            return latestValues.Select(CloneQueuedTurn).ToList();
        }
        if (SequenceEqual(latestValues, baselineValues, QueuedTurnEquals)) {
            return localValues.Select(CloneQueuedTurn).ToList();
        }

        var localById = IndexQueuedTurns(localValues);
        var baselineById = IndexQueuedTurns(baselineValues);
        var latestById = IndexQueuedTurns(latestValues);
        var ids = latestById.Keys
            .Concat(localById.Keys)
            .Concat(baselineById.Keys)
            .Distinct(StringComparer.Ordinal);
        var merged = new List<ChatQueuedTurnState>();
        foreach (var id in ids) {
            var hasLocal = localById.TryGetValue(id, out var localValue);
            var hadBaseline = baselineById.ContainsKey(id);
            var hasLatest = latestById.TryGetValue(id, out var latestValue);
            if (hadBaseline) {
                // A baseline entry missing from either side was consumed there; never resurrect it.
                if (hasLocal && hasLatest) {
                    merged.Add(CloneQueuedTurn(localValue!));
                }
                continue;
            }

            if (hasLocal || hasLatest) {
                merged.Add(CloneQueuedTurn(hasLocal ? localValue! : latestValue!));
            }
        }

        return merged
            .OrderBy(static value => EnsureUtc(value.EnqueuedUtc))
            .Take(ChatQueueContract.MaxTurns)
            .ToList();
    }

    private static Dictionary<string, ChatQueuedTurnState> IndexQueuedTurns(
        IReadOnlyList<ChatQueuedTurnState> turns) {
        var indexed = new Dictionary<string, ChatQueuedTurnState>(StringComparer.Ordinal);
        var duplicateCounters = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var turn in turns) {
            var identity = (turn.ConversationId ?? string.Empty).Trim().ToUpperInvariant()
                           + "|" + EnsureUtc(turn.EnqueuedUtc).Ticks
                           + "|" + turn.SkipUserBubbleOnDispatch
                           + "|" + (turn.Text ?? string.Empty);
            var occurrence = duplicateCounters.TryGetValue(identity, out var count) ? count + 1 : 0;
            duplicateCounters[identity] = occurrence;
            indexed[identity + "|" + occurrence] = turn;
        }
        return indexed;
    }

    private static Dictionary<string, ChatMemoryFactState> IndexMemory(IReadOnlyList<ChatMemoryFactState>? facts) {
        return (facts ?? Array.Empty<ChatMemoryFactState>())
            .Where(static fact => !string.IsNullOrWhiteSpace(fact.Fact))
            .GroupBy(
                static fact => string.IsNullOrWhiteSpace(fact.Id) ? fact.Fact.Trim() : fact.Id.Trim(),
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);
    }

    private static bool MemoryFactEquals(ChatMemoryFactState left, ChatMemoryFactState right) =>
        string.Equals(left.Id, right.Id, StringComparison.OrdinalIgnoreCase)
        && string.Equals(left.Fact, right.Fact, StringComparison.Ordinal)
        && left.Weight == right.Weight
        && left.Tags.SequenceEqual(right.Tags, StringComparer.OrdinalIgnoreCase)
        && EnsureUtc(left.UpdatedUtc) == EnsureUtc(right.UpdatedUtc);

    private static bool QueuedTurnEquals(ChatQueuedTurnState left, ChatQueuedTurnState right) =>
        string.Equals(left.Text, right.Text, StringComparison.Ordinal)
        && string.Equals(left.ConversationId, right.ConversationId, StringComparison.OrdinalIgnoreCase)
        && EnsureUtc(left.EnqueuedUtc) == EnsureUtc(right.EnqueuedUtc)
        && left.SkipUserBubbleOnDispatch == right.SkipUserBubbleOnDispatch;

    private static bool ModelCatalogContentEquals(ChatAppState left, ChatAppState right) =>
        string.Equals(left.CachedModelsTransport, right.CachedModelsTransport, StringComparison.OrdinalIgnoreCase)
        && string.Equals(left.CachedModelsBaseUrl, right.CachedModelsBaseUrl, StringComparison.OrdinalIgnoreCase)
        && SequenceEqual(left.CachedModels, right.CachedModels, ModelInfoEquals)
        && SequenceEqual(left.CachedFavoriteModels, right.CachedFavoriteModels, StringComparer.Ordinal.Equals)
        && SequenceEqual(left.CachedRecentModels, right.CachedRecentModels, StringComparer.Ordinal.Equals)
        && left.CachedModelListIsStale == right.CachedModelListIsStale
        && string.Equals(left.CachedModelListWarning, right.CachedModelListWarning, StringComparison.Ordinal);

    private static bool ModelInfoEquals(ModelInfoDto left, ModelInfoDto right) =>
        string.Equals(left.Id, right.Id, StringComparison.Ordinal)
        && string.Equals(left.Model, right.Model, StringComparison.Ordinal)
        && string.Equals(left.DisplayName, right.DisplayName, StringComparison.Ordinal)
        && string.Equals(left.Description, right.Description, StringComparison.Ordinal)
        && left.IsDefault == right.IsDefault
        && string.Equals(left.OwnedBy, right.OwnedBy, StringComparison.Ordinal)
        && string.Equals(left.Publisher, right.Publisher, StringComparison.Ordinal)
        && string.Equals(left.Architecture, right.Architecture, StringComparison.Ordinal)
        && string.Equals(left.Quantization, right.Quantization, StringComparison.Ordinal)
        && string.Equals(left.CompatibilityType, right.CompatibilityType, StringComparison.Ordinal)
        && string.Equals(left.RuntimeState, right.RuntimeState, StringComparison.Ordinal)
        && string.Equals(left.ModelType, right.ModelType, StringComparison.Ordinal)
        && left.MaxContextLength == right.MaxContextLength
        && left.LoadedContextLength == right.LoadedContextLength
        && SequenceEqual(left.Capabilities, right.Capabilities, StringComparer.Ordinal.Equals)
        && string.Equals(left.DefaultReasoningEffort, right.DefaultReasoningEffort, StringComparison.Ordinal)
        && SequenceEqual(left.SupportedReasoningEfforts, right.SupportedReasoningEfforts, ReasoningEffortEquals);

    private static bool ReasoningEffortEquals(ReasoningEffortOptionDto left, ReasoningEffortOptionDto right) =>
        string.Equals(left.ReasoningEffort, right.ReasoningEffort, StringComparison.Ordinal)
        && string.Equals(left.Description, right.Description, StringComparison.Ordinal);

    private static bool SequenceEqual<T>(
        IReadOnlyList<T>? left,
        IReadOnlyList<T>? right,
        Func<T, T, bool> equals) {
        var leftValues = left ?? Array.Empty<T>();
        var rightValues = right ?? Array.Empty<T>();
        if (leftValues.Count != rightValues.Count) {
            return false;
        }
        for (var index = 0; index < leftValues.Count; index++) {
            if (!equals(leftValues[index], rightValues[index])) {
                return false;
            }
        }
        return true;
    }

    private static ChatQueuedTurnState CloneQueuedTurn(ChatQueuedTurnState value) =>
        new() {
            Text = value.Text,
            ConversationId = value.ConversationId,
            EnqueuedUtc = value.EnqueuedUtc,
            SkipUserBubbleOnDispatch = value.SkipUserBubbleOnDispatch
        };

    private static ModelInfoDto CloneModelInfo(ModelInfoDto value) =>
        value with {
            Capabilities = value.Capabilities?.ToArray() ?? Array.Empty<string>(),
            SupportedReasoningEfforts = value.SupportedReasoningEfforts?
                .Select(static option => option with { })
                .ToArray() ?? Array.Empty<ReasoningEffortOptionDto>()
        };

    private static ChatMemoryFactState CloneMemoryFact(ChatMemoryFactState value) =>
        new() {
            Id = value.Id,
            Fact = value.Fact,
            Weight = value.Weight,
            Tags = value.Tags.ToArray(),
            UpdatedUtc = value.UpdatedUtc
        };

    private static DateTime EnsureUtc(DateTime value) => value.Kind switch {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
