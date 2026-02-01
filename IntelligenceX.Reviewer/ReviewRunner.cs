using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Copilot;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.OpenAI.Chat;

namespace IntelligenceX.Reviewer;

internal sealed class ReviewRunner {
    private readonly ReviewSettings _settings;

    public ReviewRunner(ReviewSettings settings) {
        _settings = settings;
    }

    public async Task<string> RunAsync(string prompt, Func<string, Task>? onPartial, TimeSpan? updateInterval,
        CancellationToken cancellationToken) {
        return _settings.Provider == ReviewProvider.Copilot
            ? await RunCopilotAsync(prompt, onPartial, updateInterval, cancellationToken).ConfigureAwait(false)
            : await RunOpenAiWithRetryAsync(prompt, onPartial, updateInterval, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> RunOpenAiWithRetryAsync(string prompt, Func<string, Task>? onPartial, TimeSpan? updateInterval,
        CancellationToken cancellationToken) {
        ReviewDiagnosticsSnapshot? snapshot = null;
        try {
            return await ReviewRetryPolicy.RunAsync(async () => {
                    var output = await RunOpenAiOnceAsync(prompt, onPartial, updateInterval, cancellationToken,
                            latest => snapshot = latest)
                        .ConfigureAwait(false);
                    if (string.IsNullOrWhiteSpace(output)) {
                        throw new InvalidOperationException("OpenAI response was empty.");
                    }
                    return output;
                },
                IsTransient,
                _settings.RetryCount,
                _settings.RetryDelaySeconds,
                _settings.RetryMaxDelaySeconds,
                cancellationToken,
                ex => ReviewDiagnostics.FormatExceptionSummary(ex, _settings.Diagnostics),
                _settings.RetryExtraOnResponseEnded ? 1 : 0,
                ReviewDiagnostics.IsResponseEnded).ConfigureAwait(false);
        } catch (Exception ex) {
            ReviewDiagnostics.LogFailure(ex, _settings, snapshot);
            if (_settings.FailOpen && IsTransient(ex)) {
                return ReviewDiagnostics.BuildFailureBody(ex, _settings, snapshot);
            }
            throw;
        }
    }

    private async Task<string> RunOpenAiOnceAsync(string prompt, Func<string, Task>? onPartial, TimeSpan? updateInterval,
        CancellationToken cancellationToken, Action<ReviewDiagnosticsSnapshot?>? captureSnapshot) {
        var options = new IntelligenceXClientOptions {
            DefaultModel = _settings.Model,
            TransportKind = _settings.OpenAITransport
        };
        if (options.TransportKind == OpenAITransportKind.AppServer) {
            if (!string.IsNullOrWhiteSpace(_settings.CodexPath)) {
                options.AppServerOptions.ExecutablePath = _settings.CodexPath!;
            }
            if (!string.IsNullOrWhiteSpace(_settings.CodexArgs)) {
                options.AppServerOptions.Arguments = _settings.CodexArgs!;
            }
            if (!string.IsNullOrWhiteSpace(_settings.CodexWorkingDirectory)) {
                options.AppServerOptions.WorkingDirectory = _settings.CodexWorkingDirectory;
            }
        }

        await using var client = await IntelligenceXClient.ConnectAsync(options, cancellationToken)
            .ConfigureAwait(false);
        using var diagnostics = ReviewDiagnosticsSession.TryStart(_settings, client);

        if (_settings.Preflight) {
            var timeout = _settings.PreflightTimeoutSeconds > 0
                ? TimeSpan.FromSeconds(_settings.PreflightTimeoutSeconds)
                : TimeSpan.FromSeconds(15);
            var check = await client.HealthCheckAsync(null, timeout, cancellationToken).ConfigureAwait(false);
            if (!check.Ok) {
                if (check.Error is not null) {
                    throw check.Error;
                }
                throw new InvalidOperationException(check.Message ?? "OpenAI preflight check failed.");
            }
        }

        var deltas = new StringBuilder();
        var lastDelta = DateTimeOffset.UtcNow;
        using var subscription = client.SubscribeDelta(text => {
            if (!string.IsNullOrWhiteSpace(text)) {
                lock (deltas) {
                    deltas.Append(text);
                    lastDelta = DateTimeOffset.UtcNow;
                }
            }
        });

        Task? progressTask = null;
        CancellationTokenSource? progressCts = null;
        if (onPartial is not null && updateInterval.HasValue && updateInterval.Value > TimeSpan.Zero) {
            progressCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            progressTask = Task.Run(async () => {
                while (!progressCts.IsCancellationRequested) {
                    await Task.Delay(updateInterval.Value, progressCts.Token).ConfigureAwait(false);
                    var snapshot = GetDeltas(deltas);
                    await onPartial(snapshot).ConfigureAwait(false);
                }
            }, progressCts.Token);
        }

        var chatOptions = new ChatOptions {
            Model = _settings.Model,
            NewThread = true,
            ReasoningEffort = _settings.ReasoningEffort,
            ReasoningSummary = _settings.ReasoningSummary
        };
        try {
            var input = ChatInput.FromText(prompt);
            var turn = await client.ChatAsync(input, chatOptions, cancellationToken).ConfigureAwait(false);

            var output = ExtractOutputs(turn.Outputs);
            if (!string.IsNullOrWhiteSpace(output)) {
                return output;
            }

            return await WaitForDeltasAsync(deltas, () => lastDelta, cancellationToken).ConfigureAwait(false);
        } catch {
            if (captureSnapshot is not null && diagnostics is not null) {
                captureSnapshot(diagnostics.Snapshot());
            }
            throw;
        } finally {
            if (progressTask is not null && progressCts is not null) {
                progressCts.Cancel();
                try {
                    await progressTask.ConfigureAwait(false);
                } catch (OperationCanceledException) {
                    // Expected when stopping progress updates.
                }
                progressCts.Dispose();
            }
        }
    }

    internal static bool IsTransient(Exception ex) {
        if (ex is OperationCanceledException) {
            return false;
        }
        if (ex is HttpRequestException httpRequestException) {
            if (httpRequestException.StatusCode.HasValue) {
                var code = (int)httpRequestException.StatusCode.Value;
                return code == 408 || code == 429 || (code >= 500 && code <= 599);
            }
            return true;
        }
        if (ex is IOException || ex is TimeoutException) {
            return true;
        }
        if (ReviewDiagnostics.IsResponseEnded(ex)) {
            return true;
        }
        return ex.InnerException is not null && IsTransient(ex.InnerException);
    }

    internal static class ReviewRetryPolicy {
        public static async Task<string> RunAsync(Func<Task<string>> action, Func<Exception, bool> isTransient,
            int maxAttempts, int retryDelaySeconds, int retryMaxDelaySeconds, CancellationToken cancellationToken,
            Func<Exception, string>? describeError, int extraAttempts, Func<Exception, bool>? extraRetryPredicate) {
            // maxAttempts includes the initial attempt.
            var attempts = Math.Max(1, maxAttempts);
            var extraRemaining = Math.Max(0, extraAttempts);
            var delaySeconds = Math.Max(1, retryDelaySeconds);
            var maxDelaySeconds = Math.Max(delaySeconds, retryMaxDelaySeconds);
            var delay = TimeSpan.FromSeconds(delaySeconds);

            Exception? lastError = null;
            for (var attempt = 1; attempt <= attempts; attempt++) {
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
                    }

                    var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(200, 800));
                    var wait = delay + jitter;
                    var summary = describeError is not null ? describeError(ex) : ex.Message;
                    Console.Error.WriteLine($"OpenAI request failed (attempt {attempt}/{attempts}): {summary}. Retrying in {wait.TotalSeconds:0.0}s.");
                    await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
                    var nextDelaySeconds = Math.Min(maxDelaySeconds, delay.TotalSeconds * 2);
                    delay = TimeSpan.FromSeconds(nextDelaySeconds);
                }
            }

            if (lastError is not null) {
                ExceptionDispatchInfo.Capture(lastError).Throw();
            }

            throw new InvalidOperationException("OpenAI request failed without a captured exception.");
        }
    }

    private async Task<string> RunCopilotAsync(string prompt, Func<string, Task>? onPartial, TimeSpan? updateInterval,
        CancellationToken cancellationToken) {
        var options = new CopilotClientOptions();
        if (!string.IsNullOrWhiteSpace(_settings.CopilotCliPath)) {
            options.CliPath = _settings.CopilotCliPath;
        }
        if (!string.IsNullOrWhiteSpace(_settings.CopilotCliUrl)) {
            options.CliUrl = _settings.CopilotCliUrl;
        }
        if (!string.IsNullOrWhiteSpace(_settings.CopilotWorkingDirectory)) {
            options.WorkingDirectory = _settings.CopilotWorkingDirectory;
        }
        if (_settings.CopilotAutoInstall) {
            options.AutoInstallCli = true;
        }
        if (!string.IsNullOrWhiteSpace(_settings.CopilotAutoInstallMethod) &&
            Enum.TryParse(_settings.CopilotAutoInstallMethod, true, out CopilotCliInstallMethod method)) {
            options.AutoInstallMethod = method;
        }
        options.AutoInstallPrerelease = _settings.CopilotAutoInstallPrerelease;

        await using var client = await CopilotClient.StartAsync(options, cancellationToken).ConfigureAwait(false);
        var session = await client.CreateSessionAsync(new CopilotSessionOptions {
            Model = _settings.Model,
            Streaming = true
        }, cancellationToken).ConfigureAwait(false);

        var deltas = new StringBuilder();
        string? finalMessage = null;

        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var subscription = session.OnEvent(evt => {
            if (!string.IsNullOrWhiteSpace(evt.Content)) {
                finalMessage = evt.Content;
            }
            if (!string.IsNullOrWhiteSpace(evt.DeltaContent)) {
                lock (deltas) {
                    deltas.Append(evt.DeltaContent);
                }
            }
            if (!string.IsNullOrWhiteSpace(evt.ErrorMessage)) {
                tcs.TrySetException(new InvalidOperationException(evt.ErrorMessage));
            }
            if (evt.IsIdle) {
                tcs.TrySetResult(finalMessage ?? GetDeltas(deltas));
            }
        });

        CancellationTokenSource? progressCts = null;
        Task? progressTask = null;
        if (onPartial is not null && updateInterval.HasValue && updateInterval.Value > TimeSpan.Zero) {
            progressCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            progressTask = Task.Run(async () => {
                while (!progressCts.IsCancellationRequested) {
                    await Task.Delay(updateInterval.Value, progressCts.Token).ConfigureAwait(false);
                    var snapshot = GetDeltas(deltas);
                    await onPartial(snapshot).ConfigureAwait(false);
                }
            }, progressCts.Token);
        }

        await session.SendAsync(new CopilotMessageOptions { Prompt = prompt }, cancellationToken).ConfigureAwait(false);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_settings.WaitSeconds));
        using var registration = timeout.Token.Register(() =>
            tcs.TrySetException(new TimeoutException($"Copilot review timed out after {_settings.WaitSeconds} seconds.")));

        var result = await tcs.Task.ConfigureAwait(false);

        if (progressTask is not null && progressCts is not null) {
            progressCts.Cancel();
            try {
                await progressTask.ConfigureAwait(false);
            } catch (OperationCanceledException) {
                // Expected on cancellation.
            }
            progressCts.Dispose();
        }

        return result ?? string.Empty;
    }

    private async Task<string> WaitForDeltasAsync(StringBuilder deltas, Func<DateTimeOffset> getLastDelta,
        CancellationToken cancellationToken) {
        var start = DateTimeOffset.UtcNow;
        var max = TimeSpan.FromSeconds(_settings.WaitSeconds);
        var idle = TimeSpan.FromSeconds(_settings.IdleSeconds);

        while (DateTimeOffset.UtcNow - start < max) {
            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            var last = getLastDelta();
            if (DateTimeOffset.UtcNow - last > idle) {
                break;
            }
        }

        lock (deltas) {
            return deltas.ToString();
        }
    }

    private static string ExtractOutputs(IReadOnlyList<TurnOutput> outputs) {
        if (outputs.Count == 0) {
            return string.Empty;
        }
        var builder = new StringBuilder();
        foreach (var output in outputs.Where(o => o.IsText)) {
            if (!string.IsNullOrWhiteSpace(output.Text)) {
                builder.AppendLine(output.Text);
            }
        }
        return builder.ToString().Trim();
    }

    private static string GetDeltas(StringBuilder deltas) {
        lock (deltas) {
            return deltas.ToString();
        }
    }
}
