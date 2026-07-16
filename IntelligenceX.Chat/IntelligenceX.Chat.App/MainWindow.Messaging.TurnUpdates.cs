using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.App.Conversation;
using IntelligenceX.Chat.App.Rendering;
using IntelligenceX.Chat.Client;
using Microsoft.UI.Xaml;

namespace IntelligenceX.Chat.App;

public sealed partial class MainWindow : Window {
    private ValueTask ApplyChatTurnUpdateAsync(ChatTurnUpdate update, CancellationToken _) {
        return new ValueTask(RunOnUiThreadAsync(() => {
            ProcessServiceMessage(update.Message);
            return Task.CompletedTask;
        }));
    }

    private bool TryProcessChatTurnMessage(ChatServiceMessage message, ConversationRuntime requestConversation) {
        switch (message) {
            case ChatDeltaMessage delta:
                if (!ShouldProcessLiveRequestMessage(delta.RequestId)
                    || !IsActiveTurnRequest(delta.RequestId)
                    || ShouldUseProvisionalEventsForActiveTurn(requestConversation)) {
                    return true;
                }

                MarkTurnDeltaStage(delta);
                ApplyAssistantStreamingFragment(
                    requestConversation,
                    delta.Text,
                    preferProvisionalEvents: false,
                    renderReason: "chat_delta");
                return true;
            case ChatAssistantProvisionalMessage provisional:
                if (!ShouldProcessLiveRequestMessage(provisional.RequestId)
                    || !IsActiveTurnRequest(provisional.RequestId)) {
                    return true;
                }

                MarkTurnDeltaStage(new ChatDeltaMessage {
                    Kind = provisional.Kind,
                    RequestId = provisional.RequestId,
                    ThreadId = provisional.ThreadId,
                    Text = provisional.Text
                });
                ApplyAssistantStreamingFragment(
                    requestConversation,
                    provisional.Text,
                    preferProvisionalEvents: true,
                    renderReason: "assistant_provisional");
                return true;
            case ChatInterimResultMessage interim:
                if (ShouldProcessLiveRequestMessage(interim.RequestId)
                    && IsActiveTurnRequest(interim.RequestId)) {
                    ApplyInterimAssistantResult(requestConversation, interim.Text);
                }
                return true;
            case ChatStatusMessage status:
                if (!ShouldProcessLiveRequestMessage(status.RequestId)) {
                    return true;
                }

                MarkTurnStatusStage(status);
                var assistantStatusSignalChanged = ApplyActiveTurnStatusSignal(requestConversation, status.Status);
                var routingInsightUpdated = ApplyToolRoutingInsight(status);
                var routingPromptExposureUpdated = ApplyRoutingMetaPromptExposure(status);
                var activityText = IsTerminalChatStatus(status.Status) ? null : FormatActivityText(status);
                var timelineChanged = AppendActivityTimeline(status, activityText ?? string.Empty);
                var normalizedActivityText = activityText ?? string.Empty;
                var activityChanged = !string.Equals(_latestServiceActivityText, normalizedActivityText, StringComparison.Ordinal);
                _latestServiceActivityText = normalizedActivityText;
                var timelineLabelSource = activityText ?? FormatActivityText(status);
                var assistantTimelineChanged = AppendActiveTurnAssistantTimelineLabel(
                    requestConversation,
                    BuildActivityTimelineLabel(status, timelineLabelSource));
                if (activityChanged || timelineChanged) {
                    _ = SetActivityAsync(activityText, SnapshotActivityTimeline());
                }
                if (timelineChanged || routingPromptExposureUpdated) {
                    RequestServiceDrivenSessionPublish();
                }
                if ((assistantTimelineChanged || assistantStatusSignalChanged)
                    && string.Equals(requestConversation.Id, _activeConversationId, StringComparison.OrdinalIgnoreCase)) {
                    QueueTranscriptRender("chat_status_timeline");
                }
                if (routingInsightUpdated || routingPromptExposureUpdated) {
                    _ = PublishOptionsStateSafeAsync();
                }
                if (VerboseServiceLogs || _debugMode) {
                    AppendSystem(FormatStatusTrace(status));
                }
                return true;
            case ChatMetricsMessage metrics:
                if (!ShouldProcessLiveRequestMessage(metrics.RequestId) && !IsLatestTurnRequest(metrics.RequestId)) {
                    return true;
                }

                ApplyTurnMetrics(metrics);
                StartupLog.Write(
                    "TurnMetrics request_id="
                    + (metrics.RequestId ?? string.Empty).Trim()
                    + " duration_ms="
                    + metrics.DurationMs.ToString(CultureInfo.InvariantCulture)
                    + " ttft_ms="
                    + (metrics.TtftMs?.ToString(CultureInfo.InvariantCulture) ?? "null")
                    + " outcome="
                    + ((metrics.Outcome ?? string.Empty).Trim().Length == 0 ? "unknown" : metrics.Outcome!.Trim())
                    + " tool_calls="
                    + metrics.ToolCallsCount.ToString(CultureInfo.InvariantCulture)
                    + " tool_rounds="
                    + metrics.ToolRounds.ToString(CultureInfo.InvariantCulture));
                RequestServiceDrivenSessionPublish();
                if (VerboseServiceLogs || _debugMode) {
                    AppendSystem(FormatMetricsTrace(metrics));
                }
                return true;
            case ChatResultMessage:
                return true;
            default:
                return false;
        }
    }

    private void ApplyAssistantStreamingFragment(
        ConversationRuntime conversation,
        string? fragment,
        bool preferProvisionalEvents,
        string renderReason) {
        var delta = fragment ?? string.Empty;
        if (delta.Length == 0 || !TryAcceptActiveTurnDraftMutation(conversation)) {
            return;
        }

        var normalizedPreview = _assistantStreamingState.AppendDeltaAndNormalizePreview(
            delta,
            fromProvisionalEvent: preferProvisionalEvents);
        ReplaceLastAssistantText(conversation, normalizedPreview);
        conversation.UpdatedUtc = DateTime.UtcNow;
        BindActiveTurnAssistantMessage(conversation);
        SetActiveTurnAssistantChannel(conversation, AssistantBubbleChannelKind.DraftThinking);
        SetActiveTurnAssistantProvisional(conversation, provisional: true, preferProvisionalEvents);
        if (string.Equals(conversation.Id, _activeConversationId, StringComparison.OrdinalIgnoreCase)) {
            QueueTranscriptRender(renderReason);
        }
    }

    private void ApplyInterimAssistantResult(ConversationRuntime conversation, string? text) {
        var interimText = (text ?? string.Empty).Trim();
        if (interimText.Length == 0 || !TryAcceptActiveTurnDraftMutation(conversation) || !TryMarkActiveTurnInterimResult(interimText)) {
            return;
        }

        _ = TryGetLastAssistantText(conversation, out var latestAssistantText);
        var appendInterimBubble = ShouldAppendInterimAssistantResult(
            activeTurnReceivedDelta: _assistantStreamingState.HasReceivedDelta(),
            activeTurnBoundToConversation: IsActiveTurnBoundToConversation(conversation),
            interimAssistantText: interimText,
            latestAssistantText: latestAssistantText);
        if (appendInterimBubble) {
            AppendAssistantText(conversation, interimText);
        } else {
            ReplaceLastAssistantText(conversation, interimText);
        }
        conversation.UpdatedUtc = DateTime.UtcNow;
        BindActiveTurnAssistantMessage(conversation);
        SetActiveTurnAssistantChannel(conversation, AssistantBubbleChannelKind.DraftThinking);
        SetActiveTurnAssistantProvisional(conversation, provisional: true, preferProvisionalEvents: false);
        if (string.Equals(conversation.Id, _activeConversationId, StringComparison.OrdinalIgnoreCase)) {
            QueueTranscriptRender("assistant_interim_result");
        }
    }

    internal static bool ShouldAppendInterimAssistantResult(bool activeTurnReceivedDelta, bool activeTurnBoundToConversation) {
        return !activeTurnReceivedDelta || !activeTurnBoundToConversation;
    }

    internal static bool ShouldAppendInterimAssistantResult(
        bool activeTurnReceivedDelta,
        bool activeTurnBoundToConversation,
        string? interimAssistantText,
        string? latestAssistantText) {
        if (!ShouldAppendInterimAssistantResult(activeTurnReceivedDelta, activeTurnBoundToConversation)) {
            return false;
        }

        var interimText = NormalizeAssistantSnapshotForAppendDecision(interimAssistantText);
        if (interimText.Length == 0) {
            return false;
        }

        var latestText = NormalizeAssistantSnapshotForAppendDecision(latestAssistantText);
        return latestText.Length == 0
               || (!string.Equals(interimText, latestText, StringComparison.OrdinalIgnoreCase)
                   && !AreNearDuplicateAssistantSnapshots(interimText, latestText));
    }
}
