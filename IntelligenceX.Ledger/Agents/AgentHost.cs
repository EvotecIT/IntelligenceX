using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using IntelligenceX.Ledger.Contracts;

namespace IntelligenceX.Ledger.Agents;

/// <summary>The machine's agent service, its own process running as the signed-in user. It heartbeats this host,
/// runs helper requests admitted to this machine through the Codex app server, delivers results, questions, notices and
/// answers through the supported clients, retires finished helpers, and records tokens for every turn it causes.
/// Waiting is plain HTTP reads and app-server notifications; no model is ever used to wait or poll.</summary>
public static class AgentHost {
    public static AgentSettings Load(string path) =>
        JsonSerializer.Deserialize<AgentSettings>(File.ReadAllText(path), new JsonSerializerOptions(JsonSerializerDefaults.Web) {
            UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
        }) ?? throw new InvalidDataException("Missing agent configuration.");

    /// <summary>Runs the agent service as its own process until Ctrl+C or SIGTERM. One instance runs per state folder
    /// (a held lock file), and agent-process.json records its process for the installer.</summary>
    public static async Task<int> RunProcessAsync(string configPath) {
        var settings = Load(configPath);
        Directory.CreateDirectory(settings.StateDirectory);
        FileStream instance;
        try {
            instance = new FileStream(Path.Combine(settings.StateDirectory, "agent.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        } catch (IOException) {
            Console.Error.WriteLine("The agent service is already running for " + settings.StateDirectory + ".");
            return 1;
        }
        using (instance) {
            using var self = Process.GetCurrentProcess();
            LocalFiles.WriteAtomic(Path.Combine(settings.StateDirectory, "agent-process.json"), JsonSerializer.Serialize(new {
                pid = self.Id, startedAtUtc = self.StartTime.ToUniversalTime(), config = Path.GetFullPath(configPath)
            }));
            using var stop = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
            using var terminate = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context => { context.Cancel = true; stop.Cancel(); });
            using var publisher = new SignInPublisher(settings.ReporterConfig);
            var publishing = publisher.RunAsync(stop.Token);
            await RunAsync(settings, settings.MachineId, stop.Token, publisher.Publish).ConfigureAwait(false);
            try { await publishing.ConfigureAwait(false); } catch (OperationCanceledException) { }
            return 0;
        }
    }

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
    public static async Task RunAsync(AgentSettings settings, string machineId, CancellationToken stop,
        Action<SignInFact>? publishSignIn = null) {
        while (!stop.IsCancellationRequested) {
            try {
                await new AgentService(settings, machineId, publishSignIn: publishSignIn).RunAsync(stop).ConfigureAwait(false);
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
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _runs = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _deferred = new(StringComparer.Ordinal);
    private readonly string _bootId;
    private CancellationToken _life;
    private AskPolicy _policy = new();
    private int _grantSeconds = 120;

    private readonly Action<SignInFact> _publishSignIn;

    internal AgentService(AgentSettings settings, string machineId, LedgerReadClient? ledger = null,
        Action<SignInFact>? publishSignIn = null) {
        _publishSignIn = publishSignIn ?? (_ => { });
        _settings = settings;
        _machineId = machineId;
        _ledger = ledger ?? LedgerReadClient.ForCoordination(CoordinationClientSettings.Load(settings.CoordinationConfig));
        _codex = new(settings.CodexExecutable, settings.CodexControlSocket);
        Directory.CreateDirectory(settings.StateDirectory);
        Directory.CreateDirectory(settings.WorkspaceRoot);
        _claude = new(settings.ClaudeExecutable, settings.StateDirectory);
        _bootId = BootIdentity.Get(settings.StateDirectory, machineId);
    }

    private static (string Line, DateTimeOffset At) _lastLog;

    /// <summary>One JSON line on standard error. The same line repeated within ten minutes is written once.</summary>
    internal static void Log(string code, string? detail = null) {
        var line = code + "|" + detail;
        var now = DateTimeOffset.UtcNow;
        lock (typeof(AgentService)) {
            if (_lastLog.Line == line && now - _lastLog.At < TimeSpan.FromMinutes(10)) return;
            _lastLog = (line, now);
        }
        Console.Error.WriteLine(JsonSerializer.Serialize(new { atUtc = now, agent = code, detail }));
    }

    /// <summary>Heartbeats and sweeps until stopped. A failing pass is logged and the next one runs; nothing escapes the
    /// loop but cancellation, and on the way out every operation this instance started is cancelled and awaited, so a
    /// restarted service never runs beside its predecessor's helpers.</summary>
    public async Task RunAsync(CancellationToken stop) {
        Log("agent-service-started", _machineId);
        using var life = CancellationTokenSource.CreateLinkedTokenSource(stop);
        _life = life.Token;
        try {
            var nextHeartbeat = DateTimeOffset.MinValue;
            var nextPrune = DateTimeOffset.MinValue;
            while (!stop.IsCancellationRequested) {
                try {
                    if (DateTimeOffset.UtcNow >= nextHeartbeat) {
                        await HeartbeatAsync(life.Token).ConfigureAwait(false);
                        nextHeartbeat = DateTimeOffset.UtcNow.AddSeconds(Math.Clamp(_grantSeconds / 3.0, 5, 10));
                    }
                    Start("sign-in-check", () => CheckSignInAsync(life.Token));
                    await SweepAsync(life.Token).ConfigureAwait(false);
                    if (DateTimeOffset.UtcNow >= nextPrune) {
                        RunRecord.Prune(_settings.StateDirectory, TimeSpan.FromDays(14), key => _active.ContainsKey(key));
                        nextPrune = DateTimeOffset.UtcNow.AddHours(1);
                    }
                } catch (Exception error) when (!stop.IsCancellationRequested) {
                    Log("sweep-unavailable", error.GetType().Name + ": " + error.Message);
                }
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, _settings.SweepSeconds)), stop).ConfigureAwait(false);
            }
        } finally {
            await life.CancelAsync().ConfigureAwait(false);
            await Task.WhenAll(_active.Values.Select(task => task.ContinueWith(_ => { }, TaskScheduler.Default))).ConfigureAwait(false);
        }
    }

    private async Task HeartbeatAsync(CancellationToken stop) {
        // Codex is offered while `codex login status` says it is signed in. Starting an app server just to prove it
        // answers cost a process per machine every minute; a helper that can't start one fails and is retried instead.
        bool codexReady;
        lock (_signInGate) codexReady = _signIn.TryGetValue("codex", out var codex) && codex.Ready;
        try {
            var ack = await _ledger.HostAsync(new CoordinationHost(_machineId, _bootId, DateTimeOffset.UtcNow, _settings.Capabilities,
                CreatableProviders: codexReady ? ["codex"] : []), stop).ConfigureAwait(false);
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
        await RetryReceiptsAsync(stop).ConfigureAwait(false);
        await RetryUsageAsync(stop).ConfigureAwait(false);
        var claudeReady = Ready("claude");
        var probe = !claudeReady && ProbeUsageLimit();
        var held = 0;
        var waiting = new List<string>();
        foreach (var result in await PageAsync(after => _ledger.ResultsAsync(stop, after), page => page.Items, page => page.NextOrder).ConfigureAwait(false)) {
            if (result.ReturnTo.MachineId != _machineId || result.ConsumedAtUtc is not null || result.State is not ("intent" or "held") ||
                result.ReturnTo.Provider is not ("codex" or "claude")) continue;
            waiting.Add(result.RequestId);
            // Until the Claude CLI can send, a Claude delivery is held in the Ledger, never dropped; sign-in releases it.
            // At a usage limit one delivery goes through now and then to see whether the limit has passed.
            if (result.ReturnTo.Provider == "claude" && !claudeReady && probe && !Unreachable(result.RequestId) && !Deferred("deliver:" + result.RequestId)) {
                probe = false;
                Start("result:" + result.RequestId + ":" + result.Generation, () => DeliverResultAsync(result, stop));
                continue;
            }
            if (result.ReturnTo.Provider == "claude" && !claudeReady) {
                held++;
                if (result.State != "held" || result.HeldOn != SignInHold)
                    Start("hold:" + result.RequestId, () => _ledger.ResultReceiptAsync(result.RequestId, new CoordinationDeliveryCommand(result.Generation,
                        new CoordinationDeliveryReceipt("hold-" + LocalFiles.Hash(result.RequestId, result.Generation)[..24], "held", SignInHold)), stop));
                continue;
            }
            if (result.ReturnTo.Provider == "claude" && Unreachable(result.RequestId)) {
                if (result.State != "held" || result.HeldOn != UnreachableHold)
                    Start("hold:" + result.RequestId, () => _ledger.ResultReceiptAsync(result.RequestId, new CoordinationDeliveryCommand(result.Generation,
                        new CoordinationDeliveryReceipt("hold-" + LocalFiles.Hash(result.RequestId, result.Generation)[..24], "held", UnreachableHold)), stop));
                continue;
            }
            if (Deferred("deliver:" + result.RequestId)) continue;
            Start("result:" + result.RequestId + ":" + result.Generation, () => DeliverResultAsync(result, stop));
        }
        foreach (var item in await PageAsync(after => _ledger.QuestionsAsync(stop, after), page => page.Items,
            page => page.NextOrder).ConfigureAwait(false)) {
            // Answer-side items are delivered by the helper's own run, which waits for them.
            if (item.Target != "question" || item.ReturnTo.Provider is not ("codex" or "claude")) continue;
            var question = item.Question;
            waiting.Add(question.RequestId);
            if (item.ReturnTo.Provider == "claude" && !claudeReady && probe && !Unreachable(question.RequestId) &&
                !Deferred("deliver:" + question.RequestId)) {
                probe = false;
                Start("question:" + question.RequestId + "#" + question.Number, () => DeliverQuestionAsync(item, stop));
                continue;
            }
            if (item.ReturnTo.Provider == "claude" && !claudeReady) {
                held++;
                if (question.Delivery?.EvidenceRef != SignInHold)
                    Start("hold:" + question.RequestId + "#" + question.Number, () => _ledger.QuestionDeliveryAsync(question.RequestId, question.Number,
                        new("question", "hold-" + LocalFiles.Hash(question.RequestId, question.Number)[..24], "held", SignInHold), stop));
                continue;
            }
            if (item.ReturnTo.Provider == "claude" && Unreachable(question.RequestId)) {
                if (question.Delivery?.EvidenceRef != UnreachableHold)
                    Start("hold:" + question.RequestId + "#" + question.Number, () => _ledger.QuestionDeliveryAsync(question.RequestId, question.Number,
                        new("question", "hold-" + LocalFiles.Hash(question.RequestId, question.Number)[..24], "held", UnreachableHold), stop));
                continue;
            }
            if (Deferred("deliver:" + question.RequestId)) continue;
            Start("question:" + question.RequestId + "#" + question.Number, () => DeliverQuestionAsync(item, stop));
        }
        CountHeld(held);
        // Deliveries in progress aren't in the feeds; their entries stay until the outcome is known.
        waiting.AddRange(_active.Keys.Where(key => key.StartsWith("result:", StringComparison.Ordinal) || key.StartsWith("question:", StringComparison.Ordinal))
            .Select(key => key[(key.IndexOf(':') + 1)..].Split(['#', ':'])[0]));
        ForgetUnreachable(waiting);
        foreach (var job in await PageAsync(after => _ledger.RetirementsAsync(stop, after), page => page.Items, page => page.NextOrder).ConfigureAwait(false)) {
            if (job.Binding.MachineId != _machineId || job.Binding.Provider != "codex" || job.CompletedAtUtc is not null) continue;
            Start("retire:" + job.Binding.RequestId, () => RetireAsync(job, stop));
        }
    }

    /// <summary>Reads every page of a feed. Items that stay pending (a delivery in progress, a held one) remain in the feed,
    /// so stopping at a fixed page count would let older items hide newer ones. The cursor must move forward.</summary>
    private static async Task<List<TItem>> PageAsync<TPage, TItem>(Func<long, Task<TPage>> read, Func<TPage, TItem[]> items,
        Func<TPage, long?> next) {
        var all = new List<TItem>();
        long after = 0;
        while (true) {
            var current = await read(after).ConfigureAwait(false);
            all.AddRange(items(current));
            if (next(current) is not { } order || order <= after) break;
            after = order;
        }
        return all;
    }

    /// <summary>Starts one operation unless it is running or paused after a failure; a failed operation waits a minute
    /// before a sweep tries it again, so a persistent fault is retried and logged once a minute rather than every sweep.</summary>
    private void Start(string key, Func<Task> operation) {
        if (_active.ContainsKey(key) || Deferred(key)) return;
        // Registered before it runs, so a fast operation can't remove its key before the key is added.
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var task = Task.Run(async () => {
            if (!await gate.Task.ConfigureAwait(false)) return;
            try { await operation().ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (Exception error) {
                Defer(key);
                Log("operation-held", key + " " + error.GetType().Name + ": " + error.Message);
            }
        });
        if (!_active.TryAdd(key, task)) {
            gate.SetResult(false);
            return;
        }
        _ = task.ContinueWith(done => _active.TryRemove(new KeyValuePair<string, Task>(key, done)), TaskScheduler.Default);
        gate.SetResult(true);
    }

    /// <summary>Pauses retries of a delivery that reached nobody, so a closed session isn't retried every sweep.</summary>
    private void Defer(string key) => _deferred[key] = DateTimeOffset.UtcNow.AddMinutes(1);
    private bool Deferred(string key) => _deferred.TryGetValue(key, out var until) && until > DateTimeOffset.UtcNow;

    internal sealed record PendingUsage(string RequestId, CoordinationUsage Usage);

    private string UsageFolder => Path.Combine(_settings.StateDirectory, "usage");

    /// <summary>Records tokens spent for a request. The entry is written locally first and removed once the Ledger has
    /// it, so an outage delays usage instead of losing it; a resend is identical and the Ledger keeps it once.</summary>
    private async Task RecordUsageAsync(string requestId, string kind, string provider, string model, TurnTokens tokens,
        double? cost, string? sessionId, CancellationToken stop, DateTimeOffset? at = null) {
        var usage = new CoordinationUsage(kind, provider, Safe(model), tokens.Input, tokens.CachedInput,
            tokens.Output, tokens.Total, _machineId, at ?? DateTimeOffset.UtcNow, cost, sessionId);
        var file = Path.Combine(UsageFolder, LocalFiles.Hash(requestId, kind, sessionId, usage.AtUtc.ToString("O"))[..32] + ".json");
        // Accounting runs after a delivery or turn has happened, so its own failures are logged, never thrown.
        try { LocalFiles.WriteAtomic(file, JsonSerializer.Serialize(new PendingUsage(requestId, usage))); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { Log("usage-unsaved", requestId + " " + error.Message); }
        await SendUsageAsync(file, requestId, usage, stop).ConfigureAwait(false);
    }

    private async Task RetryUsageAsync(CancellationToken stop) {
        if (!Directory.Exists(UsageFolder)) return;
        foreach (var file in Directory.EnumerateFiles(UsageFolder, "*.json")) {
            PendingUsage? pending;
            try { pending = JsonSerializer.Deserialize<PendingUsage>(File.ReadAllText(file)); } catch (JsonException) { pending = null; }
            if (pending is null) { File.Delete(file); continue; }
            await SendUsageAsync(file, pending.RequestId, pending.Usage, stop).ConfigureAwait(false);
        }
    }

    private async Task SendUsageAsync(string file, string requestId, CoordinationUsage usage, CancellationToken stop) {
        try {
            await _ledger.RecordUsageAsync(requestId, usage, stop).ConfigureAwait(false);
            TryDelete(file);
        } catch (CoordinationCommandRejectedException error) {
            Log("usage-rejected", requestId + " " + usage.Kind + " " + error.Message);
            TryDelete(file);
        } catch (Exception error) when (error is HttpRequestException or IOException or UnauthorizedAccessException ||
            error is TaskCanceledException && !stop.IsCancellationRequested) {
            // Kept on disk; the next sweep sends it again.
            Log("usage-unrecorded", requestId + " " + usage.Kind + " " + error.Message);
        }
    }

    private static void TryDelete(string file) {
        try { File.Delete(file); } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Model names may carry characters the Ledger's identifier rule doesn't allow.</summary>
    private static string Safe(string value) {
        var text = new string(value.Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or ':' ? c : '-').ToArray());
        return text.Length is 0 ? "unknown" : text.Length > 128 ? text[..128] : text;
    }
}
