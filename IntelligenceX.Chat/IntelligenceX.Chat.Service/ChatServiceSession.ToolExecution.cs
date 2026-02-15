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
    private static readonly string[] ProjectionFormattingArgumentNames = { "columns", "sort_by", "sort_direction" };
    private static readonly Regex TopValidationRegex = new(
        @"\btop\b\s*(?:must|should|has to|needs to|is|was|value|argument|parameter)|\b(?:must|should|has to|needs to|invalid|unsupported|required)\b.{0,32}\btop\b|\btop\b.{0,32}\b(?:invalid|unsupported|required|between)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline);
    private const string ProjectionColumnsArgumentName = "columns";
    private const string ProjectionSortByArgumentName = "sort_by";
    private const string ProjectionSortDirectionArgumentName = "sort_direction";
    private const string ProjectionTopArgumentName = "top";
    private const string ProjectionFallbackRecoveredStatusMessage = "Recovered from projection argument failure.";

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
                await TryWriteToolRecoveredStatusAsync(writer, requestId, threadId, call, output).ConfigureAwait(false);
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
        await TryWriteToolRecoveredStatusAsync(writer, requestId, threadId, call, output).ConfigureAwait(false);
        await TryWriteStatusAsync(writer, requestId, threadId, status: "tool_completed", toolName: call.Name, toolCallId: call.CallId,
                durationMs: sw.ElapsedMilliseconds)
            .ConfigureAwait(false);
        return output;
    }

    private async Task TryWriteToolRecoveredStatusAsync(StreamWriter writer, string requestId, string threadId, ToolCall call, ToolOutputDto output) {
        if (!WasProjectionFallbackApplied(output)) {
            return;
        }

        await TryWriteStatusAsync(
                writer,
                requestId,
                threadId,
                status: "tool_recovered",
                toolName: call.Name,
                toolCallId: call.CallId,
                message: ProjectionFallbackRecoveredStatusMessage)
            .ConfigureAwait(false);
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
        var currentCall = call;
        var projectionFallbackAttempted = false;
        ToolOutputDto? lastFailure = null;
        for (var attemptIndex = 0; attemptIndex < profile.MaxAttempts; attemptIndex++) {
            var output = await ExecuteToolAttemptAsync(tool, currentCall, toolTimeoutSeconds, cancellationToken).ConfigureAwait(false);
            if (!projectionFallbackAttempted
                && TryBuildProjectionArgsFallbackCall(currentCall, output, out var fallbackCall, out var fallbackInfo)) {
                projectionFallbackAttempted = true;
                currentCall = fallbackCall;

                // One deterministic self-heal pass for view-projection failures: retry with bare/default view args.
                var fallbackOutput = await ExecuteToolAttemptAsync(tool, currentCall, toolTimeoutSeconds, cancellationToken).ConfigureAwait(false);
                output = AttachProjectionFallbackMetadata(fallbackOutput, fallbackInfo);
                if (output.Ok is true) {
                    return output;
                }
            }

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

    private static bool TryBuildProjectionArgsFallbackCall(ToolCall call, ToolOutputDto output, out ToolCall fallbackCall,
        out ProjectionFallbackInfo fallbackInfo) {
        fallbackCall = call;
        fallbackInfo = default;

        if (!IsProjectionViewArgumentFailure(output)) {
            return false;
        }

        var fallbackArguments = CloneWithoutProjectionViewArguments(call.Arguments, output, out var removedArguments);
        if (removedArguments.Length == 0) {
            return false;
        }

        var fallbackInput = fallbackArguments is null ? null : JsonLite.Serialize(fallbackArguments);
        fallbackCall = new ToolCall(call.CallId, call.Name, fallbackInput, fallbackArguments, call.Raw);
        fallbackInfo = new ProjectionFallbackInfo(
            RemovedArguments: removedArguments,
            OriginalErrorCode: (output.ErrorCode ?? string.Empty).Trim(),
            OriginalError: CompactProjectionFallbackReason(output.Error));
        return true;
    }

    private static bool IsProjectionViewArgumentFailure(ToolOutputDto output) {
        if (output.Ok is true) {
            return false;
        }

        var text = BuildToolFailureSearchText(output);
        if (text.Length == 0) {
            return false;
        }

        var hasProjectionSignal = text.Contains("columns", StringComparison.OrdinalIgnoreCase)
                                  || text.Contains("sort_by", StringComparison.OrdinalIgnoreCase)
                                  || text.Contains("sort direction", StringComparison.OrdinalIgnoreCase)
                                  || text.Contains("sort_direction", StringComparison.OrdinalIgnoreCase)
                                  || text.Contains("tabular view", StringComparison.OrdinalIgnoreCase)
                                  || text.Contains("projection", StringComparison.OrdinalIgnoreCase)
                                  || text.Contains("projection argument", StringComparison.OrdinalIgnoreCase)
                                  || text.Contains("table view response envelope", StringComparison.OrdinalIgnoreCase);
        if (!hasProjectionSignal) {
            hasProjectionSignal = HasProjectionTopFallbackSignal(output);
            if (!hasProjectionSignal) {
                return false;
            }
        }

        if (string.Equals(output.ErrorCode, "invalid_argument", StringComparison.OrdinalIgnoreCase)
            || string.Equals(output.ErrorCode, "tool_error", StringComparison.OrdinalIgnoreCase)) {
            return true;
        }

        return text.Contains("unsupported value", StringComparison.OrdinalIgnoreCase)
               || text.Contains("must be one of", StringComparison.OrdinalIgnoreCase)
               || text.Contains("invalid", StringComparison.OrdinalIgnoreCase);
    }

    private static JsonObject? CloneWithoutProjectionViewArguments(JsonObject? arguments, ToolOutputDto output, out string[] removedArguments) {
        removedArguments = Array.Empty<string>();
        if (arguments is null || arguments.Count == 0) {
            return arguments;
        }

        if (TryBuildSelectiveProjectionFallbackArguments(arguments, output, out var selectiveFallback, out var selectiveRemoved)) {
            removedArguments = selectiveRemoved;
            return selectiveFallback;
        }

        var removeTopArgument = HasProjectionTopFallbackSignal(output);
        var clone = new JsonObject(StringComparer.Ordinal);
        var removed = new List<string>(ProjectionFormattingArgumentNames.Length + 1);
        var seenRemoved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in arguments) {
            var key = (kv.Key ?? string.Empty).Trim();
            if (key.Length == 0) {
                continue;
            }

            if (IsProjectionFormattingArgumentName(key) || (removeTopArgument && IsProjectionTopArgumentName(key))) {
                if (seenRemoved.Add(key)) {
                    removed.Add(key);
                }
                continue;
            }

            clone.Add(key, kv.Value);
        }

        if (removed.Count == 0
            && !removeTopArgument
            && ShouldDropTopForProjectionEnvelopeFallback(arguments, output)) {
            clone = new JsonObject(StringComparer.Ordinal);
            foreach (var kv in arguments) {
                var key = (kv.Key ?? string.Empty).Trim();
                if (key.Length == 0) {
                    continue;
                }

                if (IsProjectionTopArgumentName(key)) {
                    if (seenRemoved.Add(key)) {
                        removed.Add(key);
                    }
                    continue;
                }

                clone.Add(key, kv.Value);
            }
        }

        removedArguments = removed.Count == 0 ? Array.Empty<string>() : removed.ToArray();
        return clone;
    }

    private static bool TryBuildSelectiveProjectionFallbackArguments(
        JsonObject arguments,
        ToolOutputDto output,
        out JsonObject fallbackArguments,
        out string[] removedArguments) {
        fallbackArguments = arguments;
        removedArguments = Array.Empty<string>();
        if (!TryReadProjectionAvailableColumns(output, out var availableColumns) || availableColumns.Count == 0) {
            return false;
        }

        var removeColumns = false;
        JsonArray? columnsReplacement = null;
        if (TryGetArgumentValue(arguments, ProjectionColumnsArgumentName, out var rawColumns)) {
            if (rawColumns?.AsArray() is JsonArray columnsArray) {
                var requestedColumns = ToolArgs.ReadDistinctStringArray(columnsArray);
                if (requestedColumns.Count > 0) {
                    var keptColumns = requestedColumns
                        .Where(availableColumns.Contains)
                        .ToArray();
                    if (keptColumns.Length < requestedColumns.Count) {
                        if (keptColumns.Length == 0) {
                            removeColumns = true;
                        } else {
                            columnsReplacement = new JsonArray().AddRange(keptColumns);
                        }
                    }
                }
            } else if (BuildToolFailureSearchText(output).Contains("columns", StringComparison.OrdinalIgnoreCase)) {
                // When columns is malformed/non-array, remove it as a safe recovery step.
                removeColumns = true;
            }
        }

        var removeSortBy = false;
        if (TryGetArgumentString(arguments, ProjectionSortByArgumentName, out var sortByValue)
            && sortByValue.Length > 0
            && !availableColumns.Contains(sortByValue)) {
            removeSortBy = true;
        }

        var removeSortDirection = removeSortBy && HasArgument(arguments, ProjectionSortDirectionArgumentName);
        if (!removeSortDirection
            && HasArgument(arguments, ProjectionSortDirectionArgumentName)
            && IsSortDirectionValidationFailure(output)) {
            removeSortDirection = true;
        }

        if (!removeColumns && columnsReplacement is null && !removeSortBy && !removeSortDirection) {
            return false;
        }

        var clone = new JsonObject(StringComparer.Ordinal);
        var removed = new List<string>(3);
        var seenRemoved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in arguments) {
            var key = (kv.Key ?? string.Empty).Trim();
            if (key.Length == 0) {
                continue;
            }

            if (IsProjectionColumnsArgumentName(key)) {
                if (removeColumns) {
                    AddRemovedArgument(removed, seenRemoved, ProjectionColumnsArgumentName);
                    continue;
                }
                if (columnsReplacement is not null) {
                    clone.Add(key, columnsReplacement);
                    AddRemovedArgument(removed, seenRemoved, ProjectionColumnsArgumentName);
                    continue;
                }
            }

            if (IsProjectionSortByArgumentName(key) && removeSortBy) {
                AddRemovedArgument(removed, seenRemoved, ProjectionSortByArgumentName);
                continue;
            }

            if (IsProjectionSortDirectionArgumentName(key) && removeSortDirection) {
                AddRemovedArgument(removed, seenRemoved, ProjectionSortDirectionArgumentName);
                continue;
            }

            clone.Add(key, kv.Value);
        }

        removedArguments = removed.Count == 0 ? Array.Empty<string>() : removed.ToArray();
        fallbackArguments = clone;
        return removedArguments.Length > 0;
    }

    private static void AddRemovedArgument(List<string> target, HashSet<string> seen, string argumentName) {
        if (target is null || seen is null || string.IsNullOrWhiteSpace(argumentName)) {
            return;
        }

        var normalized = argumentName.Trim();
        if (seen.Add(normalized)) {
            target.Add(normalized);
        }
    }

    private static bool TryReadProjectionAvailableColumns(ToolOutputDto output, out HashSet<string> availableColumns) {
        availableColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var observed = TryAppendAvailableProjectionColumns(output.MetaJson, availableColumns);
        observed |= TryAppendAvailableProjectionColumns(output.Output, availableColumns);
        return observed;
    }

    private static bool TryAppendAvailableProjectionColumns(string? rawJson, HashSet<string> destination) {
        if (destination is null || string.IsNullOrWhiteSpace(rawJson)) {
            return false;
        }

        JsonObject? obj;
        try {
            obj = JsonLite.Parse(rawJson)?.AsObject();
        } catch {
            return false;
        }

        if (obj is null) {
            return false;
        }

        var before = destination.Count;
        AppendAvailableColumns(obj, destination);
        if (obj.GetObject("meta") is JsonObject metaObj) {
            AppendAvailableColumns(metaObj, destination);
        }
        if (obj.GetObject("failure") is JsonObject failureObj) {
            AppendAvailableColumns(failureObj, destination);
            if (failureObj.GetObject("meta") is JsonObject failureMetaObj) {
                AppendAvailableColumns(failureMetaObj, destination);
            }
        }
        return destination.Count > before;
    }

    private static void AppendAvailableColumns(JsonObject obj, HashSet<string> destination) {
        var availableColumnsArray = obj.GetArray("available_columns");
        if ((availableColumnsArray is null || availableColumnsArray.Count == 0)
            && obj.GetArray("availableColumns") is JsonArray camelCaseColumns) {
            availableColumnsArray = camelCaseColumns;
        }

        if (availableColumnsArray is null || availableColumnsArray.Count == 0) {
            return;
        }

        for (var i = 0; i < availableColumnsArray.Count; i++) {
            var value = availableColumnsArray[i].AsString();
            if (string.IsNullOrWhiteSpace(value)) {
                continue;
            }

            destination.Add(value.Trim());
        }
    }

    private static bool TryGetArgumentValue(JsonObject arguments, string argumentName, out JsonValue? value) {
        value = null;
        if (arguments is null || string.IsNullOrWhiteSpace(argumentName)) {
            return false;
        }

        foreach (var kv in arguments) {
            if (string.Equals((kv.Key ?? string.Empty).Trim(), argumentName, StringComparison.OrdinalIgnoreCase)) {
                value = kv.Value;
                return true;
            }
        }

        return false;
    }

    private static bool TryGetArgumentString(JsonObject arguments, string argumentName, out string value) {
        value = string.Empty;
        if (!TryGetArgumentValue(arguments, argumentName, out var rawValue)) {
            return false;
        }

        value = (rawValue?.AsString() ?? string.Empty).Trim();
        return true;
    }

    private static bool HasArgument(JsonObject arguments, string argumentName) {
        return TryGetArgumentValue(arguments, argumentName, out _);
    }

    private static bool IsSortDirectionValidationFailure(ToolOutputDto output) {
        var text = BuildToolFailureSearchText(output);
        if (text.Length == 0) {
            return false;
        }

        if (!text.Contains("sort_direction", StringComparison.OrdinalIgnoreCase)
            && !text.Contains("sort direction", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        return text.Contains("asc", StringComparison.OrdinalIgnoreCase)
               || text.Contains("desc", StringComparison.OrdinalIgnoreCase)
               || text.Contains("must be one of", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsProjectionColumnsArgumentName(string argumentName) =>
        string.Equals(argumentName, ProjectionColumnsArgumentName, StringComparison.OrdinalIgnoreCase);

    private static bool IsProjectionSortByArgumentName(string argumentName) =>
        string.Equals(argumentName, ProjectionSortByArgumentName, StringComparison.OrdinalIgnoreCase);

    private static bool IsProjectionSortDirectionArgumentName(string argumentName) =>
        string.Equals(argumentName, ProjectionSortDirectionArgumentName, StringComparison.OrdinalIgnoreCase);

    private static bool IsProjectionFormattingArgumentName(string argumentName) {
        for (var i = 0; i < ProjectionFormattingArgumentNames.Length; i++) {
            if (string.Equals(argumentName, ProjectionFormattingArgumentNames[i], StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }
        return false;
    }

    private static bool IsProjectionTopArgumentName(string argumentName) =>
        string.Equals(argumentName, ProjectionTopArgumentName, StringComparison.OrdinalIgnoreCase);

    private static bool HasProjectionTopFallbackSignal(ToolOutputDto output) {
        var text = BuildToolFailureSearchText(output);
        if (text.Length == 0) {
            return false;
        }

        return TopValidationRegex.IsMatch(text);
    }

    private static bool ShouldDropTopForProjectionEnvelopeFallback(JsonObject arguments, ToolOutputDto output) {
        if (arguments is null || !HasArgument(arguments, ProjectionTopArgumentName)) {
            return false;
        }

        var text = BuildToolFailureSearchText(output);
        if (text.Length == 0) {
            return false;
        }

        return text.Contains("table view response envelope", StringComparison.OrdinalIgnoreCase)
               || text.Contains("projection argument", StringComparison.OrdinalIgnoreCase)
               || text.Contains("tabular view", StringComparison.OrdinalIgnoreCase);
    }

    private static ToolOutputDto AttachProjectionFallbackMetadata(ToolOutputDto output, ProjectionFallbackInfo info) {
        if (string.IsNullOrWhiteSpace(output.Output)) {
            return output;
        }

        JsonObject? envelope = null;
        try {
            envelope = JsonLite.Parse(output.Output)?.AsObject();
        } catch {
            envelope = null;
        }

        if (envelope is null) {
            return output;
        }

        var meta = envelope.GetObject("meta") ?? new JsonObject(StringComparer.Ordinal);
        meta.Add("projection_fallback_applied", true);

        var removed = new JsonArray();
        for (var i = 0; i < info.RemovedArguments.Length; i++) {
            if (!string.IsNullOrWhiteSpace(info.RemovedArguments[i])) {
                removed.Add(info.RemovedArguments[i].Trim());
            }
        }
        meta.Add("projection_fallback_removed_args", removed);
        if (!string.IsNullOrWhiteSpace(info.OriginalErrorCode)) {
            meta.Add("projection_fallback_reason_code", info.OriginalErrorCode);
        }
        if (!string.IsNullOrWhiteSpace(info.OriginalError)) {
            meta.Add("projection_fallback_reason", info.OriginalError);
        }
        envelope.Add("meta", meta);

        const string fallbackHint = "Projection arguments were adjusted after a view-argument failure.";
        if (envelope.GetArray("hints") is JsonArray rootHints) {
            AddDistinctJsonString(rootHints, fallbackHint);
        } else {
            envelope.Add("hints", new JsonArray().Add(fallbackHint));
        }

        if (envelope.GetObject("failure") is JsonObject failure) {
            if (failure.GetArray("hints") is JsonArray failureHints) {
                AddDistinctJsonString(failureHints, fallbackHint);
            } else {
                failure.Add("hints", new JsonArray().Add(fallbackHint));
            }
            envelope.Add("failure", failure);
        }

        return BuildToolOutputDto(output.CallId, JsonLite.Serialize(envelope));
    }

    private static void AddDistinctJsonString(JsonArray target, string value) {
        if (target is null || string.IsNullOrWhiteSpace(value)) {
            return;
        }

        var normalized = value.Trim();
        for (var i = 0; i < target.Count; i++) {
            var existing = target[i]?.AsString();
            if (string.Equals(existing, normalized, StringComparison.OrdinalIgnoreCase)) {
                return;
            }
        }

        target.Add(normalized);
    }

    private static string CompactProjectionFallbackReason(string? errorText) {
        var compact = CompactFailureText(errorText);
        const int maxReasonLength = 320;
        if (compact.Length <= maxReasonLength) {
            return compact;
        }
        return compact[..maxReasonLength].TrimEnd();
    }

    private static bool WasProjectionFallbackApplied(ToolOutputDto output) {
        if (output is null) {
            return false;
        }

        return TryReadProjectionFallbackApplied(output.MetaJson, out var appliedFromMeta) && appliedFromMeta
               || TryReadProjectionFallbackApplied(output.Output, out var appliedFromOutput) && appliedFromOutput;
    }

    private static bool TryReadProjectionFallbackApplied(string? rawJson, out bool applied) {
        applied = false;
        if (string.IsNullOrWhiteSpace(rawJson)) {
            return false;
        }

        JsonObject? obj;
        try {
            obj = JsonLite.Parse(rawJson)?.AsObject();
        } catch {
            return false;
        }

        if (obj is null) {
            return false;
        }

        if (TryReadProjectionFallbackFlag(obj, out applied)) {
            return true;
        }

        if (obj.GetObject("meta") is JsonObject meta && TryReadProjectionFallbackFlag(meta, out applied)) {
            return true;
        }

        return false;
    }

    private static bool TryReadProjectionFallbackFlag(JsonObject obj, out bool applied) {
        applied = false;
        if (!obj.TryGetValue("projection_fallback_applied", out var raw)
            || raw is null
            || raw.Kind != IntelligenceX.Json.JsonValueKind.Boolean) {
            return false;
        }

        applied = raw.AsBoolean();
        return true;
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

    private readonly record struct ProjectionFallbackInfo(string[] RemovedArguments, string OriginalErrorCode, string OriginalError);

}
