using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace IntelligenceX.Tools.Common;

/// <summary>
/// Shared reliability settings for typed tool execution pipelines.
/// </summary>
public sealed class ToolPipelineReliabilityOptions {
    /// <summary>
    /// Recommended defaults for read-only tools where retries are safe.
    /// </summary>
    public static ToolPipelineReliabilityOptions ReadOnlyDefaults => new() {
        MaxAttempts = 3,
        RetryTransientErrors = true,
        RetryExceptions = true,
        RetryNonTransientExceptions = false,
        AttemptTimeoutMs = 0,
        BaseDelayMs = 120,
        MaxDelayMs = 1200,
        JitterRatio = 0.10d,
        EnableCircuitBreaker = true,
        CircuitFailureThreshold = 4,
        CircuitOpenMs = 15_000
    };

    /// <summary>
    /// Total attempts including the first execution.
    /// </summary>
    public int MaxAttempts { get; init; } = 1;

    /// <summary>
    /// Retries envelope responses marked transient (<c>is_transient=true</c>).
    /// </summary>
    public bool RetryTransientErrors { get; init; } = true;

    /// <summary>
    /// Retries exceptions classified as transient.
    /// </summary>
    public bool RetryExceptions { get; init; } = true;

    /// <summary>
    /// Retries exceptions not classified as transient.
    /// </summary>
    public bool RetryNonTransientExceptions { get; init; }

    /// <summary>
    /// Per-attempt timeout in milliseconds. Set to 0 to disable.
    /// </summary>
    public int AttemptTimeoutMs { get; init; }

    /// <summary>
    /// Base retry delay in milliseconds.
    /// </summary>
    public int BaseDelayMs { get; init; } = 100;

    /// <summary>
    /// Maximum retry delay in milliseconds.
    /// </summary>
    public int MaxDelayMs { get; init; } = 1000;

    /// <summary>
    /// Jitter applied to retry delay (0..0.5).
    /// </summary>
    public double JitterRatio { get; init; } = 0.10d;

    /// <summary>
    /// Enables a best-effort transient-failure circuit breaker.
    /// </summary>
    public bool EnableCircuitBreaker { get; init; }

    /// <summary>
    /// Transient-failure threshold that opens the circuit.
    /// </summary>
    public int CircuitFailureThreshold { get; init; } = 4;

    /// <summary>
    /// Circuit-open duration in milliseconds.
    /// </summary>
    public int CircuitOpenMs { get; init; } = 15_000;

    /// <summary>
    /// Optional circuit-key override. Defaults to tool definition name.
    /// </summary>
    public string? CircuitKey { get; init; }

    /// <summary>
    /// Optional clock provider used by reliability middleware.
    /// </summary>
    public Func<DateTimeOffset>? UtcNowProvider { get; init; }

    /// <summary>
    /// Optional delay provider used by retry backoff logic.
    /// </summary>
    public Func<TimeSpan, CancellationToken, Task>? DelayAsync { get; init; }

    internal ToolPipelineReliabilityOptions Normalize() {
        var maxAttempts = Math.Clamp(MaxAttempts, 1, 10);
        var attemptTimeoutMs = Math.Clamp(AttemptTimeoutMs, 0, 300_000);
        var baseDelayMs = Math.Clamp(BaseDelayMs, 0, 30_000);
        var maxDelayMs = Math.Clamp(MaxDelayMs, baseDelayMs, 120_000);
        var jitterRatio = Math.Clamp(JitterRatio, 0d, 0.5d);
        var circuitFailureThreshold = Math.Clamp(CircuitFailureThreshold, 1, 50);
        var circuitOpenMs = Math.Clamp(CircuitOpenMs, 100, 600_000);

        return new ToolPipelineReliabilityOptions {
            MaxAttempts = maxAttempts,
            RetryTransientErrors = RetryTransientErrors,
            RetryExceptions = RetryExceptions,
            RetryNonTransientExceptions = RetryNonTransientExceptions,
            AttemptTimeoutMs = attemptTimeoutMs,
            BaseDelayMs = baseDelayMs,
            MaxDelayMs = maxDelayMs,
            JitterRatio = jitterRatio,
            EnableCircuitBreaker = EnableCircuitBreaker,
            CircuitFailureThreshold = circuitFailureThreshold,
            CircuitOpenMs = circuitOpenMs,
            CircuitKey = string.IsNullOrWhiteSpace(CircuitKey) ? null : CircuitKey.Trim(),
            UtcNowProvider = UtcNowProvider,
            DelayAsync = DelayAsync
        };
    }
}

public static partial class ToolPipeline {
    /// <summary>
    /// Context-item key for current reliability attempt number (1-based).
    /// </summary>
    public const string ReliabilityAttemptItemKey = "tool_pipeline.reliability.attempt";

    /// <summary>
    /// Context-item key for retry count completed before current attempt.
    /// </summary>
    public const string ReliabilityRetryCountItemKey = "tool_pipeline.reliability.retry_count";

    private static readonly ConcurrentDictionary<string, CircuitState> CircuitStates =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Creates reliability middleware (retry/backoff/timeout/circuit-breaker).
    /// </summary>
    public static ToolPipelineMiddleware<TRequest> Reliability<TRequest>(ToolPipelineReliabilityOptions options)
        where TRequest : notnull {
        ArgumentNullException.ThrowIfNull(options);
        var resolved = options.Normalize();

        return async (context, cancellationToken, next) => {
            cancellationToken.ThrowIfCancellationRequested();

            var nowProvider = resolved.UtcNowProvider ?? (() => DateTimeOffset.UtcNow);
            var delayAsync = resolved.DelayAsync ?? ((delay, token) => Task.Delay(delay, token));
            var circuitKey = ResolveCircuitKey(context.Definition.Name, resolved.CircuitKey);

            if (resolved.EnableCircuitBreaker &&
                TryGetCircuitRetryAfter(circuitKey, nowProvider(), out var retryAfter)) {
                return ToolResponse.Error(
                    "circuit_open",
                    "Transient failure circuit is open for this tool. Retry after cooldown.",
                    hints: new[] { $"retry_after_ms={Math.Max(1, (int)Math.Ceiling(retryAfter.TotalMilliseconds))}" },
                    isTransient: true);
            }

            for (var attempt = 1; attempt <= resolved.MaxAttempts; attempt++) {
                context.SetItem(ReliabilityAttemptItemKey, attempt);
                context.SetItem(ReliabilityRetryCountItemKey, attempt - 1);

                var outcome = await ExecuteAttemptAsync(
                    context,
                    cancellationToken,
                    next,
                    attempt,
                    resolved).ConfigureAwait(false);

                if (!outcome.ShouldRetry) {
                    RecordCircuitOutcome(circuitKey, nowProvider(), outcome.IsTransientFailure, resolved);
                    return outcome.Result;
                }

                await DelayBeforeRetryAsync(
                    attempt,
                    resolved,
                    delayAsync,
                    cancellationToken).ConfigureAwait(false);
            }

            RecordCircuitOutcome(circuitKey, nowProvider(), transientFailure: true, resolved);
            return ToolResponse.Error("query_failed", "Tool pipeline retry budget was exhausted.", isTransient: true);
        };
    }

    private static async Task<ReliabilityAttemptOutcome> ExecuteAttemptAsync<TRequest>(
        ToolPipelineContext<TRequest> context,
        CancellationToken cancellationToken,
        ToolPipelineNext<TRequest> next,
        int attempt,
        ToolPipelineReliabilityOptions options)
        where TRequest : notnull {
        CancellationTokenSource? linked = null;
        var attemptToken = cancellationToken;

        try {
            if (options.AttemptTimeoutMs > 0) {
                linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                linked.CancelAfter(options.AttemptTimeoutMs);
                attemptToken = linked.Token;
            }

            var result = await next(context, attemptToken).ConfigureAwait(false);
            var status = ClassifyEnvelope(result);

            var transientFailure = status.IsEnvelope && !status.Ok && status.IsTransient;
            var shouldRetry = transientFailure &&
                              options.RetryTransientErrors &&
                              attempt < options.MaxAttempts;

            return new ReliabilityAttemptOutcome(result, transientFailure, shouldRetry);
        } catch (OperationCanceledException) when (
            !cancellationToken.IsCancellationRequested &&
            linked is not null &&
            linked.IsCancellationRequested) {
            var timeoutResult = ToolResponse.Error(
                "timeout",
                $"Operation timed out after {options.AttemptTimeoutMs}ms.",
                isTransient: true);
            var shouldRetry = options.RetryTransientErrors && attempt < options.MaxAttempts;
            return new ReliabilityAttemptOutcome(timeoutResult, IsTransientFailure: true, ShouldRetry: shouldRetry);
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            var transientException = IsTransientException(ex);
            var shouldRetry = attempt < options.MaxAttempts &&
                              ((transientException && options.RetryExceptions) ||
                               (!transientException && options.RetryNonTransientExceptions));

            var mapped = MapExceptionEnvelope(ex, transientException);
            return new ReliabilityAttemptOutcome(
                mapped,
                IsTransientFailure: transientException,
                ShouldRetry: shouldRetry);
        } finally {
            linked?.Dispose();
        }
    }

    private static async Task DelayBeforeRetryAsync(
        int attempt,
        ToolPipelineReliabilityOptions options,
        Func<TimeSpan, CancellationToken, Task> delayAsync,
        CancellationToken cancellationToken) {
        if (options.BaseDelayMs <= 0) {
            return;
        }

        var exponent = Math.Clamp(attempt - 1, 0, 30);
        var candidate = (long)options.BaseDelayMs << exponent;
        var delayMs = (int)Math.Min(options.MaxDelayMs, candidate);

        if (options.JitterRatio > 0d && delayMs > 0) {
            var jitterWindow = (int)Math.Round(delayMs * options.JitterRatio, MidpointRounding.AwayFromZero);
            if (jitterWindow > 0) {
                delayMs = Math.Max(0, delayMs + Random.Shared.Next(-jitterWindow, jitterWindow + 1));
            }
        }

        if (delayMs <= 0) {
            return;
        }

        await delayAsync(TimeSpan.FromMilliseconds(delayMs), cancellationToken).ConfigureAwait(false);
    }

    private static EnvelopeStatus ClassifyEnvelope(string payload) {
        if (string.IsNullOrWhiteSpace(payload)) {
            return EnvelopeStatus.NonEnvelope;
        }

        try {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("ok", out var okNode) ||
                okNode.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) {
                return EnvelopeStatus.NonEnvelope;
            }

            var ok = okNode.GetBoolean();
            if (ok) {
                return new EnvelopeStatus(IsEnvelope: true, Ok: true, IsTransient: false);
            }

            var isTransient = root.TryGetProperty("is_transient", out var transientNode) &&
                              transientNode.ValueKind is JsonValueKind.True or JsonValueKind.False &&
                              transientNode.GetBoolean();
            if (!isTransient &&
                root.TryGetProperty("error_code", out var errorCodeNode) &&
                errorCodeNode.ValueKind == JsonValueKind.String) {
                var errorCode = errorCodeNode.GetString();
                isTransient = IsKnownTransientErrorCode(errorCode);
            }

            return new EnvelopeStatus(IsEnvelope: true, Ok: false, IsTransient: isTransient);
        } catch {
            return EnvelopeStatus.NonEnvelope;
        }
    }

    private static bool IsKnownTransientErrorCode(string? errorCode) {
        return errorCode switch {
            "timeout" => true,
            "cancelled" => true,
            "circuit_open" => true,
            _ => false
        };
    }

    private static bool IsTransientException(Exception exception) {
        return exception is TimeoutException
            or IOException
            or SocketException
            or HttpRequestException
            or TaskCanceledException;
    }

    private static string MapExceptionEnvelope(Exception exception, bool transientException) {
        var mapped = ToolExceptionMapper.ErrorFromException(
            exception,
            defaultMessage: "Tool execution failed.",
            unauthorizedMessage: "Access denied.",
            timeoutMessage: "Operation timed out.");

        if (!transientException) {
            return mapped;
        }

        var status = ClassifyEnvelope(mapped);
        if (status.IsEnvelope && status.IsTransient) {
            return mapped;
        }

        return ToolResponse.Error(
            "query_failed",
            ToolExceptionMapper.SanitizeErrorMessage(exception.Message, "Tool execution failed."),
            isTransient: true);
    }

    private static string ResolveCircuitKey(string toolName, string? overrideKey) {
        if (!string.IsNullOrWhiteSpace(overrideKey)) {
            return overrideKey.Trim();
        }

        return string.IsNullOrWhiteSpace(toolName)
            ? "tool_pipeline"
            : toolName.Trim();
    }

    private static bool TryGetCircuitRetryAfter(string key, DateTimeOffset nowUtc, out TimeSpan retryAfter) {
        retryAfter = TimeSpan.Zero;
        if (!CircuitStates.TryGetValue(key, out var state)) {
            return false;
        }

        lock (state.Gate) {
            if (state.OpenUntilUtc <= nowUtc) {
                // Keep consecutive failure count while closed so thresholds can accumulate
                // across separate invocations; clear only the stale open marker.
                state.OpenUntilUtc = DateTimeOffset.MinValue;
                return false;
            }

            retryAfter = state.OpenUntilUtc - nowUtc;
            return true;
        }
    }

    private static void RecordCircuitOutcome(
        string key,
        DateTimeOffset nowUtc,
        bool transientFailure,
        ToolPipelineReliabilityOptions options) {
        if (!options.EnableCircuitBreaker) {
            return;
        }

        var state = CircuitStates.GetOrAdd(key, static _ => new CircuitState());
        lock (state.Gate) {
            if (!transientFailure) {
                state.ConsecutiveTransientFailures = 0;
                state.OpenUntilUtc = DateTimeOffset.MinValue;
                return;
            }

            state.ConsecutiveTransientFailures++;
            if (state.ConsecutiveTransientFailures < options.CircuitFailureThreshold) {
                return;
            }

            state.ConsecutiveTransientFailures = 0;
            state.OpenUntilUtc = nowUtc.AddMilliseconds(options.CircuitOpenMs);
        }
    }

    private sealed class CircuitState {
        public object Gate { get; } = new();

        public int ConsecutiveTransientFailures { get; set; }

        public DateTimeOffset OpenUntilUtc { get; set; } = DateTimeOffset.MinValue;
    }

    private readonly record struct EnvelopeStatus(bool IsEnvelope, bool Ok, bool IsTransient) {
        public static EnvelopeStatus NonEnvelope { get; } = new(IsEnvelope: false, Ok: true, IsTransient: false);
    }

    private readonly record struct ReliabilityAttemptOutcome(
        string Result,
        bool IsTransientFailure,
        bool ShouldRetry);
}
