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

namespace IntelligenceX.Reviewer;

internal sealed partial class ReviewRunner {
    private const int MinimumCopilotPromptWaitSeconds = 600;
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
            UsageTelemetryProviderAccountId = _settings.OpenAiAccountId
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
                ReviewProvider.Copilot => await RunCopilotAsync(prompt, onPartial, updateInterval, cancellationToken).ConfigureAwait(false),
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
                    var output = await RunOpenAiOnceAsync(options, prompt, onPartial, updateInterval, cancellationToken,
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

    private async Task<string> RunOpenAiOnceAsync(IntelligenceXClientOptions options, string prompt, Func<string, Task>? onPartial,
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
                    await onPartial(snapshot).ConfigureAwait(false);
                }
            }, progressCts.Token);
        }

        var chatOptions = new ChatOptions {
            Model = _settings.Model,
            NewThread = true,
            ReasoningEffort = _settings.ReasoningEffort,
            ReasoningSummary = _settings.ReasoningSummary,
            TelemetryFeature = "reviewer",
            TelemetrySurface = "cli"
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

    private async Task RunCopilotHealthCheckAsync(TimeSpan timeout, CancellationToken cancellationToken) {
        if (_settings.CopilotTransport == CopilotTransportKind.Direct) {
            if (string.IsNullOrWhiteSpace(_settings.CopilotDirectUrl)) {
                throw new InvalidOperationException("Copilot direct transport requires copilot.directUrl.");
            }
            if (!Uri.TryCreate(_settings.CopilotDirectUrl, UriKind.Absolute, out var uri)) {
                throw new InvalidOperationException($"Copilot directUrl is invalid: '{_settings.CopilotDirectUrl}'.");
            }

            using var directTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            directTimeoutCts.CancelAfter(timeout);
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            try {
                using var _ = await PreflightHttp.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, directTimeoutCts.Token)
                    .ConfigureAwait(false);
                return;
            } catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested) {
                throw new TimeoutException(
                    $"Copilot health check timed out after {timeout.TotalSeconds:0.#}s for {uri.Host}.", ex);
            } catch (HttpRequestException ex) when (!ex.StatusCode.HasValue) {
                throw new InvalidOperationException(
                    $"Copilot health check failed for {uri.Host}. Check URL, DNS, proxy, and network settings.", ex);
            }
        }

        var options = BuildCopilotClientOptions(out var launcherDiagnostic);
        options.Validate();
        var effectiveTimeout = options.AutoInstallCli && timeout < TimeSpan.FromSeconds(30)
            ? TimeSpan.FromSeconds(30)
            : timeout;
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(effectiveTimeout);

        var stderrLines = new Queue<string>();
        var stderrLock = new object();
        try {
            await using var client = await CopilotClient.StartAsync(options, timeoutCts.Token).ConfigureAwait(false);
            client.StandardErrorReceived += (_, line) => CaptureCopilotStderr(stderrLines, stderrLock, line);

            var status = await client.GetStatusAsync(timeoutCts.Token).ConfigureAwait(false);
            if (_settings.Diagnostics) {
                var version = string.IsNullOrWhiteSpace(status.Version) ? "unknown" : status.Version;
                Console.Error.WriteLine($"Copilot preflight status OK: version={version}; protocol={status.ProtocolVersion}.");
            }

            var auth = await client.GetAuthStatusAsync(timeoutCts.Token).ConfigureAwait(false);
            if (!auth.IsAuthenticated) {
                throw new UnauthorizedAccessException(BuildCopilotAuthFailureMessage(auth, launcherDiagnostic, stderrLines, stderrLock));
            }
            if (_settings.Diagnostics) {
                var login = string.IsNullOrWhiteSpace(auth.Login) ? "unknown" : auth.Login;
                var authType = string.IsNullOrWhiteSpace(auth.AuthType) ? "unknown" : auth.AuthType;
                Console.Error.WriteLine($"Copilot preflight auth OK: login={login}; authType={authType}.");
            }

            var model = ResolveCopilotModel(_settings);
            if (!string.IsNullOrWhiteSpace(model)) {
                await ValidateCopilotModelAsync(client, model!, timeoutCts.Token).ConfigureAwait(false);
            }
        } catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested) {
            throw new TimeoutException(BuildCopilotHealthTimeoutMessage(effectiveTimeout, launcherDiagnostic, stderrLines, stderrLock), ex);
        }
    }

    private CopilotClientOptions BuildCopilotClientOptions() {
        return BuildCopilotClientOptions(out _);
    }

    private CopilotClientOptions BuildCopilotClientOptions(out string launcherDiagnostic) {
        var options = new CopilotClientOptions();
        var launcher = ApplyCopilotLauncher(options);
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
        ApplyCopilotEnvironment(options);
        launcherDiagnostic = BuildCopilotLauncherDiagnostic(launcher, options);
        if (_settings.Diagnostics) {
            Console.Error.WriteLine(launcherDiagnostic);
        }
        return options;
    }

    private string ApplyCopilotLauncher(CopilotClientOptions options) {
        var launcher = ResolveCopilotLauncher(_settings);

        if (string.Equals(launcher, "gh", StringComparison.OrdinalIgnoreCase)) {
            options.CliPath = "gh";
            options.CliArgs.Add("copilot");
            options.CliArgs.Add("--");
            return "gh";
        }

        if (!string.IsNullOrWhiteSpace(_settings.CopilotCliPath)) {
            options.CliPath = _settings.CopilotCliPath;
        }
        return "binary";
    }

    internal static string ResolveCopilotLauncherForTests(ReviewSettings settings) =>
        ResolveCopilotLauncher(settings);

    private static string ResolveCopilotLauncher(ReviewSettings settings) {
        var launcher = ReviewSettings.NormalizeCopilotLauncher(settings.CopilotLauncher, "binary");
        if (!string.Equals(launcher, "auto", StringComparison.OrdinalIgnoreCase)) {
            return launcher;
        }

        return "binary";
    }

    internal static string? ResolveCopilotModel(ReviewSettings settings) {
        if (!string.IsNullOrWhiteSpace(settings.CopilotModel)) {
            return settings.CopilotModel!.Trim();
        }
        if (string.IsNullOrWhiteSpace(settings.Model) ||
            string.Equals(settings.Model, OpenAIModelCatalog.DefaultModel, StringComparison.OrdinalIgnoreCase)) {
            return null;
        }
        return settings.Model.Trim();
    }

    internal static TimeSpan ResolveCopilotPromptTimeout(ReviewSettings settings) {
        var configuredSeconds = Math.Max(1, settings.WaitSeconds);
        return TimeSpan.FromSeconds(Math.Max(configuredSeconds, MinimumCopilotPromptWaitSeconds));
    }

    private string BuildCopilotLauncherDiagnostic(string launcher, CopilotClientOptions options) {
        var cliPath = string.IsNullOrWhiteSpace(options.CliPath) ? "copilot" : options.CliPath;
        var prefixArgs = options.CliArgs.Count == 0 ? "(none)" : string.Join(" ", options.CliArgs);
        var cliUrl = string.IsNullOrWhiteSpace(options.CliUrl) ? "not configured" : "configured";
        return string.Concat(
            "Copilot launcher resolved: ",
            launcher,
            "; transport=",
            _settings.CopilotTransport.ToString().ToLowerInvariant(),
            "; cliPath=",
            cliPath,
            "; prefixArgs=",
            prefixArgs,
            "; cliUrl=",
            cliUrl,
            ".");
    }


    private async Task<string> RunCopilotAsync(string prompt, Func<string, Task>? onPartial, TimeSpan? updateInterval,
        CancellationToken cancellationToken) {
        if (_settings.CopilotTransport == CopilotTransportKind.Direct) {
            return await RunCopilotDirectAsync(prompt, cancellationToken).ConfigureAwait(false);
        }
        if (ShouldUseCopilotPromptMode(_settings)) {
            try {
                return await RunCopilotPromptAsync(prompt, onPartial, cancellationToken).ConfigureAwait(false);
            } catch (Exception ex) when (!cancellationToken.IsCancellationRequested &&
                                         ShouldFallbackFromCopilotPromptFailure(ex)) {
                if (_settings.Diagnostics) {
                    Console.Error.WriteLine(
                        $"Copilot prompt mode failed ({ex.GetType().Name}); falling back to CLI server-session mode.");
                }
            }
        }
        return await RunCopilotCliSessionAsync(prompt, onPartial, updateInterval, cancellationToken).ConfigureAwait(false);
    }

    internal static bool ShouldFallbackFromCopilotPromptFailure(Exception ex) {
        if (ex is InvalidOperationException invalid &&
            invalid.Message.StartsWith("Copilot CLI not found or failed to start in prompt mode",
                StringComparison.OrdinalIgnoreCase)) {
            return true;
        }
        return ex.InnerException is not null && ShouldFallbackFromCopilotPromptFailure(ex.InnerException);
    }

    private async Task<string> RunCopilotCliSessionAsync(string prompt, Func<string, Task>? onPartial,
        TimeSpan? updateInterval, CancellationToken cancellationToken) {
        var options = BuildCopilotClientOptions(out var launcherDiagnostic);

        await using var client = await CopilotClient.StartAsync(options, cancellationToken).ConfigureAwait(false);
        var stderrLines = new Queue<string>();
        var stderrLock = new object();
        client.StandardErrorReceived += (_, line) => CaptureCopilotStderr(stderrLines, stderrLock, line);
        var session = await client.CreateSessionAsync(new CopilotSessionOptions {
            Model = ResolveCopilotModel(_settings),
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
            tcs.TrySetException(new TimeoutException(BuildCopilotTimeoutMessage(launcherDiagnostic, stderrLines, stderrLock))));

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

    private async Task<string> RunCopilotPromptAsync(string prompt, Func<string, Task>? onPartial,
        CancellationToken cancellationToken) {
        var options = BuildCopilotClientOptions(out var launcherDiagnostic);
        var client = new ReviewerCopilotPromptRunner(options);
        var timeout = ResolveCopilotPromptTimeout(_settings);
        if (_settings.Diagnostics) {
            Console.Error.WriteLine("Copilot review execution mode: prompt.");
            if (timeout.TotalSeconds > Math.Max(1, _settings.WaitSeconds)) {
                Console.Error.WriteLine(
                    $"Copilot prompt timeout raised from {_settings.WaitSeconds}s to {timeout.TotalSeconds:0}s to cover prompt-mode startup and streaming latency.");
            }
        }
        try {
            var result = await client.RunAsync(
                prompt,
                ResolveCopilotModel(_settings),
                timeout,
                cancellationToken).ConfigureAwait(false);
            if (onPartial is not null) {
                await onPartial(result.Response).ConfigureAwait(false);
            }
            if (_settings.Diagnostics && !string.IsNullOrWhiteSpace(result.UsageSummary)) {
                Console.Error.WriteLine($"Copilot prompt usage: {result.UsageSummary}");
            }
            return result.Response;
        } catch (TimeoutException ex) {
            throw new TimeoutException(BuildCopilotPromptTimeoutMessage(launcherDiagnostic, ex.Message), ex);
        }
    }

    private static bool ShouldUseCopilotPromptMode(ReviewSettings settings) =>
        string.IsNullOrWhiteSpace(settings.CopilotCliUrl);

    private string BuildCopilotPromptTimeoutMessage(string launcherDiagnostic, string detail) {
        var sb = new StringBuilder();
        sb.Append(detail);
        sb.Append(' ');
        sb.Append(launcherDiagnostic);
        sb.Append(" The reviewer used Copilot CLI prompt mode; if this persists, check runner Copilot availability and prompt size.");
        return sb.ToString().TrimEnd();
    }

    private string BuildCopilotTimeoutMessage(string launcherDiagnostic, Queue<string> stderrLines, object stderrLock) {
        var sb = new StringBuilder();
        sb.Append("Copilot review timed out after ");
        sb.Append(_settings.WaitSeconds);
        sb.Append(" seconds. ");
        sb.Append(launcherDiagnostic);
        sb.Append(" If this run uses the GitHub CLI wrapper, try copilot.launcher=binary with copilot.autoInstall=true; ");
        sb.Append("the wrapper can launch the CLI but still hang in reviewer server mode on some runners.");

        AppendCopilotStderr(sb, stderrLines, stderrLock, include: _settings.Diagnostics);
        return sb.ToString().TrimEnd();
    }

    private string BuildCopilotHealthTimeoutMessage(TimeSpan timeout, string launcherDiagnostic, Queue<string> stderrLines, object stderrLock) {
        var sb = new StringBuilder();
        sb.Append("Copilot health check timed out after ");
        sb.Append(timeout.TotalSeconds.ToString("0"));
        sb.Append(" seconds while starting the CLI and checking auth/status. ");
        sb.Append(launcherDiagnostic);
        sb.Append(" This usually means the CLI server mode is waiting on interactive auth or is not responding in GitHub Actions.");
        AppendCopilotStderr(sb, stderrLines, stderrLock, include: _settings.Diagnostics);
        return sb.ToString().TrimEnd();
    }

    private string BuildCopilotAuthFailureMessage(CopilotAuthStatus auth, string launcherDiagnostic, Queue<string> stderrLines, object stderrLock) {
        var sb = new StringBuilder();
        sb.Append("Copilot CLI is not authenticated for this non-interactive reviewer run. ");
        sb.Append(launcherDiagnostic);
        if (!string.IsNullOrWhiteSpace(auth.StatusMessage)) {
            sb.Append(" Status: ");
            sb.Append(auth.StatusMessage);
            sb.Append('.');
        }
        sb.Append(" Sign in on the runner before using provider=copilot, use a self-hosted runner with a persisted Copilot CLI session, or configure copilot.transport=direct with an explicit token-backed endpoint.");
        AppendCopilotStderr(sb, stderrLines, stderrLock, include: _settings.Diagnostics);
        return sb.ToString().TrimEnd();
    }

    private async Task ValidateCopilotModelAsync(CopilotClient client, string model, CancellationToken cancellationToken) {
        var models = await client.ListModelsAsync(cancellationToken).ConfigureAwait(false);
        if (models.Count == 0) {
            if (_settings.Diagnostics) {
                Console.Error.WriteLine("Copilot preflight model list returned no entries; skipping model availability validation.");
            }
            return;
        }

        foreach (var candidate in models) {
            if (string.Equals(candidate.Id, model, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(candidate.Name, model, StringComparison.OrdinalIgnoreCase)) {
                if (_settings.Diagnostics) {
                    Console.Error.WriteLine($"Copilot preflight model OK: {model}.");
                }
                return;
            }
        }

        var available = string.Join(", ", models
            .Select(candidate => string.IsNullOrWhiteSpace(candidate.Name) ||
                                 string.Equals(candidate.Id, candidate.Name, StringComparison.OrdinalIgnoreCase)
                ? candidate.Id
                : $"{candidate.Id} ({candidate.Name})")
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Take(12));
        throw new InvalidOperationException(
            string.IsNullOrWhiteSpace(available)
                ? $"Copilot model '{model}' is not available to this account."
                : $"Copilot model '{model}' is not available to this account. Available models: {available}.");
    }

    private static void AppendCopilotStderr(StringBuilder sb, Queue<string> stderrLines, object stderrLock, bool include) {
        if (!include) {
            return;
        }
        List<string> lines;
        lock (stderrLock) {
            lines = new List<string>(stderrLines);
        }
        if (lines.Count == 0) {
            return;
        }

        sb.AppendLine();
        sb.AppendLine("Recent Copilot CLI stderr:");
        for (var i = Math.Max(0, lines.Count - 6); i < lines.Count; i++) {
            sb.Append("  ");
            sb.AppendLine(lines[i]);
        }
    }

    private static void CaptureCopilotStderr(Queue<string> stderrLines, object stderrLock, string? line) {
        if (string.IsNullOrWhiteSpace(line)) {
            return;
        }
        lock (stderrLock) {
            stderrLines.Enqueue(line.Trim());
            while (stderrLines.Count > 8) {
                stderrLines.Dequeue();
            }
        }
    }

    private async Task<string> RunCopilotDirectAsync(string prompt, CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(_settings.CopilotDirectUrl)) {
            throw new InvalidOperationException("Copilot direct transport requires copilot.directUrl.");
        }
        var model = ResolveCopilotModel(_settings);
        if (string.IsNullOrWhiteSpace(model)) {
            throw new InvalidOperationException("Copilot direct transport requires copilot.model or review.model to be set.");
        }
        var token = ResolveCopilotDirectToken();
        if (string.IsNullOrWhiteSpace(token) && !HasAuthorizationHeader(_settings.CopilotDirectHeaders)) {
            throw new InvalidOperationException("Copilot direct transport requires a token or Authorization header.");
        }
        if (HasAuthorizationHeader(_settings.CopilotDirectHeaders)) {
            SecretsAudit.Record("Copilot direct Authorization header from copilot.directHeaders");
        }
        var options = new IntelligenceX.Copilot.Direct.CopilotDirectOptions {
            Url = _settings.CopilotDirectUrl,
            Token = token,
            Timeout = TimeSpan.FromSeconds(Math.Max(1, _settings.CopilotDirectTimeoutSeconds))
        };
        foreach (var entry in _settings.CopilotDirectHeaders) {
            if (string.IsNullOrWhiteSpace(entry.Key) || entry.Value is null) {
                continue;
            }
            options.Headers[entry.Key] = entry.Value;
        }
        options.Validate();

        using var client = new IntelligenceX.Copilot.Direct.CopilotDirectClient(options);
        return await client.ChatAsync(prompt, model!, cancellationToken).ConfigureAwait(false);
    }

    private string? ResolveCopilotDirectToken() {
        if (!string.IsNullOrWhiteSpace(_settings.CopilotDirectTokenEnv)) {
            var value = Environment.GetEnvironmentVariable(_settings.CopilotDirectTokenEnv);
            if (!string.IsNullOrWhiteSpace(value)) {
                SecretsAudit.Record($"Copilot direct token from {_settings.CopilotDirectTokenEnv}");
                return value;
            }
        }
        if (!string.IsNullOrWhiteSpace(_settings.CopilotDirectToken)) {
            SecretsAudit.Record("Copilot direct token from config (copilot.directToken)");
            return _settings.CopilotDirectToken;
        }
        return null;
    }

    private static bool HasAuthorizationHeader(IReadOnlyDictionary<string, string> headers) {
        foreach (var entry in headers) {
            if (string.Equals(entry.Key, "Authorization", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(entry.Value)) {
                return true;
            }
        }
        return false;
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

    private void ApplyCopilotEnvironment(CopilotClientOptions options) {
        options.InheritEnvironment = _settings.CopilotInheritEnvironment;

        if (_settings.CopilotEnvAllowlist.Count == 0 && _settings.CopilotEnv.Count == 0) {
            return;
        }

        foreach (var name in _settings.CopilotEnvAllowlist) {
            if (string.IsNullOrWhiteSpace(name)) {
                continue;
            }
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value)) {
                options.Environment[name] = value;
                SecretsAudit.Record($"Copilot CLI env from {name}");
            }
        }

        foreach (var entry in _settings.CopilotEnv) {
            if (string.IsNullOrWhiteSpace(entry.Key) || entry.Value is null) {
                continue;
            }
            options.Environment[entry.Key] = entry.Value;
            SecretsAudit.Record($"Copilot CLI env set: {entry.Key}");
        }
    }

}
