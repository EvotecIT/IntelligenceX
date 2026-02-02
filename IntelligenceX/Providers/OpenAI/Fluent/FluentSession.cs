using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.AppServer;
using IntelligenceX.OpenAI.AppServer.Models;

namespace IntelligenceX.OpenAI.Fluent;

/// <summary>
/// Fluent wrapper for app-server sessions.
/// </summary>
/// <example>
/// <code>
/// await using var session = await AppServerFluent.StartAsync();
/// await session.InitializeAsync(new ClientInfo("IntelligenceX", "Demo", "1.0"));
/// var login = await session.LoginChatGptAsync();
/// await login.WaitAsync();
/// var thread = await session.StartThreadAsync("gpt-5.2-codex");
/// var turn = await thread.SendAsync("Summarize the latest changes.");
/// </code>
/// </example>
public sealed class FluentSession : IDisposable
#if NET5_0_OR_GREATER || NETSTANDARD2_1_OR_GREATER
    , IAsyncDisposable
#endif
{
    internal FluentSession(AppServerClient client) {
        Client = client;
    }

    /// <summary>Underlying app-server client.</summary>
    public AppServerClient Client { get; }

    /// <summary>Initializes the session with client identity metadata.</summary>
    public async Task<FluentSession> InitializeAsync(ClientInfo clientInfo, CancellationToken cancellationToken = default) {
        await Client.InitializeAsync(clientInfo, cancellationToken).ConfigureAwait(false);
        return this;
    }

    /// <summary>Starts a ChatGPT login flow.</summary>
    public async Task<FluentLoginSession> LoginChatGptAsync(CancellationToken cancellationToken = default) {
        var login = await Client.StartChatGptLoginAsync(cancellationToken).ConfigureAwait(false);
        return new FluentLoginSession(this, login);
    }

    /// <summary>Logs in using an API key.</summary>
    public async Task<FluentSession> LoginApiKeyAsync(string apiKey, CancellationToken cancellationToken = default) {
        await Client.LoginWithApiKeyAsync(apiKey, cancellationToken).ConfigureAwait(false);
        return this;
    }

    /// <summary>Reads account information.</summary>
    public Task<AccountInfo> GetAccountAsync(CancellationToken cancellationToken = default) {
        return Client.ReadAccountAsync(cancellationToken);
    }

    /// <summary>Starts a new thread.</summary>
    public async Task<FluentThreadSession> StartThreadAsync(string model, string? currentDirectory = null, string? approvalPolicy = null,
        string? sandbox = null, CancellationToken cancellationToken = default) {
        var thread = await Client.StartThreadAsync(model, currentDirectory, approvalPolicy, sandbox, cancellationToken).ConfigureAwait(false);
        return new FluentThreadSession(this, thread);
    }

    /// <summary>Resumes an existing thread.</summary>
    public async Task<FluentThreadSession> ResumeThreadAsync(string threadId, CancellationToken cancellationToken = default) {
        var thread = await Client.ResumeThreadAsync(threadId, cancellationToken).ConfigureAwait(false);
        return new FluentThreadSession(this, thread);
    }

    /// <summary>Lists threads for the current account.</summary>
    public Task<ThreadListResult> ListThreadsAsync(string? cursor = null, int? limit = null, string? sortKey = null,
        IReadOnlyList<string>? modelProviders = null, CancellationToken cancellationToken = default) {
        return Client.ListThreadsAsync(cursor, limit, sortKey, modelProviders, cancellationToken);
    }

    /// <summary>Logs out of the current session.</summary>
    public Task LogoutAsync(CancellationToken cancellationToken = default) {
        return Client.LogoutAsync(cancellationToken);
    }

    public void Dispose() {
        Client.Dispose();
    }

#if NET5_0_OR_GREATER || NETSTANDARD2_1_OR_GREATER
    public ValueTask DisposeAsync() {
        Client.Dispose();
        return ValueTask.CompletedTask;
    }
#else
    public Task DisposeAsync() {
        Client.Dispose();
        return Task.CompletedTask;
    }
#endif
}
