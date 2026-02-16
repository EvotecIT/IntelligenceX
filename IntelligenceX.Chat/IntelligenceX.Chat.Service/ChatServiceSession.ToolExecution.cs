using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
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
    private const int MaxParallelToolConcurrencyCap = 6;
    private const int MinParallelToolConcurrencyCap = 2;
    private const int MaxLowConcurrencyRecoveryCalls = 3;
    private static readonly TimeSpan ToolHeartbeatInterval = TimeSpan.FromSeconds(8);

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

        if (TryShouldRetryWithoutToolsFromStructuredError(ex, out var shouldRetryStructured)) {
            return shouldRetryStructured;
        }

        var message = ex.Message ?? string.Empty;
        if (message.Length == 0) {
            return false;
        }

        if (LooksLikeToolSchemaValidationMessage(message)) {
            return true;
        }

        // Compatibility fallback for local providers that return plain-text context-window failures.
        return LooksLikeContextWindowFailureMessage(message);
    }

    private static bool TryShouldRetryWithoutToolsFromStructuredError(Exception ex, out bool shouldRetry) {
        shouldRetry = false;
        if (ex is null) {
            return false;
        }

        var pending = new Stack<Exception>();
        pending.Push(ex);
        while (pending.Count > 0) {
            var current = pending.Pop();

            if (TryReadNativeErrorDiagnostics(current, out var code, out var param)) {
                if (LooksLikeToolSchemaValidationCode(code, param) || LooksLikeContextWindowFailureCode(code)) {
                    shouldRetry = true;
                    return true;
                }

                // Structured diagnostic was present and explicitly non-retryable.
                shouldRetry = false;
                return true;
            }

            if (current is AggregateException aggregate) {
                foreach (var inner in aggregate.InnerExceptions) {
                    if (inner is not null) {
                        pending.Push(inner);
                    }
                }
            }

            if (current.InnerException is not null) {
                pending.Push(current.InnerException);
            }
        }

        return false;
    }

    private static bool TryReadNativeErrorDiagnostics(Exception ex, out string errorCode, out string errorParam) {
        errorCode = string.Empty;
        errorParam = string.Empty;
        if (ex is null) {
            return false;
        }

        if (!(ex.Data?["openai:native_transport"] is bool marker && marker)) {
            return false;
        }

        errorCode = ((ex.Data?["openai:error_code"] as string) ?? string.Empty).Trim();
        errorParam = ((ex.Data?["openai:error_param"] as string) ?? string.Empty).Trim();
        return errorCode.Length > 0 || errorParam.Length > 0;
    }

    private static bool LooksLikeToolSchemaValidationCode(string errorCode, string errorParam) {
        var code = (errorCode ?? string.Empty).Trim();
        if (code.Length == 0) {
            return false;
        }

        var hasToolsPath = errorParam.IndexOf("tools", StringComparison.OrdinalIgnoreCase) >= 0;
        if (!hasToolsPath) {
            return false;
        }

        if (code.IndexOf("unknown_parameter", StringComparison.OrdinalIgnoreCase) >= 0) {
            return true;
        }

        var missingRequiredParameter =
            code.IndexOf("missing_required_parameter", StringComparison.OrdinalIgnoreCase) >= 0
            || code.IndexOf("required_parameter_missing", StringComparison.OrdinalIgnoreCase) >= 0;
        if (!missingRequiredParameter) {
            return false;
        }

        return errorParam.IndexOf(".name", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool LooksLikeContextWindowFailureCode(string errorCode) {
        var code = (errorCode ?? string.Empty).Trim();
        if (code.Length == 0) {
            return false;
        }

        return code.IndexOf("context_length", StringComparison.OrdinalIgnoreCase) >= 0
               || code.IndexOf("max_context", StringComparison.OrdinalIgnoreCase) >= 0
               || code.IndexOf("prompt_too_long", StringComparison.OrdinalIgnoreCase) >= 0
               || code.IndexOf("too_many_tokens", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool LooksLikeToolSchemaValidationMessage(string message) {
        var text = message ?? string.Empty;
        if (text.Length == 0) {
            return false;
        }

        return text.IndexOf("missing required parameter", StringComparison.OrdinalIgnoreCase) >= 0
               && text.IndexOf("tools", StringComparison.OrdinalIgnoreCase) >= 0
               && text.IndexOf(".name", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool LooksLikeContextWindowFailureMessage(string message) {
        var text = message ?? string.Empty;
        if (text.Length == 0) {
            return false;
        }

        return text.IndexOf("cannot truncate prompt with n_keep", StringComparison.OrdinalIgnoreCase) >= 0
               || text.IndexOf("n_ctx", StringComparison.OrdinalIgnoreCase) >= 0
               || text.IndexOf("context length", StringComparison.OrdinalIgnoreCase) >= 0
               || text.IndexOf("context window", StringComparison.OrdinalIgnoreCase) >= 0
               || text.IndexOf("maximum context length", StringComparison.OrdinalIgnoreCase) >= 0
               || text.IndexOf("prompt too long", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private async Task<IReadOnlyList<ToolOutputDto>> ExecuteToolsAsync(StreamWriter writer, string requestId, string threadId, IReadOnlyList<ToolCall> calls,
        bool parallel, bool allowMutatingParallel, IReadOnlyDictionary<string, bool>? mutatingToolHintsByName, int toolTimeoutSeconds,
        CancellationToken cancellationToken) {
        var hasMutatingCalls = HasMutatingToolCallsWithHints(calls, mutatingToolHintsByName, out var mutatingToolNames);
        if (parallel && calls.Count > 1 && hasMutatingCalls && !allowMutatingParallel) {
            parallel = false;
            await TryWriteStatusAsync(
                    writer,
                    requestId,
                    threadId,
                    status: "tool_parallel_safety_off",
                    message: BuildToolParallelSafetyOffMessage(mutatingToolNames))
                .ConfigureAwait(false);
        } else if (parallel && calls.Count > 1 && allowMutatingParallel && hasMutatingCalls) {
            await TryWriteStatusAsync(
                    writer,
                    requestId,
                    threadId,
                    status: "tool_parallel_forced",
                    message: BuildToolParallelForcedMessage(mutatingToolNames))
                .ConfigureAwait(false);
        }

        if (!parallel || calls.Count <= 1) {
            var outputs = new List<ToolOutputDto>(calls.Count);
            foreach (var call in calls) {
                var output = await ExecuteToolWithStatusAsync(writer, requestId, threadId, call, toolTimeoutSeconds, cancellationToken)
                    .ConfigureAwait(false);
                outputs.Add(output);
            }
            return outputs;
        }

        var maxConcurrency = ResolveParallelToolConcurrency(calls.Count);
        await TryWriteStatusAsync(
                writer,
                requestId,
                threadId,
                status: "tool_batch_started",
                message: BuildToolBatchStartedMessage(calls.Count, maxConcurrency))
            .ConfigureAwait(false);

        var completed = 0;
        var failed = 0;
        using var gate = new SemaphoreSlim(maxConcurrency, maxConcurrency);

        var tasks = new Task<ToolOutputDto>[calls.Count];
        for (var i = 0; i < calls.Count; i++) {
            var idx = i;
            tasks[i] = ExecuteParallelToolCallAsync(idx);
        }

        var outputsInCallOrder = await Task.WhenAll(tasks).ConfigureAwait(false);
        var recoveryIndexes = CollectLowConcurrencyRecoveryIndexesWithHints(calls, outputsInCallOrder, mutatingToolHintsByName);
        if (maxConcurrency > 1 && recoveryIndexes.Length > 0) {
            await TryWriteStatusAsync(
                    writer,
                    requestId,
                    threadId,
                    status: "tool_batch_recovering",
                    message: BuildToolBatchRecoveringMessage(recoveryIndexes.Length, calls.Count))
                .ConfigureAwait(false);

            for (var i = 0; i < recoveryIndexes.Length; i++) {
                var index = recoveryIndexes[i];
                outputsInCallOrder[index] =
                    await ExecuteToolWithStatusAsync(writer, requestId, threadId, calls[index], toolTimeoutSeconds, cancellationToken)
                        .ConfigureAwait(false);
            }

            await TryWriteStatusAsync(
                    writer,
                    requestId,
                    threadId,
                    status: "tool_batch_recovered",
                    message: BuildToolBatchRecoveredMessage(
                        recoveryIndexes.Length,
                        CountFailedToolOutputs(outputsInCallOrder)))
                .ConfigureAwait(false);
        }

        await TryWriteStatusAsync(
                writer,
                requestId,
                threadId,
                status: "tool_batch_completed",
                message: BuildToolBatchCompletedMessage(calls.Count, maxConcurrency, CountFailedToolOutputs(outputsInCallOrder)))
            .ConfigureAwait(false);
        return outputsInCallOrder;

        async Task<ToolOutputDto> ExecuteParallelToolCallAsync(int index) {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            ToolOutputDto output;
            try {
                var call = calls[index];
                output = await ExecuteToolWithStatusAsync(writer, requestId, threadId, call, toolTimeoutSeconds, cancellationToken)
                    .ConfigureAwait(false);
            } finally {
                gate.Release();
            }

            if (output.Ok is false) {
                Interlocked.Increment(ref failed);
            }

            var completedCount = Interlocked.Increment(ref completed);
            await TryWriteStatusAsync(
                    writer,
                    requestId,
                    threadId,
                    status: "tool_batch_progress",
                    message: BuildToolBatchProgressMessage(
                        completedCount,
                        calls.Count,
                        maxConcurrency,
                        Math.Max(0, Volatile.Read(ref failed))))
                .ConfigureAwait(false);
            return output;
        }
    }

    private static IReadOnlyDictionary<string, bool> BuildMutatingToolHintsByName(IReadOnlyList<ToolDefinition> definitions) {
        if (definitions.Count == 0) {
            return new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        }

        var hints = new Dictionary<string, bool>(definitions.Count, StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < definitions.Count; i++) {
            var definition = definitions[i];
            if (definition is null || string.IsNullOrWhiteSpace(definition.Name)) {
                continue;
            }

            var capability = ClassifyMutatingCapabilityFromDefinition(definition);
            if (!capability.HasValue) {
                continue;
            }

            hints[definition.Name.Trim()] = capability.Value;
        }

        return hints;
    }

    private static bool? ClassifyMutatingCapabilityFromDefinition(ToolDefinition definition) {
        if (TryClassifyMutatingCapabilityFromTags(definition.Tags, out var fromTags)) {
            return fromTags;
        }

        if (TryClassifyMutatingCapabilityFromSchema(definition.Parameters, out var fromSchema)) {
            return fromSchema;
        }

        return null;
    }

    private static bool TryClassifyMutatingCapabilityFromTags(IReadOnlyList<string> tags, out bool isMutating) {
        isMutating = false;
        if (tags is not { Count: > 0 }) {
            return false;
        }

        var sawReadOnlyHint = false;
        var sawMutatingHint = false;
        for (var i = 0; i < tags.Count; i++) {
            var token = NormalizeMutationHintToken(tags[i]);
            if (token.Length == 0) {
                continue;
            }

            if (IsMutatingMetadataToken(token)) {
                sawMutatingHint = true;
                continue;
            }

            if (IsReadOnlyMetadataToken(token)) {
                sawReadOnlyHint = true;
            }
        }

        if (!sawReadOnlyHint && !sawMutatingHint) {
            return false;
        }

        isMutating = sawMutatingHint;
        return true;
    }

    private static bool TryClassifyMutatingCapabilityFromSchema(JsonObject? parameters, out bool isMutating) {
        isMutating = false;
        var properties = parameters?.GetObject("properties");
        if (properties is null || properties.Count == 0) {
            return false;
        }

        var sawReadOnlyHint = false;
        var sawMutatingHint = false;
        foreach (var property in properties) {
            var keyToken = NormalizeMutationHintToken(property.Key);
            if (keyToken.Length > 0) {
                sawMutatingHint |= IsMutatingMetadataToken(keyToken);
                sawReadOnlyHint |= IsReadOnlyMetadataToken(keyToken);
            }

            var propertySchema = property.Value?.AsObject();
            var enumValues = propertySchema?.GetArray("enum");
            if (enumValues is null || enumValues.Count == 0) {
                continue;
            }

            foreach (var enumValue in enumValues) {
                var enumToken = NormalizeMutationHintToken(enumValue?.AsString());
                if (enumToken.Length == 0) {
                    continue;
                }

                sawMutatingHint |= IsMutatingMetadataToken(enumToken);
                sawReadOnlyHint |= IsReadOnlyMetadataToken(enumToken);
            }
        }

        if (!sawReadOnlyHint && !sawMutatingHint) {
            return false;
        }

        isMutating = sawMutatingHint;
        return true;
    }

    private static bool IsMutatingMetadataToken(string token) {
        if (token.Contains("read_write", StringComparison.Ordinal)
            || token.Contains("readwrite", StringComparison.Ordinal)
            || token.Contains("allow_write", StringComparison.Ordinal)
            || token.Contains("danger", StringComparison.Ordinal)
            || token.Contains("mutat", StringComparison.Ordinal)
            || token.Contains("state_change", StringComparison.Ordinal)
            || token.Contains("destruct", StringComparison.Ordinal)) {
            return true;
        }

        return false;
    }

    private static bool IsReadOnlyMetadataToken(string token) {
        return token.Contains("read_only", StringComparison.Ordinal)
               || token.Contains("readonly", StringComparison.Ordinal)
               || token.Contains("safe_read", StringComparison.Ordinal)
               || token.Contains("query_only", StringComparison.Ordinal)
               || token.Equals("inventory", StringComparison.Ordinal)
               || token.Equals("diagnostic", StringComparison.Ordinal);
    }

    private static string NormalizeMutationHintToken(string? value) {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0) {
            return string.Empty;
        }

        var chars = new char[normalized.Length];
        var len = 0;
        var previousWasSeparator = false;
        for (var i = 0; i < normalized.Length; i++) {
            var ch = normalized[i];
            if (char.IsLetterOrDigit(ch)) {
                chars[len++] = ch;
                previousWasSeparator = false;
                continue;
            }

            if (previousWasSeparator) {
                continue;
            }

            chars[len++] = '_';
            previousWasSeparator = true;
        }

        if (len == 0) {
            return string.Empty;
        }

        return new string(chars, 0, len).Trim('_');
    }

    private static bool HasMutatingToolCallsWithHints(IReadOnlyList<ToolCall> calls, IReadOnlyDictionary<string, bool>? mutatingToolHintsByName,
        out string[] mutatingToolNames) {
        mutatingToolNames = Array.Empty<string>();
        if (calls.Count == 0) {
            return false;
        }

        var mutating = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < calls.Count; i++) {
            if (!IsLikelyMutatingToolCallWithHints(calls[i], mutatingToolHintsByName)) {
                continue;
            }

            var name = (calls[i].Name ?? string.Empty).Trim();
            if (name.Length == 0) {
                name = "tool";
            }

            if (seen.Add(name)) {
                mutating.Add(name);
            }
        }

        if (mutating.Count == 0) {
            return false;
        }

        mutatingToolNames = mutating.ToArray();
        return true;
    }

    private static bool IsLikelyMutatingToolCallWithHints(ToolCall call, IReadOnlyDictionary<string, bool>? mutatingToolHintsByName) {
        var toolName = (call.Name ?? string.Empty).Trim();
        if (toolName.Length > 0 && mutatingToolHintsByName is not null && mutatingToolHintsByName.TryGetValue(toolName, out var isMutating)) {
            return isMutating;
        }

        return false;
    }

    private static string BuildToolParallelSafetyOffMessage(string[] mutatingToolNames) {
        if (mutatingToolNames.Length == 0) {
            return "Parallel execution paused for mutating tools; running calls sequentially.";
        }

        var listed = string.Join(", ", mutatingToolNames.Take(3));
        var suffix = mutatingToolNames.Length > 3 ? ", ..." : string.Empty;
        return $"Parallel execution paused for mutating tools ({listed}{suffix}); running calls sequentially.";
    }

    private static string BuildToolParallelForcedMessage(string[] mutatingToolNames) {
        if (mutatingToolNames.Length == 0) {
            return "Parallel mode forced by caller, including mutating tools.";
        }

        var listed = string.Join(", ", mutatingToolNames.Take(3));
        var suffix = mutatingToolNames.Length > 3 ? ", ..." : string.Empty;
        return $"Parallel mode forced by caller for mutating tools ({listed}{suffix}).";
    }

    private static int[] CollectLowConcurrencyRecoveryIndexesWithHints(IReadOnlyList<ToolCall> calls, IReadOnlyList<ToolOutputDto> outputs,
        IReadOnlyDictionary<string, bool>? mutatingToolHintsByName) {
        var max = Math.Min(calls.Count, outputs.Count);
        if (max <= 0) {
            return Array.Empty<int>();
        }

        var list = new List<int>(Math.Min(max, MaxLowConcurrencyRecoveryCalls));
        for (var i = 0; i < max; i++) {
            if (!ShouldReplayToolCallAtLowConcurrencyWithHints(calls[i], outputs[i], mutatingToolHintsByName)) {
                continue;
            }

            list.Add(i);
            if (list.Count >= MaxLowConcurrencyRecoveryCalls) {
                break;
            }
        }

        return list.Count == 0 ? Array.Empty<int>() : list.ToArray();
    }

    private static bool ShouldReplayToolCallAtLowConcurrencyWithHints(ToolCall call, ToolOutputDto output,
        IReadOnlyDictionary<string, bool>? mutatingToolHintsByName) {
        if (output.Ok is true) {
            return false;
        }

        if (IsLikelyMutatingToolCallWithHints(call, mutatingToolHintsByName)) {
            return false;
        }

        if (string.Equals(output.ErrorCode, "tool_not_registered", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        if (output.IsTransient is true) {
            return true;
        }

        var errorCode = (output.ErrorCode ?? string.Empty).Trim();
        if (errorCode.Contains("timeout", StringComparison.OrdinalIgnoreCase)
            || errorCode.Contains("transport", StringComparison.OrdinalIgnoreCase)
            || errorCode.Contains("transient", StringComparison.OrdinalIgnoreCase)
            || errorCode.Contains("unavailable", StringComparison.OrdinalIgnoreCase)) {
            return true;
        }

        return false;
    }

    private static int[] CollectLowConcurrencyRecoveryIndexes(IReadOnlyList<ToolCall> calls, IReadOnlyList<ToolOutputDto> outputs) {
        return CollectLowConcurrencyRecoveryIndexesWithHints(calls, outputs, mutatingToolHintsByName: null);
    }

    private static bool ShouldReplayToolCallAtLowConcurrency(ToolCall call, ToolOutputDto output) {
        return ShouldReplayToolCallAtLowConcurrencyWithHints(call, output, mutatingToolHintsByName: null);
    }

    private static bool IsLikelyMutatingToolName(string? toolName) {
        return false;
    }

    private static bool HasLikelyMutatingToolCalls(IReadOnlyList<ToolCall> calls) {
        return HasMutatingToolCallsWithHints(calls, mutatingToolHintsByName: null, out _);
    }

    private static int ResolveParallelToolConcurrency(int callCount) {
        var normalizedCount = Math.Max(0, callCount);
        if (normalizedCount <= 1) {
            return 1;
        }

        var processorCap = Math.Clamp(Environment.ProcessorCount, MinParallelToolConcurrencyCap, MaxParallelToolConcurrencyCap);
        return Math.Clamp(normalizedCount, 1, processorCap);
    }

    private static string BuildToolBatchStartedMessage(int totalCalls, int maxConcurrency) {
        var total = Math.Max(0, totalCalls);
        var concurrency = Math.Max(1, maxConcurrency);
        if (total <= 1) {
            return "Running tool call...";
        }

        if (concurrency >= total) {
            return $"Running {total} tool calls in parallel.";
        }

        return $"Running {total} tool calls in parallel batches ({concurrency} at a time).";
    }

    private static string BuildToolBatchProgressMessage(int completedCalls, int totalCalls, int maxConcurrency, int failedCalls) {
        var total = Math.Max(0, totalCalls);
        var completed = Math.Clamp(completedCalls, 0, total);
        var failed = Math.Clamp(failedCalls, 0, completed);
        var concurrency = Math.Max(1, maxConcurrency);
        if (failed <= 0) {
            return $"Tool batch progress: {completed}/{total} complete ({concurrency} max parallel).";
        }

        return $"Tool batch progress: {completed}/{total} complete ({failed} failed, {concurrency} max parallel).";
    }

    private static string BuildToolBatchCompletedMessage(int totalCalls, int maxConcurrency, int failedCalls) {
        var total = Math.Max(0, totalCalls);
        var failed = Math.Clamp(failedCalls, 0, total);
        var concurrency = Math.Max(1, maxConcurrency);
        if (failed <= 0) {
            return $"Tool batch finished: {total}/{total} complete ({concurrency} max parallel).";
        }

        return $"Tool batch finished: {total}/{total} complete ({failed} failed, {concurrency} max parallel).";
    }

    private static string BuildToolBatchRecoveringMessage(int recoveredCalls, int totalCalls) {
        var recovered = Math.Max(0, recoveredCalls);
        var total = Math.Max(0, totalCalls);
        return $"Retrying {recovered} transient tool failure(s) at low concurrency ({total} total calls).";
    }

    private static string BuildToolBatchRecoveredMessage(int recoveredCalls, int remainingFailures) {
        var recovered = Math.Max(0, recoveredCalls);
        var failures = Math.Max(0, remainingFailures);
        if (failures <= 0) {
            return $"Low-concurrency recovery completed ({recovered} retried).";
        }

        return $"Low-concurrency recovery completed ({recovered} retried, {failures} failure(s) remain).";
    }

    private static string BuildToolHeartbeatMessage(string? toolName, int elapsedSeconds) {
        var label = (toolName ?? string.Empty).Trim();
        if (label.Length == 0) {
            label = "tool";
        }

        return $"Still running {label} ({Math.Max(1, elapsedSeconds)}s)...";
    }

    private static int CountFailedToolOutputs(IReadOnlyList<ToolOutputDto> outputs) {
        if (outputs.Count == 0) {
            return 0;
        }

        var failed = 0;
        for (var i = 0; i < outputs.Count; i++) {
            if (outputs[i].Ok is false) {
                failed++;
            }
        }

        return failed;
    }

    private async Task<ToolOutputDto> ExecuteToolWithStatusAsync(StreamWriter writer, string requestId, string threadId, ToolCall call,
        int toolTimeoutSeconds, CancellationToken cancellationToken) {
        await TryWriteStatusAsync(writer, requestId, threadId, status: "tool_running", toolName: call.Name, toolCallId: call.CallId)
            .ConfigureAwait(false);
        var sw = Stopwatch.StartNew();
        var executeTask = ExecuteToolAsync(call, toolTimeoutSeconds, cancellationToken);
        var cancellationTask = Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        while (!executeTask.IsCompleted) {
            // Use a non-cancelable heartbeat delay and a separate cancellation task.
            // This avoids a cancellation race where Task.Delay(..., token) can complete immediately
            // and trigger a tight heartbeat loop while the tool task is still finishing.
            var heartbeatDelayTask = Task.Delay(ToolHeartbeatInterval);
            var completedTask = await Task.WhenAny(executeTask, heartbeatDelayTask, cancellationTask).ConfigureAwait(false);
            if (ReferenceEquals(completedTask, executeTask) || ReferenceEquals(completedTask, cancellationTask)) {
                break;
            }

            await TryWriteStatusAsync(
                    writer,
                    requestId,
                    threadId,
                    status: "tool_heartbeat",
                    toolName: call.Name,
                    toolCallId: call.CallId,
                    durationMs: sw.ElapsedMilliseconds,
                    message: BuildToolHeartbeatMessage(call.Name, Math.Max(1, (int)Math.Round(sw.Elapsed.TotalSeconds))))
                .ConfigureAwait(false);
        }

        var output = await executeTask.ConfigureAwait(false);
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
        if (output.IsTransient is true && string.IsNullOrWhiteSpace(output.ErrorCode)) {
            // Some providers/tools mark failures transient without a structured error code.
            // Keep retry behavior resilient for those adapters when retry slots remain.
            return true;
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
        return output.IsTransient is true;
    }

    private static bool IsLikelyPermanentToolFailure(ToolOutputDto output) {
        var code = (output.ErrorCode ?? string.Empty).Trim();
        if (code.Length == 0) {
            return false;
        }

        return code.Contains("invalid", StringComparison.OrdinalIgnoreCase)
               || code.Contains("argument", StringComparison.OrdinalIgnoreCase)
               || code.Contains("validation", StringComparison.OrdinalIgnoreCase)
               || code.Contains("permission", StringComparison.OrdinalIgnoreCase)
               || code.Contains("forbidden", StringComparison.OrdinalIgnoreCase)
               || code.Contains("unauthorized", StringComparison.OrdinalIgnoreCase)
               || code.Contains("access_denied", StringComparison.OrdinalIgnoreCase)
               || code.Contains("auth", StringComparison.OrdinalIgnoreCase);
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

        var compact = CollapseWhitespace(rawText);
        const int maxLength = 768;
        return compact.Length <= maxLength ? compact : compact[..maxLength];
    }

    private static bool IsLikelyTransientToolException(Exception ex) {
        if (ex is OperationCanceledException) {
            return false;
        }

        if (HasKnownTransientExceptionInChain(ex)) {
            return true;
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
