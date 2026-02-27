using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.OpenAI.Chat;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    internal static string ResolveAssistantTextBeforeNoTextFallback(
        string assistantDraft,
        string lastNonEmptyAssistantDraft,
        bool hasToolActivity) {
        var normalizedAssistantDraft = assistantDraft ?? string.Empty;
        var current = normalizedAssistantDraft.Trim();
        if (current.Length > 0) {
            return normalizedAssistantDraft;
        }

        if (!hasToolActivity) {
            return normalizedAssistantDraft;
        }

        var prior = (lastNonEmptyAssistantDraft ?? string.Empty).Trim();
        if (prior.Length == 0
            || prior.StartsWith("[warning] No response text was produced", StringComparison.OrdinalIgnoreCase)
            || LooksLikeRuntimeControlPayloadArtifact(prior)) {
            return normalizedAssistantDraft;
        }

        return prior;
    }

    internal static string ResolveAssistantTextFromToolOutputsFallback(
        string assistantDraft,
        IReadOnlyList<ToolCallDto> toolCalls,
        IReadOnlyList<ToolOutputDto> toolOutputs) {
        var normalizedAssistantDraft = assistantDraft ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(normalizedAssistantDraft)) {
            return normalizedAssistantDraft;
        }

        if (toolOutputs is null || toolOutputs.Count == 0) {
            return normalizedAssistantDraft;
        }

        var toolNamesByCallId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (toolCalls is { Count: > 0 }) {
            for (var i = 0; i < toolCalls.Count; i++) {
                ToolCallDto? call = toolCalls[i];
                if (call is null) {
                    continue;
                }

                var callId = (call.CallId ?? string.Empty).Trim();
                var toolName = (call.Name ?? string.Empty).Trim();
                if (callId.Length == 0 || toolName.Length == 0) {
                    continue;
                }

                toolNamesByCallId[callId] = toolName;
            }
        }

        var bulletLines = new List<string>(capacity: Math.Min(3, toolOutputs.Count));
        var usableOutputCount = 0;
        for (var i = 0; i < toolOutputs.Count; i++) {
            ToolOutputDto? output = toolOutputs[i];
            if (output is null) {
                continue;
            }

            usableOutputCount++;
            if (bulletLines.Count >= 3) {
                continue;
            }

            var summary = BuildToolOutputFallbackSummary(output);
            if (summary.Length == 0) {
                continue;
            }

            var callId = (output.CallId ?? string.Empty).Trim();
            var toolName = toolNamesByCallId.TryGetValue(callId, out var name)
                ? name
                : "tool";
            bulletLines.Add("- `" + toolName + "`: " + summary);
        }

        if (bulletLines.Count == 0) {
            return normalizedAssistantDraft;
        }

        var remainingCount = Math.Max(0, usableOutputCount - bulletLines.Count);
        var builder = new StringBuilder();
        builder.AppendLine("Recovered findings from executed tools (model returned no text):");
        for (var i = 0; i < bulletLines.Count; i++) {
            builder.AppendLine(bulletLines[i]);
        }

        if (remainingCount > 0) {
            builder.Append("... and ")
                .Append(remainingCount.ToString())
                .Append(" more tool output(s).");
        }

        return builder.ToString().TrimEnd();
    }

    private static string BuildToolOutputFallbackSummary(ToolOutputDto output) {
        var summaryMarkdown = TruncateToolOutputSummary((output.SummaryMarkdown ?? string.Empty).Trim(), maxChars: 220);
        if (summaryMarkdown.Length > 0) {
            return summaryMarkdown;
        }

        var errorText = TruncateToolOutputSummary((output.Error ?? string.Empty).Trim(), maxChars: 220);
        if (errorText.Length > 0) {
            return "error: " + errorText;
        }

        var errorCode = (output.ErrorCode ?? string.Empty).Trim();
        if (errorCode.Length > 0) {
            return "error code " + errorCode;
        }

        var raw = (output.Output ?? string.Empty).Trim();
        if (raw.Length == 0) {
            return output.Ok is false ? "returned an empty error payload." : "completed successfully.";
        }

        if (TryExtractPreferredJsonSummary(raw, out var jsonSummary)) {
            return TruncateToolOutputSummary(jsonSummary, maxChars: 220);
        }

        if (LooksLikeJsonPayload(raw)) {
            return output.Ok is false ? "returned structured error output." : "returned structured output.";
        }

        return TruncateToolOutputSummary(raw, maxChars: 220);
    }

    private static bool TryExtractPreferredJsonSummary(string raw, out string summary) {
        summary = string.Empty;
        if (!LooksLikeJsonPayload(raw)) {
            return false;
        }

        try {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) {
                return false;
            }

            if (TryGetJsonString(root, "summary_markdown", out summary)
                || TryGetJsonString(root, "summary", out summary)
                || TryGetJsonString(root, "message", out summary)
                || TryGetJsonString(root, "error", out summary)) {
                return true;
            }

            if (root.TryGetProperty("failure", out var failure)
                && failure.ValueKind == JsonValueKind.Object
                && (TryGetJsonString(failure, "message", out summary)
                    || TryGetJsonString(failure, "code", out summary))) {
                return true;
            }

            if (root.TryGetProperty("rows", out var rows) && rows.ValueKind == JsonValueKind.Array) {
                summary = "returned " + rows.GetArrayLength().ToString() + " row(s).";
                return true;
            }

            return false;
        } catch (JsonException) {
            return false;
        }
    }

    private static bool TryGetJsonString(JsonElement obj, string propertyName, out string value) {
        value = string.Empty;
        if (!obj.TryGetProperty(propertyName, out var node) || node.ValueKind != JsonValueKind.String) {
            return false;
        }

        var candidate = (node.GetString() ?? string.Empty).Trim();
        if (candidate.Length == 0) {
            return false;
        }

        value = candidate;
        return true;
    }

    private static bool LooksLikeJsonPayload(string text) {
        var value = (text ?? string.Empty).TrimStart();
        return value.StartsWith("{") || value.StartsWith("[");
    }

    private static string TruncateToolOutputSummary(string text, int maxChars) {
        var normalized = CollapseWhitespace((text ?? string.Empty).Trim());
        if (normalized.Length == 0) {
            return string.Empty;
        }

        if (normalized.Length <= maxChars || maxChars <= 4) {
            return normalized;
        }

        return normalized.Substring(0, maxChars - 3).TrimEnd() + "...";
    }

    internal static string BuildProactiveFollowUpReviewPrompt(string userRequest, string assistantDraft) {
        var requestText = TrimForPrompt(userRequest, 520);
        var draftText = TrimForPrompt(assistantDraft, 1800);
        return $$"""
            [Proactive follow-up review]
            {{ProactiveFollowUpMarker}}
            Expand the response with proactive intelligence based on current tool findings.

            User request:
            {{requestText}}

            Current assistant draft:
            {{draftText}}

            Requirements:
            - Keep all existing factual findings that are already supported by tool output.
            - Keep the response natural and conversational, not scripted.
            - Add proactive follow-ups only when they provide real value (typically 1-3 key items).
            - You may present results in the format that best fits the findings: short paragraphs, bullets, compact tables, or simple diagrams/charts.
            - If a diagram/chart improves clarity, include it directly without asking for permission first.
            - When listing checks/fixes, make each item actionable and specific.
            - Include "why it matters" context when the impact is not obvious, but do not force that label on every line.
            - Vary structure naturally across turns; avoid repeating rigid templates.
            - If confidence is uncertain, say what evidence is missing and how to collect it.
            - Prefer proactive checks that can catch hidden regressions, not just obvious follow-ups.
            - Do not invent tool outputs or claim completed actions that were not executed.
            Return only the revised assistant response text.
            """;
    }

    internal async Task RunPhaseProgressLoopAsync(
        StreamWriter writer,
        string requestId,
        string threadId,
        string phaseStatus,
        string? phaseMessage,
        string heartbeatLabel,
        int heartbeatSeconds,
        CancellationToken cancellationToken,
        Task phaseTask) {
        var status = string.IsNullOrWhiteSpace(phaseStatus) ? "thinking" : phaseStatus.Trim();
        if (!string.IsNullOrWhiteSpace(phaseMessage)) {
            await TryWriteStatusAsync(writer, requestId, threadId, status: status, message: phaseMessage).ConfigureAwait(false);
        }

        if (heartbeatSeconds <= 0) {
            await phaseTask.ConfigureAwait(false);
            return;
        }

        var heartbeatInterval = TimeSpan.FromSeconds(Math.Max(1, heartbeatSeconds));
        var sw = Stopwatch.StartNew();
        using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var timer = new PeriodicTimer(heartbeatInterval);
        var stopHeartbeatTask = phaseTask.ContinueWith(
            static (_, state) => {
                try {
                    ((CancellationTokenSource)state!).Cancel();
                } catch {
                    // Best-effort heartbeat stop.
                }
            },
            heartbeatCts,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        try {
            while (await timer.WaitForNextTickAsync(heartbeatCts.Token).ConfigureAwait(false)) {
                if (phaseTask.IsCompleted) {
                    break;
                }

                var elapsedSeconds = Math.Max(1, (int)Math.Round(sw.Elapsed.TotalSeconds));
                await TryWriteStatusAsync(
                        writer,
                        requestId,
                        threadId,
                        status: ChatStatusCodes.PhaseHeartbeat,
                        durationMs: sw.ElapsedMilliseconds,
                        message: $"{heartbeatLabel}... ({elapsedSeconds}s)")
                    .ConfigureAwait(false);
            }
        } catch (OperationCanceledException) when (heartbeatCts.IsCancellationRequested) {
            // Expected when the model phase completes or the turn is canceled.
        } finally {
            await stopHeartbeatTask.ConfigureAwait(false);
        }

        await phaseTask.ConfigureAwait(false);
    }

    internal async Task<TurnInfo> RunModelPhaseWithProgressAsync(
        IntelligenceXClient client,
        StreamWriter writer,
        string requestId,
        string threadId,
        ChatInput input,
        ChatOptions options,
        CancellationToken cancellationToken,
        string phaseStatus,
        string phaseMessage,
        string heartbeatLabel,
        int heartbeatSeconds) {
        // Defensive rebind: planner routing may temporarily switch threads.
        // Reassert the active conversation thread before each model phase.
        if (!string.IsNullOrWhiteSpace(threadId)) {
            var requestedThreadId = threadId.Trim();
            var reboundThreadId = ResolveRecoveredThreadAlias(requestedThreadId);
            try {
                await client.UseThreadAsync(reboundThreadId, cancellationToken).ConfigureAwait(false);
            } catch (Exception ex) when (ShouldRecoverMissingTransportThread(ex)) {
                var recoveredThread = await client.StartNewThreadAsync(options.Model, cancellationToken: cancellationToken).ConfigureAwait(false);
                var recoveredThreadId = (recoveredThread.Id ?? string.Empty).Trim();
                if (recoveredThreadId.Length > 0) {
                    await client.UseThreadAsync(recoveredThreadId, cancellationToken).ConfigureAwait(false);
                    RememberRecoveredThreadAlias(requestedThreadId, recoveredThreadId);
                    if (!string.Equals(reboundThreadId, requestedThreadId, StringComparison.Ordinal)) {
                        RememberRecoveredThreadAlias(reboundThreadId, recoveredThreadId);
                    }
                }
            }
        }

        var chatTask = ChatWithToolSchemaRecoveryAsync(client, input, options, cancellationToken);
        await RunPhaseProgressLoopAsync(
                writer,
                requestId,
                threadId,
                phaseStatus,
                phaseMessage,
                heartbeatLabel,
                heartbeatSeconds,
                cancellationToken,
                chatTask)
            .ConfigureAwait(false);
        return await chatTask.ConfigureAwait(false);
    }

    internal Task<TurnInfo> RunReviewOnlyModelPhaseWithProgressAsync(
        IntelligenceXClient client,
        StreamWriter writer,
        string requestId,
        string threadId,
        ChatInput input,
        ChatOptions options,
        CancellationToken cancellationToken,
        string phaseStatus,
        string phaseMessage,
        string heartbeatLabel,
        int heartbeatSeconds) {
        // Review-only passes are in-thread rewrites of the current draft and must never execute tools.
        return RunModelPhaseWithProgressAsync(
            client,
            writer,
            requestId,
            threadId,
            input,
            CopyChatOptionsWithoutTools(options, newThreadOverride: false),
            cancellationToken,
            phaseStatus,
            phaseMessage,
            heartbeatLabel,
            heartbeatSeconds);
    }

    private static ChatOptions CopyChatOptions(ChatOptions options, bool? newThreadOverride = null) {
        if (options is null) {
            throw new ArgumentNullException(nameof(options));
        }

        var copy = options.Clone();
        if (newThreadOverride.HasValue) {
            copy.NewThread = newThreadOverride.Value;
        }
        return copy;
    }

    internal static ChatOptions CopyChatOptionsWithoutTools(ChatOptions options, bool? newThreadOverride = null) {
        var copy = CopyChatOptions(options, newThreadOverride);
        copy.Tools = null;
        copy.ToolChoice = null;
        copy.ParallelToolCalls = false;
        return copy;
    }
}
