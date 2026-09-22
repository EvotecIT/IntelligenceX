using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Copilot;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.OpenAI.Chat;
using IntelligenceX.OpenAI.Native;
using IntelligenceX.Telemetry.Usage;

namespace IntelligenceX.Reviewer;

internal sealed partial class ReviewRunner {
    private readonly ReviewSettings _settings;
    public ReviewProvider EffectiveProvider { get; private set; }
    public bool FallbackActivated { get; private set; }

    public ReviewRunner(ReviewSettings settings) {
        _settings = settings;
        EffectiveProvider = settings.Provider;
    }

    private IntelligenceXClientOptions BuildClientOptions() {
        var options = new IntelligenceXClientOptions {
            DefaultModel = _settings.Model,
            TransportKind = _settings.OpenAITransport,
            EnableUsageTelemetry = true,
            UsageTelemetryProviderAccountId = _settings.OpenAiAccountId,
            UsageTelemetrySessionFactory = new SqliteInternalIxUsageTelemetrySessionFactory()
        };
        options.NativeOptions.AuthAccountId = _settings.OpenAiAccountId;
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
        return options;
    }

    public async Task<string> RunAsync(string prompt, Func<string, Task>? onPartial, TimeSpan? updateInterval,
        CancellationToken cancellationToken) {
        var primaryProvider = ReviewProviderContracts.Get(_settings.Provider).Provider;
        var fallbackProvider = ResolveFallbackProvider(primaryProvider, _settings.ProviderFallback);
        EffectiveProvider = primaryProvider;
        FallbackActivated = false;

        Exception? primaryException = null;
        string? primaryFailureBody = null;
        try {
            var primaryResult = await RunWithProviderAsync(primaryProvider, prompt, onPartial, updateInterval, cancellationToken)
                .ConfigureAwait(false);
            if (!fallbackProvider.HasValue || !ShouldFallbackOnResult(primaryResult)) {
                return primaryResult;
            }
            primaryFailureBody = primaryResult;
            if (_settings.Diagnostics) {
                Console.Error.WriteLine(
                    $"Primary provider '{primaryProvider.ToString().ToLowerInvariant()}' returned a fail-open body; attempting fallback provider '{fallbackProvider.Value.ToString().ToLowerInvariant()}'.");
            }
        } catch (Exception ex) when (!cancellationToken.IsCancellationRequested && fallbackProvider.HasValue) {
            primaryException = ex;
            if (_settings.Diagnostics) {
                Console.Error.WriteLine(
                    $"Primary provider '{primaryProvider.ToString().ToLowerInvariant()}' failed ({ex.GetType().Name}); attempting fallback provider '{fallbackProvider.Value.ToString().ToLowerInvariant()}'.");
            }
        }

        if (!fallbackProvider.HasValue) {
            if (primaryException is not null) {
                ExceptionDispatchInfo.Capture(primaryException).Throw();
            }
            return primaryFailureBody ?? string.Empty;
        }

        try {
            var fallbackResult = await RunWithProviderAsync(fallbackProvider.Value, prompt, onPartial, updateInterval, cancellationToken)
                .ConfigureAwait(false);
            EffectiveProvider = fallbackProvider.Value;
            FallbackActivated = true;
            return fallbackResult;
        } catch {
            if (primaryException is not null) {
                ExceptionDispatchInfo.Capture(primaryException).Throw();
            }
            if (primaryFailureBody is not null) {
                EffectiveProvider = primaryProvider;
                return primaryFailureBody;
            }
            throw;
        }
    }

    internal static ReviewProvider? ResolveFallbackProvider(ReviewProvider primaryProvider, ReviewProvider? configuredFallback) {
        if (!configuredFallback.HasValue || configuredFallback.Value == primaryProvider) {
            return null;
        }
        return configuredFallback.Value;
    }

    internal static bool ShouldFallbackOnResult(string? result) {
        return !string.IsNullOrWhiteSpace(result) && ReviewDiagnostics.IsFailureBody(result);
    }

    private async Task<string> RunWithProviderAsync(ReviewProvider provider, string prompt, Func<string, Task>? onPartial,
        TimeSpan? updateInterval, CancellationToken cancellationToken) {
        var now = DateTimeOffset.UtcNow;
        if (ReviewProviderCircuitBreaker.IsOpen(provider, now, out var remaining)) {
            throw new InvalidOperationException(
                $"Provider circuit breaker is open for '{provider.ToString().ToLowerInvariant()}'. Retry in {Math.Ceiling(Math.Max(1, remaining.TotalSeconds)):0}s.");
        }

        var openDuration = TimeSpan.FromSeconds(Math.Max(1, _settings.ProviderCircuitBreakerOpenSeconds));
        var failureThreshold = Math.Max(0, _settings.ProviderCircuitBreakerFailures);
        try {
            if (_settings.ProviderHealthChecks) {
                await RunProviderHealthCheckAsync(provider, cancellationToken).ConfigureAwait(false);
            }
            var output = await RunWithSelectedProviderAsync(provider, prompt, onPartial, updateInterval, cancellationToken)
                .ConfigureAwait(false);
            ReviewProviderCircuitBreaker.RecordSuccess(provider);
            return output;
        } catch (Exception) when (!cancellationToken.IsCancellationRequested) {
            ReviewProviderCircuitBreaker.RecordFailure(provider, failureThreshold, openDuration, DateTimeOffset.UtcNow);
            throw;
        }
    }

    private async Task<string> RunWithSelectedProviderAsync(ReviewProvider provider, string prompt, Func<string, Task>? onPartial,
        TimeSpan? updateInterval, CancellationToken cancellationToken) {
        var previousProvider = _settings.Provider;
        _settings.Provider = provider;
        try {
            return provider switch {
                ReviewProvider.Copilot => await RunCopilotWithRetryAsync(prompt, onPartial, updateInterval, cancellationToken).ConfigureAwait(false),
                ReviewProvider.OpenAI => await RunOpenAiWithRetryAsync(prompt, onPartial, updateInterval, cancellationToken).ConfigureAwait(false),
                ReviewProvider.OpenAICompatible => await RunOpenAiCompatibleWithRetryAsync(prompt, cancellationToken).ConfigureAwait(false),
                ReviewProvider.Claude => await RunClaudeWithRetryAsync(prompt, cancellationToken).ConfigureAwait(false),
                _ => throw new NotSupportedException($"Unsupported review provider '{provider}'.")
            };
        } finally {
            _settings.Provider = previousProvider;
        }
    }

    private async Task RunProviderHealthCheckAsync(ReviewProvider provider, CancellationToken cancellationToken) {
        var timeout = TimeSpan.FromSeconds(Math.Max(1, _settings.ProviderHealthCheckTimeoutSeconds));
        switch (provider) {
            case ReviewProvider.OpenAI:
                await RunOpenAiPreflightAsync(BuildClientOptions(), timeout, cancellationToken).ConfigureAwait(false);
                return;
            case ReviewProvider.Copilot:
                await RunCopilotHealthCheckAsync(timeout, cancellationToken).ConfigureAwait(false);
                return;
            case ReviewProvider.OpenAICompatible:
                await RunOpenAiCompatiblePreflightAsync(timeout, cancellationToken).ConfigureAwait(false);
                return;
            case ReviewProvider.Claude:
                await RunClaudePreflightAsync(timeout, cancellationToken).ConfigureAwait(false);
                return;
            default:
                throw new NotSupportedException($"Unsupported review provider '{provider}'.");
        }
    }

    private async Task<string> RunOpenAiWithRetryAsync(string prompt, Func<string, Task>? onPartial, TimeSpan? updateInterval,
        CancellationToken cancellationToken) {
        ReviewDiagnosticsSnapshot? snapshot = null;
        var retryState = new ReviewRetryState();
        var options = BuildClientOptions();
        try {
            if (_settings.Preflight && !_settings.ProviderHealthChecks) {
                var timeout = _settings.PreflightTimeoutSeconds > 0
                    ? TimeSpan.FromSeconds(_settings.PreflightTimeoutSeconds)
                    : TimeSpan.FromSeconds(15);
                await RunOpenAiPreflightAsync(options, timeout, cancellationToken).ConfigureAwait(false);
            }
            return await ReviewRetryPolicy.RunAsync(async () => {
                    var output = await RunChatOnceAsync(options, prompt, onPartial, updateInterval, cancellationToken,
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
                Math.Max(1.0, _settings.RetryBackoffMultiplier),
                Math.Max(0, _settings.RetryJitterMinMs),
                Math.Max(0, _settings.RetryJitterMaxMs),
                cancellationToken,
                ex => ReviewDiagnostics.FormatExceptionSummary(ex, _settings.Diagnostics),
                _settings.RetryExtraOnResponseEnded ? 1 : 0,
                ReviewDiagnostics.IsResponseEnded,
                retryState).ConfigureAwait(false);
        } catch (Exception ex) {
            ReviewDiagnostics.LogFailure(ex, _settings, snapshot, retryState);
            if (ShouldFailOpen(_settings, ex)) {
                return ReviewDiagnostics.BuildFailureBody(ex, _settings, snapshot, retryState);
            }
            throw;
        }
    }

    private async Task<string> RunChatOnceAsync(IntelligenceXClientOptions options, string prompt, Func<string, Task>? onPartial,
        TimeSpan? updateInterval,
        CancellationToken cancellationToken, Action<ReviewDiagnosticsSnapshot?>? captureSnapshot) {
        await using var client = await IntelligenceXClient.ConnectAsync(options, cancellationToken)
            .ConfigureAwait(false);
        using var diagnostics = ReviewDiagnosticsSession.TryStart(_settings, client);

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
                    try { await onPartial(snapshot).WaitAsync(progressCts.Token).ConfigureAwait(false); }
                    catch (Exception) when (!progressCts.IsCancellationRequested) { /* Progress is observational. */ }
                }
            }, progressCts.Token);
        }

        var chatOptions = new ChatOptions {
            Model = options.DefaultModel,
            NewThread = true,
            Ephemeral = options.TransportKind != OpenAITransportKind.AppServer,
            ReasoningEffort = _settings.ReasoningEffort,
            ReasoningSummary = _settings.ReasoningSummary,
            TelemetryFeature = "reviewer",
            TelemetrySurface = "cli"
        };
        try {
            var input = ChatInput.FromText(prompt);
            var turn = await client.ChatAsync(input, chatOptions, cancellationToken).ConfigureAwait(false);
            if (options.TransportKind != OpenAITransportKind.AppServer && !string.Equals(turn.Status, "completed", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The provider returned an incomplete review.");

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

    private async Task RunOpenAiPreflightAsync(IntelligenceXClientOptions options, TimeSpan timeout, CancellationToken cancellationToken) {
        if (options.TransportKind == OpenAITransportKind.Native) {
            await PreflightNativeConnectivityAsync(options.NativeOptions, timeout, cancellationToken).ConfigureAwait(false);
        }

        await using var client = await IntelligenceXClient.ConnectAsync(options, cancellationToken)
            .ConfigureAwait(false);
        var check = await client.HealthCheckAsync(null, timeout, cancellationToken).ConfigureAwait(false);
        if (!check.Ok) {
            if (check.Error is not null) {
                throw check.Error;
            }
            throw new InvalidOperationException(check.Message ?? "OpenAI preflight check failed.");
        }
    }

    internal static bool IsTransient(Exception ex) {
        return ReviewDiagnostics.Classify(ex).IsTransient;
    }

    internal static bool ShouldFailOpen(ReviewSettings settings, Exception ex) {
        if (!settings.FailOpen) {
            return false;
        }
        if (IsTransient(ex)) {
            return true;
        }
        return !settings.FailOpenTransientOnly;
    }

    private async Task<string> RunCopilotWithRetryAsync(string prompt, Func<string, Task>? onPartial,
        TimeSpan? updateInterval, CancellationToken cancellationToken) {
        ReviewRetryState? retryState = null;
        try {
            if (_settings.Preflight && !_settings.ProviderHealthChecks) {
                var timeout = _settings.PreflightTimeoutSeconds > 0
                    ? TimeSpan.FromSeconds(_settings.PreflightTimeoutSeconds)
                    : TimeSpan.FromSeconds(15);
                await RunCopilotHealthCheckAsync(timeout, cancellationToken).ConfigureAwait(false);
            }

            retryState = new ReviewRetryState();
            return await ReviewRetryPolicy.RunAsync(async () => {
                    var output = await RunCopilotAsync(prompt, onPartial, updateInterval, cancellationToken)
                        .ConfigureAwait(false);
                    if (string.IsNullOrWhiteSpace(output)) {
                        throw new InvalidOperationException("Copilot response was empty.");
                    }
                    return output;
                },
                IsTransient,
                _settings.RetryCount,
                _settings.RetryDelaySeconds,
                _settings.RetryMaxDelaySeconds,
                Math.Max(1.0, _settings.RetryBackoffMultiplier),
                Math.Max(0, _settings.RetryJitterMinMs),
                Math.Max(0, _settings.RetryJitterMaxMs),
                cancellationToken,
                ex => ReviewDiagnostics.FormatExceptionSummary(ex, _settings.Diagnostics),
                _settings.RetryExtraOnResponseEnded ? 1 : 0,
                ReviewDiagnostics.IsResponseEnded,
                retryState,
                operationName: "Copilot").ConfigureAwait(false);
        } catch (Exception ex) {
            ReviewDiagnostics.LogFailure(ex, _settings, snapshot: null, retryState);
            if (ShouldFailOpen(_settings, ex)) {
                return ReviewDiagnostics.BuildFailureBody(ex, _settings, snapshot: null, retryState);
            }
            throw;
        }
    }

    internal static string? ResolveCopilotModel(ReviewSettings settings) {
        if (!string.IsNullOrWhiteSpace(settings.CopilotModel)) {
            return settings.CopilotModel!.Trim();
        }
        if (string.IsNullOrWhiteSpace(settings.Model) || !settings.ModelExplicitlyConfigured) {
            return null;
        }
        return settings.Model.Trim();
    }

    private async Task PreflightNativeConnectivityAsync(OpenAINativeOptions options, TimeSpan timeout, CancellationToken cancellationToken) {
        if (!Uri.TryCreate(options.ChatGptApiBaseUrl, UriKind.Absolute, out var uri)) {
            throw new InvalidOperationException($"ChatGptApiBaseUrl is invalid: '{options.ChatGptApiBaseUrl}'.");
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        if (!string.IsNullOrWhiteSpace(options.UserAgent)) {
            request.Headers.TryAddWithoutValidation("User-Agent", options.UserAgent);
        }

        try {
            using var response = await PreflightHttp.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) {
                var code = (int)response.StatusCode;
                if (IsPreflightAuthReachableStatus(response.StatusCode)) {
                    if (_settings.Diagnostics) {
                        Console.Error.WriteLine($"Connectivity preflight returned HTTP {code} for {uri.Host} (reachable, auth required).");
                    }
                    return;
                }
                throw new HttpRequestException(BuildPreflightStatusErrorMessage(response.StatusCode, uri.Host), null,
                    response.StatusCode);
            }
        } catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested) {
            throw new TimeoutException($"Connectivity preflight timed out after {timeout.TotalSeconds:0.#}s for {uri.Host}.", ex);
        } catch (HttpRequestException ex) {
            var mapped = MapPreflightConnectivityException(ex, uri.Host, timeout, cancellationToken.IsCancellationRequested);
            if (mapped is not null) {
                throw mapped;
            }
            throw;
        }
    }

    private static bool IsPreflightAuthReachableStatus(HttpStatusCode statusCode) {
        return statusCode == HttpStatusCode.Unauthorized || statusCode == HttpStatusCode.Forbidden;
    }

    private static string BuildPreflightStatusErrorMessage(HttpStatusCode statusCode, string host) {
        var code = (int)statusCode;
        if (statusCode == HttpStatusCode.TooManyRequests) {
            return $"Connectivity preflight returned HTTP {code} for {host} (reachable, rate limited).";
        }
        if (code >= 500) {
            return $"Connectivity preflight returned HTTP {code} for {host} (reachable, server error).";
        }
        return $"Connectivity preflight returned HTTP {code} for {host}.";
    }

    private static Exception? MapPreflightConnectivityException(HttpRequestException ex, string host, TimeSpan timeout,
        bool cancellationRequested) {
        if (cancellationRequested &&
            (ex.InnerException is TaskCanceledException || ex.InnerException is OperationCanceledException)) {
            return null;
        }
        if (ex.InnerException is TaskCanceledException && !cancellationRequested) {
            return new TimeoutException($"Connectivity preflight timed out after {timeout.TotalSeconds:0.#}s for {host}.", ex);
        }
        if (ex.StatusCode.HasValue) {
            return null;
        }
        if (ex.InnerException is SocketException socketException) {
            if (socketException.SocketErrorCode == SocketError.HostNotFound ||
                socketException.SocketErrorCode == SocketError.NoData ||
                socketException.SocketErrorCode == SocketError.TryAgain) {
                return new InvalidOperationException(
                    $"Connectivity preflight failed for {host}. Check DNS resolution, proxy settings, and firewall rules.", ex);
            }
            return new InvalidOperationException(
                $"Connectivity preflight failed for {host}. Check proxy settings, firewall rules, and network connectivity.", ex);
        }
        return new InvalidOperationException(
            $"Connectivity preflight failed for {host}. Check TLS/proxy settings and network connectivity.", ex);
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
