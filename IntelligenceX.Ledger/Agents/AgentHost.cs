using System.Collections.Concurrent;
using System.Text.Json;
using IntelligenceX.Ledger.Contracts;

namespace IntelligenceX.Ledger.Agents;

/// <summary>The machine's agent service, running inside the reporter as the signed-in user. It heartbeats this host,
/// runs helper requests admitted to this machine through the Codex app server, delivers results, questions, notices and
/// answers through the supported clients, retires finished helpers, and records tokens for every turn it causes.
/// Waiting is plain HTTP reads and app-server notifications; no model is ever used to wait or poll.</summary>
public static class AgentHost {
    /// <summary>Checks this machine's routes without touching the Ledger: the Codex app server (live daemon or private)
    /// and the Claude executable. Prints one JSON line; exit code 0 only when Codex answers.</summary>
    public static async Task<int> CheckAsync(AgentSettings settings) {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        object codex;
        try {
            await using var server = await CodexAppServer.OpenAsync(settings.CodexExecutable, settings.CodexControlSocket, timeout.Token);
            var threads = await server.CallAsync("thread/list", new System.Text.Json.Nodes.JsonObject { ["limit"] = 1 }, TimeSpan.FromSeconds(20), timeout.Token);
            codex = new { available = true, live = server.Live, listed = threads.TryGetProperty("data", out var data) ? data.GetArrayLength() : -1 };
        } catch (Exception error) {
            codex = new { available = false, error = error.GetType().Name + ": " + error.Message };
        }
        string? claude = null;
        try {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(settings.ClaudeExecutable, "--version") {
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true });
            claude = process is null ? null : (await process.StandardOutput.ReadToEndAsync(timeout.Token)).Trim();
        } catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException) { }
        Console.WriteLine(JsonSerializer.Serialize(new { codex, claude, stateDirectory = settings.StateDirectory, workspaceRoot = settings.WorkspaceRoot }));
        return codex.GetType().GetProperty("available")?.GetValue(codex) is true ? 0 : 1;
    }

    /// <summary>Runs until stopped. A failure restarts the service loop after a pause, so one bad sweep never ends it.</summary>
    public static async Task RunAsync(AgentSettings settings, string machineId, CancellationToken stop) {
        while (!stop.IsCancellationRequested) {
            try {
                await new AgentService(settings, machineId).RunAsync(stop).ConfigureAwait(false);
            } catch (OperationCanceledException) when (stop.IsCancellationRequested) {
                return;
            } catch (Exception error) {
                AgentService.Log("agent-service-restart", error.GetType().Name + ": " + error.Message);
                try { await Task.Delay(TimeSpan.FromSeconds(30), stop).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
            }
        }
    }
}

internal sealed partial class AgentService {
    private readonly AgentSettings _settings;
    private readonly string _machineId;
    private readonly LedgerReadClient _ledger;
    private readonly CodexRunner _codex;
    private readonly ClaudeSender _claude;
    private readonly ConcurrentDictionary<string, Task> _active = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _deferred = new(StringComparer.Ordinal);
    private readonly string _bootId;
    private AskPolicy _policy = new();
    private int _grantSeconds = 120;
    private bool _codexAvailable;
    private DateTimeOffset _codexCheckedAt = DateTimeOffset.MinValue;

    internal AgentService(AgentSettings settings, string machineId, LedgerReadClient? ledger = null) {
        _settings = settings;
        _machineId = machineId;
        _ledger = ledger ?? LedgerReadClient.ForCoordination(CoordinationClientSettings.Load(settings.CoordinationConfig));
        _codex = new(settings.CodexExecutable, settings.CodexControlSocket);
        Directory.CreateDirectory(settings.StateDirectory);
        Directory.CreateDirectory(settings.WorkspaceRoot);
        _claude = new(settings.ClaudeExecutable, settings.StateDirectory);
        _bootId = BootIdentity.Get(settings.StateDirectory, machineId);
    }

    internal static void Log(string code, string? detail = null) =>
        Console.Error.WriteLine(JsonSerializer.Serialize(new { atUtc = DateTimeOffset.UtcNow, agent = code, detail }));

    public async Task RunAsync(CancellationToken stop) {
        Log("agent-service-started", _machineId);
        var nextHeartbeat = DateTimeOffset.MinValue;
        while (!stop.IsCancellationRequested) {
            if (DateTimeOffset.UtcNow >= nextHeartbeat) {
                await HeartbeatAsync(stop).ConfigureAwait(false);
                nextHeartbeat = DateTimeOffset.UtcNow.AddSeconds(Math.Clamp(_grantSeconds / 3.0, 5, 10));
            }
            try { await SweepAsync(stop).ConfigureAwait(false); }
            catch (Exception error) when (error is HttpRequestException or IOException or TaskCanceledException or
                CoordinationCommandRejectedException or JsonException or InvalidDataException && !stop.IsCancellationRequested) {
                Log("sweep-unavailable", error.GetType().Name + ": " + error.Message);
            }
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, _settings.SweepSeconds)), stop).ConfigureAwait(false);
        }
        await Task.WhenAll(_active.Values.Select(task => task.ContinueWith(_ => { }, TaskScheduler.Default))).ConfigureAwait(false);
    }

    private async Task HeartbeatAsync(CancellationToken stop) {
        if (DateTimeOffset.UtcNow - _codexCheckedAt > TimeSpan.FromMinutes(1)) {
            _codexAvailable = await _codex.AvailableAsync(stop).ConfigureAwait(false);
            _codexCheckedAt = DateTimeOffset.UtcNow;
        }
        try {
            var ack = await _ledger.HostAsync(new CoordinationHost(_machineId, _bootId, DateTimeOffset.UtcNow, _settings.Capabilities,
                CreatableProviders: _codexAvailable ? ["codex"] : []), stop).ConfigureAwait(false);
            if (ack.GrantSeconds is > 0) _grantSeconds = ack.GrantSeconds.Value;
            if (ack.Ask is not null) _policy = ack.Ask;
        } catch (Exception error) when (error is HttpRequestException or IOException or TaskCanceledException or
            CoordinationCommandRejectedException or JsonException && !stop.IsCancellationRequested) {
            Log("heartbeat-unavailable", error.GetType().Name + ": " + error.Message);
        }
    }

    /// <summary>One pass over the four feeds this host serves. Each actionable item starts at most one task.</summary>
    private async Task SweepAsync(CancellationToken stop) {
        foreach (var work in await PageAsync(after => _ledger.AssignedAsync(stop, after), page => page.Items, page => page.NextOrder).ConfigureAwait(false)) {
            if (work.Machine != _machineId || work.Request.Kind != "help" || work.Provider != "codex") continue;
            var creation = work.State == "admitted" && work.Assignee is null && work.Reason == "worker-creation-required";
            var record = RunRecord.Load(_settings.StateDirectory, work.Request.Id, work.Generation);
            if (work.State == "reconcile") Start("reconcile:" + work.Request.Id, () => ReconcileAsync(work, record, stop));
            else if (creation || record is not null && record.Phase != "done")
                Start("run:" + work.Request.Id + ":" + work.Generation, () => RunHelperAsync(work, stop));
        }
        foreach (var result in await PageAsync(after => _ledger.ResultsAsync(stop, after), page => page.Items, page => page.NextOrder).ConfigureAwait(false)) {
            if (result.ReturnTo.MachineId != _machineId || result.ConsumedAtUtc is not null || result.State is not ("intent" or "held") ||
                result.ReturnTo.Provider is not ("codex" or "claude") || Deferred("deliver:" + result.RequestId)) continue;
            Start("result:" + result.RequestId + ":" + result.Generation, () => DeliverResultAsync(result, stop));
        }
        foreach (var item in await PageAsync(after => _ledger.QuestionsAsync(stop, after), page => page.Items,
            page => page.NextOrder).ConfigureAwait(false)) {
            if (item.Target == "question" && item.ReturnTo.Provider is "codex" or "claude" && !Deferred("deliver:" + item.Question.RequestId))
                Start("question:" + item.Question.RequestId + "#" + item.Question.Number, () => DeliverQuestionAsync(item, stop));
            // Answer-side items are delivered by the helper's own run, which waits for them.
        }
        foreach (var job in await PageAsync(after => _ledger.RetirementsAsync(stop, after), page => page.Items, page => page.NextOrder).ConfigureAwait(false)) {
            if (job.Binding.MachineId != _machineId || job.Binding.Provider != "codex" || job.CompletedAtUtc is not null) continue;
            Start("retire:" + job.Binding.RequestId, () => RetireAsync(job, stop));
        }
    }

    private static async Task<List<TItem>> PageAsync<TPage, TItem>(Func<long, Task<TPage>> read, Func<TPage, TItem[]> items,
        Func<TPage, long?> next) {
        var all = new List<TItem>();
        long after = 0;
        for (var page = 0; page < 16; page++) {
            var current = await read(after).ConfigureAwait(false);
            all.AddRange(items(current));
            if (next(current) is not { } order) break;
            after = order;
        }
        return all;
    }

    private void Start(string key, Func<Task> operation) {
        if (_active.ContainsKey(key)) return;
        var task = Task.Run(async () => {
            try { await operation().ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (Exception error) { Log("operation-held", key + " " + error.GetType().Name + ": " + error.Message); }
            finally { _active.TryRemove(key, out _); }
        });
        _active.TryAdd(key, task);
    }

    /// <summary>Pauses retries of a delivery that reached nobody, so a closed session isn't retried every sweep.</summary>
    private void Defer(string key) => _deferred[key] = DateTimeOffset.UtcNow.AddMinutes(1);
    private bool Deferred(string key) => _deferred.TryGetValue(key, out var until) && until > DateTimeOffset.UtcNow;

    private async Task RecordUsageAsync(string requestId, string kind, string provider, string model, TurnTokens tokens,
        double? cost, string? sessionId, CancellationToken stop) {
        try {
            await _ledger.RecordUsageAsync(requestId, new CoordinationUsage(kind, provider, Safe(model), tokens.Input, tokens.CachedInput,
                tokens.Output, tokens.Total, _machineId, DateTimeOffset.UtcNow, cost, sessionId), stop).ConfigureAwait(false);
        } catch (Exception error) when (error is HttpRequestException or IOException or CoordinationCommandRejectedException or TaskCanceledException) {
            Log("usage-unrecorded", requestId + " " + kind + " " + error.Message);
        }
    }

    /// <summary>Model names may carry characters the Ledger's identifier rule doesn't allow.</summary>
    private static string Safe(string value) {
        var text = new string(value.Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or ':' ? c : '-').ToArray());
        return text.Length is 0 ? "unknown" : text.Length > 128 ? text[..128] : text;
    }
}
