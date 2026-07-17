using System;
using System.Collections.Generic;
using System.Linq;

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
               && SequenceEqual(left.Conversations, right.Conversations, ConversationEquals);
    }

    internal static ChatAppState MergeLegacySnapshot(
        ChatAppState local,
        ChatAppState? baseline,
        ChatAppState? latest) {
        ArgumentNullException.ThrowIfNull(local);
        if (baseline is null || latest is null) {
            return local;
        }

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
        local.Conversations = MergeConversations(local.Conversations, baseline.Conversations, latest.Conversations);
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
            local.Messages = active.Messages.Select(CloneMessage).ToList();
        } else {
            local.ThreadId = Resolve(
                local.ThreadId,
                baseline.ThreadId,
                latest.ThreadId,
                StringComparer.Ordinal);
            local.Messages = MergeMessages(local.Messages, baseline.Messages, latest.Messages);
        }

        return local;
    }

    private static List<ChatConversationState> MergeConversations(
        IReadOnlyList<ChatConversationState>? local,
        IReadOnlyList<ChatConversationState>? baseline,
        IReadOnlyList<ChatConversationState>? latest) {
        var localById = IndexConversations(local);
        var baselineById = IndexConversations(baseline);
        var latestById = IndexConversations(latest);
        var ids = latestById.Keys
            .Concat(localById.Keys)
            .Concat(baselineById.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        var merged = new List<ChatConversationState>();
        foreach (var id in ids) {
            localById.TryGetValue(id, out var localValue);
            baselineById.TryGetValue(id, out var baselineValue);
            latestById.TryGetValue(id, out var latestValue);
            var resolved = ResolveConversation(localValue, baselineValue, latestValue);
            if (resolved is not null) {
                merged.Add(resolved);
            }
        }

        merged.Sort(static (left, right) => right.UpdatedUtc.CompareTo(left.UpdatedUtc));
        return merged;
    }

    private static ChatConversationState? ResolveConversation(
        ChatConversationState? local,
        ChatConversationState? baseline,
        ChatConversationState? latest) {
        if (baseline is null) {
            return local is null ? latest is null ? null : CloneConversation(latest) : CloneConversation(local);
        }

        if (local is null) {
            return latest is null || ConversationEquals(latest, baseline)
                ? null
                : CloneConversation(latest);
        }

        if (latest is null) {
            return ConversationEquals(local, baseline) ? null : CloneConversation(local);
        }

        if (ConversationEquals(local, baseline)) {
            return CloneConversation(latest);
        }

        if (ConversationEquals(latest, baseline)) {
            return CloneConversation(local);
        }

        var merged = CloneConversation(latest);
        merged.Title = Resolve(local.Title, baseline.Title, latest.Title, StringComparer.Ordinal)
                       ?? ChatConversationIdentity.DefaultTitle;
        merged.ThreadId = Resolve(local.ThreadId, baseline.ThreadId, latest.ThreadId, StringComparer.Ordinal);
        merged.RuntimeLabel = Resolve(local.RuntimeLabel, baseline.RuntimeLabel, latest.RuntimeLabel, StringComparer.Ordinal);
        merged.ModelLabel = Resolve(local.ModelLabel, baseline.ModelLabel, latest.ModelLabel, StringComparer.Ordinal);
        merged.ModelOverride = Resolve(local.ModelOverride, baseline.ModelOverride, latest.ModelOverride, StringComparer.Ordinal);
        merged.PendingAssistantQuestionHint = Resolve(
            local.PendingAssistantQuestionHint,
            baseline.PendingAssistantQuestionHint,
            latest.PendingAssistantQuestionHint,
            StringComparer.Ordinal);
        merged.Messages = MergeMessages(local.Messages, baseline.Messages, latest.Messages);
        merged.PendingActions = MergePendingActions(local.PendingActions, baseline.PendingActions, latest.PendingActions);
        merged.UpdatedUtc = EnsureUtc(local.UpdatedUtc) >= EnsureUtc(latest.UpdatedUtc)
            ? EnsureUtc(local.UpdatedUtc)
            : EnsureUtc(latest.UpdatedUtc);
        return merged;
    }

    private static List<ChatMessageState> MergeMessages(
        IReadOnlyList<ChatMessageState>? local,
        IReadOnlyList<ChatMessageState>? baseline,
        IReadOnlyList<ChatMessageState>? latest) {
        var localByKey = IndexMessages(local);
        var baselineByKey = IndexMessages(baseline);
        var latestByKey = IndexMessages(latest);
        var keys = latestByKey.Keys
            .Concat(localByKey.Keys)
            .Concat(baselineByKey.Keys)
            .Distinct(StringComparer.Ordinal);
        var merged = new List<ChatMessageState>();
        foreach (var key in keys) {
            localByKey.TryGetValue(key, out var localValue);
            baselineByKey.TryGetValue(key, out var baselineValue);
            latestByKey.TryGetValue(key, out var latestValue);
            var resolved = ResolveEntity(localValue, baselineValue, latestValue, MessageEquals, CloneMessage);
            if (resolved is not null) {
                merged.Add(resolved);
            }
        }

        merged.Sort(static (left, right) => EnsureUtc(left.TimeUtc).CompareTo(EnsureUtc(right.TimeUtc)));
        return merged;
    }

    private static List<ChatPendingActionState> MergePendingActions(
        IReadOnlyList<ChatPendingActionState>? local,
        IReadOnlyList<ChatPendingActionState>? baseline,
        IReadOnlyList<ChatPendingActionState>? latest) {
        var localValues = (local ?? Array.Empty<ChatPendingActionState>()).ToList();
        var baselineValues = (baseline ?? Array.Empty<ChatPendingActionState>()).ToList();
        var latestValues = (latest ?? Array.Empty<ChatPendingActionState>()).ToList();
        if (PendingActionsEqual(localValues, baselineValues)) {
            return latestValues.Select(ClonePendingAction).ToList();
        }
        if (PendingActionsEqual(latestValues, baselineValues)) {
            return localValues.Select(ClonePendingAction).ToList();
        }
        if (localValues.Count == 0 || latestValues.Count == 0) {
            return new List<ChatPendingActionState>();
        }

        return localValues.Select(ClonePendingAction).ToList();
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

    private static Dictionary<string, ChatConversationState> IndexConversations(
        IReadOnlyList<ChatConversationState>? conversations) {
        return (conversations ?? Array.Empty<ChatConversationState>())
            .Where(static conversation => !string.IsNullOrWhiteSpace(conversation.Id))
            .GroupBy(static conversation => conversation.Id.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, ChatMessageState> IndexMessages(IReadOnlyList<ChatMessageState>? messages) {
        var indexed = new Dictionary<string, ChatMessageState>(StringComparer.Ordinal);
        var duplicateCounters = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var message in messages ?? Array.Empty<ChatMessageState>()) {
            var identity = (message.Role ?? string.Empty).Trim().ToLowerInvariant()
                           + "|" + EnsureUtc(message.TimeUtc).Ticks;
            var occurrence = duplicateCounters.TryGetValue(identity, out var count) ? count + 1 : 0;
            duplicateCounters[identity] = occurrence;
            indexed[identity + "|" + occurrence] = message;
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

    private static bool ConversationEquals(ChatConversationState left, ChatConversationState right) =>
        string.Equals(left.Id, right.Id, StringComparison.OrdinalIgnoreCase)
        && string.Equals(left.Title, right.Title, StringComparison.Ordinal)
        && string.Equals(left.ThreadId, right.ThreadId, StringComparison.Ordinal)
        && string.Equals(left.RuntimeLabel, right.RuntimeLabel, StringComparison.Ordinal)
        && string.Equals(left.ModelLabel, right.ModelLabel, StringComparison.Ordinal)
        && string.Equals(left.ModelOverride, right.ModelOverride, StringComparison.Ordinal)
        && string.Equals(left.PendingAssistantQuestionHint, right.PendingAssistantQuestionHint, StringComparison.Ordinal)
        && SequenceEqual(left.Messages, right.Messages, MessageEquals)
        && PendingActionsEqual(left.PendingActions, right.PendingActions);

    private static bool MessageEquals(ChatMessageState left, ChatMessageState right) =>
        string.Equals(left.Role, right.Role, StringComparison.OrdinalIgnoreCase)
        && string.Equals(left.Text, right.Text, StringComparison.Ordinal)
        && EnsureUtc(left.TimeUtc) == EnsureUtc(right.TimeUtc)
        && string.Equals(left.Model, right.Model, StringComparison.Ordinal)
        && string.Equals(left.Status, right.Status, StringComparison.Ordinal);

    private static bool MemoryFactEquals(ChatMemoryFactState left, ChatMemoryFactState right) =>
        string.Equals(left.Id, right.Id, StringComparison.OrdinalIgnoreCase)
        && string.Equals(left.Fact, right.Fact, StringComparison.Ordinal)
        && left.Weight == right.Weight
        && left.Tags.SequenceEqual(right.Tags, StringComparer.OrdinalIgnoreCase)
        && EnsureUtc(left.UpdatedUtc) == EnsureUtc(right.UpdatedUtc);

    private static bool PendingActionsEqual(
        IReadOnlyList<ChatPendingActionState>? left,
        IReadOnlyList<ChatPendingActionState>? right) =>
        SequenceEqual(left, right, static (a, b) =>
            string.Equals(a.Id, b.Id, StringComparison.Ordinal)
            && string.Equals(a.Title, b.Title, StringComparison.Ordinal)
            && string.Equals(a.Request, b.Request, StringComparison.Ordinal)
            && string.Equals(a.Reply, b.Reply, StringComparison.Ordinal));

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

    private static ChatConversationState CloneConversation(ChatConversationState value) =>
        new() {
            Id = value.Id,
            Title = value.Title,
            ThreadId = value.ThreadId,
            RuntimeLabel = value.RuntimeLabel,
            ModelLabel = value.ModelLabel,
            ModelOverride = value.ModelOverride,
            PendingAssistantQuestionHint = value.PendingAssistantQuestionHint,
            Messages = (value.Messages ?? new List<ChatMessageState>()).Select(CloneMessage).ToList(),
            PendingActions = (value.PendingActions ?? new List<ChatPendingActionState>()).Select(ClonePendingAction).ToList(),
            UpdatedUtc = value.UpdatedUtc
        };

    private static ChatMessageState CloneMessage(ChatMessageState value) =>
        new() {
            Role = value.Role,
            Text = value.Text,
            TimeUtc = value.TimeUtc,
            Model = value.Model,
            Status = value.Status
        };

    private static ChatPendingActionState ClonePendingAction(ChatPendingActionState value) =>
        new() {
            Id = value.Id,
            Title = value.Title,
            Request = value.Request,
            Reply = value.Reply
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
