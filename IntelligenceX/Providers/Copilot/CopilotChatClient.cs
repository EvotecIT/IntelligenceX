using System;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Copilot.Direct;

namespace IntelligenceX.Copilot;

/// <summary>
/// High-level Copilot chat client that can switch between CLI and direct HTTP transports.
/// </summary>
public sealed class CopilotChatClient : IDisposable
#if NET5_0_OR_GREATER || NETSTANDARD2_1_OR_GREATER
    , IAsyncDisposable
#endif
{
    private readonly CopilotChatClientOptions _options;
    private readonly CopilotTransportKind _transport;
    private CopilotClient? _cliClient;
    private CopilotDirectClient? _directClient;
    private bool _disposed;

    private CopilotChatClient(CopilotChatClientOptions options, CopilotTransportKind transport) {
        _options = options;
        _transport = transport;
    }

    /// <summary>
    /// Starts a Copilot chat client using the selected transport.
    /// </summary>
    /// <param name="options">Optional client options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task<CopilotChatClient> StartAsync(CopilotChatClientOptions? options = null,
        CancellationToken cancellationToken = default) {
        options ??= new CopilotChatClientOptions();
        options.Validate();
        var client = new CopilotChatClient(options, options.Transport);
        if (options.Transport == CopilotTransportKind.Cli) {
            client._cliClient = await CopilotClient.StartAsync(options.Cli, cancellationToken).ConfigureAwait(false);
        } else {
            client._directClient = new CopilotDirectClient(options.Direct);
        }
        return client;
    }

    /// <summary>
    /// Sends a single prompt and returns the response text.
    /// </summary>
    /// <param name="prompt">Prompt text to send.</param>
    /// <param name="model">Optional model override.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>
    /// The active transport is selected by <see cref="CopilotChatClientOptions.Transport"/>.
    /// Direct transport requires a model to be provided or configured on the options.
    /// </remarks>
    public async Task<string> ChatAsync(string prompt, string? model = null, CancellationToken cancellationToken = default) {
        if (_disposed) {
            throw new ObjectDisposedException(nameof(CopilotChatClient));
        }
        if (string.IsNullOrWhiteSpace(prompt)) {
            throw new ArgumentException("Prompt cannot be empty.", nameof(prompt));
        }

        if (_transport == CopilotTransportKind.Direct) {
            var effectiveModel = ResolveModel(model);
            if (string.IsNullOrWhiteSpace(effectiveModel)) {
                throw new InvalidOperationException("Copilot direct transport requires a model.");
            }
            if (_directClient is null) {
                throw new InvalidOperationException("Copilot direct client is not initialized.");
            }
            return await _directClient.ChatAsync(prompt, effectiveModel!, cancellationToken).ConfigureAwait(false);
        }

        if (_cliClient is null) {
            throw new InvalidOperationException("Copilot CLI client is not initialized.");
        }

        var sessionOptions = new CopilotSessionOptions();
        var modelOverride = ResolveModel(model);
        if (!string.IsNullOrWhiteSpace(modelOverride)) {
            sessionOptions.Model = modelOverride;
        }

        var session = await _cliClient.CreateSessionAsync(sessionOptions, cancellationToken).ConfigureAwait(false);
        try {
            var message = new CopilotMessageOptions { Prompt = prompt };
            var response = await session.SendAndWaitAsync(message, _options.Timeout, cancellationToken).ConfigureAwait(false);
            return response ?? string.Empty;
        } finally {
            try {
                await _cliClient.DeleteSessionAsync(session.SessionId, CancellationToken.None).ConfigureAwait(false);
            } catch {
                // Ignore session cleanup failures.
            }
        }
    }

    private string? ResolveModel(string? model) {
        if (!string.IsNullOrWhiteSpace(model)) {
            return model;
        }
        return _options.DefaultModel;
    }

    /// <summary>
    /// Disposes the client synchronously.
    /// </summary>
    public void Dispose() {
        if (_disposed) {
            return;
        }
        _disposed = true;
        _directClient?.Dispose();
        _cliClient?.Dispose();
    }

#if NET5_0_OR_GREATER || NETSTANDARD2_1_OR_GREATER
    /// <summary>
    /// Disposes the client asynchronously.
    /// </summary>
    public async ValueTask DisposeAsync() {
#else
    /// <summary>
    /// Disposes the client asynchronously.
    /// </summary>
    public async Task DisposeAsync() {
#endif
        if (_disposed) {
            return;
        }
        _disposed = true;
        if (_cliClient is not null) {
            await _cliClient.DisposeAsync().ConfigureAwait(false);
        }
        _directClient?.Dispose();
    }
}
