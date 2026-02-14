using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
            var json = JsonSerializer.Serialize(message, ChatServiceJsonContext.Default.ChatServiceMessage);
            await writer.WriteLineAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
        } finally {
            _writeLock.Release();
        }
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
