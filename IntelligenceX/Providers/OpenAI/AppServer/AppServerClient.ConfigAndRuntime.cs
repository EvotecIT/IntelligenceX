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
    public Task WriteSkillConfigAsync(string path, bool enabled, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(path, nameof(path));
        var parameters = new JsonObject()
            .Add("path", path)
            .Add("enabled", enabled);
        return CallWithRetryAsync("skills/config/write", parameters, false, cancellationToken);
    }

    /// <summary>
    /// Reads the current configuration.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ConfigReadResult> ReadConfigAsync(CancellationToken cancellationToken = default) {
        var result = await CallWithRetryAsync("config/read", (JsonObject?)null, true, cancellationToken).ConfigureAwait(false);
        var obj = result?.AsObject();
        if (obj is null) {
            throw new InvalidOperationException("Unexpected config response.");
        }
        return ConfigReadResult.FromJson(obj);
    }

    /// <summary>
    /// Writes a single configuration value.
    /// </summary>
    /// <param name="key">Configuration key.</param>
    /// <param name="value">Configuration value.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task WriteConfigValueAsync(string key, JsonValue value, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(key, nameof(key));
        Guard.NotNull(value, nameof(value));
        var parameters = new JsonObject()
            .Add("key", key)
            .Add("value", value);
        return CallWithRetryAsync("config/value/write", parameters, false, cancellationToken);
    }

    /// <summary>
    /// Writes a batch of configuration values.
    /// </summary>
    /// <param name="entries">Entries to write.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
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
    /// Reads configuration requirements.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ConfigRequirementsReadResult> ReadConfigRequirementsAsync(CancellationToken cancellationToken = default) {
        var result = await CallWithRetryAsync("configRequirements/read", (JsonObject?)null, true, cancellationToken).ConfigureAwait(false);
        var obj = result?.AsObject();
        if (obj is null) {
            throw new InvalidOperationException("Unexpected config requirements response.");
        }
        return ConfigRequirementsReadResult.FromJson(obj);
    }

    /// <summary>
    /// Starts an MCP OAuth login flow.
    /// </summary>
    /// <param name="serverId">Optional server id.</param>
    /// <param name="serverName">Optional server name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
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
    /// Lists MCP server status entries.
    /// </summary>
    /// <param name="cursor">Pagination cursor.</param>
    /// <param name="limit">Maximum number of items.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
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
    /// Reloads MCP server configuration.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task ReloadMcpServerConfigAsync(CancellationToken cancellationToken = default) {
        await CallWithRetryAsync("config/mcpServer/reload", (JsonObject?)null, false, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Requests user input responses.
    /// </summary>
    /// <param name="questions">Questions to prompt.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
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
    public Task<JsonValue?> CallAsync(string method, JsonObject? parameters, CancellationToken cancellationToken = default) {
        return _rpc.CallAsync(method, parameters, cancellationToken);
    }

    /// <summary>
    /// Sends a JSON-RPC notification.
    /// </summary>
    /// <param name="method">Method name.</param>
    /// <param name="parameters">Optional parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task NotifyAsync(string method, JsonObject? parameters, CancellationToken cancellationToken = default) {
        return _rpc.NotifyAsync(method, parameters, cancellationToken);
    }

    /// <summary>
    /// Waits for a login completion notification.
    /// </summary>
    /// <param name="loginId">Optional login id to match.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
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
