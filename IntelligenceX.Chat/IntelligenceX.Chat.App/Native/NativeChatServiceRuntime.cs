using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.App.Launch;
using IntelligenceX.Chat.Client;

namespace IntelligenceX.Chat.App.Native;

/// <summary>
/// Adds native-shell authentication prompts to the shared desktop service and turn runtime.
/// </summary>
internal sealed class NativeChatServiceRuntime : INativeChatRuntime, IAsyncDisposable {
    private const string DefaultPipeName = "intelligencex.chat";
    private const int LoginTimeoutSeconds = 180;
    private const int ConnectRetryCount = 8;
    private static readonly TimeSpan InitialConnectProbeTimeout = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan ConnectAttemptTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ConnectRetryDelay = TimeSpan.FromMilliseconds(350);
    private static readonly TimeSpan ServiceStartupProbeDelay = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan LoginCancellationTimeout = TimeSpan.FromSeconds(5);

    private readonly string _pipeName;
    private readonly Func<ChatServiceLaunchProfileOptions?>? _profileOptionsProvider;
    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private readonly ChatServiceProcessHost _processHost = new();
    private ChatServiceClient? _client;
    private ChatServiceTurnRunner? _turnRunner;
    private bool _detachOwnedServiceOnDispose;

    internal SessionPolicyDto? SessionPolicy { get; private set; }

    public NativeChatServiceRuntime(
        string? pipeName = null,
        Func<ChatServiceLaunchProfileOptions?>? profileOptionsProvider = null) {
        _pipeName = string.IsNullOrWhiteSpace(pipeName) ? DefaultPipeName : pipeName.Trim();
        _profileOptionsProvider = profileOptionsProvider;
        _processHost.OutputReceived += line => StartupLog.Write("[native-service] " + line);
        _processHost.ErrorReceived += line => StartupLog.Write("[native-service:err] " + line);
        _processHost.Exited += () => StartupLog.Write("Native chat service exited.");
    }

    public async Task<ChatTurnRunResult> RunTurnAsync(
        ChatRequest request,
        Func<ChatTurnUpdate, CancellationToken, ValueTask>? onUpdate,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(request);
        var runner = await EnsureTurnRunnerAsync(_ => Task.CompletedTask, cancellationToken).ConfigureAwait(false);
        return await runner.RunAsync(request, onUpdate, cancellationToken).ConfigureAwait(false);
    }

    public async Task CancelTurnAsync(string requestId, CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(requestId)) {
            return;
        }

        var runner = _turnRunner;
        if (runner is not null) {
            await runner.CancelAsync(requestId.Trim(), cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<NativeLoginResult> EnsureLoginAsync(
        Func<string, Task> status,
        CancellationToken cancellationToken) {
        status ??= _ => Task.CompletedTask;
        var client = await EnsureConnectedAsync(status, cancellationToken).ConfigureAwait(false);
        await status("Checking sign-in status...").ConfigureAwait(false);
        var login = await client.RequestAsync<LoginStatusMessage>(
                new EnsureLoginRequest {
                    RequestId = "native-login-check-" + Guid.NewGuid().ToString("N")
                },
                cancellationToken)
            .ConfigureAwait(false);

        var text = login.IsAuthenticated
            ? string.IsNullOrWhiteSpace(login.AccountId)
                ? "Signed in."
                : "Signed in as " + login.AccountId!.Trim() + "."
            : "Sign-in required.";
        await status(text).ConfigureAwait(false);
        return new NativeLoginResult(login.IsAuthenticated, login.AccountId);
    }

    public async Task<NativeLoginResult> StartLoginAsync(
        NativeLoginCallbacks callbacks,
        CancellationToken cancellationToken) {
        callbacks ??= new NativeLoginCallbacks();
        var client = await EnsureConnectedAsync(callbacks.Status, cancellationToken).ConfigureAwait(false);
        var requestId = "native-login-" + Guid.NewGuid().ToString("N");
        var messages = Channel.CreateUnbounded<ChatServiceMessage>(new UnboundedChannelOptions {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });

        void OnMessage(ChatServiceMessage message) {
            if (string.Equals(message.RequestId, requestId, StringComparison.Ordinal)) {
                _ = messages.Writer.TryWrite(message);
            }
        }

        string? loginId = null;
        client.MessageReceived += OnMessage;
        try {
            await callbacks.Status("Starting sign-in...").ConfigureAwait(false);
            var started = await client.RequestAsync<ChatGptLoginStartedMessage>(
                    new StartChatGptLoginRequest {
                        RequestId = requestId,
                        TimeoutSeconds = LoginTimeoutSeconds
                    },
                    cancellationToken)
                .ConfigureAwait(false);
            loginId = started.LoginId;

            await foreach (var message in messages.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false)) {
                switch (message) {
                    case ChatGptLoginUrlMessage url when IsCurrentLogin(url.LoginId, loginId):
                        if (Uri.TryCreate(url.Url, UriKind.Absolute, out var uri)) {
                            await callbacks.Status("Complete sign-in in your browser...").ConfigureAwait(false);
                            await callbacks.OpenUrl(uri).ConfigureAwait(false);
                        }
                        break;
                    case ChatGptLoginPromptMessage prompt when IsCurrentLogin(prompt.LoginId, loginId):
                        await callbacks.Status("Waiting for sign-in input...").ConfigureAwait(false);
                        var input = await callbacks.PromptForInput(
                                new NativeLoginPrompt(prompt.LoginId, prompt.PromptId, prompt.Prompt))
                            .ConfigureAwait(false);
                        if (string.IsNullOrWhiteSpace(input)) {
                            await CancelLoginAsync(client, prompt.LoginId).ConfigureAwait(false);
                            await callbacks.Status("Sign-in canceled.").ConfigureAwait(false);
                            return new NativeLoginResult(false, null, IsCanceled: true);
                        }

                        _ = await client.RequestAsync<AckMessage>(
                                new ChatGptLoginPromptResponseRequest {
                                    RequestId = "native-login-prompt-" + Guid.NewGuid().ToString("N"),
                                    LoginId = prompt.LoginId,
                                    PromptId = prompt.PromptId,
                                    Input = input.Trim()
                                },
                                cancellationToken)
                            .ConfigureAwait(false);
                        break;
                    case ChatGptLoginCompletedMessage completed when IsCurrentLogin(completed.LoginId, loginId):
                        if (completed.Ok) {
                            await callbacks.Status("Signed in.").ConfigureAwait(false);
                            return new NativeLoginResult(true, null);
                        }

                        var canceled = string.Equals(completed.Error?.Trim(), "Canceled.", StringComparison.OrdinalIgnoreCase);
                        await callbacks.Status(canceled
                                ? "Sign-in canceled."
                                : string.IsNullOrWhiteSpace(completed.Error)
                                    ? "Sign-in failed."
                                    : "Sign-in failed: " + completed.Error)
                            .ConfigureAwait(false);
                        return new NativeLoginResult(false, null, canceled ? null : completed.Error, canceled);
                    case ErrorMessage error:
                        await callbacks.Status("Sign-in failed: " + error.Error).ConfigureAwait(false);
                        return new NativeLoginResult(false, null, error.Error);
                }
            }

            return new NativeLoginResult(false, null, "The sign-in flow ended without a result.");
        } catch (OperationCanceledException) {
            if (!string.IsNullOrWhiteSpace(loginId)) {
                await CancelLoginAsync(client, loginId).ConfigureAwait(false);
            }
            throw;
        } finally {
            client.MessageReceived -= OnMessage;
            messages.Writer.TryComplete();
        }
    }

    public async ValueTask DisposeAsync() {
        var client = Interlocked.Exchange(ref _client, null);
        _turnRunner = null;
        if (client is not null) {
            client.Disconnected -= OnClientDisconnected;
            await client.DisposeAsync().ConfigureAwait(false);
        }

        if (_detachOwnedServiceOnDispose) {
            _processHost.Stop(terminateProcess: false);
        }
        _processHost.Dispose();
        _connectLock.Dispose();
    }

    private async Task<ChatServiceTurnRunner> EnsureTurnRunnerAsync(
        Func<string, Task> status,
        CancellationToken cancellationToken) {
        _ = await EnsureConnectedAsync(status, cancellationToken).ConfigureAwait(false);
        return _turnRunner ?? throw new InvalidOperationException("The chat turn runtime was not initialized.");
    }

    private async Task<ChatServiceClient> EnsureConnectedAsync(
        Func<string, Task> status,
        CancellationToken cancellationToken) {
        var existing = _client;
        if (existing is not null) {
            return existing;
        }

        await _connectLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            if (_client is not null) {
                return _client;
            }

            var client = new ChatServiceClient();
            try {
                await status("Connecting to local chat service...").ConfigureAwait(false);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(InitialConnectProbeTimeout);
                await client.ConnectAsync(_pipeName, timeout.Token).ConfigureAwait(false);
            } catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested) {
                await client.DisposeAsync().ConfigureAwait(false);
                await StartServiceAsync(status, cancellationToken).ConfigureAwait(false);
                client = new ChatServiceClient();
                await RetryConnectAsync(client, status, cancellationToken).ConfigureAwait(false);
            }

            try {
                await SynchronizeSelectedProfileAsync(client, status, cancellationToken).ConfigureAwait(false);
                var hello = await client.RequestAsync<HelloMessage>(
                        new HelloRequest { RequestId = "native-hello-" + Guid.NewGuid().ToString("N") },
                        cancellationToken)
                    .ConfigureAwait(false);
                SessionPolicy = hello.Policy;
            } catch {
                await client.DisposeAsync().ConfigureAwait(false);
                throw;
            }
            client.Disconnected += OnClientDisconnected;
            _client = client;
            _turnRunner = new ChatServiceTurnRunner(client);
            await status("Runtime connected.").ConfigureAwait(false);
            return client;
        } catch {
            _client = null;
            _turnRunner = null;
            throw;
        } finally {
            _connectLock.Release();
        }
    }

    private async Task StartServiceAsync(Func<string, Task> status, CancellationToken cancellationToken) {
        await status("Starting local chat service...").ConfigureAwait(false);
        var startOptions = CreateServiceProcessStartOptions();
        var result = await _processHost.EnsureRunningAsync(startOptions, cancellationToken)
            .ConfigureAwait(false);
        if (result.IsRunning) {
            _detachOwnedServiceOnDispose = ResolveDetachedServiceOwnership(
                _detachOwnedServiceOnDispose,
                result.Launched,
                startOptions.DetachedServiceMode);
            return;
        }

        var message = result.Failure switch {
            ChatServiceProcessStartFailure.SourceNotFound => "Local chat service payload was not found.",
            ChatServiceProcessStartFailure.PayloadNotFound => "Local chat service executable was not found.",
            ChatServiceProcessStartFailure.StagingFailed => "Local chat service staging failed.",
            ChatServiceProcessStartFailure.ExitedDuringStartup => "Local chat service exited during startup.",
            _ => "Local chat service failed to start."
        };
        await status(message).ConfigureAwait(false);
        throw new InvalidOperationException(message, result.Exception);
    }

    internal static bool ResolveDetachedServiceOwnership(
        bool currentlyOwned,
        bool launched,
        bool detachedServiceMode) =>
        currentlyOwned || launched && detachedServiceMode;

    private async Task SynchronizeSelectedProfileAsync(
        ChatServiceClient client,
        Func<string, Task> status,
        CancellationToken cancellationToken) {
        var options = _profileOptionsProvider?.Invoke();
        if (options is null) {
            return;
        }

        await status("Applying selected chat profile...").ConfigureAwait(false);
        var desiredProfile = ChatServiceLaunchProfileMapper.NormalizeProfileName(options.LoadProfileName);
        var profiles = await client.ListProfilesAsync(cancellationToken).ConfigureAwait(false);
        if (!string.Equals(profiles.ActiveProfile, desiredProfile, StringComparison.OrdinalIgnoreCase)
            && profiles.Profiles?.Any(profile => string.Equals(profile, desiredProfile, StringComparison.OrdinalIgnoreCase)) == true) {
            var selected = await client.SetProfileAsync(
                    desiredProfile,
                    newThread: false,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            EnsureAccepted(selected, "select the configured profile");
        }

        if (!options.ApplyRuntimeOverrides) {
            await status("Selected existing service profile without app runtime overrides.").ConfigureAwait(false);
            return;
        }

        BuildPackToggleLists(options.PackToggles, out var enabledPackIds, out var disabledPackIds);
        var applied = await client.ApplyRuntimeSettingsAsync(
                model: options.Model,
                openAITransport: options.OpenAITransport,
                openAIBaseUrl: options.OpenAIBaseUrl ?? string.Empty,
                openAIApiKey: options.OpenAIApiKey,
                openAIAuthMode: options.OpenAIAuthMode,
                openAIBasicUsername: options.OpenAIBasicUsername,
                openAIBasicPassword: options.OpenAIBasicPassword,
                openAIAccountId: options.OpenAIAccountId ?? string.Empty,
                clearOpenAIApiKey: options.ClearOpenAIApiKey,
                clearOpenAIBasicAuth: options.ClearOpenAIBasicAuth,
                openAIStreaming: options.OpenAIStreaming,
                openAIAllowInsecureHttp: options.OpenAIAllowInsecureHttp,
                reasoningEffort: options.ReasoningEffort ?? string.Empty,
                reasoningSummary: options.ReasoningSummary ?? string.Empty,
                textVerbosity: options.TextVerbosity ?? string.Empty,
                temperature: options.Temperature,
                imageGenerationEnabled: options.ImageGenerationEnabled,
                imageGenerationQuality: options.ClearImageGenerationQuality ? string.Empty : options.ImageGenerationQuality,
                imageGenerationSize: options.ClearImageGenerationSize ? string.Empty : options.ImageGenerationSize,
                imageGenerationOutputFormat: options.ClearImageGenerationOutputFormat ? string.Empty : options.ImageGenerationOutputFormat,
                imageGenerationOutputCompression: options.ImageGenerationOutputCompression,
                clearImageGenerationOutputCompression: options.ClearImageGenerationOutputCompression,
                imageGenerationBackground: options.ClearImageGenerationBackground ? string.Empty : options.ImageGenerationBackground,
                imageGenerationOutputDirectory: options.ClearImageGenerationOutputDirectory ? string.Empty : options.ImageGenerationOutputDirectory,
                enablePackIds: enabledPackIds,
                disablePackIds: disabledPackIds,
                profileName: ChatServiceLaunchProfileMapper.NormalizeProfileName(options.SaveProfileName ?? desiredProfile),
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        EnsureAccepted(applied, "apply the configured runtime settings");
    }

    private static void BuildPackToggleLists(
        IReadOnlyList<ChatServicePackToggle>? toggles,
        out string[]? enabledPackIds,
        out string[]? disabledPackIds) {
        enabledPackIds = toggles?
            .Where(static toggle => toggle.Enabled && !string.IsNullOrWhiteSpace(toggle.PackId))
            .Select(static toggle => toggle.PackId.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        disabledPackIds = toggles?
            .Where(static toggle => !toggle.Enabled && !string.IsNullOrWhiteSpace(toggle.PackId))
            .Select(static toggle => toggle.PackId.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (enabledPackIds is { Length: 0 }) enabledPackIds = null;
        if (disabledPackIds is { Length: 0 }) disabledPackIds = null;
    }

    private static void EnsureAccepted(AckMessage response, string action) {
        if (response.Ok) {
            return;
        }

        throw new InvalidOperationException(
            string.IsNullOrWhiteSpace(response.Message)
                ? "The chat service could not " + action + "."
                : response.Message);
    }

    internal ChatServiceProcessStartOptions CreateServiceProcessStartOptions() =>
        new() {
            PipeName = _pipeName,
            DetachedServiceMode = ChatAppLaunchModeResolver.IsTruthy(Environment.GetEnvironmentVariable("IXCHAT_DETACHED_SERVICE")),
            ParentProcessId = Environment.ProcessId,
            ProfileOptions = _profileOptionsProvider?.Invoke(),
            AppBaseDirectory = AppContext.BaseDirectory,
            StartupExitProbeDelay = ServiceStartupProbeDelay
        };

    private async Task RetryConnectAsync(
        ChatServiceClient client,
        Func<string, Task> status,
        CancellationToken cancellationToken) {
        Exception? lastError = null;
        for (var attempt = 1; attempt <= ConnectRetryCount; attempt++) {
            cancellationToken.ThrowIfCancellationRequested();
            try {
                await status($"Connecting to local chat service ({attempt}/{ConnectRetryCount})...").ConfigureAwait(false);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(ConnectAttemptTimeout);
                await client.ConnectAsync(_pipeName, timeout.Token).ConfigureAwait(false);
                return;
            } catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested) {
                lastError = ex;
                if (attempt < ConnectRetryCount) {
                    await Task.Delay(ConnectRetryDelay, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        await client.DisposeAsync().ConfigureAwait(false);
        throw new InvalidOperationException("Unable to connect to the local chat service.", lastError);
    }

    private void OnClientDisconnected(ChatServiceClient client) {
        if (!ReferenceEquals(Interlocked.CompareExchange(ref _client, null, client), client)) {
            return;
        }

        _turnRunner = null;
        SessionPolicy = null;
        _ = DisposeDisconnectedClientAsync(client);
    }

    private static async Task DisposeDisconnectedClientAsync(ChatServiceClient client) {
        try {
            await client.DisposeAsync().ConfigureAwait(false);
        } catch (Exception ex) {
            StartupLog.Write("Native chat client cleanup failed: " + ex.Message);
        }
    }

    private static async Task CancelLoginAsync(ChatServiceClient client, string loginId) {
        try {
            using var timeout = new CancellationTokenSource(LoginCancellationTimeout);
            _ = await client.RequestAsync<AckMessage>(
                    new CancelChatGptLoginRequest {
                        RequestId = "native-login-cancel-" + Guid.NewGuid().ToString("N"),
                        LoginId = loginId
                    },
                    timeout.Token)
                .ConfigureAwait(false);
        } catch (Exception ex) {
            StartupLog.Write("Native sign-in cancellation was not acknowledged: " + ex.Message);
        }
    }

    private static bool IsCurrentLogin(string candidate, string? loginId) =>
        !string.IsNullOrWhiteSpace(loginId)
        && string.Equals(candidate, loginId, StringComparison.Ordinal);
}
