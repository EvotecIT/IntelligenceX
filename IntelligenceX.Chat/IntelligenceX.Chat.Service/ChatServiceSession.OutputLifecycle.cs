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
    private string? _lastFinalResultRequestId;
    private string? _lastFinalResultThreadId;
    private string? _lastFinalResultText;

    private static ToolOutputMetadata TryExtractToolOutputMetadata(string output) {
        if (string.IsNullOrWhiteSpace(output)) {
            return default;
        }

        // Tool outputs are free-form strings. When they happen to be JSON envelopes,
        // extract fields so the UI can render errors/tables/markdown consistently.
        try {
            var parsed = JsonLite.Parse(output);
            var obj = parsed?.AsObject();
            if (obj is null) {
                return default;
            }

            bool? ok = null;
            try {
                ok = obj.GetBoolean("ok");
            } catch {
                ok = null;
            }

            var errorCode = obj.GetString("error_code");
            var error = obj.GetString("error");

            bool? isTransient = null;
            try {
                isTransient = obj.GetBoolean("is_transient");
            } catch {
                isTransient = null;
            }

            string[]? hints = null;
            try {
                hints = ParseHintsArray(obj.GetArray("hints"));
            } catch {
                hints = null;
            }

            string? failureJson = null;
            try {
                if (obj.GetObject("failure") is JsonObject failureObj) {
                    failureJson = JsonLite.Serialize(failureObj);

                    if (string.IsNullOrWhiteSpace(errorCode)) {
                        errorCode = failureObj.GetString("code");
                    }
                    if (string.IsNullOrWhiteSpace(error)) {
                        error = failureObj.GetString("message");
                    }
                    if (!isTransient.HasValue) {
                        try {
                            isTransient = failureObj.GetBoolean("is_transient");
                        } catch {
                            isTransient = null;
                        }
                    }
                    if (hints is null || hints.Length == 0) {
                        hints = ParseHintsArray(failureObj.GetArray("hints"));
                    }
                }
            } catch {
                failureJson = null;
            }

            var summaryMarkdown = obj.GetString("summary_markdown");
            string? metaJson = null;
            try {
                if (obj.GetObject("meta") is JsonObject metaObj) {
                    metaJson = JsonLite.Serialize(metaObj);
                }
            } catch {
                metaJson = null;
            }

            string? renderJson = null;
            try {
                if (obj.GetObject("render") is JsonObject renderObj) {
                    renderJson = JsonLite.Serialize(renderObj);
                } else if (obj.GetArray("render") is JsonArray renderArr) {
                    renderJson = JsonLite.Serialize(renderArr);
                }
            } catch {
                renderJson = null;
            }

            if (ok is null && errorCode is null && error is null && hints is null && isTransient is null && summaryMarkdown is null
                && metaJson is null && renderJson is null && failureJson is null) {
                return default;
            }

            return new ToolOutputMetadata(ok, errorCode, error, hints, isTransient, summaryMarkdown, metaJson, renderJson, failureJson);
        } catch {
            return default;
        }
    }

    private static string[]? ParseHintsArray(JsonArray? arr) {
        if (arr is null || arr.Count == 0) {
            return null;
        }

        var list = new List<string>(arr.Count);
        foreach (var item in arr) {
            var s = item?.AsString();
            if (!string.IsNullOrWhiteSpace(s)) {
                list.Add(s!);
            }
        }

        return list.Count > 0 ? list.ToArray() : null;
    }

    private readonly record struct ToolOutputMetadata(
        bool? Ok,
        string? ErrorCode,
        string? Error,
        string[]? Hints,
        bool? IsTransient,
        string? SummaryMarkdown,
        string? MetaJson,
        string? RenderJson,
        string? FailureJson);

    private async Task WriteAsync(StreamWriter writer, ChatServiceMessage message, CancellationToken cancellationToken) {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            var normalizedMessage = NormalizeFinalResultMessageText(message);
            if (ShouldSuppressDuplicateFinalResultMessage(normalizedMessage)) {
                return;
            }

            var json = JsonSerializer.Serialize(normalizedMessage, ChatServiceJsonContext.Default.ChatServiceMessage);
            await writer.WriteLineAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
        } finally {
            _writeLock.Release();
        }
    }

    private bool ShouldSuppressDuplicateFinalResultMessage(ChatServiceMessage message) {
        if (message is not ChatResultMessage result) {
            return false;
        }

        var requestId = (result.RequestId ?? string.Empty).Trim();
        var threadId = (result.ThreadId ?? string.Empty).Trim();
        var text = (result.Text ?? string.Empty).Trim();
        if (ShouldSuppressDuplicateFinalResultForRequest(
                previousRequestId: _lastFinalResultRequestId,
                previousThreadId: _lastFinalResultThreadId,
                previousText: _lastFinalResultText,
                requestId: requestId,
                threadId: threadId,
                text: text)) {
            Trace.WriteLine($"[chat-result] duplicate_final_result_suppressed requestId={requestId} threadId={threadId}");
            return true;
        }

        _lastFinalResultRequestId = requestId;
        _lastFinalResultThreadId = threadId;
        _lastFinalResultText = text;
        return false;
    }

    internal static string NormalizeFinalResultTextForProtocol(string? text) {
        var normalized = text ?? string.Empty;
        if (normalized.Length == 0) {
            return normalized;
        }

        normalized = ReplaceOrdinalIgnoreCase(normalized, "I refreshed", "I reran the check");
        normalized = ReplaceOrdinalIgnoreCase(normalized, "fresh results", "current results");
        normalized = ReplaceOrdinalIgnoreCase(normalized, "just reran", "reran");
        return normalized;
    }

    private static ChatServiceMessage NormalizeFinalResultMessageText(ChatServiceMessage message) {
        if (message is not ChatResultMessage result) {
            return message;
        }

        return result with {
            Text = NormalizeFinalResultTextForProtocol(result.Text)
        };
    }

    private static string ReplaceOrdinalIgnoreCase(string text, string oldValue, string newValue) {
        if (string.IsNullOrEmpty(text)
            || string.IsNullOrEmpty(oldValue)
            || string.Equals(oldValue, newValue, StringComparison.Ordinal)) {
            return text;
        }

        var firstIndex = text.IndexOf(oldValue, StringComparison.OrdinalIgnoreCase);
        if (firstIndex < 0) {
            return text;
        }

        var builder = new StringBuilder(text.Length + Math.Max(0, newValue.Length - oldValue.Length));
        var searchStart = 0;
        while (true) {
            var matchIndex = text.IndexOf(oldValue, searchStart, StringComparison.OrdinalIgnoreCase);
            if (matchIndex < 0) {
                builder.Append(text, searchStart, text.Length - searchStart);
                break;
            }

            builder.Append(text, searchStart, matchIndex - searchStart);
            builder.Append(newValue);
            searchStart = matchIndex + oldValue.Length;
        }

        return builder.ToString();
    }

    internal static bool ShouldSuppressDuplicateFinalResultForRequest(
        string? previousRequestId,
        string? previousThreadId,
        string? previousText,
        string? requestId,
        string? threadId,
        string? text) {
        var normalizedRequestId = (requestId ?? string.Empty).Trim();
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedRequestId.Length == 0 || normalizedThreadId.Length == 0) {
            return false;
        }

        var normalizedPreviousRequestId = (previousRequestId ?? string.Empty).Trim();
        var normalizedPreviousThreadId = (previousThreadId ?? string.Empty).Trim();
        if (!string.Equals(normalizedPreviousRequestId, normalizedRequestId, StringComparison.Ordinal)
            || !string.Equals(normalizedPreviousThreadId, normalizedThreadId, StringComparison.Ordinal)) {
            return false;
        }

        var normalizedPreviousText = (previousText ?? string.Empty).Trim();
        var normalizedText = (text ?? string.Empty).Trim();
        if (normalizedPreviousText.Length == 0 && normalizedText.Length > 0) {
            // Recovery allowance: if a runtime emitted an empty final first, allow a later non-empty
            // final for the same request/thread to surface once.
            return false;
        }

        // Protocol guardrail: emit at most one meaningful final result envelope per request/thread pair.
        return true;
    }

    private void CancelLoginIfActive() {
        LoginFlow? flow;
        lock (_loginLock) {
            flow = _login;
            _login = null;
        }
        flow?.Cancel();
    }

    private static CancellationTokenSource? CreateTimeoutCts(CancellationToken ct, int seconds) {
        if (seconds <= 0) {
            return null;
        }
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(seconds));
        return cts;
    }

    // Tool errors are returned as JSON strings to the model. Use the shared contract helper so
    // tool packs and hosts converge on the same envelope over time.

}
