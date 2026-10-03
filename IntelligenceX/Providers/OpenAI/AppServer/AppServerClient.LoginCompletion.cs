using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Rpc;
using IntelligenceX.Telemetry;
using IntelligenceX.Utils;

namespace IntelligenceX.OpenAI.AppServer;

public sealed partial class AppServerClient {
    // Keep recent outcomes for start-then-wait callers without retaining an unbounded login history.
    private const int RetainedLoginCompletionLimit = 32;
    private readonly object _loginCompletionGate = new();
    private readonly Dictionary<string, LoginCompletion> _loginCompletions = new(StringComparer.Ordinal);
    private readonly Queue<string> _loginCompletionOrder = new();
    private LoginCompletion? _latestLoginCompletion;
    private event EventHandler<LoginCompletion>? LoginCompletionReceived;

    private sealed class LoginCompletion {
        internal LoginCompletion(string? id, string? error) { Id = id; Error = error; }
        internal string? Id { get; }
        internal string? Error { get; }
        internal void Complete(TaskCompletionSource<LoginCompletion> completion) {
            if (Error is not null) completion.TrySetException(new InvalidOperationException(Error));
            else completion.TrySetResult(this);
        }
    }

    private void ResetLatestLoginCompletion() {
        lock (_loginCompletionGate) _latestLoginCompletion = null;
    }

    private void RecordLoginCompletion(JsonRpcNotificationEventArgs args) {
        if (args.Method != "account/login/completed") return;
        var parameters = args.Params?.AsObject();
        if (parameters is null) return;
        var outcome = new LoginCompletion(parameters.GetString("loginId"), parameters.GetBoolean("success", defaultValue: true)
            ? null : parameters.GetString("error") ?? "The app-server login failed.");
        lock (_loginCompletionGate) {
            _latestLoginCompletion = outcome;
            if (outcome.Id is not null) {
                if (!_loginCompletions.ContainsKey(outcome.Id)) {
                    if (_loginCompletions.Count == RetainedLoginCompletionLimit) _loginCompletions.Remove(_loginCompletionOrder.Dequeue());
                    _loginCompletionOrder.Enqueue(outcome.Id);
                }
                _loginCompletions[outcome.Id] = outcome;
            }
        }
        ObserverDispatcher.Raise(LoginCompletionReceived, this, outcome);
    }

    private LoginCompletion? GetLoginCompletion(string? loginId) {
        lock (_loginCompletionGate) {
            if (loginId is null) return _latestLoginCompletion;
            return _loginCompletions.TryGetValue(loginId, out var outcome) ? outcome : null;
        }
    }

    /// <summary>
    /// Waits for login completion, including an outcome already received for a recent login.
    /// </summary>
    /// <remarks>Matching outcomes for the most recent 32 login identifiers are retained by this client.</remarks>
    /// <param name="loginId">Optional login id to match.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the operation finishes.</returns>
    public async Task WaitForLoginCompletionAsync(string? loginId = null, CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<LoginCompletion>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(object? sender, LoginCompletion outcome) {
            if (loginId is not null && !string.Equals(outcome.Id, loginId, StringComparison.Ordinal)) return;
            outcome.Complete(completion);
        }
        void Closed(object? sender, Exception error) => completion.TrySetException(error);
        LoginCompletionReceived += Handler;
        ConnectionClosed += Closed;
        try {
            // Subscribe first, then read history, so completion cannot fall between the two.
            if (GetLoginCompletion(loginId) is { } outcome) outcome.Complete(completion);
            else if (ConnectionError is { } error) completion.TrySetException(error);
            var completed = await TaskCancellation.WaitAsync(completion.Task, cancellationToken).ConfigureAwait(false);
            ObserverDispatcher.Raise(LoginCompleted, this, new LoginEventArgs("chatgpt", completed.Id));
        } finally {
            LoginCompletionReceived -= Handler;
            ConnectionClosed -= Closed;
            completion.TrySetCanceled();
        }
    }
}
