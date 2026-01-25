using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.AppServer;
using IntelligenceX.OpenAI.AppServer.Models;

namespace IntelligenceX.OpenAI.Fluent;

public sealed class FluentSession : IAsyncDisposable {
    internal FluentSession(AppServerClient client) {
        Client = client;
    }

    public AppServerClient Client { get; }

    public async Task<FluentSession> InitializeAsync(ClientInfo clientInfo, CancellationToken cancellationToken = default) {
        await Client.InitializeAsync(clientInfo, cancellationToken).ConfigureAwait(false);
        return this;
    }

    public async Task<FluentLoginSession> LoginChatGptAsync(CancellationToken cancellationToken = default) {
        var login = await Client.StartChatGptLoginAsync(cancellationToken).ConfigureAwait(false);
        return new FluentLoginSession(this, login);
    }

    public async Task<FluentSession> LoginApiKeyAsync(string apiKey, CancellationToken cancellationToken = default) {
        await Client.LoginWithApiKeyAsync(apiKey, cancellationToken).ConfigureAwait(false);
        return this;
    }

    public Task<AccountInfo> GetAccountAsync(CancellationToken cancellationToken = default) {
        return Client.ReadAccountAsync(cancellationToken);
    }

    public async Task<FluentThreadSession> StartThreadAsync(string model, string? currentDirectory = null, string? approvalPolicy = null,
        string? sandbox = null, CancellationToken cancellationToken = default) {
        var thread = await Client.StartThreadAsync(model, currentDirectory, approvalPolicy, sandbox, cancellationToken).ConfigureAwait(false);
        return new FluentThreadSession(this, thread);
    }

    public async Task<FluentThreadSession> ResumeThreadAsync(string threadId, CancellationToken cancellationToken = default) {
        var thread = await Client.ResumeThreadAsync(threadId, cancellationToken).ConfigureAwait(false);
        return new FluentThreadSession(this, thread);
    }

    public Task<ThreadListResult> ListThreadsAsync(string? cursor = null, int? limit = null, string? sortKey = null,
        IReadOnlyList<string>? modelProviders = null, CancellationToken cancellationToken = default) {
        return Client.ListThreadsAsync(cursor, limit, sortKey, modelProviders, cancellationToken);
    }

    public Task LogoutAsync(CancellationToken cancellationToken = default) {
        return Client.LogoutAsync(cancellationToken);
    }

    public ValueTask DisposeAsync() {
        Client.Dispose();
        return ValueTask.CompletedTask;
    }
}
