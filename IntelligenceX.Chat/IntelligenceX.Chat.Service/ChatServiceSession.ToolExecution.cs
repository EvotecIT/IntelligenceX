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

    private static async Task<TurnInfo> ChatWithToolSchemaRecoveryAsync(IntelligenceXClient client, ChatInput input, ChatOptions options,
        CancellationToken cancellationToken) {
        try {
            return await client.ChatAsync(input, options, cancellationToken).ConfigureAwait(false);
        } catch (Exception ex) when (ShouldRetryWithoutTools(ex, options)) {
            options.Tools = null;
            options.ToolChoice = null;
            return await client.ChatAsync(input, options, cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool ShouldRetryWithoutTools(Exception ex, ChatOptions options) {
        if (options.Tools is not { Count: > 0 }) {
            return false;
        }

        var message = ex.Message ?? string.Empty;
        if (message.Length == 0) {
            return false;
        }

        var missingToolName = message.IndexOf("missing required parameter", StringComparison.OrdinalIgnoreCase) >= 0
                              && message.IndexOf("tools", StringComparison.OrdinalIgnoreCase) >= 0
                              && message.IndexOf(".name", StringComparison.OrdinalIgnoreCase) >= 0;
        if (missingToolName) {
            return true;
        }

        // Compatible local providers (for example LM Studio with low n_ctx) can reject requests
        // once tool schemas push prompt size over context limits.
        return message.IndexOf("cannot truncate prompt with n_keep", StringComparison.OrdinalIgnoreCase) >= 0
               || message.IndexOf("n_ctx", StringComparison.OrdinalIgnoreCase) >= 0
               || message.IndexOf("context length", StringComparison.OrdinalIgnoreCase) >= 0
               || message.IndexOf("context window", StringComparison.OrdinalIgnoreCase) >= 0
               || message.IndexOf("maximum context length", StringComparison.OrdinalIgnoreCase) >= 0
               || message.IndexOf("prompt too long", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private async Task<IReadOnlyList<ToolOutputDto>> ExecuteToolsAsync(StreamWriter writer, string requestId, string threadId, IReadOnlyList<ToolCall> calls,
        bool parallel, int toolTimeoutSeconds, CancellationToken cancellationToken) {
        if (!parallel || calls.Count <= 1) {
            var outputs = new List<ToolOutputDto>(calls.Count);
            foreach (var call in calls) {
                await TryWriteStatusAsync(writer, requestId, threadId, status: "tool_running", toolName: call.Name, toolCallId: call.CallId)
                    .ConfigureAwait(false);
                var sw = Stopwatch.StartNew();
                var output = await ExecuteToolAsync(call, toolTimeoutSeconds, cancellationToken).ConfigureAwait(false);
                sw.Stop();
                await TryWriteStatusAsync(writer, requestId, threadId, status: "tool_completed", toolName: call.Name, toolCallId: call.CallId,
                        durationMs: sw.ElapsedMilliseconds)
                    .ConfigureAwait(false);
                outputs.Add(output);
            }
            return outputs;
        }

        var tasks = new Task<ToolOutputDto>[calls.Count];
        for (var i = 0; i < calls.Count; i++) {
            var call = calls[i];
            tasks[i] = ExecuteToolWithStatusAsync(writer, requestId, threadId, call, toolTimeoutSeconds, cancellationToken);
        }
        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task<ToolOutputDto> ExecuteToolWithStatusAsync(StreamWriter writer, string requestId, string threadId, ToolCall call,
        int toolTimeoutSeconds, CancellationToken cancellationToken) {
        await TryWriteStatusAsync(writer, requestId, threadId, status: "tool_running", toolName: call.Name, toolCallId: call.CallId)
            .ConfigureAwait(false);
        var sw = Stopwatch.StartNew();
        var output = await ExecuteToolAsync(call, toolTimeoutSeconds, cancellationToken).ConfigureAwait(false);
        sw.Stop();
        await TryWriteStatusAsync(writer, requestId, threadId, status: "tool_completed", toolName: call.Name, toolCallId: call.CallId,
                durationMs: sw.ElapsedMilliseconds)
            .ConfigureAwait(false);
        return output;
    }

    private async Task<ToolOutputDto> ExecuteToolAsync(ToolCall call, int toolTimeoutSeconds, CancellationToken cancellationToken) {
        if (!_registry.TryGet(call.Name, out var tool)) {
            var output = ToolOutputEnvelope.Error(
                errorCode: "tool_not_registered",
                error: $"Tool '{call.Name}' is not registered.",
                hints: new[] { "Call list_tools to list available tools.", "Check that the correct packs are enabled." },
                isTransient: false);

            return BuildToolOutputDto(call.CallId, output);
        }

        // Retry profile wiring is enforced in this execution loop.
        var profile = ResolveRetryProfile(call.Name);
        ToolOutputDto? lastFailure = null;
        for (var attemptIndex = 0; attemptIndex < profile.MaxAttempts; attemptIndex++) {
            var output = await ExecuteToolAttemptAsync(tool, call, toolTimeoutSeconds, cancellationToken).ConfigureAwait(false);
            if (!ShouldRetryToolCall(output, profile, attemptIndex)) {
                return output;
            }

            lastFailure = output;
            if (profile.DelayBaseMs > 0) {
                var delayMs = Math.Min(800, profile.DelayBaseMs * (attemptIndex + 1));
                try {
                    await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
                } catch (OperationCanceledException) {
                    return output;
                }
            }
        }

        return lastFailure ?? await ExecuteToolAttemptAsync(tool, call, toolTimeoutSeconds, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ToolOutputDto> ExecuteToolAttemptAsync(ITool tool, ToolCall call, int toolTimeoutSeconds, CancellationToken cancellationToken) {
        using var toolCts = CreateTimeoutCts(cancellationToken, toolTimeoutSeconds);
        var toolToken = toolCts?.Token ?? cancellationToken;
        try {
            var output = await tool.InvokeAsync(call.Arguments, toolToken).ConfigureAwait(false);
            var text = output ?? string.Empty;
            if (_options.Redact) {
                text = RedactText(text);
            }
            return BuildToolOutputDto(call.CallId, text);
        } catch (OperationCanceledException) when (toolCts is not null && toolCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested) {
            var output = ToolOutputEnvelope.Error(
                errorCode: "tool_timeout",
                error: $"Tool '{call.Name}' timed out after {toolTimeoutSeconds}s.",
                hints: new[] { "Increase toolTimeoutSeconds, or narrow the query (OU scoping, tighter filters)." },
                isTransient: true);
            return BuildToolOutputDto(call.CallId, output);
        } catch (Exception ex) {
            var isTransient = IsLikelyTransientToolException(ex);
            var output = ToolOutputEnvelope.Error(
                errorCode: "tool_exception",
                error: $"{ex.GetType().Name}: {ex.Message}",
                hints: new[] {
                    "Try again. If it keeps failing, narrow the query and capture tool args/output.",
                    "Check tool parameter names and value types in the tool details panel."
                },
                isTransient: isTransient);
            return BuildToolOutputDto(call.CallId, output);
        }
    }

    private static ToolOutputDto BuildToolOutputDto(string callId, string output) {
        var meta = TryExtractToolOutputMetadata(output);
        return new ToolOutputDto {
            CallId = callId,
            Output = output,
            Ok = meta.Ok,
            ErrorCode = meta.ErrorCode,
            Error = meta.Error,
            Hints = meta.Hints,
            IsTransient = meta.IsTransient,
            SummaryMarkdown = meta.SummaryMarkdown,
            MetaJson = meta.MetaJson,
            RenderJson = meta.RenderJson,
            FailureJson = meta.FailureJson
        };
    }

    private static bool ShouldRetryToolCall(ToolOutputDto output, ToolRetryProfile profile, int attemptIndex) {
        // attemptIndex is zero-based current attempt. We can only retry when there is another slot left.
        if (attemptIndex + 1 >= profile.MaxAttempts) {
            return false;
        }
        if (output.Ok is true) {
            return false;
        }

        if (string.Equals(output.ErrorCode, "tool_not_registered", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }
        if (string.Equals(output.ErrorCode, "tool_timeout", StringComparison.OrdinalIgnoreCase) && profile.RetryOnTimeout) {
            return true;
        }
        if (!string.IsNullOrWhiteSpace(output.ErrorCode)) {
            var code = output.ErrorCode.Trim();
            var transientTransportCode = code.Contains("transport", StringComparison.OrdinalIgnoreCase)
                                         || code.Contains("transient", StringComparison.OrdinalIgnoreCase)
                                         || code.Contains("unavailable", StringComparison.OrdinalIgnoreCase);
            if (transientTransportCode && profile.RetryOnTransport) {
                return true;
            }
        }
        if (IsLikelyPermanentToolFailure(output)) {
            return false;
        }
        if (output.IsTransient is true) {
            return true;
        }

        var text = BuildToolFailureSearchText(output);
        if (text.Length == 0) {
            return false;
        }

        var timeoutSignal = text.Contains("timed out", StringComparison.OrdinalIgnoreCase)
                            || text.Contains("timeout", StringComparison.OrdinalIgnoreCase)
                            || text.Contains("rpc server unavailable", StringComparison.OrdinalIgnoreCase)
                            || text.Contains("server unavailable", StringComparison.OrdinalIgnoreCase);
        if (timeoutSignal && profile.RetryOnTimeout) {
            return true;
        }

        var transportSignal = text.Contains("temporarily unavailable", StringComparison.OrdinalIgnoreCase)
                              || text.Contains("connection reset", StringComparison.OrdinalIgnoreCase)
                              || text.Contains("connection closed", StringComparison.OrdinalIgnoreCase)
                              || text.Contains("connection refused", StringComparison.OrdinalIgnoreCase)
                              || text.Contains("name resolution", StringComparison.OrdinalIgnoreCase)
                              || text.Contains("dns", StringComparison.OrdinalIgnoreCase)
                              || text.Contains("remote host closed", StringComparison.OrdinalIgnoreCase)
                              || text.Contains("service unavailable", StringComparison.OrdinalIgnoreCase)
                              || text.Contains("gateway timeout", StringComparison.OrdinalIgnoreCase)
                              || text.Contains("econnreset", StringComparison.OrdinalIgnoreCase)
                              || text.Contains("etimedout", StringComparison.OrdinalIgnoreCase)
                              || text.Contains("network", StringComparison.OrdinalIgnoreCase)
                              || text.Contains("try again", StringComparison.OrdinalIgnoreCase)
                              || text.Contains("throttl", StringComparison.OrdinalIgnoreCase);
        return transportSignal && profile.RetryOnTransport;
    }

    private static bool IsLikelyPermanentToolFailure(ToolOutputDto output) {
        var text = BuildToolFailureSearchText(output);
        if (text.Length == 0) {
            return false;
        }

        return text.Contains("unsupported columns", StringComparison.OrdinalIgnoreCase)
               || text.Contains("unknown projection", StringComparison.OrdinalIgnoreCase)
               || text.Contains("invalid parameter", StringComparison.OrdinalIgnoreCase)
               || text.Contains("invalid argument", StringComparison.OrdinalIgnoreCase)
               || text.Contains("missing required", StringComparison.OrdinalIgnoreCase)
               || text.Contains("cannot bind parameter", StringComparison.OrdinalIgnoreCase)
               || text.Contains("access denied", StringComparison.OrdinalIgnoreCase)
               || text.Contains("permission denied", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildToolFailureSearchText(ToolOutputDto output) {
        var parts = new List<string>(8);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddFailureSearchPart(parts, seen, output.ErrorCode);
        AddFailureSearchPart(parts, seen, output.Error);
        AppendFailureSearchContext(parts, seen, output.FailureJson);
        AppendFailureSearchContext(parts, seen, output.MetaJson);
        AppendFailureSearchContext(parts, seen, output.Output, includeRawFallback: false);
        return parts.Count == 0 ? string.Empty : string.Join(" ", parts);
    }

    private static void AppendFailureSearchContext(List<string> parts, HashSet<string> seen, string? rawText, bool includeRawFallback = true) {
        if (string.IsNullOrWhiteSpace(rawText)) {
            return;
        }

        if (TryAppendFailureJsonSignals(parts, seen, rawText!)) {
            return;
        }

        if (includeRawFallback) {
            AddFailureSearchPart(parts, seen, rawText);
        }
    }

    private static bool TryAppendFailureJsonSignals(List<string> parts, HashSet<string> seen, string rawText) {
        try {
            var parsed = JsonLite.Parse(rawText);
            var obj = parsed?.AsObject();
            if (obj is null) {
                return false;
            }

            var before = parts.Count;
            AppendFailureSignalsFromObject(parts, seen, obj);
            return parts.Count > before;
        } catch {
            return false;
        }
    }

    private static void AppendFailureSignalsFromObject(List<string> parts, HashSet<string> seen, JsonObject obj) {
        AddFailureSearchPart(parts, seen, obj.GetString("error_code"));
        AddFailureSearchPart(parts, seen, obj.GetString("code"));
        AddFailureSearchPart(parts, seen, obj.GetString("error"));
        AddFailureSearchPart(parts, seen, obj.GetString("message"));
        AddFailureSearchPart(parts, seen, obj.GetString("reason"));
        AddFailureSearchPart(parts, seen, obj.GetString("exception"));
        AddFailureSearchPart(parts, seen, obj.GetString("exception_type"));
        AddFailureSearchPart(parts, seen, obj.GetString("exceptionType"));
        AddFailureSearchPart(parts, seen, obj.GetString("details"));

        try {
            if (obj.GetObject("failure") is JsonObject failureObj) {
                AddFailureSearchPart(parts, seen, failureObj.GetString("code"));
                AddFailureSearchPart(parts, seen, failureObj.GetString("error"));
                AddFailureSearchPart(parts, seen, failureObj.GetString("message"));
                AddFailureSearchPart(parts, seen, failureObj.GetString("reason"));
            }
        } catch {
            // best-effort extraction only
        }

        try {
            if (obj.GetObject("meta") is JsonObject metaObj) {
                AddFailureSearchPart(parts, seen, metaObj.GetString("error_code"));
                AddFailureSearchPart(parts, seen, metaObj.GetString("error"));
                AddFailureSearchPart(parts, seen, metaObj.GetString("message"));
                AddFailureSearchPart(parts, seen, metaObj.GetString("reason"));
            }
        } catch {
            // best-effort extraction only
        }
    }

    private static void AddFailureSearchPart(List<string> parts, HashSet<string> seen, string? rawText) {
        var compact = CompactFailureText(rawText);
        if (compact.Length == 0) {
            return;
        }

        if (seen.Add(compact)) {
            parts.Add(compact);
        }
    }

    private static string CompactFailureText(string? rawText) {
        if (string.IsNullOrWhiteSpace(rawText)) {
            return string.Empty;
        }

        var compact = Regex.Replace(rawText.Trim(), @"\s+", " ");
        const int maxLength = 768;
        return compact.Length <= maxLength ? compact : compact[..maxLength];
    }

    private static bool IsLikelyTransientToolException(Exception ex) {
        if (ex is OperationCanceledException) {
            return false;
        }
        if (HasLikelyPermanentExceptionSignal(ex)) {
            return false;
        }

        if (HasKnownTransientExceptionInChain(ex)) {
            return true;
        }

        var message = ex.Message ?? string.Empty;
        return message.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0
               || message.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0
               || message.IndexOf("temporarily", StringComparison.OrdinalIgnoreCase) >= 0
               || message.IndexOf("unavailable", StringComparison.OrdinalIgnoreCase) >= 0
               || message.IndexOf("try again", StringComparison.OrdinalIgnoreCase) >= 0
               || message.IndexOf("connection", StringComparison.OrdinalIgnoreCase) >= 0
               || message.IndexOf("throttl", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool HasLikelyPermanentExceptionSignal(Exception ex) {
        var depth = 0;
        for (Exception? current = ex; current is not null && depth < 8; current = current.InnerException, depth++) {
            if (current is UnauthorizedAccessException) {
                return true;
            }

            var message = current.Message ?? string.Empty;
            if (message.IndexOf("access denied", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("permission denied", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("unauthorized", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("forbidden", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("invalid credential", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("authentication failed", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("invalid parameter", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("invalid argument", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("missing required", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("cannot bind parameter", StringComparison.OrdinalIgnoreCase) >= 0) {
                return true;
            }
        }

        return false;
    }

    private static bool HasKnownTransientExceptionInChain(Exception ex) {
        var depth = 0;
        for (Exception? current = ex; current is not null && depth < 8; current = current.InnerException, depth++) {
            if (current is OperationCanceledException) {
                return false;
            }
            if (current is TimeoutException || current is IOException) {
                return true;
            }

            var name = current.GetType().FullName ?? current.GetType().Name;
            if (name.IndexOf("SocketException", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("HttpRequestException", StringComparison.OrdinalIgnoreCase) >= 0) {
                return true;
            }
        }

        return false;
    }

    private static ToolRetryProfile ResolveRetryProfile(string? toolName) {
        var normalized = (toolName ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.StartsWith("ad_", StringComparison.Ordinal)) {
            return new ToolRetryProfile(MaxAttempts: 2, DelayBaseMs: 200, RetryOnTimeout: true, RetryOnTransport: true);
        }
        if (normalized.StartsWith("eventlog_", StringComparison.Ordinal)) {
            return new ToolRetryProfile(MaxAttempts: 2, DelayBaseMs: 150, RetryOnTimeout: true, RetryOnTransport: true);
        }
        if (normalized.StartsWith("system_", StringComparison.Ordinal)
            || normalized.StartsWith("wsl_", StringComparison.Ordinal)) {
            return new ToolRetryProfile(MaxAttempts: 2, DelayBaseMs: 120, RetryOnTimeout: true, RetryOnTransport: true);
        }
        if (normalized.StartsWith("fs_", StringComparison.Ordinal)) {
            return new ToolRetryProfile(MaxAttempts: 2, DelayBaseMs: 90, RetryOnTimeout: true, RetryOnTransport: false);
        }

        return new ToolRetryProfile(MaxAttempts: 1, DelayBaseMs: 0, RetryOnTimeout: false, RetryOnTransport: false);
    }

}
