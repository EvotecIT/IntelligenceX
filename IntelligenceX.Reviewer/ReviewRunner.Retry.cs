using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace IntelligenceX.Reviewer;

internal sealed partial class ReviewRunner {
    internal static class ReviewRetryPolicy {
        public static Task<string> RunAsync(Func<Task<string>> action, Func<Exception, bool> isTransient,
            int maxAttempts, int retryDelaySeconds, int retryMaxDelaySeconds, CancellationToken cancellationToken,
            Func<Exception, string>? describeError, int extraAttempts, Func<Exception, bool>? extraRetryPredicate,
            ReviewRetryState? retryState, string? operationName = null) {
            return RunAsync(action, isTransient, maxAttempts, retryDelaySeconds, retryMaxDelaySeconds,
                2.0, 200, 800, cancellationToken, describeError, extraAttempts, extraRetryPredicate, retryState, operationName);
        }

        public static async Task<string> RunAsync(Func<Task<string>> action, Func<Exception, bool> isTransient,
            int maxAttempts, int retryDelaySeconds, int retryMaxDelaySeconds, double backoffMultiplier,
            int retryJitterMinMs, int retryJitterMaxMs, CancellationToken cancellationToken,
            Func<Exception, string>? describeError, int extraAttempts, Func<Exception, bool>? extraRetryPredicate,
            ReviewRetryState? retryState, string? operationName = null) {
            // maxAttempts includes the initial attempt.
            var attempts = Math.Max(1, maxAttempts);
            var extraRemaining = Math.Max(0, extraAttempts);
            var delaySeconds = Math.Max(1, retryDelaySeconds);
            var maxDelaySeconds = Math.Max(delaySeconds, retryMaxDelaySeconds);
            var delay = TimeSpan.FromSeconds(delaySeconds);
            var jitterMin = Math.Max(0, retryJitterMinMs);
            var jitterMax = Math.Max(jitterMin, retryJitterMaxMs);
            var backoff = Math.Max(1.0, backoffMultiplier);
            var operationLabel = string.IsNullOrWhiteSpace(operationName) ? "OpenAI" : operationName!.Trim();

            Exception? lastError = null;
            for (var attempt = 1; attempt <= attempts; attempt++) {
                if (retryState is not null) {
                    retryState.LastAttempt = attempt;
                    retryState.MaxAttempts = attempts;
                }
                try {
                    return await action().ConfigureAwait(false);
                } catch (Exception ex) when (isTransient(ex) &&
                                             !cancellationToken.IsCancellationRequested &&
                                             (attempt < attempts ||
                                              (extraRemaining > 0 &&
                                               extraRetryPredicate is not null &&
                                               extraRetryPredicate(ex)))) {
                    lastError = ex;
                    if (attempt >= attempts) {
                        attempts += extraRemaining;
                        extraRemaining = 0;
                        if (retryState is not null) {
                            retryState.MaxAttempts = attempts;
                        }
                    }

                    int jitterMs;
                    if (jitterMax > jitterMin) {
                        var upperExclusive = jitterMax == int.MaxValue ? jitterMax : jitterMax + 1;
                        jitterMs = Random.Shared.Next(jitterMin, upperExclusive);
                    } else {
                        jitterMs = jitterMin;
                    }
                    var jitter = TimeSpan.FromMilliseconds(jitterMs);
                    var wait = delay + jitter;
                    var summary = describeError is not null ? describeError(ex) : ex.Message;
                    Console.Error.WriteLine(
                        $"{operationLabel} request failed (attempt {attempt}/{attempts}): {summary}. Retrying in {wait.TotalSeconds:0.0}s.");
                    await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
                    var nextDelaySeconds = Math.Min(maxDelaySeconds, delay.TotalSeconds * backoff);
                    delay = TimeSpan.FromSeconds(nextDelaySeconds);
                }
            }

            if (lastError is not null) {
                ExceptionDispatchInfo.Capture(lastError).Throw();
            }

            throw new InvalidOperationException($"{operationLabel} request failed without a captured exception.");
        }
    }
}
