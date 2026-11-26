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
            var local = RunLocalAsync(settings, stop.Token);
            await RunAsync(settings, settings.MachineId, stop.Token, publisher.Publish).ConfigureAwait(false);
            await local.ConfigureAwait(false);
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
            await using var server = await CodexAppServer.OpenAsync(CodexCli.Resolve(settings.CodexExecutable), settings.CodexControlSocket, timeout.Token);
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

    /// <summary>Serves the local coordination store once a policy for this machine has been cached, runs a second agent
    /// instance against it (its own state folder, the same helper and delivery code), keeps its policy current and
    /// imports its finished requests into the Ledger whenever the Ledger answers.</summary>
    internal static async Task RunLocalAsync(AgentSettings settings, CancellationToken stop) {
        while (!stop.IsCancellationRequested) {
            LocalCoordination? local = null;
            try {
                local = await LocalCoordination.StartAsync(settings, stop).ConfigureAwait(false);
                if (local is null) {
                    await Task.Delay(TimeSpan.FromMinutes(1), stop).ConfigureAwait(false);
                    continue;
                }
                AgentService.Log("local-store-started", local.Endpoint);
                using var ledger = LedgerReadClient.ForCoordination(CoordinationClientSettings.Load(settings.CoordinationConfig));
                using var client = LedgerReadClient.ForCoordination(new CoordinationClientSettings(local.Endpoint, local.HostToken));
                var service = new AgentService(settings with {
                        StateDirectory = Path.Combine(LocalCoordination.Folder(settings), "agent"), HookStateDirectory = settings.StateDirectory },
                    settings.MachineId, client, publishSignIn: _ => { }, cachesPolicy: false) {
                    // The instance that works the Ledger already asks the user to sign in; a second dialog would repeat it.
                    SignInPrompt = (_, _) => Task.CompletedTask
                };
                using var restart = CancellationTokenSource.CreateLinkedTokenSource(stop);
                var running = service.RunAsync(restart.Token);
                while (!stop.IsCancellationRequested && !running.IsCompleted) {
                    if (local.ProjectsChanged(settings)) {
                        // The store's principals carry the projects it started with: restart it with the new ones.
                        AgentService.Log("local-store-projects-changed", local.Endpoint);
                        await restart.CancelAsync().ConfigureAwait(false);
                        try { await running.ConfigureAwait(false); } catch (OperationCanceledException) when (!stop.IsCancellationRequested) { }
                        break;
                    }
                    try {
                        await local.RefreshPolicyAsync(settings, stop).ConfigureAwait(false);
                        var imported = await local.ExportAsync(ledger, stop).ConfigureAwait(false);
                        if (imported > 0) AgentService.Log("local-imported", imported.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    } catch (Exception error) when (error is HttpRequestException or IOException or TaskCanceledException or JsonException &&
                        !stop.IsCancellationRequested) {
                        // The Ledger is still unreachable; the next pass tries again.
                    }
                    await Task.WhenAny(running, Task.Delay(TimeSpan.FromMinutes(1), stop)).ConfigureAwait(false);
                }
                if (!restart.IsCancellationRequested || stop.IsCancellationRequested) await running.ConfigureAwait(false);
            } catch (OperationCanceledException) when (stop.IsCancellationRequested) {
                return;
            } catch (Exception error) {
                AgentService.Log("local-store-restart", error.GetType().Name + ": " + error.Message);
                try { await Task.Delay(TimeSpan.FromSeconds(30), stop).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
            } finally {
                if (local is not null) await local.DisposeAsync().ConfigureAwait(false);
            }
        }
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
    private readonly ClaudeRunner _claudeHelper;
    private readonly ConcurrentDictionary<string, Task> _active = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _runs = new(StringComparer.Ordinal);
    /// <summary>The grant of each helper run in <see cref="_runs"/>, reported with every heartbeat so the service renews it.</summary>
    private readonly ConcurrentDictionary<string, CoordinationActiveGrant> _running = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _deferred = new(StringComparer.Ordinal);
    private readonly string _bootId;
    private CancellationToken _life;
    private AskPolicy _policy = new();
    private int _grantSeconds = 120;
    /// <summary>Set once the service acknowledged that it renews listed grants; until then heartbeats omit the list.</summary>
    private volatile bool _renewsActiveGrants;

    private readonly Action<SignInFact> _publishSignIn;
    /// <summary>True for the instance that works the Ledger: it keeps the policy copy the local store starts from.</summary>
    private readonly bool _cachesPolicy;

    internal AgentService(AgentSettings settings, string machineId, LedgerReadClient? ledger = null,
        Action<SignInFact>? publishSignIn = null, bool cachesPolicy = true) {
        _cachesPolicy = cachesPolicy;
        _publishSignIn = publishSignIn ?? (_ => { });
        _settings = settings;
        _machineId = machineId;
        _ledger = ledger ?? LedgerReadClient.ForCoordination(CoordinationClientSettings.Load(settings.CoordinationConfig));
        _codex = new(settings.CodexExecutable, settings.CodexControlSocket);
        Directory.CreateDirectory(settings.StateDirectory);
        Directory.CreateDirectory(settings.WorkspaceRoot);
        _claude = new(settings.ClaudeExecutable, settings.StateDirectory);
        _claudeHelper = new(settings.ClaudeExecutable, settings.ClaudeHelperPermissionMode);
        _bootId = BootIdentity.Get(settings.HookState, machineId);
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

    /// <summary>The grants this host has taken up: the helpers running now first, then unfinished run records, at most
    /// what one observation may list. A listed grant is never returned to the queue; only running ones are renewed.
    /// Null while a run record can't be read, or when there are more than one observation may list, so the service
    /// keeps reconciliation for everything instead of treating a missing grant as never taken up.</summary>
    internal CoordinationActiveGrant[]? ActiveGrants() {
        var running = _running.Values.ToArray();
        if (RunRecord.Unfinished(_settings.StateDirectory) is not { } unfinished) return null;
        var taken = unfinished
            .Where(r => !running.Any(g => g.RequestId == r.RequestId && g.Generation == r.Generation))
            .Select(r => new CoordinationActiveGrant(r.RequestId, r.Generation, r.GrantId, Running: false));
        var all = running.OrderBy(g => g.RequestId, StringComparer.Ordinal).ThenBy(g => g.Generation).Concat(taken).ToArray();
        return all.Length <= Coordination.CoordinationEngine.MaximumActiveGrants ? all : null;
    }

    private async Task HeartbeatAsync(CancellationToken stop) {
        // A provider is offered while its status check says it is signed in. Starting one just to prove it answers cost a
        // process per machine every minute; a helper that can't start fails and is retried instead.
        string[] creatable;
        lock (_signInGate) creatable = new[] { "codex", "claude" }
            .Where(provider => _signIn.TryGetValue(provider, out var state) && state.Ready).ToArray();
        try {
            var ack = await _ledger.HostAsync(new CoordinationHost(_machineId, _bootId, DateTimeOffset.UtcNow, _settings.Capabilities,
                CreatableProviders: creatable) { ActiveGrants = _renewsActiveGrants ? ActiveGrants() : null }, stop).ConfigureAwait(false);
            _renewsActiveGrants = ack.RenewsActiveGrants;
            if (ack.GrantSeconds is > 0) _grantSeconds = ack.GrantSeconds.Value;
            if (ack.Ask is not null) _policy = ack.Ask;
            if (_cachesPolicy && ack.PolicyRevision is { } revision && ack.Projects is { } projects &&
                (LocalCoordination.Cached(_settings) is not { } cached || cached.Policy.Revision != revision || !cached.Projects.SequenceEqual(projects)))
                LocalCoordination.Cache(_settings, await _ledger.HostPolicyAsync(stop).ConfigureAwait(false), projects);
        } catch (Exception error) when (error is HttpRequestException or IOException or TaskCanceledException or
            CoordinationCommandRejectedException or JsonException && !stop.IsCancellationRequested) {
            Log("heartbeat-unavailable", error.GetType().Name + ": " + error.Message);
        }
    }

    /// <summary>One pass over the four feeds this host serves. Each actionable item starts at most one task.</summary>
    private async Task SweepAsync(CancellationToken stop) {
        foreach (var work in await PageAsync(after => _ledger.AssignedAsync(stop, after), page => page.Items, page => page.NextOrder).ConfigureAwait(false)) {
            if (work.Machine != _machineId || work.Request.Kind != "help" || work.Provider is not ("codex" or "claude")) continue;
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
        foreach (var wake in await PageAsync(after => _ledger.WakesAsync(stop, after), page => page.Items, page => page.NextOrder).ConfigureAwait(false)) {
            if (wake.MachineId != _machineId || wake.Provider is not ("codex" or "claude")) continue;
            waiting.Add(wake.Id);
            if (wake.Provider == "claude" && (!claudeReady && !probe || Unreachable(wake.Id))) {
                var reason = claudeReady ? UnreachableHold : SignInHold;
                if (!claudeReady) held++;
                if (wake.Delivery?.EvidenceRef != reason)
                    Start("hold:" + wake.Id, () => _ledger.WakeDeliveryAsync(wake.Id, new("hold-" + LocalFiles.Hash(wake.Id)[..24], "held", reason), stop));
                continue;
            }
            if (wake.Provider == "claude" && !claudeReady) probe = false;
            if (Deferred("deliver:" + wake.Id)) continue;
            Start("wake:" + wake.Id, () => DeliverWakeAsync(wake, stop));
        }
        CountHeld(held);
        // Deliveries in progress aren't in the feeds; their entries stay until the outcome is known.
        waiting.AddRange(_active.Keys.Where(key => key.StartsWith("result:", StringComparison.Ordinal) || key.StartsWith("question:", StringComparison.Ordinal) ||
                key.StartsWith("wake:", StringComparison.Ordinal) && key.Count(c => c == ':') == 1)
            .Select(key => key[(key.IndexOf(':') + 1)..].Split(['#', ':'])[0]));
        ForgetUnreachable(waiting);
        foreach (var job in await PageAsync(after => _ledger.RetirementsAsync(stop, after), page => page.Items, page => page.NextOrder).ConfigureAwait(false)) {
            if (job.Binding.MachineId != _machineId || job.Binding.Provider is not ("codex" or "claude") || job.CompletedAtUtc is not null ||
                Deferred("retire:" + job.Binding.RequestId)) continue;
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

    private readonly ConcurrentDictionary<string, int> _failures = new(StringComparer.Ordinal);

    /// <summary>Pauses a delivery that keeps failing for longer each time (1, 2, 4 … up to 60 minutes), so a thread that
    /// can't be reached doesn't start a Codex process every minute for hours.</summary>
    private void Backoff(string key) {
        var failures = _failures.AddOrUpdate(key, 1, (_, count) => count + 1);
        _deferred[key] = DateTimeOffset.UtcNow.Add(BackoffDelay(failures));
    }

    internal static TimeSpan BackoffDelay(int failures) => TimeSpan.FromMinutes(Math.Min(60, Math.Pow(2, Math.Clamp(failures - 1, 0, 6))));

    private void Succeeded(string key) => _failures.TryRemove(key, out _);

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
