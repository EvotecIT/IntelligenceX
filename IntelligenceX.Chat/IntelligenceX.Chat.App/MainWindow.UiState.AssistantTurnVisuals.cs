using System;
using System.Collections.Generic;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.App.Rendering;
using Microsoft.UI.Xaml;

namespace IntelligenceX.Chat.App;

public sealed partial class MainWindow : Window {
    private void ResetActiveTurnAssistantVisuals(string? conversationId) {
        var normalizedConversationId = (conversationId ?? string.Empty).Trim();
        lock (_turnDiagnosticsSync) {
            _activeTurnAssistantConversationId = normalizedConversationId.Length == 0 ? null : normalizedConversationId;
            _activeTurnAssistantMessageIndex = -1;
            _activeTurnAssistantPendingTimeline.Clear();
            _activeTurnAssistantProvisional = false;
            _activeTurnUsesProvisionalEvents = false;
            _activeTurnInterimResultSeen = false;
            _activeTurnInterimFingerprint = null;
        }
    }

    private void ClearConversationAssistantVisualState(string? conversationId) {
        var normalizedConversationId = (conversationId ?? string.Empty).Trim();
        if (normalizedConversationId.Length == 0) {
            return;
        }

        lock (_turnDiagnosticsSync) {
            _assistantTurnVisualStateByConversationId.Remove(normalizedConversationId);
            if (string.Equals(_activeTurnAssistantConversationId, normalizedConversationId, StringComparison.OrdinalIgnoreCase)) {
                _activeTurnAssistantConversationId = null;
                _activeTurnAssistantMessageIndex = -1;
                _activeTurnAssistantPendingTimeline.Clear();
                _activeTurnAssistantProvisional = false;
                _activeTurnUsesProvisionalEvents = false;
                _activeTurnInterimResultSeen = false;
                _activeTurnInterimFingerprint = null;
            }
        }
    }

    private bool TryMarkActiveTurnInterimResult(string text) {
        var normalized = (text ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return false;
        }

        lock (_turnDiagnosticsSync) {
            if (string.Equals(_activeTurnInterimFingerprint, normalized, StringComparison.Ordinal)) {
                return false;
            }

            _activeTurnInterimResultSeen = true;
            _activeTurnInterimFingerprint = normalized;
            return true;
        }
    }

    private bool HasActiveTurnInterimResult() {
        lock (_turnDiagnosticsSync) {
            return _activeTurnInterimResultSeen;
        }
    }

    private bool IsActiveTurnBoundToConversation(ConversationRuntime conversation) {
        var conversationId = (conversation?.Id ?? string.Empty).Trim();
        if (conversationId.Length == 0) {
            return false;
        }

        lock (_turnDiagnosticsSync) {
            return string.Equals(_activeTurnAssistantConversationId, conversationId, StringComparison.OrdinalIgnoreCase);
        }
    }

    private int BindActiveTurnAssistantMessage(ConversationRuntime conversation) {
        var conversationId = (conversation.Id ?? string.Empty).Trim();
        if (conversationId.Length == 0) {
            return -1;
        }

        lock (_turnDiagnosticsSync) {
            if (!string.Equals(_activeTurnAssistantConversationId, conversationId, StringComparison.OrdinalIgnoreCase)) {
                return -1;
            }

            var messageIndex = FindLastAssistantMessageIndex(conversation);
            if (messageIndex < 0) {
                return -1;
            }

            _activeTurnAssistantMessageIndex = messageIndex;
            var state = GetOrCreateAssistantVisualState(conversationId, messageIndex);
            state.IsProvisional = _activeTurnAssistantProvisional;
            for (var i = 0; i < _activeTurnAssistantPendingTimeline.Count; i++) {
                AppendTimelineLabel(state.Timeline, _activeTurnAssistantPendingTimeline[i]);
            }
            _activeTurnAssistantPendingTimeline.Clear();
            TrimConversationAssistantVisualStateLocked(conversation, conversationId);
            return messageIndex;
        }
    }

    private bool AppendActiveTurnAssistantTimelineLabel(ConversationRuntime conversation, string label) {
        var normalizedLabel = (label ?? string.Empty).Trim();
        if (normalizedLabel.Length == 0) {
            return false;
        }

        var conversationId = (conversation.Id ?? string.Empty).Trim();
        if (conversationId.Length == 0) {
            return false;
        }

        lock (_turnDiagnosticsSync) {
            if (!string.Equals(_activeTurnAssistantConversationId, conversationId, StringComparison.OrdinalIgnoreCase)) {
                return false;
            }

            var changed = AppendTimelineLabel(_activeTurnAssistantPendingTimeline, normalizedLabel);
            if (_activeTurnAssistantMessageIndex >= 0) {
                var state = GetOrCreateAssistantVisualState(conversationId, _activeTurnAssistantMessageIndex);
                changed |= AppendTimelineLabel(state.Timeline, normalizedLabel);
            }
            return changed;
        }
    }

    private bool SetActiveTurnAssistantProvisional(ConversationRuntime conversation, bool provisional, bool preferProvisionalEvents) {
        var conversationId = (conversation.Id ?? string.Empty).Trim();
        if (conversationId.Length == 0) {
            return false;
        }

        lock (_turnDiagnosticsSync) {
            if (!string.Equals(_activeTurnAssistantConversationId, conversationId, StringComparison.OrdinalIgnoreCase)) {
                return false;
            }

            var changed = false;
            if (_activeTurnAssistantProvisional != provisional) {
                _activeTurnAssistantProvisional = provisional;
                changed = true;
            }

            if (preferProvisionalEvents) {
                _activeTurnUsesProvisionalEvents = true;
            }

            if (_activeTurnAssistantMessageIndex >= 0) {
                var state = GetOrCreateAssistantVisualState(conversationId, _activeTurnAssistantMessageIndex);
                if (state.IsProvisional != provisional) {
                    state.IsProvisional = provisional;
                    changed = true;
                }
            }

            return changed;
        }
    }

    private bool ShouldUseProvisionalEventsForActiveTurn(ConversationRuntime conversation) {
        var conversationId = (conversation.Id ?? string.Empty).Trim();
        if (conversationId.Length == 0) {
            return false;
        }

        lock (_turnDiagnosticsSync) {
            if (!string.Equals(_activeTurnAssistantConversationId, conversationId, StringComparison.OrdinalIgnoreCase)) {
                return false;
            }

            return ShouldSuppressChatDeltaWhenProvisionalPreferred(
                _activeTurnUsesProvisionalEvents,
                _assistantStreamingState.HasReceivedProvisionalDelta());
        }
    }

    internal static bool ShouldSuppressChatDeltaWhenProvisionalPreferred(
        bool provisionalModePreferredForTurn,
        bool hasReceivedProvisionalFragment) {
        return provisionalModePreferredForTurn && hasReceivedProvisionalFragment;
    }

    private bool ApplyFinalAssistantTurnTimeline(ConversationRuntime conversation, IReadOnlyList<TurnTimelineEventDto>? timelineEvents) {
        var conversationId = (conversation.Id ?? string.Empty).Trim();
        if (conversationId.Length == 0) {
            return false;
        }

        var normalizedTimeline = BuildTimelineLabelsFromEvents(timelineEvents);
        lock (_turnDiagnosticsSync) {
            var messageIndex = FindLastAssistantMessageIndex(conversation);
            if (messageIndex < 0) {
                return false;
            }

            var state = GetOrCreateAssistantVisualState(conversationId, messageIndex);
            var changed = state.IsProvisional;
            state.IsProvisional = false;
            changed |= MergeFinalTimeline(state.Timeline, normalizedTimeline);

            if (string.Equals(_activeTurnAssistantConversationId, conversationId, StringComparison.OrdinalIgnoreCase)) {
                _activeTurnAssistantMessageIndex = messageIndex;
                _activeTurnAssistantProvisional = false;
                _activeTurnAssistantPendingTimeline.Clear();
            }

            TrimConversationAssistantVisualStateLocked(conversation, conversationId);
            return changed;
        }
    }

    internal static bool MergeFinalTimeline(List<string> existingTimeline, IReadOnlyList<string>? finalTimeline) {
        if (existingTimeline is null) {
            return false;
        }

        if (finalTimeline is null || finalTimeline.Count == 0) {
            // Keep live-captured timeline when the final envelope does not include one.
            return false;
        }

        return ReplaceTimeline(existingTimeline, finalTimeline);
    }

    private Dictionary<int, TranscriptMessageDecoration>? SnapshotTranscriptMessageDecorations(ConversationRuntime conversation) {
        var conversationId = (conversation.Id ?? string.Empty).Trim();
        if (conversationId.Length == 0) {
            return null;
        }

        lock (_turnDiagnosticsSync) {
            TrimConversationAssistantVisualStateLocked(conversation, conversationId);
            if (!_assistantTurnVisualStateByConversationId.TryGetValue(conversationId, out var stateByIndex)
                || stateByIndex.Count == 0) {
                return null;
            }

            var snapshot = new Dictionary<int, TranscriptMessageDecoration>();
            foreach (var item in stateByIndex) {
                var messageIndex = item.Key;
                if (messageIndex < 0 || messageIndex >= conversation.Messages.Count) {
                    continue;
                }

                var message = conversation.Messages[messageIndex];
                if (!string.Equals(message.Role, "Assistant", StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }

                var state = item.Value;
                if (!state.IsProvisional && state.Timeline.Count == 0) {
                    continue;
                }

                snapshot[messageIndex] = new TranscriptMessageDecoration {
                    IsProvisional = state.IsProvisional,
                    Timeline = state.Timeline.Count == 0 ? Array.Empty<string>() : state.Timeline.ToArray()
                };
            }

            return snapshot.Count == 0 ? null : snapshot;
        }
    }

    private List<string> BuildTimelineLabelsFromEvents(IReadOnlyList<TurnTimelineEventDto>? timelineEvents) {
        var labels = new List<string>();
        if (timelineEvents is not { Count: > 0 }) {
            return labels;
        }

        for (var i = 0; i < timelineEvents.Count; i++) {
            var ev = timelineEvents[i];
            if (ev is null) {
                continue;
            }

            var status = new ChatStatusMessage {
                Kind = ChatServiceMessageKind.Event,
                RequestId = _activeTurnRequestId ?? string.Empty,
                ThreadId = _threadId ?? string.Empty,
                Status = ev.Status ?? string.Empty,
                ToolName = ev.ToolName,
                ToolCallId = ev.ToolCallId,
                DurationMs = ev.DurationMs,
                Message = ev.Message
            };
            var activityText = FormatActivityText(status);
            var label = BuildActivityTimelineLabel(status, activityText);
            if (label.Length == 0) {
                continue;
            }

            AppendTimelineLabel(labels, label);
        }

        return labels;
    }

    private AssistantTurnVisualState GetOrCreateAssistantVisualState(string conversationId, int messageIndex) {
        EnsureTurnDiagnosticsLockHeld();

        if (!_assistantTurnVisualStateByConversationId.TryGetValue(conversationId, out var stateByIndex)) {
            stateByIndex = new Dictionary<int, AssistantTurnVisualState>();
            _assistantTurnVisualStateByConversationId[conversationId] = stateByIndex;
        }

        if (!stateByIndex.TryGetValue(messageIndex, out var state)) {
            state = new AssistantTurnVisualState();
            stateByIndex[messageIndex] = state;
        }

        return state;
    }

    private void TrimConversationAssistantVisualStateLocked(ConversationRuntime conversation, string conversationId) {
        EnsureTurnDiagnosticsLockHeld();

        if (!_assistantTurnVisualStateByConversationId.TryGetValue(conversationId, out var stateByIndex)
            || stateByIndex.Count == 0) {
            return;
        }

        var stale = new List<int>();
        foreach (var item in stateByIndex) {
            var index = item.Key;
            if (index < 0 || index >= conversation.Messages.Count
                || !string.Equals(conversation.Messages[index].Role, "Assistant", StringComparison.OrdinalIgnoreCase)) {
                stale.Add(index);
            }
        }

        for (var i = 0; i < stale.Count; i++) {
            stateByIndex.Remove(stale[i]);
        }

        if (stateByIndex.Count == 0) {
            _assistantTurnVisualStateByConversationId.Remove(conversationId);
        }
    }

    private static int FindLastAssistantMessageIndex(ConversationRuntime conversation) {
        for (var i = conversation.Messages.Count - 1; i >= 0; i--) {
            if (string.Equals(conversation.Messages[i].Role, "Assistant", StringComparison.OrdinalIgnoreCase)) {
                return i;
            }
        }

        return -1;
    }

    private static bool ReplaceTimeline(List<string> destination, IReadOnlyList<string> source) {
        if (destination is null) {
            return false;
        }

        var changed = destination.Count != source.Count;
        if (!changed) {
            for (var i = 0; i < destination.Count; i++) {
                if (!string.Equals(destination[i], source[i], StringComparison.OrdinalIgnoreCase)) {
                    changed = true;
                    break;
                }
            }
        }

        if (!changed) {
            return false;
        }

        destination.Clear();
        for (var i = 0; i < source.Count; i++) {
            AppendTimelineLabel(destination, source[i]);
        }

        return true;
    }

    private static bool AppendTimelineLabel(List<string> timeline, string label) {
        var normalized = (label ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return false;
        }

        if (timeline.Count > 0 && string.Equals(timeline[^1], normalized, StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        timeline.Add(normalized);
        while (timeline.Count > MaxAssistantTurnTimelineEntries) {
            timeline.RemoveAt(0);
        }
        return true;
    }

    private void EnsureTurnDiagnosticsLockHeld() {
        // Unit tests that materialize MainWindow via RuntimeHelpers.GetUninitializedObject
        // intentionally bypass field initializers; skip lock assertions for that synthetic shape.
        if (_turnDiagnosticsSync is null) {
            return;
        }

        if (!System.Threading.Monitor.IsEntered(_turnDiagnosticsSync)) {
            throw new InvalidOperationException(
                "Assistant turn visual state access must be synchronized via _turnDiagnosticsSync.");
        }
    }
}
