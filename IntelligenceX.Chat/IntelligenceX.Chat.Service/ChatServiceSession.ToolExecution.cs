using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
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
    private const int MaxModelPhaseAttempts = 2;
    private const int ModelPhaseRetryBaseDelayMs = 350;
    private static readonly TimeSpan ToolHeartbeatInterval = TimeSpan.FromSeconds(8);

    private static async Task<TurnInfo> ChatWithToolSchemaRecoveryAsync(IntelligenceXClient client, ChatInput input, ChatOptions options,
        CancellationToken cancellationToken) {
        for (var attempt = 0; attempt < MaxModelPhaseAttempts; attempt++) {
            var attemptOptions = options.Clone();
            try {
                return await ChatWithToolSchemaRecoverySingleAttemptAsync(client, input, attemptOptions, cancellationToken).ConfigureAwait(false);
            } catch (Exception ex) when (ShouldRetryModelPhaseAttempt(
                                            ex,
                                            attempt,
                                            MaxModelPhaseAttempts,
                                            cancellationToken)) {
                var delayMs = ModelPhaseRetryBaseDelayMs * (attempt + 1);
                await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
            }
        }

        throw new InvalidOperationException("Model phase retry loop exhausted without returning a result.");
    }

    private static async Task<TurnInfo> ChatWithToolSchemaRecoverySingleAttemptAsync(
        IntelligenceXClient client,
        ChatInput input,
        ChatOptions options,
        CancellationToken cancellationToken) {
        try {
            return await client.ChatAsync(input, options, cancellationToken).ConfigureAwait(false);
        } catch (Exception ex) when (ShouldRetryWithoutTools(ex, options)) {
            options.Tools = null;
            options.ToolChoice = null;
            return await client.ChatAsync(input, options, cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool ShouldRetryModelPhaseAttempt(Exception ex, int attempt, int maxAttempts, CancellationToken cancellationToken) {
        if (attempt + 1 >= maxAttempts) {
            return false;
        }

        if (cancellationToken.IsCancellationRequested || ex is OperationCanceledException) {
            return false;
        }

        if (ex is OpenAIAuthenticationRequiredException) {
            return false;
        }

        if (LooksLikeToolOutputPairingReferenceGap(ex)) {
            return true;
        }

        return HasRetryableTransportFailureInChain(ex);
    }

    private static bool HasRetryableTransportFailureInChain(Exception ex) {
        var depth = 0;
        for (Exception? current = ex; current is not null && depth < 8; current = current.InnerException, depth++) {
            if (current is TimeoutException || current is IOException || current is HttpRequestException) {
                return true;
            }

            var statusCode = TryGetStatusCodeFromExceptionData(current);
            if (statusCode is >= 500) {
                return true;
            }

            var message = (current.Message ?? string.Empty).Trim();
            if (message.Length == 0) {
                continue;
            }

            if (message.Contains("connection refused", StringComparison.OrdinalIgnoreCase)
                || message.Contains("connection reset", StringComparison.OrdinalIgnoreCase)
                || message.Contains("broken pipe", StringComparison.OrdinalIgnoreCase)
                || message.Contains("temporarily unavailable", StringComparison.OrdinalIgnoreCase)
                || message.Contains("timed out", StringComparison.OrdinalIgnoreCase)
                || message.Contains("unexpected end of stream", StringComparison.OrdinalIgnoreCase)
                || message.Contains("server disconnected", StringComparison.OrdinalIgnoreCase)
                || message.Contains("disconnected", StringComparison.OrdinalIgnoreCase)
                || message.Contains("service unavailable", StringComparison.OrdinalIgnoreCase)
                || message.Contains("bad gateway", StringComparison.OrdinalIgnoreCase)
                || message.Contains("gateway timeout", StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        return false;
    }

    private static int? TryGetStatusCodeFromExceptionData(Exception ex) {
        if (ex?.Data is null) {
            return null;
        }

        var raw = ex.Data["openai:status_code"];
        return raw switch {
            int intCode => intCode,
            long longCode => (int)longCode,
            short shortCode => shortCode,
            byte byteCode => byteCode,
            string text when int.TryParse(text, out var parsed) => parsed,
            _ => null
        };
    }

    private static bool LooksLikeToolOutputPairingReferenceGap(Exception ex) {
        var depth = 0;
        for (Exception? current = ex; current is not null && depth < 8; current = current.InnerException, depth++) {
            var message = (current.Message ?? string.Empty).Trim();
            if (message.Length == 0) {
                continue;
            }

            if (message.Contains("No tool call found for custom tool call output", StringComparison.OrdinalIgnoreCase)
                || message.Contains("custom tool call output with call_id", StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        return false;
    }

    private static bool ShouldRetryWithoutTools(Exception ex, ChatOptions options) {
        return ToolSchemaRecoveryClassifier.ShouldRetryWithoutTools(ex, options);
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
                    status: ChatStatusCodes.ToolParallelSafetyOff,
                    message: BuildToolParallelSafetyOffMessage(mutatingToolNames))
                .ConfigureAwait(false);
        } else if (parallel && calls.Count > 1 && allowMutatingParallel && hasMutatingCalls) {
            await TryWriteStatusAsync(
                    writer,
                    requestId,
                    threadId,
                    status: ChatStatusCodes.ToolParallelForced,
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
                status: ChatStatusCodes.ToolBatchStarted,
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
                    status: ChatStatusCodes.ToolBatchRecovering,
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
                    status: ChatStatusCodes.ToolBatchRecovered,
                    message: BuildToolBatchRecoveredMessage(
                        recoveryIndexes.Length,
                        CountFailedToolOutputs(outputsInCallOrder)))
                .ConfigureAwait(false);
        }

        await TryWriteStatusAsync(
                writer,
                requestId,
                threadId,
                status: ChatStatusCodes.ToolBatchCompleted,
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
                    status: ChatStatusCodes.ToolBatchProgress,
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
                        status: ChatStatusCodes.ToolBatchHeartbeat,
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

}
