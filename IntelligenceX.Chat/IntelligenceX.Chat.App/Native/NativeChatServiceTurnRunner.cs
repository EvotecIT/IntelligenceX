using System;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Client;

namespace IntelligenceX.Chat.App.Native;

/// <summary>
/// Native-shell chat runner backed by the existing named-pipe chat service protocol.
/// </summary>
internal sealed class NativeChatServiceTurnRunner : INativeChatTurnRunner, IAsyncDisposable {
    private const string DefaultPipeName = "intelligencex.chat";
    private static readonly TimeSpan InitialConnectProbeTimeout = TimeSpan.FromMilliseconds(750);
    private readonly string _pipeName;
    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private readonly NativeChatServiceProcessHost _processHost = new();
    private ChatServiceClient? _client;

    public NativeChatServiceTurnRunner(string? pipeName = null) {
        _pipeName = string.IsNullOrWhiteSpace(pipeName) ? DefaultPipeName : pipeName.Trim();
    }

    public async Task<NativeChatTurnResult> SendAsync(
        NativeChatTurnRequest request,
        NativeChatTurnCallbacks callbacks,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(request);
        callbacks ??= new NativeChatTurnCallbacks();

        var client = await EnsureConnectedAsync(callbacks.Status, cancellationToken).ConfigureAwait(false);
        var requestId = string.IsNullOrWhiteSpace(request.RequestId)
            ? "native-" + Guid.NewGuid().ToString("N")
            : request.RequestId.Trim();
        var chatRequest = new ChatRequest {
            RequestId = requestId,
            ThreadId = request.ThreadId,
            Text = request.Text
        };

        async void OnMessage(ChatServiceMessage message) {
            if (!string.Equals(message.RequestId, requestId, StringComparison.Ordinal)) {
                return;
            }

            try {
                switch (message) {
                    case ChatStatusMessage status:
                        await callbacks.Status(FormatStatus(status)).ConfigureAwait(false);
                        break;
                    case ChatDeltaMessage delta:
                        await callbacks.Delta(delta.Text).ConfigureAwait(false);
                        break;
                    case ChatAssistantProvisionalMessage provisional:
                        await callbacks.Interim(provisional.Text).ConfigureAwait(false);
                        break;
                    case ChatInterimResultMessage interim:
                        await callbacks.Interim(interim.Text).ConfigureAwait(false);
                        break;
                }
            } catch {
                // UI callbacks are best-effort; the final response still completes the turn.
            }
        }

        client.MessageReceived += OnMessage;
        try {
            await callbacks.Status("Runtime accepted the turn...").ConfigureAwait(false);
            var result = await client.RequestAsync<ChatResultMessage>(chatRequest, cancellationToken).ConfigureAwait(false);
            return new NativeChatTurnResult(result.Text, result.ThreadId);
        } finally {
            client.MessageReceived -= OnMessage;
        }
    }

    public async Task CancelAsync(string requestId, CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(requestId)) {
            return;
        }

        var client = _client;
        if (client is null) {
            return;
        }

        _ = await client.RequestAsync<AckMessage>(
                new CancelChatRequest {
                    RequestId = "native-cancel-" + Guid.NewGuid().ToString("N"),
                    ChatRequestId = requestId.Trim()
                },
                cancellationToken)
            .ConfigureAwait(false);
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
        var completion = new TaskCompletionSource<NativeLoginResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        async void OnMessage(ChatServiceMessage message) {
            try {
                switch (message) {
                    case ChatGptLoginUrlMessage url when string.Equals(url.RequestId, requestId, StringComparison.Ordinal):
                        if (Uri.TryCreate(url.Url, UriKind.Absolute, out var uri)) {
                            await callbacks.Status("Complete sign-in in your browser...").ConfigureAwait(false);
                            await callbacks.OpenUrl(uri).ConfigureAwait(false);
                        }
                        break;
                    case ChatGptLoginPromptMessage prompt when string.Equals(prompt.RequestId, requestId, StringComparison.Ordinal):
                        await callbacks.Status("Waiting for sign-in input...").ConfigureAwait(false);
                        var input = await callbacks.PromptForInput(new NativeLoginPrompt(prompt.LoginId, prompt.PromptId, prompt.Prompt)).ConfigureAwait(false);
                        if (!string.IsNullOrWhiteSpace(input)) {
                            await client.RequestAsync<AckMessage>(
                                    new ChatGptLoginPromptResponseRequest {
                                        RequestId = "native-login-prompt-" + Guid.NewGuid().ToString("N"),
                                        LoginId = prompt.LoginId,
                                        PromptId = prompt.PromptId,
                                        Input = input.Trim()
                                    },
                                    cancellationToken)
                                .ConfigureAwait(false);
                        }
                        break;
                    case ChatGptLoginCompletedMessage completed when string.Equals(completed.RequestId, requestId, StringComparison.Ordinal):
                        completion.TrySetResult(new NativeLoginResult(completed.Ok, null, completed.Error));
                        break;
                    case ErrorMessage error when string.Equals(error.RequestId, requestId, StringComparison.Ordinal):
                        completion.TrySetResult(new NativeLoginResult(false, null, error.Error));
                        break;
                }
            } catch (Exception ex) {
                completion.TrySetException(ex);
            }
        }

        client.MessageReceived += OnMessage;
        try {
            await callbacks.Status("Starting sign-in...").ConfigureAwait(false);
            _ = await client.RequestAsync<ChatGptLoginStartedMessage>(
                    new StartChatGptLoginRequest {
                        RequestId = requestId
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            using var registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
            var result = await completion.Task.ConfigureAwait(false);
            if (result.IsAuthenticated) {
                await callbacks.Status("Signed in.").ConfigureAwait(false);
                return result;
            }

            await callbacks.Status(
                    string.IsNullOrWhiteSpace(result.Error)
                        ? "Sign-in failed."
                        : "Sign-in failed: " + result.Error)
                .ConfigureAwait(false);
            return result;
        } finally {
            client.MessageReceived -= OnMessage;
        }
    }

    public async ValueTask DisposeAsync() {
        var client = Interlocked.Exchange(ref _client, null);
        if (client is not null) {
            await client.DisposeAsync().ConfigureAwait(false);
        }

        _processHost.Dispose();
        _connectLock.Dispose();
    }

    private async Task<ChatServiceClient> EnsureConnectedAsync(Func<string, Task> status, CancellationToken cancellationToken) {
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
            client.Disconnected += OnClientDisconnected;
            try {
                await status("Connecting to local chat service...").ConfigureAwait(false);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(InitialConnectProbeTimeout);
                await client.ConnectAsync(_pipeName, timeout.Token).ConfigureAwait(false);
            } catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested) {
                await client.DisposeAsync().ConfigureAwait(false);
                await _processHost.EnsureRunningAsync(_pipeName, status, cancellationToken).ConfigureAwait(false);

                client = new ChatServiceClient();
                client.Disconnected += OnClientDisconnected;
                await RetryConnectAsync(client, status, cancellationToken).ConfigureAwait(false);
            }

            _client = client;
            return client;
        } catch {
            _client = null;
            throw;
        } finally {
            _connectLock.Release();
        }
    }

    private async Task RetryConnectAsync(ChatServiceClient client, Func<string, Task> status, CancellationToken cancellationToken) {
        Exception? lastError = null;
        for (var attempt = 1; attempt <= 8; attempt++) {
            cancellationToken.ThrowIfCancellationRequested();
            try {
                await status("Connecting to local chat service (" + attempt + "/8)...").ConfigureAwait(false);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(2));
                await client.ConnectAsync(_pipeName, timeout.Token).ConfigureAwait(false);
                return;
            } catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested) {
                lastError = ex;
                await Task.Delay(TimeSpan.FromMilliseconds(350), cancellationToken).ConfigureAwait(false);
            }
        }

        throw new InvalidOperationException("Unable to connect to local chat service.", lastError);
    }

    private void OnClientDisconnected(ChatServiceClient client) {
        if (!ReferenceEquals(_client, client)) {
            return;
        }

        _client = null;
    }

    private static string FormatStatus(ChatStatusMessage status) {
        if (!string.IsNullOrWhiteSpace(status.Message)) {
            return status.Message!;
        }

        if (!string.IsNullOrWhiteSpace(status.ToolName)) {
            return status.Status + ": " + status.ToolName;
        }

        return status.Status;
    }
}
