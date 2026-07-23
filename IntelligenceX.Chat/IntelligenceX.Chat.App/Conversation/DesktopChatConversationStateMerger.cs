using System;
using System.Collections.Generic;
using System.Linq;

namespace IntelligenceX.Chat.App.Conversation;

/// <summary>
/// Owns the three-way merge contract for conversations shared by every desktop shell.
/// </summary>
/// <remarks>
/// Conversation deletion is honored unless another writer changed that conversation. Transcript
/// rows use stable role/timestamp identities so partial assistant updates replace their prior
/// snapshot, and consuming a pending action wins over a stale writer.
/// </remarks>
internal static class DesktopChatConversationStateMerger {
    internal static List<ChatConversationState> MergeConversations(
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
            var resolved = MergeConversation(localValue, baselineValue, latestValue);
            if (resolved is not null) {
                merged.Add(resolved);
            }
        }

        merged.Sort(static (left, right) => EnsureUtc(right.UpdatedUtc).CompareTo(EnsureUtc(left.UpdatedUtc)));
        return merged;
    }

    internal static ChatConversationState? MergeConversation(
        ChatConversationState? local,
        ChatConversationState? baseline,
        ChatConversationState? latest) {
        if (baseline is null) {
            if (local is null) {
                return latest is null ? null : CloneConversation(latest);
            }
            if (latest is null) {
                return CloneConversation(local);
            }

            return MergeConcurrentConversations(local, baseline: null, latest);
        }

        if (local is null) {
            return latest is null || ConversationEquals(latest, baseline)
                ? null
                : CloneConversation(latest);
        }

        if (latest is null) {
            return ConversationEquals(local, baseline) ? null : CloneConversation(local);
        }

        return MergeConcurrentConversations(local, baseline, latest);
    }

    internal static List<ChatMessageState> MergeMessages(
        IReadOnlyList<ChatMessageState>? local,
        IReadOnlyList<ChatMessageState>? baseline,
        IReadOnlyList<ChatMessageState>? latest,
        DateTime? localUpdatedUtc = null,
        DateTime? latestUpdatedUtc = null) {
        var localByKey = IndexMessages(local);
        var baselineByKey = IndexMessages(baseline);
        var latestByKey = IndexMessages(latest);
        var keys = baselineByKey.Keys
            .Concat(latestByKey.Keys)
            .Concat(localByKey.Keys)
            .Distinct(StringComparer.Ordinal);
        var merged = new List<ChatMessageState>();
        foreach (var key in keys) {
            localByKey.TryGetValue(key, out var localValue);
            baselineByKey.TryGetValue(key, out var baselineValue);
            latestByKey.TryGetValue(key, out var latestValue);
            if (baselineValue is null
                && localValue is not null
                && latestValue is not null
                && !MessageEquals(localValue, latestValue)) {
                // With no common ancestor, two different rows at the same role/timestamp are
                // concurrent additions rather than competing edits. Preserve both branches.
                merged.Add(CloneMessage(localValue));
                merged.Add(CloneMessage(latestValue));
                continue;
            }

            var resolved = MergeMessage(
                localValue,
                baselineValue,
                latestValue,
                localUpdatedUtc,
                latestUpdatedUtc);
            if (resolved is not null) {
                merged.Add(resolved);
            }
        }

        merged.Sort(static (left, right) => EnsureUtc(left.TimeUtc).CompareTo(EnsureUtc(right.TimeUtc)));
        return merged;
    }

    internal static bool ConversationEquals(ChatConversationState left, ChatConversationState right) =>
        string.Equals(left.Id, right.Id, StringComparison.OrdinalIgnoreCase)
        && string.Equals(left.Title, right.Title, StringComparison.Ordinal)
        && string.Equals(left.ThreadId, right.ThreadId, StringComparison.Ordinal)
        && string.Equals(left.RuntimeLabel, right.RuntimeLabel, StringComparison.Ordinal)
        && string.Equals(left.ModelLabel, right.ModelLabel, StringComparison.Ordinal)
        && string.Equals(left.ModelOverride, right.ModelOverride, StringComparison.Ordinal)
        && string.Equals(left.PendingAssistantQuestionHint, right.PendingAssistantQuestionHint, StringComparison.Ordinal)
        && SequenceEqual(left.Messages, right.Messages, MessageEquals)
        && SequenceEqual(left.PendingActions, right.PendingActions, PendingActionEquals);

    internal static bool ConversationEqualsIncludingTimestamp(
        ChatConversationState left,
        ChatConversationState right) =>
        ConversationEquals(left, right)
        && EnsureUtc(left.UpdatedUtc) == EnsureUtc(right.UpdatedUtc);

    internal static ChatConversationState CloneConversation(ChatConversationState value) =>
        new() {
            Id = value.Id,
            Title = value.Title,
            ThreadId = value.ThreadId,
            RuntimeLabel = value.RuntimeLabel,
            ModelLabel = value.ModelLabel,
            ModelOverride = value.ModelOverride,
            PendingAssistantQuestionHint = value.PendingAssistantQuestionHint,
            Messages = (value.Messages ?? new List<ChatMessageState>()).Select(CloneMessage).ToList(),
            PendingActions = (value.PendingActions ?? new List<ChatPendingActionState>())
                .Select(ClonePendingAction)
                .ToList(),
            UpdatedUtc = value.UpdatedUtc
        };

    internal static ChatMessageState CloneMessage(ChatMessageState value) =>
        new() {
            Role = value.Role,
            Text = value.Text,
            TimeUtc = value.TimeUtc,
            Model = value.Model,
            Status = value.Status
        };

    private static ChatConversationState MergeConcurrentConversations(
        ChatConversationState local,
        ChatConversationState? baseline,
        ChatConversationState latest) {
        var localUpdatedUtc = EnsureUtc(local.UpdatedUtc);
        var latestUpdatedUtc = EnsureUtc(latest.UpdatedUtc);
        var hasBaseline = baseline is not null;
        var merged = CloneConversation(latest);
        merged.Id = string.IsNullOrWhiteSpace(local.Id) ? latest.Id : local.Id;
        merged.Title = ResolveValue(
                           local.Title,
                           baseline?.Title,
                           latest.Title,
                           hasBaseline,
                           localUpdatedUtc,
                           latestUpdatedUtc)
                       ?? ChatConversationIdentity.DefaultTitle;
        merged.ThreadId = ResolveValue(
            local.ThreadId,
            baseline?.ThreadId,
            latest.ThreadId,
            hasBaseline,
            localUpdatedUtc,
            latestUpdatedUtc);
        merged.RuntimeLabel = ResolveValue(
            local.RuntimeLabel,
            baseline?.RuntimeLabel,
            latest.RuntimeLabel,
            hasBaseline,
            localUpdatedUtc,
            latestUpdatedUtc);
        merged.ModelLabel = ResolveValue(
            local.ModelLabel,
            baseline?.ModelLabel,
            latest.ModelLabel,
            hasBaseline,
            localUpdatedUtc,
            latestUpdatedUtc);
        merged.ModelOverride = ResolveValue(
            local.ModelOverride,
            baseline?.ModelOverride,
            latest.ModelOverride,
            hasBaseline,
            localUpdatedUtc,
            latestUpdatedUtc);
        merged.PendingAssistantQuestionHint = ResolveValue(
            local.PendingAssistantQuestionHint,
            baseline?.PendingAssistantQuestionHint,
            latest.PendingAssistantQuestionHint,
            hasBaseline,
            localUpdatedUtc,
            latestUpdatedUtc);
        merged.Messages = MergeMessages(
            local.Messages,
            baseline?.Messages,
            latest.Messages,
            localUpdatedUtc,
            latestUpdatedUtc);
        merged.PendingActions = MergePendingActions(
            local.PendingActions,
            baseline?.PendingActions,
            latest.PendingActions,
            localUpdatedUtc,
            latestUpdatedUtc);
        merged.UpdatedUtc = localUpdatedUtc >= latestUpdatedUtc ? localUpdatedUtc : latestUpdatedUtc;
        return merged;
    }

    private static string? ResolveValue(
        string? local,
        string? baseline,
        string? latest,
        bool hasBaseline,
        DateTime localUpdatedUtc,
        DateTime latestUpdatedUtc) {
        if (hasBaseline) {
            if (string.Equals(local, baseline, StringComparison.Ordinal)) {
                return latest;
            }
            if (string.Equals(latest, baseline, StringComparison.Ordinal)
                || string.Equals(local, latest, StringComparison.Ordinal)) {
                return local;
            }
        } else if (string.Equals(local, latest, StringComparison.Ordinal)) {
            return local;
        }

        return latestUpdatedUtc > localUpdatedUtc ? latest : local;
    }

    private static ChatMessageState? MergeMessage(
        ChatMessageState? local,
        ChatMessageState? baseline,
        ChatMessageState? latest,
        DateTime? localUpdatedUtc,
        DateTime? latestUpdatedUtc) {
        if (baseline is null) {
            if (local is null) {
                return latest is null ? null : CloneMessage(latest);
            }
            if (latest is null || MessageEquals(local, latest)) {
                return CloneMessage(local);
            }

            return CloneMessage(ShouldPreferLatest(localUpdatedUtc, latestUpdatedUtc) ? latest : local);
        }

        if (local is null) {
            return latest is null || MessageEquals(latest, baseline)
                ? null
                : CloneMessage(latest);
        }
        if (latest is null) {
            return MessageEquals(local, baseline) ? null : CloneMessage(local);
        }
        if (MessageEquals(local, baseline)) {
            return CloneMessage(latest);
        }
        if (MessageEquals(latest, baseline) || MessageEquals(local, latest)) {
            return CloneMessage(local);
        }

        return CloneMessage(ShouldPreferLatest(localUpdatedUtc, latestUpdatedUtc) ? latest : local);
    }

    private static List<ChatPendingActionState> MergePendingActions(
        IReadOnlyList<ChatPendingActionState>? local,
        IReadOnlyList<ChatPendingActionState>? baseline,
        IReadOnlyList<ChatPendingActionState>? latest,
        DateTime localUpdatedUtc,
        DateTime latestUpdatedUtc) {
        var localByKey = IndexPendingActions(local);
        var baselineByKey = IndexPendingActions(baseline);
        var latestByKey = IndexPendingActions(latest);
        var keys = baselineByKey.Keys
            .Concat(latestByKey.Keys)
            .Concat(localByKey.Keys)
            .Distinct(StringComparer.Ordinal);
        var merged = new List<ChatPendingActionState>();
        foreach (var key in keys) {
            var hasLocal = localByKey.TryGetValue(key, out var localValue);
            var hadBaseline = baselineByKey.TryGetValue(key, out var baselineValue);
            var hasLatest = latestByKey.TryGetValue(key, out var latestValue);
            if (hadBaseline && (!hasLocal || !hasLatest)) {
                // Missing from either writer means the baseline action was consumed.
                continue;
            }

            var resolved = ResolvePendingAction(
                hasLocal ? localValue : null,
                hadBaseline ? baselineValue : null,
                hasLatest ? latestValue : null,
                localUpdatedUtc,
                latestUpdatedUtc);
            if (resolved is not null) {
                merged.Add(resolved);
            }
        }

        return merged;
    }

    private static ChatPendingActionState? ResolvePendingAction(
        ChatPendingActionState? local,
        ChatPendingActionState? baseline,
        ChatPendingActionState? latest,
        DateTime localUpdatedUtc,
        DateTime latestUpdatedUtc) {
        if (local is null) {
            return latest is null ? null : ClonePendingAction(latest);
        }
        if (latest is null) {
            return ClonePendingAction(local);
        }
        if (baseline is not null) {
            if (PendingActionEquals(local, baseline)) {
                return ClonePendingAction(latest);
            }
            if (PendingActionEquals(latest, baseline) || PendingActionEquals(local, latest)) {
                return ClonePendingAction(local);
            }
        } else if (PendingActionEquals(local, latest)) {
            return ClonePendingAction(local);
        }

        return ClonePendingAction(latestUpdatedUtc > localUpdatedUtc ? latest : local);
    }

    private static Dictionary<string, ChatConversationState> IndexConversations(
        IReadOnlyList<ChatConversationState>? conversations) =>
        (conversations ?? Array.Empty<ChatConversationState>())
        .Where(static conversation => !string.IsNullOrWhiteSpace(conversation.Id))
        .GroupBy(static conversation => conversation.Id.Trim(), StringComparer.OrdinalIgnoreCase)
        .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, ChatMessageState> IndexMessages(
        IReadOnlyList<ChatMessageState>? messages) {
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

    private static Dictionary<string, ChatPendingActionState> IndexPendingActions(
        IReadOnlyList<ChatPendingActionState>? actions) {
        var indexed = new Dictionary<string, ChatPendingActionState>(StringComparer.Ordinal);
        var duplicateCounters = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var action in actions ?? Array.Empty<ChatPendingActionState>()) {
            var identity = string.IsNullOrWhiteSpace(action.Id)
                ? (action.Title ?? string.Empty) + "|" + (action.Request ?? string.Empty) + "|" + (action.Reply ?? string.Empty)
                : action.Id.Trim();
            var occurrence = duplicateCounters.TryGetValue(identity, out var count) ? count + 1 : 0;
            duplicateCounters[identity] = occurrence;
            indexed[identity + "|" + occurrence] = action;
        }
        return indexed;
    }

    private static bool MessageEquals(ChatMessageState left, ChatMessageState right) =>
        string.Equals(left.Role, right.Role, StringComparison.OrdinalIgnoreCase)
        && string.Equals(left.Text, right.Text, StringComparison.Ordinal)
        && EnsureUtc(left.TimeUtc) == EnsureUtc(right.TimeUtc)
        && string.Equals(left.Model, right.Model, StringComparison.Ordinal)
        && string.Equals(
            NormalizeMessageStatus(left.Role, left.Status),
            NormalizeMessageStatus(right.Role, right.Status),
            StringComparison.Ordinal);

    private static bool PendingActionEquals(ChatPendingActionState left, ChatPendingActionState right) =>
        string.Equals(left.Id, right.Id, StringComparison.Ordinal)
        && string.Equals(left.Title, right.Title, StringComparison.Ordinal)
        && string.Equals(left.Request, right.Request, StringComparison.Ordinal)
        && string.Equals(left.Reply, right.Reply, StringComparison.Ordinal);

    private static ChatPendingActionState ClonePendingAction(ChatPendingActionState value) =>
        new() {
            Id = value.Id,
            Title = value.Title,
            Request = value.Request,
            Reply = value.Reply
        };

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

    private static string NormalizeMessageStatus(string? role, string? status) {
        if (!string.Equals(role, "assistant", StringComparison.OrdinalIgnoreCase)) {
            return string.Empty;
        }

        var normalized = (status ?? string.Empty).Trim();
        return normalized.Length == 0 ? "Complete" : normalized;
    }

    private static bool ShouldPreferLatest(DateTime? localUpdatedUtc, DateTime? latestUpdatedUtc) =>
        localUpdatedUtc.HasValue
        && latestUpdatedUtc.HasValue
        && EnsureUtc(latestUpdatedUtc.Value) > EnsureUtc(localUpdatedUtc.Value);

    private static DateTime EnsureUtc(DateTime value) => value.Kind switch {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
