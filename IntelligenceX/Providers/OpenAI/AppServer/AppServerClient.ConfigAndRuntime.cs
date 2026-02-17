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
    /// Lists available models.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that resolves to ModelListResult.</returns>
    public async Task<ModelListResult> ListModelsAsync(CancellationToken cancellationToken = default) {
        var result = await CallWithRetryAsync("model/list", (JsonObject?)null, true, cancellationToken).ConfigureAwait(false);
        var obj = result?.AsObject();
        if (obj is null) {
            throw new InvalidOperationException("Unexpected model list response.");
        }
        return ModelListResult.FromJson(obj);
    }

    /// <summary>
    /// Lists available collaboration modes.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that resolves to CollaborationModeListResult.</returns>
    public async Task<CollaborationModeListResult> ListCollaborationModesAsync(CancellationToken cancellationToken = default) {
        var result = await CallWithRetryAsync("collaborationMode/list", (JsonObject?)null, true, cancellationToken).ConfigureAwait(false);
        var obj = result?.AsObject();
        if (obj is null) {
            throw new InvalidOperationException("Unexpected collaboration mode response.");
        }
        return CollaborationModeListResult.FromJson(obj);
    }

    /// <summary>
    /// Lists available skills.
    /// </summary>
    /// <param name="cwds">Optional working directories to query.</param>
    /// <param name="forceReload">Whether to force reload.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that resolves to SkillListResult.</returns>
    public async Task<SkillListResult> ListSkillsAsync(IReadOnlyList<string>? cwds = null, bool? forceReload = null,
        CancellationToken cancellationToken = default) {
        var parameters = new JsonObject();
        if (cwds is not null && cwds.Count > 0) {
            var array = new JsonArray();
            foreach (var cwd in cwds) {
                array.Add(cwd);
            }
            parameters.Add("cwds", array);
        }
        if (forceReload.HasValue) {
            parameters.Add("forceReload", forceReload.Value);
        }
        var result = await CallWithRetryAsync("skills/list", parameters, true, cancellationToken).ConfigureAwait(false);
        var obj = result?.AsObject();
        if (obj is null) {
            throw new InvalidOperationException("Unexpected skills list response.");
        }
        return SkillListResult.FromJson(obj);
    }

    /// <summary>
    /// Writes a skill configuration entry.
    /// </summary>
    /// <param name="path">Skill path.</param>
    /// <param name="enabled">Whether the skill is enabled.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the operation finishes.</returns>
    public Task WriteSkillConfigAsync(string path, bool enabled, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(path, nameof(path));
        var parameters = new JsonObject()
            .Add("path", path)
            .Add("enabled", enabled);
        return CallWithRetryAsync("skills/config/write", parameters, false, cancellationToken);
    }

    /// <summary>
    /// Reads the effective app-server configuration and layer metadata.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A <see cref="ConfigReadResult"/> containing merged configuration values, source metadata, and layer details.
    /// </returns>
    /// <example>
    /// <code>
    /// var config = await client.ReadConfigAsync(cancellationToken);
    /// var model = config.Config.GetString("model");
    /// </code>
    /// </example>
    public async Task<ConfigReadResult> ReadConfigAsync(CancellationToken cancellationToken = default) {
        var result = await CallWithRetryAsync("config/read", (JsonObject?)null, true, cancellationToken).ConfigureAwait(false);
        var obj = result?.AsObject();
        if (obj is null) {
            throw new InvalidOperationException("Unexpected config response.");
        }
        return ConfigReadResult.FromJson(obj);
    }

    /// <summary>
    /// Writes a single app-server configuration value.
    /// </summary>
    /// <param name="key">Configuration key to write (for example <c>model</c>).</param>
    /// <param name="value">Configuration value encoded as JSON.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the write request is accepted.</returns>
    /// <remarks>
    /// Use <see cref="ReadConfigAsync(System.Threading.CancellationToken)"/> after writing to confirm the effective value
    /// and inspect origin/layer metadata.
    /// </remarks>
    /// <example>
    /// <code>
    /// await client.WriteConfigValueAsync("model", JsonConversion.ToJsonValue("gpt-5.3-codex"), cancellationToken);
    /// </code>
    /// </example>
    public Task WriteConfigValueAsync(string key, JsonValue value, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(key, nameof(key));
        Guard.NotNull(value, nameof(value));
        var parameters = new JsonObject()
            .Add("key", key)
            .Add("value", value);
        return CallWithRetryAsync("config/value/write", parameters, false, cancellationToken);
    }

    /// <summary>
    /// Writes multiple app-server configuration values in a single request.
    /// </summary>
    /// <param name="entries">Configuration entries to write as a batch.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the batch write request is accepted.</returns>
    /// <example>
    /// <code>
    /// var entries = new[] {
    ///     new ConfigEntry("model", JsonConversion.ToJsonValue("gpt-5.3-codex")),
    ///     new ConfigEntry("approvalPolicy", JsonConversion.ToJsonValue("on-failure"))
    /// };
    /// await client.WriteConfigBatchAsync(entries, cancellationToken);
    /// </code>
    /// </example>
    public Task WriteConfigBatchAsync(IReadOnlyList<ConfigEntry> entries, CancellationToken cancellationToken = default) {
        Guard.NotNull(entries, nameof(entries));
        var items = new JsonArray();
        foreach (var entry in entries) {
            items.Add(new JsonObject()
                .Add("key", entry.Key)
                .Add("value", entry.Value));
        }
        var parameters = new JsonObject().Add("items", items);
        return CallWithRetryAsync("config/batchWrite", parameters, false, cancellationToken);
    }

    /// <summary>
    /// Reads server-side constraints for configuration values.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A <see cref="ConfigRequirementsReadResult"/> describing allowed values such as approval policies and sandbox modes.
    /// </returns>
    /// <example>
    /// <code>
    /// var requirements = await client.ReadConfigRequirementsAsync(cancellationToken);
    /// var allowedPolicies = requirements.Requirements?.AllowedApprovalPolicies;
    /// </code>
    /// </example>
    public async Task<ConfigRequirementsReadResult> ReadConfigRequirementsAsync(CancellationToken cancellationToken = default) {
        var result = await CallWithRetryAsync("configRequirements/read", (JsonObject?)null, true, cancellationToken).ConfigureAwait(false);
        var obj = result?.AsObject();
        if (obj is null) {
            throw new InvalidOperationException("Unexpected config requirements response.");
        }
        return ConfigRequirementsReadResult.FromJson(obj);
    }

    /// <summary>
    /// Starts an MCP OAuth login flow for a configured server.
    /// </summary>
    /// <param name="serverId">Optional MCP server identifier.</param>
    /// <param name="serverName">Optional MCP server name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A <see cref="McpOauthLoginStart"/> containing <c>LoginId</c> and browser <c>AuthUrl</c>.
    /// </returns>
    /// <remarks>
    /// Provide either <paramref name="serverId"/> or <paramref name="serverName"/>.
    /// </remarks>
    /// <example>
    /// <code>
    /// var login = await client.StartMcpOauthLoginAsync(serverName: "github", cancellationToken: cancellationToken);
    /// Console.WriteLine(login.AuthUrl);
    /// </code>
    /// </example>
    public async Task<McpOauthLoginStart> StartMcpOauthLoginAsync(string? serverId, string? serverName = null,
        CancellationToken cancellationToken = default) {
        var parameters = new JsonObject();
        if (!string.IsNullOrWhiteSpace(serverId)) {
            parameters.Add("serverId", serverId);
        }
        if (!string.IsNullOrWhiteSpace(serverName)) {
            parameters.Add("serverName", serverName);
        }
        var result = await CallWithRetryAsync("mcpServer/oauth/login", parameters, false, cancellationToken).ConfigureAwait(false);
        var obj = result?.AsObject();
        if (obj is null) {
            throw new InvalidOperationException("Unexpected MCP OAuth response.");
        }
        return McpOauthLoginStart.FromJson(obj);
    }

    /// <summary>
    /// Lists MCP server status entries, including auth and capability metadata.
    /// </summary>
    /// <param name="cursor">Optional pagination cursor from a previous result.</param>
    /// <param name="limit">Optional maximum number of items to return in this page.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A <see cref="McpServerStatusListResult"/> with the current page and optional <c>NextCursor</c>.
    /// </returns>
    /// <example>
    /// <code>
    /// var firstPage = await client.ListMcpServerStatusAsync(limit: 25, cancellationToken: cancellationToken);
    /// foreach (var server in firstPage.Servers) {
    ///     Console.WriteLine($"{server.Name}: {server.AuthStatus}");
    /// }
    /// </code>
    /// </example>
    public async Task<McpServerStatusListResult> ListMcpServerStatusAsync(string? cursor = null, int? limit = null,
        CancellationToken cancellationToken = default) {
        var parameters = new JsonObject();
        if (!string.IsNullOrWhiteSpace(cursor)) {
            parameters.Add("cursor", cursor);
        }
        if (limit.HasValue) {
            parameters.Add("limit", limit.Value);
        }
        var result = await CallWithRetryAsync("mcpServerStatus/list", parameters, true, cancellationToken).ConfigureAwait(false);
        var obj = result?.AsObject();
        if (obj is null) {
            throw new InvalidOperationException("Unexpected MCP server status response.");
        }
        return McpServerStatusListResult.FromJson(obj);
    }

    /// <summary>
    /// Reloads MCP server configuration in the running app-server.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the reload request is accepted.</returns>
    /// <remarks>
    /// Use this method after modifying MCP configuration files to refresh server definitions without restarting the process.
    /// </remarks>
    /// <example>
    /// <code>
    /// await client.ReloadMcpServerConfigAsync(cancellationToken);
    /// var status = await client.ListMcpServerStatusAsync(cancellationToken: cancellationToken);
    /// </code>
    /// </example>
    public async Task ReloadMcpServerConfigAsync(CancellationToken cancellationToken = default) {
        await CallWithRetryAsync("config/mcpServer/reload", (JsonObject?)null, false, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Requests user input responses.
    /// </summary>
    /// <param name="questions">Questions to prompt.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that resolves to UserInputResponse.</returns>
    public async Task<UserInputResponse> RequestUserInputAsync(IReadOnlyList<string> questions, CancellationToken cancellationToken = default) {
        Guard.NotNull(questions, nameof(questions));
        var array = new JsonArray();
        foreach (var question in questions) {
            array.Add(question);
        }
        var parameters = new JsonObject().Add("questions", array);
        var result = await CallWithRetryAsync("tool/requestUserInput", parameters, false, cancellationToken).ConfigureAwait(false);
        var obj = result?.AsObject();
        if (obj is null) {
            throw new InvalidOperationException("Unexpected user input response.");
        }
        return UserInputResponse.FromJson(obj);
    }

    /// <summary>
    /// Uploads feedback content.
    /// </summary>
    /// <param name="content">Feedback content.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the operation finishes.</returns>
    public Task UploadFeedbackAsync(string content, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(content, nameof(content));
        var parameters = new JsonObject().Add("content", content);
        return CallWithRetryAsync("feedback/upload", parameters, false, cancellationToken);
    }

    /// <summary>
    /// Executes a raw JSON-RPC call.
    /// </summary>
    /// <param name="method">Method name.</param>
    /// <param name="parameters">Optional parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that resolves to JsonValue?.</returns>
    public Task<JsonValue?> CallAsync(string method, JsonObject? parameters, CancellationToken cancellationToken = default) {
        return _rpc.CallAsync(method, parameters, cancellationToken);
    }

    /// <summary>
    /// Sends a JSON-RPC notification.
    /// </summary>
    /// <param name="method">Method name.</param>
    /// <param name="parameters">Optional parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the operation finishes.</returns>
    public Task NotifyAsync(string method, JsonObject? parameters, CancellationToken cancellationToken = default) {
        return _rpc.NotifyAsync(method, parameters, cancellationToken);
    }

    /// <summary>
    /// Waits for a login completion notification.
    /// </summary>
    /// <param name="loginId">Optional login id to match.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the operation finishes.</returns>
    public Task WaitForLoginCompletionAsync(string? loginId = null, CancellationToken cancellationToken = default) {
        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

        void Handler(object? sender, JsonRpcNotificationEventArgs args) {
            if (!string.Equals(args.Method, "account/login/completed", StringComparison.Ordinal)) {
                return;
            }
            if (loginId is null) {
                tcs.TrySetResult(null);
                return;
            }
            var id = args.Params?.AsObject()?.GetString("loginId");
            if (string.Equals(id, loginId, StringComparison.Ordinal)) {
                tcs.TrySetResult(null);
            }
        }

        NotificationReceived += Handler;
        if (cancellationToken.CanBeCanceled) {
            cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
        }

        return tcs.Task.ContinueWith(task => {
            NotificationReceived -= Handler;
            if (IsTaskSuccessful(task)) {
                LoginCompleted?.Invoke(this, new LoginEventArgs("chatgpt", loginId));
            }
            return task;
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default).Unwrap();
    }

    private async Task SendLineAsync(string line) {
        await _stdin.WriteLineAsync(line).ConfigureAwait(false);
    }

    private async Task ReadLoopAsync() {
        try {
            while (!_cts.IsCancellationRequested) {
                var line = await _stdout.ReadLineAsync().ConfigureAwait(false);
                if (line is null) {
                    break;
                }
                ProtocolLineReceived?.Invoke(this, line);
                _rpc.HandleLine(line);
            }
        } catch (Exception ex) {
            ProtocolError?.Invoke(this, ex);
        }
    }

    private async Task ReadErrorLoopAsync() {
        try {
            while (!_cts.IsCancellationRequested && _stderr is not null) {
                var line = await _stderr.ReadLineAsync().ConfigureAwait(false);
                if (line is null) {
                    break;
                }
                StandardErrorReceived?.Invoke(this, line);
            }
        } catch (Exception ex) {
            ProtocolError?.Invoke(this, ex);
        }
    }

    private static TimeSpan NextDelay(TimeSpan current, TimeSpan max) {
        if (current <= TimeSpan.Zero) {
            return TimeSpan.Zero;
        }
        var next = TimeSpan.FromMilliseconds(current.TotalMilliseconds * 2);
        return next > max ? max : next;
    }

    private static CancellationToken CreateTimeoutToken(TimeSpan? timeout, CancellationToken cancellationToken, out CancellationTokenSource? cts) {
        if (timeout.HasValue && timeout.Value > TimeSpan.Zero) {
            cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout.Value);
            return cts.Token;
        }
        cts = null;
        return cancellationToken;
    }

    private static void TryWait(Task? task, TimeSpan timeout) {
        if (task is null) {
            return;
        }
        try {
            task.Wait(timeout);
        } catch {
            // Ignore shutdown wait failures.
        }
    }

    private static bool IsTaskSuccessful(Task task) {
#if NETSTANDARD2_0 || NET472
        return task.Status == TaskStatus.RanToCompletion;
#else
        return task.IsCompletedSuccessfully;
#endif
    }

    /// <summary>
    /// Disposes the app-server client and underlying process.
    /// </summary>
    public void Dispose() {
        if (_disposed) {
            return;
        }
        _disposed = true;

        _cts.Cancel();
        TryWait(_readerTask, _shutdownTimeout);
        TryWait(_stderrTask, _shutdownTimeout);
        _rpc.Dispose();

        try {
            if (!_process.HasExited) {
                _process.Kill();
            }
        } catch {
            // Ignore process shutdown errors.
        }

        _process.Dispose();
        _cts.Dispose();
        _stdin.Dispose();
        _stdout.Dispose();
        _stderr?.Dispose();
    }
}
