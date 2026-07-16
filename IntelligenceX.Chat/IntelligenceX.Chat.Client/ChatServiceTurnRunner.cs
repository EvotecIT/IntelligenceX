using System.Threading.Channels;
using IntelligenceX.Chat.Abstractions.Protocol;

namespace IntelligenceX.Chat.Client;

/// <summary>
/// Runs request-scoped chat turns over a connected service client and preserves the full
/// ordered status, stream, interim, metrics, tool, timeline, and final-result contract.
/// </summary>
public sealed class ChatServiceTurnRunner {
    private readonly IChatServiceClient _client;

    /// <summary>
    /// Creates a turn runner over an already connected service client.
    /// </summary>
    public ChatServiceTurnRunner(IChatServiceClient client) {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    /// <summary>
    /// Runs a chat request and serially forwards every correlated turn update before returning.
    /// </summary>
    public async Task<ChatTurnRunResult> RunAsync(
        ChatRequest request,
        Func<ChatTurnUpdate, CancellationToken, ValueTask>? onUpdate = null,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.RequestId)) {
            throw new ArgumentException("RequestId is required.", nameof(request));
        }

        var requestId = request.RequestId;
        var updates = Channel.CreateUnbounded<ChatTurnUpdate>(new UnboundedChannelOptions {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
        ChatMetricsMessage? metrics = null;

        void OnMessage(ChatServiceMessage message) {
            if (!string.Equals(message.RequestId, requestId, StringComparison.Ordinal)) {
                return;
            }

            var update = ChatTurnUpdate.FromMessage(message);
            if (update is not null) {
                _ = updates.Writer.TryWrite(update);
            }
        }

        async Task PumpUpdatesAsync() {
            await foreach (var update in updates.Reader.ReadAllAsync(CancellationToken.None).ConfigureAwait(false)) {
                if (update is ChatTurnMetricsUpdate metricsUpdate) {
                    metrics = metricsUpdate.Metrics;
                }

                if (onUpdate is not null) {
                    await onUpdate(update, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        _client.MessageReceived += OnMessage;
        var updatePump = PumpUpdatesAsync();
        try {
            var response = await _client.RequestAsync<ChatResultMessage>(request, cancellationToken).ConfigureAwait(false);
            updates.Writer.TryComplete();
            await updatePump.ConfigureAwait(false);
            return new ChatTurnRunResult(response, metrics);
        } catch {
            updates.Writer.TryComplete();
            try {
                await updatePump.ConfigureAwait(false);
            } catch {
                // Preserve the exception already being handled. When the pump itself was the
                // original failure, the rethrow below still preserves that callback exception.
            }
            throw;
        } finally {
            _client.MessageReceived -= OnMessage;
            updates.Writer.TryComplete();
        }
    }

    /// <summary>
    /// Requests cancellation of an active service turn.
    /// </summary>
    public async Task CancelAsync(string chatRequestId, CancellationToken cancellationToken = default) {
        var normalizedRequestId = (chatRequestId ?? string.Empty).Trim();
        if (normalizedRequestId.Length == 0) {
            throw new ArgumentException("Chat request id is required.", nameof(chatRequestId));
        }

        var acknowledgement = await _client.RequestAsync<AckMessage>(
                new CancelChatRequest {
                    RequestId = "turn-cancel-" + Guid.NewGuid().ToString("N"),
                    ChatRequestId = normalizedRequestId
                },
                cancellationToken)
            .ConfigureAwait(false);
        if (!acknowledgement.Ok) {
            throw new ChatServiceRequestException(
                string.IsNullOrWhiteSpace(acknowledgement.Message)
                    ? "The service did not accept the chat cancellation request."
                    : acknowledgement.Message!,
                acknowledgement.Code);
        }
    }
}
