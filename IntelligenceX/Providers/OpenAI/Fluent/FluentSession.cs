using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.AppServer;
using IntelligenceX.OpenAI.AppServer.Models;

namespace IntelligenceX.OpenAI.Fluent;

/// <summary>
/// Fluent wrapper around <see cref="AppServerClient"/>.
/// </summary>
public sealed class FluentSession : IDisposable
#if NET5_0_OR_GREATER || NETSTANDARD2_1_OR_GREATER
    , IAsyncDisposable
#endif
{
    internal FluentSession(AppServerClient client) {
        Client = client;
    }

    /// <summary>
    /// Gets the underlying app-server client.
    /// </summary>
    public AppServerClient Client { get; }

    /// <summary>
    /// Initializes the session with client metadata.
    /// </summary>
    /// <param name="clientInfo">Client identity information.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<FluentSession> InitializeAsync(ClientInfo clientInfo, CancellationToken cancellationToken = default) {
        await Client.InitializeAsync(clientInfo, cancellationToken).ConfigureAwait(false);
        return this;
    }

    /// <summary>
    /// Starts a ChatGPT login flow.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<FluentLoginSession> LoginChatGptAsync(CancellationToken cancellationToken = default) {
        var login = await Client.StartChatGptLoginAsync(cancellationToken).ConfigureAwait(false);
        return new FluentLoginSession(this, login);
    }

    /// <summary>
    /// Logs in using an API key.
    /// </summary>
    /// <param name="apiKey">API key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<FluentSession> LoginApiKeyAsync(string apiKey, CancellationToken cancellationToken = default) {
        await Client.LoginWithApiKeyAsync(apiKey, cancellationToken).ConfigureAwait(false);
        return this;
    }

    /// <summary>
    /// Retrieves account information.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<AccountInfo> GetAccountAsync(CancellationToken cancellationToken = default) {
        return Client.ReadAccountAsync(cancellationToken);
    }

    /// <summary>
    /// Starts a new thread and returns a fluent thread session.
    /// </summary>
    /// <param name="model">Model name.</param>
    /// <param name="currentDirectory">Optional working directory.</param>
    /// <param name="approvalPolicy">Optional approval policy.</param>
    /// <param name="sandbox">Optional sandbox mode.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<FluentThreadSession> StartThreadAsync(string model, string? currentDirectory = null, string? approvalPolicy = null,
        string? sandbox = null, CancellationToken cancellationToken = default) {
        var thread = await Client.StartThreadAsync(model, currentDirectory, approvalPolicy, sandbox, cancellationToken).ConfigureAwait(false);
        return new FluentThreadSession(this, thread);
    }

    /// <summary>
    /// Resumes an existing thread.
    /// </summary>
    /// <param name="threadId">Thread id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<FluentThreadSession> ResumeThreadAsync(string threadId, CancellationToken cancellationToken = default) {
        var thread = await Client.ResumeThreadAsync(threadId, cancellationToken).ConfigureAwait(false);
        return new FluentThreadSession(this, thread);
    }

    /// <summary>
    /// Lists threads with optional filters.
    /// </summary>
    /// <param name="cursor">Pagination cursor.</param>
    /// <param name="limit">Maximum number of items.</param>
    /// <param name="sortKey">Sort key.</param>
    /// <param name="modelProviders">Optional model provider filter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<ThreadListResult> ListThreadsAsync(string? cursor = null, int? limit = null, string? sortKey = null,
        IReadOnlyList<string>? modelProviders = null, CancellationToken cancellationToken = default) {
        return Client.ListThreadsAsync(cursor, limit, sortKey, modelProviders, cancellationToken);
    }

    /// <summary>
    /// Logs out of the current session.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task LogoutAsync(CancellationToken cancellationToken = default) {
        return Client.LogoutAsync(cancellationToken);
    }

    /// <summary>
    /// Disposes the session.
    /// </summary>
    public void Dispose() {
        Client.Dispose();
    }

#if NET5_0_OR_GREATER || NETSTANDARD2_1_OR_GREATER
    /// <summary>
    /// Disposes the session asynchronously.
    /// </summary>
    public ValueTask DisposeAsync() {
        Client.Dispose();
        return ValueTask.CompletedTask;
    }
#else
    /// <summary>
    /// Disposes the session asynchronously.
    /// </summary>
    public Task DisposeAsync() {
        Client.Dispose();
        return Task.CompletedTask;
    }
#endif
}
