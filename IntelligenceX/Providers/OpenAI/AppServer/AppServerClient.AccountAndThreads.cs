using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.Json;
using IntelligenceX.Rpc;
using IntelligenceX.Telemetry;
using IntelligenceX.Utils;

namespace IntelligenceX.OpenAI.AppServer;

/// <summary>
/// Low-level client for the OpenAI app-server JSON-RPC protocol.
/// </summary>
public sealed partial class AppServerClient : IDisposable {

    /// <summary>
    /// Starts a ChatGPT login flow.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ChatGptLoginStart> StartChatGptLoginAsync(CancellationToken cancellationToken = default) {
        var parameters = new JsonObject().Add("type", "chatgpt");
        var result = await CallWithRetryAsync("account/login/start", parameters, false, cancellationToken).ConfigureAwait(false);
        var obj = result?.AsObject();
        if (obj is null) {
            throw new InvalidOperationException("Unexpected login response.");
        }
        var login = ChatGptLoginStart.FromJson(obj);
        LoginStarted?.Invoke(this, new LoginEventArgs("chatgpt", login.LoginId, login.AuthUrl));
        return login;
    }

    /// <summary>
    /// Logs in using an API key.
    /// </summary>
    /// <param name="apiKey">API key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task LoginWithApiKeyAsync(string apiKey, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(apiKey, nameof(apiKey));
        LoginStarted?.Invoke(this, new LoginEventArgs("apikey"));
        var parameters = new JsonObject()
            .Add("type", "apiKey")
            .Add("apiKey", apiKey);
        return CallWithRetryAsync("account/login/start", parameters, false, cancellationToken)
            .ContinueWith(task => {
                if (IsTaskSuccessful(task)) {
                    LoginCompleted?.Invoke(this, new LoginEventArgs("apikey"));
                }
                return task;
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default).Unwrap();
    }

    /// <summary>
    /// Reads account information.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<AccountInfo> ReadAccountAsync(CancellationToken cancellationToken = default) {
        var result = await CallWithRetryAsync("account/read", (JsonObject?)null, true, cancellationToken).ConfigureAwait(false);
        var obj = result?.AsObject();
        if (obj is null) {
            throw new InvalidOperationException("Unexpected account response.");
        }
        return AccountInfo.FromJson(obj);
    }

    /// <summary>
    /// Logs out of the current session.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task LogoutAsync(CancellationToken cancellationToken = default)
        => CallWithRetryAsync("account/logout", (JsonObject?)null, false, cancellationToken);

    /// <summary>
    /// Starts a new chat thread.
    /// </summary>
    /// <param name="model">Model name.</param>
    /// <param name="currentDirectory">Optional working directory.</param>
    /// <param name="approvalPolicy">Optional approval policy.</param>
    /// <param name="sandbox">Optional sandbox mode.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ThreadInfo> StartThreadAsync(string model, string? currentDirectory = null, string? approvalPolicy = null,
        string? sandbox = null, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(model, nameof(model));

        var parameters = new JsonObject()
            .Add("model", model);
        if (!string.IsNullOrWhiteSpace(currentDirectory)) {
            parameters.Add("cwd", currentDirectory);
        }
        if (!string.IsNullOrWhiteSpace(approvalPolicy)) {
            parameters.Add("approvalPolicy", approvalPolicy);
        }
        if (!string.IsNullOrWhiteSpace(sandbox)) {
            parameters.Add("sandbox", sandbox);
        }

        var result = await CallWithRetryAsync("thread/start", parameters, false, cancellationToken).ConfigureAwait(false);
        var obj = result?.AsObject();
        var threadObj = obj?.GetObject("thread");
        if (threadObj is null) {
            throw new InvalidOperationException("Unexpected thread response.");
        }
        return ThreadInfo.FromJson(threadObj);
    }

    /// <summary>
    /// Starts a turn with a text-only input.
    /// </summary>
    /// <param name="threadId">Thread id.</param>
    /// <param name="text">Prompt text.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<TurnInfo> StartTurnAsync(string threadId, string text, CancellationToken cancellationToken = default) {
        return await StartTurnAsync(threadId, text, null, null, null, null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Starts a turn with a text-only input and optional overrides.
    /// </summary>
    /// <param name="threadId">Thread id.</param>
    /// <param name="text">Prompt text.</param>
    /// <param name="model">Optional model override.</param>
    /// <param name="currentDirectory">Optional working directory.</param>
    /// <param name="approvalPolicy">Optional approval policy.</param>
    /// <param name="sandboxPolicy">Optional sandbox policy.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<TurnInfo> StartTurnAsync(string threadId, string text, string? model, string? currentDirectory,
        string? approvalPolicy, SandboxPolicy? sandboxPolicy, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(threadId, nameof(threadId));
        Guard.NotNullOrWhiteSpace(text, nameof(text));

        var input = new JsonArray().Add(new JsonObject()
            .Add("type", "text")
            .Add("text", text));

        var parameters = new JsonObject()
            .Add("threadId", threadId)
            .Add("input", input);
        return await StartTurnAsync(parameters, model, currentDirectory, approvalPolicy, sandboxPolicy, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Starts a turn with a structured input payload.
    /// </summary>
    /// <param name="threadId">Thread id.</param>
    /// <param name="input">Input items.</param>
    /// <param name="model">Optional model override.</param>
    /// <param name="currentDirectory">Optional working directory.</param>
    /// <param name="approvalPolicy">Optional approval policy.</param>
    /// <param name="sandboxPolicy">Optional sandbox policy.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<TurnInfo> StartTurnAsync(string threadId, JsonArray input, string? model, string? currentDirectory,
        string? approvalPolicy, SandboxPolicy? sandboxPolicy, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(threadId, nameof(threadId));
        Guard.NotNull(input, nameof(input));

        var parameters = new JsonObject()
            .Add("threadId", threadId)
            .Add("input", input);
        return await StartTurnAsync(parameters, model, currentDirectory, approvalPolicy, sandboxPolicy, cancellationToken).ConfigureAwait(false);
    }

    private async Task<TurnInfo> StartTurnAsync(JsonObject parameters, string? model, string? currentDirectory,
        string? approvalPolicy, SandboxPolicy? sandboxPolicy, CancellationToken cancellationToken = default) {
        if (!string.IsNullOrWhiteSpace(model)) {
            parameters.Add("model", model);
        }
        if (!string.IsNullOrWhiteSpace(currentDirectory)) {
            parameters.Add("cwd", currentDirectory);
        }
        if (!string.IsNullOrWhiteSpace(approvalPolicy)) {
            parameters.Add("approvalPolicy", approvalPolicy);
        }
        if (sandboxPolicy is not null) {
            parameters.Add("sandboxPolicy", sandboxPolicy.ToJson());
        }

        var result = await CallWithRetryAsync("turn/start", parameters, false, cancellationToken).ConfigureAwait(false);
        var obj = result?.AsObject();
        var turnObj = obj?.GetObject("turn");
        if (turnObj is null) {
            throw new InvalidOperationException("Unexpected turn response.");
        }
        return TurnInfo.FromJson(turnObj);
    }

    /// <summary>
    /// Resumes a thread by id.
    /// </summary>
    /// <param name="threadId">Thread id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ThreadInfo> ResumeThreadAsync(string threadId, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(threadId, nameof(threadId));
        var parameters = new JsonObject().Add("threadId", threadId);
        var result = await CallWithRetryAsync("thread/resume", parameters, false, cancellationToken).ConfigureAwait(false);
        var obj = result?.AsObject();
        var threadObj = obj?.GetObject("thread");
        if (threadObj is null) {
            throw new InvalidOperationException("Unexpected thread response.");
        }
        return ThreadInfo.FromJson(threadObj);
    }

    /// <summary>
    /// Forks a thread by id.
    /// </summary>
    /// <param name="threadId">Thread id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ThreadInfo> ForkThreadAsync(string threadId, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(threadId, nameof(threadId));
        var parameters = new JsonObject().Add("threadId", threadId);
        var result = await CallWithRetryAsync("thread/fork", parameters, false, cancellationToken).ConfigureAwait(false);
        var obj = result?.AsObject();
        var threadObj = obj?.GetObject("thread");
        if (threadObj is null) {
            throw new InvalidOperationException("Unexpected thread response.");
        }
        return ThreadInfo.FromJson(threadObj);
    }

    /// <summary>
    /// Lists threads with optional filters.
    /// </summary>
    /// <param name="cursor">Pagination cursor.</param>
    /// <param name="limit">Maximum number of items.</param>
    /// <param name="sortKey">Sort key.</param>
    /// <param name="modelProviders">Optional model provider filter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ThreadListResult> ListThreadsAsync(string? cursor = null, int? limit = null, string? sortKey = null,
        IReadOnlyList<string>? modelProviders = null, CancellationToken cancellationToken = default) {
        var parameters = new JsonObject();
        if (!string.IsNullOrWhiteSpace(cursor)) {
            parameters.Add("cursor", cursor);
        }
        if (limit.HasValue) {
            parameters.Add("limit", limit.Value);
        }
        if (!string.IsNullOrWhiteSpace(sortKey)) {
            parameters.Add("sortKey", sortKey);
        }
        if (modelProviders is not null && modelProviders.Count > 0) {
            var providers = new JsonArray();
            foreach (var provider in modelProviders) {
                providers.Add(provider);
            }
            parameters.Add("modelProviders", providers);
        }

        var result = await CallWithRetryAsync("thread/list", parameters, true, cancellationToken).ConfigureAwait(false);
        var obj = result?.AsObject();
        if (obj is null) {
            throw new InvalidOperationException("Unexpected thread list response.");
        }
        return ThreadListResult.FromJson(obj);
    }

    /// <summary>
    /// Lists currently loaded thread ids.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ThreadIdListResult> ListLoadedThreadsAsync(CancellationToken cancellationToken = default) {
        var result = await CallWithRetryAsync("thread/loaded/list", (JsonObject?)null, true, cancellationToken).ConfigureAwait(false);
        var obj = result?.AsObject();
        if (obj is null) {
            throw new InvalidOperationException("Unexpected loaded thread response.");
        }
        return ThreadIdListResult.FromJson(obj);
    }

    /// <summary>
    /// Archives a thread by id.
    /// </summary>
    /// <param name="threadId">Thread id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task ArchiveThreadAsync(string threadId, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(threadId, nameof(threadId));
        var parameters = new JsonObject().Add("threadId", threadId);
        return CallWithRetryAsync("thread/archive", parameters, false, cancellationToken);
    }

    /// <summary>
    /// Rolls back a thread by a number of turns.
    /// </summary>
    /// <param name="threadId">Thread id.</param>
    /// <param name="turns">Number of turns to roll back.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ThreadInfo> RollbackThreadAsync(string threadId, int turns, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(threadId, nameof(threadId));
        var parameters = new JsonObject()
            .Add("threadId", threadId)
            .Add("turns", turns);
        var result = await CallWithRetryAsync("thread/rollback", parameters, false, cancellationToken).ConfigureAwait(false);
        var obj = result?.AsObject();
        var threadObj = obj?.GetObject("thread");
        if (threadObj is null) {
            throw new InvalidOperationException("Unexpected thread response.");
        }
        return ThreadInfo.FromJson(threadObj);
    }

    /// <summary>
    /// Interrupts an in-flight turn.
    /// </summary>
    /// <param name="threadId">Thread id.</param>
    /// <param name="turnId">Turn id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task InterruptTurnAsync(string threadId, string turnId, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(threadId, nameof(threadId));
        Guard.NotNullOrWhiteSpace(turnId, nameof(turnId));
        var parameters = new JsonObject()
            .Add("threadId", threadId)
            .Add("turnId", turnId);
        return CallWithRetryAsync("turn/interrupt", parameters, false, cancellationToken);
    }

    /// <summary>
    /// Starts a review for a thread.
    /// </summary>
    /// <param name="threadId">Thread id.</param>
    /// <param name="delivery">Delivery channel.</param>
    /// <param name="target">Review target.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ReviewStartResult> StartReviewAsync(string threadId, string delivery, ReviewTarget target, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(threadId, nameof(threadId));
        Guard.NotNullOrWhiteSpace(delivery, nameof(delivery));
        Guard.NotNull(target, nameof(target));

        var parameters = new JsonObject()
            .Add("threadId", threadId)
            .Add("delivery", delivery)
            .Add("target", target.Payload);

        var result = await CallWithRetryAsync("review/start", parameters, false, cancellationToken).ConfigureAwait(false);
        var obj = result?.AsObject();
        if (obj is null) {
            throw new InvalidOperationException("Unexpected review response.");
        }
        return ReviewStartResult.FromJson(obj);
    }

    /// <summary>
    /// Executes a command through the app-server.
    /// </summary>
    /// <param name="request">Command execution request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<CommandExecResult> ExecuteCommandAsync(CommandExecRequest request, CancellationToken cancellationToken = default) {
        Guard.NotNull(request, nameof(request));

        var commandArray = new JsonArray();
        foreach (var item in request.Command) {
            commandArray.Add(item);
        }

        var parameters = new JsonObject()
            .Add("command", commandArray);
        if (!string.IsNullOrWhiteSpace(request.WorkingDirectory)) {
            parameters.Add("cwd", request.WorkingDirectory);
        }
        if (request.SandboxPolicy is not null) {
            parameters.Add("sandboxPolicy", request.SandboxPolicy.ToJson());
        }
        if (request.TimeoutMs.HasValue) {
            parameters.Add("timeoutMs", request.TimeoutMs.Value);
        }

        var result = await CallWithRetryAsync("command/exec", parameters, false, cancellationToken).ConfigureAwait(false);
        var obj = result?.AsObject();
        if (obj is null) {
            throw new InvalidOperationException("Unexpected command response.");
        }
        return CommandExecResult.FromJson(obj);
    }

}
