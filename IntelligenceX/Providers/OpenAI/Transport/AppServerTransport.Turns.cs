using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.OpenAI.AppServer;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.Rpc;
using IntelligenceX.Utils;

namespace IntelligenceX.OpenAI.Transport;

internal sealed partial class AppServerTransport {
    private static readonly TimeSpan CancellationCleanupTimeout = TimeSpan.FromSeconds(2);

    private async Task InterruptCanceledTurnAsync(string threadId, Task<TurnInfo> start) {
        try {
            using var receiptTimeout = new CancellationTokenSource(CancellationCleanupTimeout);
            var started = await TaskCancellation.WaitAsync(start, receiptTimeout.Token).ConfigureAwait(false);
            if (started.Status == "completed" || started.Status == "interrupted" || started.Status == "failed") return;
            if (string.IsNullOrWhiteSpace(started.Id)) throw new InvalidOperationException("The app-server returned a turn without an id.");
            using var interruptTimeout = new CancellationTokenSource(CancellationCleanupTimeout);
            await _client.InterruptTurnAsync(threadId, started.Id, interruptTimeout.Token).ConfigureAwait(false);
        } catch (Exception) {
            // An unidentified or unacknowledged turn must not continue behind a canceled caller.
            // Close the owned process connection; the caller can reconnect for subsequent work.
            try {
                _client.Dispose();
            } catch (Exception) {
                // Preserve the caller's cancellation if shutdown also fails.
            }
        }
    }

    /// <summary>Collects one chat operation's turn notifications without changing the low-level start API.</summary>
    private sealed class TurnCompletionWaiter : IDisposable {
        private sealed class TurnState {
            internal readonly List<JsonObject> Items = new();
            internal JsonObject? Completed;
            internal JsonObject? Usage;

            internal void AddItem(JsonObject item) {
                var id = item.GetString("id");
                if (!string.IsNullOrEmpty(id)) {
                    for (var i = 0; i < Items.Count; i++) {
                        if (string.Equals(Items[i].GetString("id"), id, StringComparison.Ordinal)) {
                            Items[i] = item;
                            return;
                        }
                    }
                }
                Items.Add(item);
            }
        }

        private readonly AppServerClient _client;
        private readonly string _threadId;
        private readonly object _gate = new();
        private readonly Dictionary<string, TurnState> _turns = new(StringComparer.Ordinal);
        private readonly TaskCompletionSource<TurnInfo> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private string? _turnId;
        private Exception? _connectionError;
        private bool _disposed;

        internal TurnCompletionWaiter(AppServerClient client, string threadId) {
            _client = client;
            _threadId = threadId;
            _client.NotificationReceived += OnNotification;
            _client.ConnectionClosed += OnConnectionClosed;
            if (_client.ConnectionError is { } error) OnConnectionClosed(_client, error);
        }

        internal async Task<TurnInfo> WaitAsync(TurnInfo started, CancellationToken cancellationToken) {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(started.Id)) throw new InvalidOperationException("The app-server returned a turn without an id.");
            lock (_gate) {
                _turnId = started.Id;
                _turns.TryGetValue(_turnId, out var state);
                _turns.Clear();
                state ??= new TurnState();
                _turns.Add(_turnId, state);
                if (IsTerminal(started.Status)) state.Completed ??= started.Raw;
                if (state.Completed is not null) Complete(state);
                else if (_connectionError is not null) _completion.TrySetException(_connectionError);
            }
            return await TaskCancellation.WaitAsync(_completion.Task, cancellationToken).ConfigureAwait(false);
        }

        private void OnNotification(object? sender, JsonRpcNotificationEventArgs args) {
            if (args.Method != "turn/completed" && args.Method != "item/completed" && args.Method != "thread/tokenUsage/updated") return;
            var parameters = args.Params?.AsObject();
            if (!string.Equals(parameters?.GetString("threadId"), _threadId, StringComparison.Ordinal)) return;
            var completed = args.Method == "turn/completed" ? parameters?.GetObject("turn") : null;
            var turnId = completed?.GetString("id") ?? parameters?.GetString("turnId");
            if (string.IsNullOrWhiteSpace(turnId)) return;
            lock (_gate) {
                if (_disposed || (_turnId is not null && !string.Equals(turnId, _turnId, StringComparison.Ordinal))) return;
                if (!_turns.TryGetValue(turnId!, out var state)) _turns.Add(turnId!, state = new TurnState());
                if (completed is not null) state.Completed = completed;
                else if (args.Method == "item/completed" && parameters?.GetObject("item") is { } item) state.AddItem(item);
                else if (args.Method == "thread/tokenUsage/updated") state.Usage = parameters?.GetObject("tokenUsage")?.GetObject("last");
                if (_turnId is not null && state.Completed is not null) Complete(state);
            }
        }

        private void Complete(TurnState state) {
            var raw = new JsonObject();
            foreach (var field in state.Completed!) raw.Add(field.Key, field.Value ?? JsonValue.Null);
            if (string.Equals(raw.GetString("status"), "failed", StringComparison.Ordinal)) {
                _completion.TrySetException(new InvalidOperationException(raw.GetObject("error")?.GetString("message") ?? "The app-server turn failed."));
                return;
            }
            if (!IsTerminal(raw.GetString("status"))) {
                _completion.TrySetException(new InvalidOperationException("The app-server sent a completion with a non-terminal status."));
                return;
            }
            if ((raw.GetArray("items")?.Count ?? 0) == 0 && state.Items.Count > 0) {
                var items = new JsonArray();
                foreach (var item in state.Items) items.Add(item);
                raw.Add("items", items);
            }
            if (raw.GetObject("usage") is null && state.Usage is not null) raw.Add("usage", state.Usage);
            _completion.TrySetResult(TurnInfo.FromJson(raw));
        }

        private void OnConnectionClosed(object? sender, Exception error) {
            lock (_gate) {
                _connectionError = error;
                // A completed notification can precede both the receipt continuation and EOF.
                if (_turnId is not null && !_completion.Task.IsCompleted) _completion.TrySetException(error);
            }
        }

        private static bool IsTerminal(string? status) => status == "completed" || status == "interrupted" || status == "failed";

        public void Dispose() {
            _client.NotificationReceived -= OnNotification;
            _client.ConnectionClosed -= OnConnectionClosed;
            lock (_gate) {
                _disposed = true;
                _turns.Clear();
                _completion.TrySetCanceled();
            }
        }
    }
}
