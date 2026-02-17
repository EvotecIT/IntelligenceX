using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
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
        var started = 0;
        using var gate = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        var batchStopwatch = Stopwatch.StartNew();
        using var batchHeartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var tasks = new Task<ToolOutputDto>[calls.Count];
        for (var i = 0; i < calls.Count; i++) {
            var idx = i;
            tasks[i] = ExecuteParallelToolCallAsync(idx);
        }

        var batchHeartbeatTask = RunToolBatchHeartbeatLoopAsync(
            writer,
            requestId,
            threadId,
            totalCalls: calls.Count,
            maxConcurrency,
            batchStopwatch,
            () => Math.Max(0, Volatile.Read(ref started)),
            () => Math.Max(0, Volatile.Read(ref completed)),
            () => Math.Max(0, Volatile.Read(ref failed)),
            batchHeartbeatCts.Token);

        ToolOutputDto[]? outputsInCallOrder = null;
        ExceptionDispatchInfo? batchFailure = null;
        ExceptionDispatchInfo? heartbeatFailure = null;
        try {
            outputsInCallOrder = await Task.WhenAll(tasks).ConfigureAwait(false);
        } catch (Exception ex) {
            batchFailure = ExceptionDispatchInfo.Capture(ex);
        } finally {
            heartbeatFailure = await FinalizeToolBatchHeartbeatAsync(batchHeartbeatTask, batchHeartbeatCts, batchFailure).ConfigureAwait(false);
            batchStopwatch.Stop();
        }

        batchFailure?.Throw();
        heartbeatFailure?.Throw();
        if (outputsInCallOrder is null) {
            throw new InvalidOperationException("Parallel tool batch completed without outputs.");
        }

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
                Interlocked.Increment(ref started);
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

    private static async Task<ExceptionDispatchInfo?> FinalizeToolBatchHeartbeatAsync(Task batchHeartbeatTask, CancellationTokenSource batchHeartbeatCts,
        ExceptionDispatchInfo? primaryBatchFailure) {
        batchHeartbeatCts.Cancel();
        try {
            await batchHeartbeatTask.ConfigureAwait(false);
            return null;
        } catch (OperationCanceledException) when (batchHeartbeatCts.IsCancellationRequested) {
            // Expected when the batch completes or turn cancellation is requested.
            return null;
        } catch (Exception ex) when (primaryBatchFailure is not null) {
            // Preserve the primary batch failure so callers observe the real tool execution root cause.
            Trace.TraceWarning($"Tool batch heartbeat finalize failed after primary batch failure: {ex.GetType().Name}: {ex.Message}");
            return null;
        } catch (Exception ex) {
            return ExceptionDispatchInfo.Capture(ex);
        }
    }

    private async Task RunToolBatchHeartbeatLoopAsync(
        StreamWriter writer,
        string requestId,
        string threadId,
        int totalCalls,
        int maxConcurrency,
        Stopwatch batchStopwatch,
        Func<int> readStartedCalls,
        Func<int> readCompletedCalls,
        Func<int> readFailedCalls,
        CancellationToken cancellationToken) {
        if (totalCalls <= 1) {
            return;
        }

        using var timer = new PeriodicTimer(ToolHeartbeatInterval);
        try {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false)) {
                var completed = Math.Clamp(readCompletedCalls(), 0, totalCalls);
                if (completed >= totalCalls) {
                    break;
                }

                var started = Math.Clamp(readStartedCalls(), completed, totalCalls);
                var failed = Math.Clamp(readFailedCalls(), 0, completed);
                var elapsedSeconds = Math.Max(1, (int)Math.Round(batchStopwatch.Elapsed.TotalSeconds));

                await TryWriteStatusAsync(
                        writer,
                        requestId,
                        threadId,
                        status: "tool_batch_heartbeat",
                        durationMs: batchStopwatch.ElapsedMilliseconds,
                        message: BuildToolBatchHeartbeatMessage(
                            completed,
                            totalCalls,
                            maxConcurrency,
                            started,
                            failed,
                            elapsedSeconds))
                    .ConfigureAwait(false);
            }
        } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            // Expected when the tool batch completes or turn cancellation is requested.
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

    private static string BuildToolBatchHeartbeatMessage(int completedCalls, int totalCalls, int maxConcurrency, int startedCalls, int failedCalls,
        int elapsedSeconds) {
        var total = Math.Max(0, totalCalls);
        var completed = Math.Clamp(completedCalls, 0, total);
        var started = Math.Clamp(startedCalls, completed, total);
        var failed = Math.Clamp(failedCalls, 0, completed);
        var active = Math.Clamp(started - completed, 0, Math.Max(1, maxConcurrency));
        var queued = Math.Max(0, total - started);
        var concurrency = Math.Max(1, maxConcurrency);
        var elapsed = Math.Max(1, elapsedSeconds);

        if (failed <= 0) {
            return
                $"Tool batch still running: {completed}/{total} complete ({active} active, {queued} queued, {concurrency} max parallel, {elapsed}s elapsed).";
        }

        return
            $"Tool batch still running: {completed}/{total} complete ({active} active, {queued} queued, {failed} failed, {concurrency} max parallel, {elapsed}s elapsed).";
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
}
