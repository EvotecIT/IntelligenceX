using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.ExceptionServices;
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
    internal const string PhaseHeartbeatSuppressionReasonIo = "io";
    internal const string PhaseHeartbeatSuppressionReasonCanceled = "canceled";
    internal const string PhaseHeartbeatSuppressionReasonHeartbeatCanceled = "heartbeat-canceled";
    internal const string PhaseHeartbeatSuppressionReasonRequestCanceled = "request-canceled";
    private const string ProactiveVisualizationMarker = "ix:proactive-visualization:v1";
    private const string MermaidFenceLanguage = "mermaid";
    private const string ChartFenceLanguage = "ix-chart";
    private const string NetworkFenceLanguage = "ix-network";
    private const string LegacyNetworkFenceLanguage = "visnetwork";

    private readonly record struct ProactiveVisualizationPolicy(
        bool AllowNewVisuals,
        bool DraftHasVisuals,
        bool RequestHasVisualContract);

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
        IReadOnlyList<ToolCallDto?> toolCalls,
        IReadOnlyList<ToolOutputDto?> toolOutputs) {
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
        var visualPolicy = ResolveProactiveVisualizationPolicy(userRequest, assistantDraft);
        var allowNewVisualsText = visualPolicy.AllowNewVisuals ? "true" : "false";
        var draftHasVisualsText = visualPolicy.DraftHasVisuals ? "true" : "false";
        var requestHasVisualContractText = visualPolicy.RequestHasVisualContract ? "true" : "false";
        var visualRequirementLine = visualPolicy.AllowNewVisuals
            ? "- If allow_new_visuals is true, include at most one new visual block and only when it materially compresses complex evidence."
            : "- If allow_new_visuals is false, do not introduce new mermaid/ix-chart/ix-network blocks in this proactive rewrite.";
        return $$"""
            [Proactive follow-up review]
            {{ProactiveFollowUpMarker}}
            Expand the response with proactive intelligence based on current tool findings.

            [Proactive visualization guidance]
            {{ProactiveVisualizationMarker}}
            allow_new_visuals: {{allowNewVisualsText}}
            draft_has_visuals: {{draftHasVisualsText}}
            request_has_visual_contract: {{requestHasVisualContractText}}

            User request:
            {{requestText}}

            Current assistant draft:
            {{draftText}}

            Requirements:
            - Keep all existing factual findings that are already supported by tool output.
            - Keep the response natural and conversational, not scripted.
            - Add proactive follow-ups only when they provide real value (typically 1-3 key items).
            - Prefer concise prose/bullets by default; keep tables/diagrams/charts/networks optional.
            - Use visuals only when they materially improve clarity over plain markdown.
            {{visualRequirementLine}}
            - Preserve existing visual blocks when they are already present and still accurate.
            - When listing checks/fixes, make each item actionable and specific.
            - Include "why it matters" context when the impact is not obvious, but do not force that label on every line.
            - Vary structure naturally across turns; avoid repeating rigid templates.
            - If confidence is uncertain, say what evidence is missing and how to collect it.
            - Prefer proactive checks that can catch hidden regressions, not just obvious follow-ups.
            - Do not invent tool outputs or claim completed actions that were not executed.
            Return only the revised assistant response text.
            """;
    }

    private static ProactiveVisualizationPolicy ResolveProactiveVisualizationPolicy(string userRequest, string assistantDraft) {
        var requestHasVisualContract = ContainsVisualContractSignal(userRequest);
        var draftHasVisuals = ContainsVisualContractSignal(assistantDraft);
        return new ProactiveVisualizationPolicy(
            AllowNewVisuals: requestHasVisualContract,
            DraftHasVisuals: draftHasVisuals,
            RequestHasVisualContract: requestHasVisualContract);
    }

    private static bool ContainsVisualContractSignal(string? text) {
        var value = (text ?? string.Empty).Trim();
        if (value.Length == 0) {
            return false;
        }

        return ContainsFenceLanguage(value, MermaidFenceLanguage)
               || ContainsFenceLanguage(value, ChartFenceLanguage)
               || ContainsFenceLanguage(value, NetworkFenceLanguage)
               || ContainsFenceLanguage(value, LegacyNetworkFenceLanguage)
               || ContainsToken(value, MermaidFenceLanguage)
               || ContainsToken(value, ChartFenceLanguage)
               || ContainsToken(value, NetworkFenceLanguage)
               || ContainsToken(value, LegacyNetworkFenceLanguage);
    }

    private static bool ContainsFenceLanguage(string text, string language) {
        var expectedLanguage = (language ?? string.Empty).Trim();
        if (expectedLanguage.Length == 0 || string.IsNullOrWhiteSpace(text)) {
            return false;
        }

        var value = text.AsSpan();
        var lineStart = 0;
        while (lineStart < value.Length) {
            var lineEnd = value.Slice(lineStart).IndexOf('\n');
            if (lineEnd < 0) {
                lineEnd = value.Length - lineStart;
            }

            var line = value.Slice(lineStart, lineEnd).TrimStart();
            if (TryGetFenceLanguage(line, out var fenceLanguage)
                && fenceLanguage.Equals(expectedLanguage.AsSpan(), StringComparison.OrdinalIgnoreCase)) {
                return true;
            }

            lineStart += lineEnd + 1;
        }

        return false;
    }

    private static bool ContainsToken(string text, string token) {
        var expectedToken = (token ?? string.Empty).Trim();
        if (expectedToken.Length == 0 || string.IsNullOrWhiteSpace(text)) {
            return false;
        }

        // Treat token as a structured signal only when used as an explicit inline-code token.
        // Accept single or repeated backtick delimiters and optional surrounding whitespace.
        var value = text.AsSpan();
        var index = 0;
        while (index < value.Length) {
            var start = value.Slice(index).IndexOf('`');
            if (start < 0) {
                break;
            }

            var fenceStart = index + start;
            var fenceLength = CountRepeated(value, fenceStart, '`');
            if (fenceLength <= 0) {
                index = fenceStart + 1;
                continue;
            }

            var contentStart = fenceStart + fenceLength;
            if (contentStart >= value.Length) {
                break;
            }

            var contentEnd = FindClosingRepeated(value, contentStart, '`', fenceLength);
            if (contentEnd < 0) {
                break;
            }

            var content = value.Slice(contentStart, contentEnd - contentStart).Trim();
            if (content.Equals(expectedToken.AsSpan(), StringComparison.OrdinalIgnoreCase)) {
                return true;
            }

            index = contentEnd + fenceLength;
        }

        return false;
    }

    private static bool TryGetFenceLanguage(ReadOnlySpan<char> line, out ReadOnlySpan<char> language) {
        language = default;
        if (line.Length < 4) {
            return false;
        }

        var fenceChar = line[0];
        if ((fenceChar != '`' && fenceChar != '~') || line[1] != fenceChar || line[2] != fenceChar) {
            return false;
        }

        var index = 3;
        while (index < line.Length && char.IsWhiteSpace(line[index])) {
            index++;
        }

        if (index >= line.Length) {
            return false;
        }

        var start = index;
        while (index < line.Length && IsFenceLanguageChar(line[index])) {
            index++;
        }

        if (index <= start) {
            return false;
        }

        language = line.Slice(start, index - start);
        return true;
    }

    private static bool IsFenceLanguageChar(char ch) {
        return char.IsLetterOrDigit(ch) || ch is '-' or '_';
    }

    private static int CountRepeated(ReadOnlySpan<char> value, int start, char expected) {
        var index = start;
        while (index < value.Length && value[index] == expected) {
            index++;
        }

        return index - start;
    }

    private static int FindClosingRepeated(ReadOnlySpan<char> value, int start, char expected, int length) {
        if (length <= 0 || start >= value.Length) {
            return -1;
        }

        var index = start;
        while (index < value.Length) {
            var next = value.Slice(index).IndexOf(expected);
            if (next < 0) {
                return -1;
            }

            var candidate = index + next;
            if (CountRepeated(value, candidate, expected) >= length) {
                return candidate;
            }

            index = candidate + 1;
        }

        return -1;
    }

    internal Task RunPhaseProgressLoopAsync(
        StreamWriter writer,
        string requestId,
        string threadId,
        string phaseStatus,
        string? phaseMessage,
        string heartbeatLabel,
        int heartbeatSeconds,
        CancellationToken cancellationToken,
        Task phaseTask) {
        ValidatePhaseProgressLoopArgs(writer, requestId, threadId, phaseTask);
        return RunPhaseProgressLoopCoreAsync(
            writer,
            requestId,
            threadId,
            phaseStatus,
            phaseMessage,
            heartbeatLabel,
            heartbeatSeconds,
            cancellationToken,
            phaseTask,
            heartbeatTaskFactory: null);
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    internal Task RunPhaseProgressLoopForTestingAsync(
        StreamWriter writer,
        string requestId,
        string threadId,
        string phaseStatus,
        string? phaseMessage,
        string heartbeatLabel,
        int heartbeatSeconds,
        CancellationToken cancellationToken,
        Task phaseTask,
        Func<CancellationToken, Task> heartbeatTaskFactory) {
        ValidatePhaseProgressLoopArgs(writer, requestId, threadId, phaseTask);
        if (heartbeatTaskFactory is null) {
            throw new ArgumentNullException(nameof(heartbeatTaskFactory));
        }

        return RunPhaseProgressLoopCoreAsync(
            writer,
            requestId,
            threadId,
            phaseStatus,
            phaseMessage,
            heartbeatLabel,
            heartbeatSeconds,
            cancellationToken,
            phaseTask,
            heartbeatTaskFactory);
    }

    private static void ValidatePhaseProgressLoopArgs(StreamWriter writer, string requestId, string threadId, Task phaseTask) {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(requestId);
        ArgumentNullException.ThrowIfNull(threadId);
        ArgumentNullException.ThrowIfNull(phaseTask);
    }

    private async Task RunPhaseProgressLoopCoreAsync(
        StreamWriter writer,
        string requestId,
        string threadId,
        string phaseStatus,
        string? phaseMessage,
        string heartbeatLabel,
        int heartbeatSeconds,
        CancellationToken cancellationToken,
        Task phaseTask,
        Func<CancellationToken, Task>? heartbeatTaskFactory) {
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
        var heartbeatTask = heartbeatTaskFactory is null
            ? RunPhaseHeartbeatLoopAsync(
                writer,
                requestId,
                threadId,
                heartbeatLabel,
                sw,
                phaseTask,
                timer,
                heartbeatCts.Token)
            : heartbeatTaskFactory(heartbeatCts.Token);
        await Task.WhenAny(phaseTask, heartbeatTask).ConfigureAwait(false);
        heartbeatCts.Cancel();
        Exception? heartbeatFailure = null;
        try {
            await heartbeatTask.ConfigureAwait(false);
        } catch (Exception ex) {
            heartbeatFailure = ex;
        }

        await phaseTask.ConfigureAwait(false);
        FinalizePhaseHeartbeatFailure(heartbeatFailure, status, requestId, threadId, heartbeatCts.Token, cancellationToken);
    }

    internal static void FinalizePhaseHeartbeatFailure(
        Exception? heartbeatFailure,
        string phaseStatus,
        string requestId,
        string threadId,
        CancellationToken heartbeatCancellationToken,
        CancellationToken cancellationToken) {
        if (heartbeatFailure is null) {
            return;
        }

        var suppressionReason = GetPhaseHeartbeatSuppressionReason(heartbeatFailure, heartbeatCancellationToken, cancellationToken);
        if (suppressionReason is not null) {
            Trace.TraceWarning(
                $"Phase heartbeat loop suppressed failure after phase completion: phase={phaseStatus}; request={requestId}; thread={threadId}; " +
                $"reason={suppressionReason}; error={heartbeatFailure}");
            return;
        }

        ExceptionDispatchInfo.Capture(heartbeatFailure).Throw();
    }

    internal static bool ShouldSuppressPhaseHeartbeatFailure(Exception heartbeatFailure, CancellationToken heartbeatCancellationToken,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(heartbeatFailure);
        return GetPhaseHeartbeatSuppressionReason(heartbeatFailure, heartbeatCancellationToken, cancellationToken) is not null;
    }

    internal static string? GetPhaseHeartbeatSuppressionReason(Exception heartbeatFailure, CancellationToken heartbeatCancellationToken,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(heartbeatFailure);
        if (heartbeatFailure is IOException) {
            return PhaseHeartbeatSuppressionReasonIo;
        }

        if (heartbeatFailure is not OperationCanceledException canceledException) {
            return null;
        }

        var failureToken = canceledException.CancellationToken;
        if (!failureToken.CanBeCanceled) {
            return heartbeatCancellationToken.IsCancellationRequested || cancellationToken.IsCancellationRequested
                ? PhaseHeartbeatSuppressionReasonCanceled
                : null;
        }

        // The heartbeat loop should throw OCE with either the linked heartbeat token
        // or the outer request token. Treat other canceled tokens as unexpected.
        if (failureToken == heartbeatCancellationToken && heartbeatCancellationToken.IsCancellationRequested) {
            return PhaseHeartbeatSuppressionReasonHeartbeatCanceled;
        }

        if (failureToken == cancellationToken && cancellationToken.IsCancellationRequested) {
            return PhaseHeartbeatSuppressionReasonRequestCanceled;
        }

        return null;
    }

    private async Task RunPhaseHeartbeatLoopAsync(
        StreamWriter writer,
        string requestId,
        string threadId,
        string heartbeatLabel,
        Stopwatch sw,
        Task phaseTask,
        PeriodicTimer timer,
        CancellationToken cancellationToken) {
        try {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false)) {
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
        } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            // Expected when the model phase completes or the turn is canceled.
        }
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
