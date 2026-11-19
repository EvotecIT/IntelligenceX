using System.Text.Json;
using System.Text.Json.Nodes;

namespace IntelligenceX.Ledger.Agents;

/// <summary>Tokens for one turn, summed from the app server's per-response usage updates.</summary>
internal sealed record TurnTokens(long Input, long CachedInput, long Output, long Total) {
    public static TurnTokens None { get; } = new(0, 0, 0, 0);
    public TurnTokens Add(TurnTokens other) => new(Input + other.Input, CachedInput + other.CachedInput, Output + other.Output, Total + other.Total);
    public TurnTokens Add(JsonElement breakdown) => new(Input + Long(breakdown, "inputTokens"),
        CachedInput + Long(breakdown, "cachedInputTokens"), Output + Long(breakdown, "outputTokens"), Total + Long(breakdown, "totalTokens"));
    private static long Long(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var number) && number.TryGetInt64(out var result) ? result : 0;
}

/// <summary>A turn that outlived its limit and could not be confirmed stopped; it stays subject to reconciliation.</summary>
internal sealed class TurnStillRunningException(string message) : InvalidOperationException(message);

/// <summary>How one turn ended. Status is the app server's turn status (completed, failed, interrupted) or timed-out.</summary>
internal sealed record TurnOutcome(string TurnId, string Status, string? FinalText, TurnTokens Tokens, string Model, string? Error) {
    /// <summary>The account ran out of Codex usage: nothing about the task failed, so the turn is retried once it passes.</summary>
    public bool UsageLimited => Error?.StartsWith(CodexRunner.UsageLimitPrefix, StringComparison.Ordinal) == true;
}

/// <summary>How a message reached a Codex thread: a new turn, or input steered into the turn already running.</summary>
internal sealed record CodexDelivery(string Route, string TurnId, string Model);

/// <summary>Helper threads and deliveries through the Codex app server. Completion comes from turn/completed
/// notifications; a lost connection is reconnected and the turn read once, never polled.</summary>
internal sealed class CodexRunner(string executable, string? controlSocket) {
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(30);

    public Task<CodexAppServer> OpenAsync(CancellationToken token) => CodexAppServer.OpenAsync(executable, controlSocket, token);

    public async Task<string> StartThreadAsync(string cwd, string? model, string title, CancellationToken token) {
        await using var server = await OpenAsync(token).ConfigureAwait(false);
        var parameters = new JsonObject { ["cwd"] = cwd, ["ephemeral"] = false, ["approvalPolicy"] = "never" };
        if (model is not null) parameters["model"] = model;
        var started = await server.CallAsync("thread/start", parameters, CallTimeout, token).ConfigureAwait(false);
        var id = started.GetProperty("thread").GetProperty("id").GetString() ?? throw new InvalidDataException("thread/start returned no id.");
        await NameAsync(server, id, title, token).ConfigureAwait(false);
        return id;
    }

    /// <summary>Starts a new thread holding a copy of an earlier thread's conversation, in a new working directory, so a
    /// follow-up keeps its context while the earlier thread stays as it was. An archived thread can be forked too.</summary>
    public async Task<string> ForkThreadAsync(string sourceThreadId, string cwd, string? model, string title, CancellationToken token) {
        await using var server = await OpenAsync(token).ConfigureAwait(false);
        var parameters = new JsonObject { ["threadId"] = sourceThreadId, ["cwd"] = cwd, ["ephemeral"] = false,
            ["approvalPolicy"] = "never", ["excludeTurns"] = true };
        if (model is not null) parameters["model"] = model;
        var forked = await server.CallAsync("thread/fork", parameters, CallTimeout, token).ConfigureAwait(false);
        var id = forked.GetProperty("thread").GetProperty("id").GetString() ?? throw new InvalidDataException("thread/fork returned no id.");
        await NameAsync(server, id, title, token).ConfigureAwait(false);
        return id;
    }

    /// <summary>Gives a helper thread the routed-worker title and section the Codex app and the Ledger recognise.</summary>
    private static async Task NameAsync(CodexAppServer server, string threadId, string title, CancellationToken token) {
        try {
            await server.CallAsync("thread/name/set", new JsonObject { ["threadId"] = threadId, ["name"] = "Auto: " + title }, CallTimeout, token).ConfigureAwait(false);
            if (!server.Live) return;
            var sections = await server.CallAsync("threadSection/list", new JsonObject { ["limit"] = 200 }, CallTimeout, token).ConfigureAwait(false);
            string? section = null;
            var list = sections.TryGetProperty("data", out var data) ? data : sections.TryGetProperty("sections", out var named) ? named : default;
            if (list.ValueKind == JsonValueKind.Array)
                section = list.EnumerateArray().Where(s => s.TryGetProperty("name", out var n) && n.GetString() == "Auto-routed")
                    .Select(s => s.GetProperty("id").GetString()).FirstOrDefault();
            if (section is null) {
                var created = await server.CallAsync("threadSection/create", new JsonObject { ["name"] = "Auto-routed" }, CallTimeout, token).ConfigureAwait(false);
                section = (created.TryGetProperty("section", out var s) ? s : created).GetProperty("id").GetString();
            }
            if (section is not null)
                await server.CallAsync("thread/section/move", new JsonObject { ["threadId"] = threadId, ["sectionId"] = section }, CallTimeout, token).ConfigureAwait(false);
        } catch (Exception error) when (error is IOException or TimeoutException or KeyNotFoundException or InvalidOperationException) {
            // The title is display metadata; the thread's identity is already bound.
        }
    }

    /// <summary>Starts one helper turn and waits for its completion notification, up to the policy's turn limit. A turn
    /// that outlives the limit is interrupted. Usage is summed from token-usage notifications for this turn.
    /// <paramref name="submitting"/> records the thread's turn count before turn/start; when an earlier call recorded one
    /// (<paramref name="knownTurns"/>) and the thread has more turns now, that submission was accepted and its turn is
    /// followed instead of submitting the input again.</summary>
    public async Task<TurnOutcome> RunTurnAsync(string threadId, string cwd, string text, JsonObject? outputSchema, string? model,
        string? effort, TimeSpan limit, int? knownTurns, Func<int, Task> submitting, Func<string, Task> started, TurnWatch watch,
        CancellationToken token) {
        var deadline = DateTimeOffset.UtcNow + limit;
        var server = await OpenAsync(token).ConfigureAwait(false);
        try {
            server.Notification += watch.Observe;
            var resumed = await ResumeAsync(server, threadId, cwd, token).ConfigureAwait(false);
            var turns = await TurnIdsAsync(server, threadId, knownTurns.HasValue, token).ConfigureAwait(false);
            string turnId;
            if (knownTurns is { } known && turns.Count > known) turnId = turns[known];
            else {
                await submitting(turns.Count).ConfigureAwait(false);
                var parameters = new JsonObject { ["threadId"] = threadId, ["input"] = Input(text), ["approvalPolicy"] = "never" };
                if (model is not null) parameters["model"] = model;
                if (effort is not null) parameters["effort"] = effort;
                if (outputSchema is not null) parameters["outputSchema"] = outputSchema.DeepClone();
                JsonElement response;
                try { response = await server.CallAsync("turn/start", parameters, CallTimeout, token).ConfigureAwait(false); }
                catch (CodexRpcException error) {
                    // The app server refused the turn (for example a model or effort it doesn't offer), so nothing started.
                    // The caller sends the same input again a bounded number of times, then publishes a failure result.
                    return new("", "rejected", null, TurnTokens.None, model ?? resumed, "turn/start rejected: " + error.Message);
                }
                turnId = response.GetProperty("turn").GetProperty("id").GetString() ?? throw new InvalidDataException("turn/start returned no id.");
            }
            watch.Follow(turnId);
            await started(turnId).ConfigureAwait(false);
            return await AwaitTurnAsync(server, watch, cwd, model ?? resumed, deadline, token, reconnect: async () => {
                server.Notification -= watch.Observe;
                await server.DisposeAsync().ConfigureAwait(false);
                server = await OpenAsync(token).ConfigureAwait(false);
                server.Notification += watch.Observe;
                await ResumeAsync(server, threadId, cwd, token).ConfigureAwait(false);
                return server;
            }).ConfigureAwait(false);
        } finally {
            server.Notification -= watch.Observe;
            await server.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Follows a turn that was started before this process restarted.</summary>
    public async Task<TurnOutcome> FollowTurnAsync(string threadId, string turnId, string cwd, TimeSpan limit, TurnWatch watch,
        CancellationToken token) {
        watch.Follow(turnId);
        var server = await OpenAsync(token).ConfigureAwait(false);
        try {
            server.Notification += watch.Observe;
            var model = await ResumeAsync(server, threadId, cwd, token).ConfigureAwait(false);
            return await AwaitTurnAsync(server, watch, cwd, model, DateTimeOffset.UtcNow + limit, token, reconnect: async () => {
                server.Notification -= watch.Observe;
                await server.DisposeAsync().ConfigureAwait(false);
                server = await OpenAsync(token).ConfigureAwait(false);
                server.Notification += watch.Observe;
                await ResumeAsync(server, threadId, cwd, token).ConfigureAwait(false);
                return server;
            }).ConfigureAwait(false);
        } finally {
            server.Notification -= watch.Observe;
            await server.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static async Task<TurnOutcome> AwaitTurnAsync(CodexAppServer server, TurnWatch watch, string cwd, string model,
        DateTimeOffset deadline, CancellationToken token, Func<Task<CodexAppServer>> reconnect) {
        while (true) {
            // The turn may have finished before this subscription; read it once, then wait for its notification.
            if (await ReadTurnAsync(server, watch.ThreadId, watch.TurnId!, token).ConfigureAwait(false) is { } finished && finished.Status != "inProgress")
                watch.Complete(finished.Status, finished.FinalText, finished.Error);
            var remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero) break;
            var closed = WaitClosedAsync(server, token);
            var done = await Task.WhenAny(watch.Completed, closed, Task.Delay(remaining, token)).ConfigureAwait(false);
            if (done == watch.Completed) {
                var (status, text, error) = await watch.Completed.ConfigureAwait(false);
                return new(watch.TurnId!, status, text, watch.Tokens, model, error);
            }
            token.ThrowIfCancellationRequested();
            if (done == closed) { server = await reconnect().ConfigureAwait(false); continue; }
            break;
        }
        // A timed-out turn counts as ended only once it is seen stopped; otherwise a resume or terminal receipt could
        // overlap a turn the live daemon is still running.
        try {
            await server.CallAsync("turn/interrupt", new JsonObject { ["threadId"] = watch.ThreadId, ["turnId"] = watch.TurnId }, CallTimeout, token).ConfigureAwait(false);
        } catch (Exception error) when (error is IOException or TimeoutException) { }
        if (await Task.WhenAny(watch.Completed, Task.Delay(StopTimeout, token)).ConfigureAwait(false) != watch.Completed &&
            !await StoppedAsync(server, watch.ThreadId, watch.TurnId!, token).ConfigureAwait(false))
            throw new TurnStillRunningException("Turn " + watch.TurnId + " passed its limit and was not confirmed stopped.");
        return new(watch.TurnId!, "timed-out", watch.LastMessage, watch.Tokens, model, "turn-limit-reached");
    }

    private static async Task<bool> StoppedAsync(CodexAppServer server, string threadId, string turnId, CancellationToken token) {
        try { return await ReadTurnAsync(server, threadId, turnId, token).ConfigureAwait(false) is { Status: not "inProgress" }; }
        catch (Exception error) when (error is IOException or TimeoutException) { return false; }
    }

    /// <summary>The thread's turn ids, oldest first. A failed read counts as no turns only for a fresh submission, or when
    /// Codex says the thread has no rollout yet (no turn was ever accepted); otherwise it could submit the input twice.</summary>
    private static async Task<List<string>> TurnIdsAsync(CodexAppServer server, string threadId, bool pending, CancellationToken token) {
        try {
            var read = await server.CallAsync("thread/read", new JsonObject { ["threadId"] = threadId, ["includeTurns"] = true }, CallTimeout, token).ConfigureAwait(false);
            return read.GetProperty("thread").TryGetProperty("turns", out var turns) && turns.ValueKind == JsonValueKind.Array
                ? turns.EnumerateArray().Select(turn => turn.GetProperty("id").GetString()!).ToList() : [];
        } catch (CodexRpcException error) when (!pending || error.Message.Contains("no rollout", StringComparison.OrdinalIgnoreCase)) { return []; }
    }

    private static async Task WaitClosedAsync(CodexAppServer server, CancellationToken token) {
        while (!server.Closed) await Task.Delay(TimeSpan.FromSeconds(5), token).ConfigureAwait(false);
    }

    private static async Task<string> ResumeAsync(CodexAppServer server, string threadId, string cwd, CancellationToken token) {
        var resumed = await server.CallAsync("thread/resume", new JsonObject {
            ["threadId"] = threadId, ["cwd"] = cwd, ["excludeTurns"] = true
        }, CallTimeout, token).ConfigureAwait(false);
        return resumed.TryGetProperty("model", out var model) && model.ValueKind == JsonValueKind.String ? model.GetString()! : "unknown";
    }

    private static async Task<(string Status, string? FinalText, string? Error)?> ReadTurnAsync(CodexAppServer server, string threadId,
        string turnId, CancellationToken token) {
        var read = await server.CallAsync("thread/read", new JsonObject { ["threadId"] = threadId, ["includeTurns"] = true }, CallTimeout, token).ConfigureAwait(false);
        if (!read.TryGetProperty("thread", out var thread) || !thread.TryGetProperty("turns", out var turns) || turns.ValueKind != JsonValueKind.Array) return null;
        foreach (var turn in turns.EnumerateArray()) {
            if (turn.GetProperty("id").GetString() != turnId) continue;
            return (Status(turn), FinalText(turn), Error(turn));
        }
        return null;
    }

    internal static string Status(JsonElement turn) => turn.TryGetProperty("status", out var status)
        ? status.ValueKind == JsonValueKind.String ? status.GetString()! : status.TryGetProperty("type", out var type) ? type.GetString()! : "unknown"
        : "unknown";

    internal static string? FinalText(JsonElement turn) => turn.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array
        ? items.EnumerateArray().Where(item => item.TryGetProperty("type", out var type) && type.GetString() == "agentMessage")
            .Select(item => item.TryGetProperty("text", out var text) ? text.GetString() : null).LastOrDefault(text => !string.IsNullOrWhiteSpace(text))
        : null;

    internal const string UsageLimitPrefix = "[usageLimitExceeded] ";

    private static string? Error(JsonElement turn) => turn.TryGetProperty("error", out var error) ? ErrorText(error) : null;

    /// <summary>A turn error's message, prefixed when the app server classifies it as the account's usage limit.</summary>
    internal static string? ErrorText(JsonElement error) {
        if (error.ValueKind != JsonValueKind.Object) return null;
        var message = error.TryGetProperty("message", out var text) && text.ValueKind == JsonValueKind.String ? text.GetString() : null;
        var limited = error.TryGetProperty("codexErrorInfo", out var info) && info.ValueKind == JsonValueKind.String &&
            info.GetString() == "usageLimitExceeded";
        return limited ? UsageLimitPrefix + (message ?? "usage limit reached") : message;
    }

    private static JsonArray Input(string text) => [new JsonObject { ["type"] = "text", ["text"] = text, ["text_elements"] = new JsonArray() }];

    /// <summary>Delivers text into an existing thread: steered into its running turn, or as a new turn when idle.</summary>
    public async Task<(CodexDelivery Delivery, CodexAppServer Server, TurnWatch Watch)> DeliverAsync(string threadId, string text,
        CancellationToken token) {
        var server = await OpenAsync(token).ConfigureAwait(false);
        try {
            var watch = new TurnWatch(threadId);
            server.Notification += watch.Observe;
            var read = await server.CallAsync("thread/read", new JsonObject { ["threadId"] = threadId, ["includeTurns"] = true }, CallTimeout, token).ConfigureAwait(false);
            var running = read.GetProperty("thread").TryGetProperty("turns", out var turns) && turns.ValueKind == JsonValueKind.Array
                ? turns.EnumerateArray().LastOrDefault() : default;
            var known = turns.ValueKind == JsonValueKind.Array ? turns.GetArrayLength() : 0;
            // The Codex app (or the local daemon) holding the thread refuses this server's resume with "active writer", the
            // same answer turn/start gives; either way the message goes through the queue to the holder.
            string model;
            try {
                model = await ResumeAsync(server, threadId, read.GetProperty("thread").TryGetProperty("cwd", out var cwd) &&
                    cwd.ValueKind == JsonValueKind.String ? cwd.GetString()! : Environment.CurrentDirectory, token).ConfigureAwait(false);
            } catch (CodexRpcException error) when (ActiveWriter(error)) {
                return await QueueInsteadAsync(server, watch, threadId, text, "unknown", known, token).ConfigureAwait(false);
            }
            try {
                if (running.ValueKind == JsonValueKind.Object && Status(running) == "inProgress") {
                    var turnId = running.GetProperty("id").GetString()!;
                    try {
                        await server.CallAsync("turn/steer", new JsonObject { ["threadId"] = threadId, ["expectedTurnId"] = turnId, ["input"] = Input(text) },
                            CallTimeout, token).ConfigureAwait(false);
                        watch.Follow(turnId);
                        return (new("steer", turnId, model), server, watch);
                    } catch (CodexRpcException) { /* The turn ended meanwhile; start a new one below. */ }
                }
                var response = await server.CallAsync("turn/start", new JsonObject { ["threadId"] = threadId, ["input"] = Input(text) }, CallTimeout, token).ConfigureAwait(false);
                var started = response.GetProperty("turn").GetProperty("id").GetString()!;
                watch.Follow(started);
                return (new("turn", started, model), server, watch);
            } catch (CodexRpcException error) when (ActiveWriter(error)) {
                return await QueueInsteadAsync(server, watch, threadId, text, model, known, token).ConfigureAwait(false);
            } catch (Exception error) when (error is TimeoutException or System.Net.WebSockets.WebSocketException ||
                error is IOException and not CodexRpcException) {
                // The request was written; whether the thread took it is unknown.
                throw new DeliveryUncertainException("Codex delivery outcome unknown.", error);
            }
        } catch {
            await server.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Interrupts a running turn, if any, waits until it is seen stopped, and returns the thread's last agent
    /// message. Throws <see cref="TurnStillRunningException"/> when the turn can't be confirmed stopped.</summary>
    public async Task<string?> CloseAsync(string threadId, CancellationToken token) {
        await using var server = await OpenAsync(token).ConfigureAwait(false);
        var read = await server.CallAsync("thread/read", new JsonObject { ["threadId"] = threadId, ["includeTurns"] = true }, CallTimeout, token).ConfigureAwait(false);
        if (!read.GetProperty("thread").TryGetProperty("turns", out var turns) || turns.ValueKind != JsonValueKind.Array) return null;
        var last = turns.EnumerateArray().LastOrDefault();
        if (last.ValueKind != JsonValueKind.Object) return null;
        if (Status(last) == "inProgress") {
            var turnId = last.GetProperty("id").GetString()!;
            try {
                await server.CallAsync("turn/interrupt", new JsonObject { ["threadId"] = threadId, ["turnId"] = turnId }, CallTimeout, token).ConfigureAwait(false);
            } catch (CodexRpcException) { }
            var deadline = DateTimeOffset.UtcNow + StopTimeout;
            while (!await StoppedAsync(server, threadId, turnId, token).ConfigureAwait(false)) {
                if (DateTimeOffset.UtcNow > deadline) throw new TurnStillRunningException("Turn " + turnId + " was not confirmed stopped.");
                await Task.Delay(TimeSpan.FromSeconds(2), token).ConfigureAwait(false);
            }
            read = await server.CallAsync("thread/read", new JsonObject { ["threadId"] = threadId, ["includeTurns"] = true }, CallTimeout, token).ConfigureAwait(false);
            turns = read.GetProperty("thread").GetProperty("turns");
        }
        return turns.EnumerateArray().Select(FinalText).LastOrDefault(text => text is not null);
    }

    /// <summary>True when the thread has no running turn, so its helper holds no process. An archived thread has none.</summary>
    public async Task<bool> IdleAsync(string threadId, CancellationToken token) {
        await using var server = await OpenAsync(token).ConfigureAwait(false);
        try {
            var read = await server.CallAsync("thread/read", new JsonObject { ["threadId"] = threadId, ["includeTurns"] = true }, CallTimeout, token).ConfigureAwait(false);
            return !read.GetProperty("thread").TryGetProperty("turns", out var turns) || turns.ValueKind != JsonValueKind.Array ||
                turns.EnumerateArray().All(turn => Status(turn) != "inProgress");
        } catch (CodexRpcException) {
            if (await ArchivedAsync(server, threadId, token).ConfigureAwait(false)) return true;
            throw;
        }
    }

    private static bool ActiveWriter(CodexRpcException error) => error.Message.Contains("active writer", StringComparison.OrdinalIgnoreCase);

    /// <summary>The Codex app (or the local daemon) holds the thread. The supported route for outside input is
    /// `codex queue --resume`: the message is stored for the holder, or the daemon is asked to run it. Without either the
    /// message only waits in the queue, which nothing may drain, so that is reported apart. A writer lock alone doesn't
    /// show that its holder reads the queue, so a message left with a holder counts as delivered only once the thread has
    /// a new turn (queue-app); otherwise it stays unconfirmed (queue-held).</summary>
    private async Task<(CodexDelivery, CodexAppServer, TurnWatch)> QueueInsteadAsync(CodexAppServer server, TurnWatch watch, string threadId,
        string text, string model, int knownTurns, CancellationToken token) {
        await server.DisposeAsync().ConfigureAwait(false);
        var queued = await QueueAsync(threadId, text, token).ConfigureAwait(false);
        var route = queued switch {
            QueueOutcome.Resumed => "queue",
            QueueOutcome.HolderOwned => await NewTurnAsync(threadId, knownTurns, HolderConfirmTimeout, token).ConfigureAwait(false) ? "queue-app" : "queue-held",
            _ => "queue-parked"
        };
        return (new(route, "queued", model), server, watch);
    }

    /// <summary>How long a message left with the thread's holder is watched for the turn that takes it.</summary>
    internal static readonly TimeSpan HolderConfirmTimeout = TimeSpan.FromMinutes(2);

    /// <summary>True once the thread has more than <paramref name="knownTurns"/> turns, read without taking the thread.</summary>
    private async Task<bool> NewTurnAsync(string threadId, int knownTurns, TimeSpan timeout, CancellationToken token) {
        var deadline = DateTimeOffset.UtcNow + timeout;
        try {
            await using var reader = await OpenAsync(token).ConfigureAwait(false);
            while (true) {
                var read = await reader.CallAsync("thread/read", new JsonObject { ["threadId"] = threadId, ["includeTurns"] = true }, CallTimeout, token).ConfigureAwait(false);
                if (read.GetProperty("thread").TryGetProperty("turns", out var turns) && turns.ValueKind == JsonValueKind.Array &&
                    turns.GetArrayLength() > knownTurns) return true;
                if (DateTimeOffset.UtcNow >= deadline) return false;
                await Task.Delay(TimeSpan.FromSeconds(5), token).ConfigureAwait(false);
            }
        } catch (Exception error) when (error is IOException or TimeoutException or System.ComponentModel.Win32Exception or
            InvalidOperationException or System.Net.WebSockets.WebSocketException) {
            // The message is already queued: a failed look is an unconfirmed delivery, never a reason to queue it again.
            return false;
        }
    }

    /// <summary>What `codex queue` did: queued and resumed by the local daemon; queued for the client that holds the thread
    /// (the app runs it as its next turn); queued only (no daemon resumed it, or the CLI can't ask); a CLI too old for
    /// --resume (nothing was queued, so it can be asked again without it); or failed.</summary>
    internal enum QueueOutcome { Resumed, HolderOwned, Parked, ResumeUnsupported, Failed }

    internal static QueueOutcome ClassifyQueue(int exitCode, string stderr, bool askedToResume) {
        // clap rejects an unknown flag with exit code 2 before doing anything.
        if (exitCode == 2 && askedToResume && stderr.Contains("unexpected argument '--resume'", StringComparison.Ordinal)) return QueueOutcome.ResumeUnsupported;
        if (exitCode != 0) return QueueOutcome.Failed;
        if (!askedToResume) return QueueOutcome.Parked;
        if (!stderr.Contains("could not resume thread", StringComparison.OrdinalIgnoreCase)) return QueueOutcome.Resumed;
        // The resume was refused because a client already holds the thread: that client takes the queued message next.
        return stderr.Contains("active writer", StringComparison.OrdinalIgnoreCase) ? QueueOutcome.HolderOwned : QueueOutcome.Parked;
    }

    private async Task<QueueOutcome> QueueAsync(string threadId, string text, CancellationToken token) {
        var (exit, stderr) = await RunQueueAsync(threadId, text, resume: true, token).ConfigureAwait(false);
        var outcome = ClassifyQueue(exit, stderr, askedToResume: true);
        if (outcome == QueueOutcome.ResumeUnsupported) {
            (exit, stderr) = await RunQueueAsync(threadId, text, resume: false, token).ConfigureAwait(false);
            outcome = ClassifyQueue(exit, stderr, askedToResume: false);
        }
        if (outcome == QueueOutcome.Failed) throw new DeliveryUncertainException("codex queue exited with " + exit + ".", new IOException(stderr.Trim()));
        return outcome;
    }

    private async Task<(int Exit, string Error)> RunQueueAsync(string threadId, string text, bool resume, CancellationToken token) {
        var start = new System.Diagnostics.ProcessStartInfo(executable) {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[] { "queue", "--thread", threadId, "--message", text }) start.ArgumentList.Add(argument);
        if (resume) start.ArgumentList.Add("--resume");
        using var process = System.Diagnostics.Process.Start(start) ?? throw new IOException("codex queue did not start.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(60));
        var error = process.StandardError.ReadToEndAsync(deadline.Token);
        try {
            await process.StandardOutput.ReadToEndAsync(deadline.Token).ConfigureAwait(false);
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
        } catch (OperationCanceledException error2) {
            // A queue that outlives its wait (or the service) is ended, so it can't hand the message over later.
            AgentService.Stop(process);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            if (token.IsCancellationRequested) throw;
            throw new DeliveryUncertainException("codex queue did not finish within a minute.", error2);
        }
        return (process.ExitCode, await error.ConfigureAwait(false));
    }

    /// <summary>Archives the thread. One that is already archived (for example by a pass interrupted before it recorded
    /// the step) counts as archived once the archived list shows it.</summary>
    public async Task ArchiveAsync(string threadId, CancellationToken token) {
        await using var server = await OpenAsync(token).ConfigureAwait(false);
        try {
            await server.CallAsync("thread/archive", new JsonObject { ["threadId"] = threadId }, CallTimeout, token).ConfigureAwait(false);
        } catch (CodexRpcException) {
            if (!await ArchivedAsync(server, threadId, token).ConfigureAwait(false)) throw;
        }
    }

    private static async Task<bool> ArchivedAsync(CodexAppServer server, string threadId, CancellationToken token) {
        string? cursor = null;
        for (var page = 0; page < 100; page++) {
            var parameters = new JsonObject { ["archived"] = true, ["limit"] = 200 };
            if (cursor is not null) parameters["cursor"] = cursor;
            var listed = await server.CallAsync("thread/list", parameters, CallTimeout, token).ConfigureAwait(false);
            if (listed.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array &&
                data.EnumerateArray().Any(thread => thread.TryGetProperty("id", out var id) && id.GetString() == threadId)) return true;
            cursor = listed.TryGetProperty("nextCursor", out var next) && next.ValueKind == JsonValueKind.String ? next.GetString() : null;
            if (cursor is null) return false;
        }
        return false;
    }
}

/// <summary>Collects one turn's notifications: completion, final message and summed token usage. Usage starts from
/// <paramref name="tokens"/> (what a turn followed after a restart had already spent) and each update is reported to
/// <paramref name="checkpoint"/>.</summary>
internal sealed class TurnWatch(string threadId, TurnTokens? tokens = null, Action<TurnTokens>? checkpoint = null) {
    private readonly TaskCompletionSource<(string Status, string? Text, string? Error)> _completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _gate = new();
    private TurnTokens _tokens = tokens ?? TurnTokens.None;
    public string ThreadId { get; } = threadId;
    public string? TurnId { get; private set; }
    public string? LastMessage { get; private set; }
    public bool ServerRequested { get; private set; }
    public TurnTokens Tokens { get { lock (_gate) return _tokens; } }
    public Task<(string Status, string? Text, string? Error)> Completed => _completed.Task;

    private readonly Dictionary<string, TurnTokens> _early = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _earlyText = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string Status, string? Text, string? Error)> _earlyDone = new(StringComparer.Ordinal);

    /// <summary>Starts a new turn's count from zero; the checkpoint it was created with belonged to the previous turn.</summary>
    public void Reset() { lock (_gate) _tokens = TurnTokens.None; }

    /// <summary>Follows a turn, taking any usage and message that arrived for it before its id was known.</summary>
    public void Follow(string turnId) {
        TurnTokens? now = null;
        lock (_gate) {
            TurnId = turnId;
            if (_early.Remove(turnId, out var early)) now = _tokens = _tokens.Add(early);
            if (_earlyText.Remove(turnId, out var text)) LastMessage = text;
            _earlyDone.Remove(turnId, out var done);
            _early.Clear();
            _earlyText.Clear();
            _earlyDone.Clear();
            if (done.Status is not null) Complete(done.Status, done.Text, done.Error);
            if (now is not null) checkpoint?.Invoke(now);
        }
    }
    public void Complete(string status, string? text, string? error) => _completed.TrySetResult((status, text ?? LastMessage, error));
    public void MarkServerRequest() => ServerRequested = true;

    public void Observe(string method, JsonElement parameters) {
        if (parameters.ValueKind != JsonValueKind.Object || !parameters.TryGetProperty("threadId", out var thread) ||
            thread.GetString() != ThreadId) return;
        if (TurnId is null && method == "turn/completed" && parameters.TryGetProperty("turn", out var earlyTurn) &&
            earlyTurn.GetProperty("id").GetString() is { } earlyId) {
            lock (_gate) {
                if (TurnId is null) {
                    var earlyError = earlyTurn.TryGetProperty("error", out var earlyFailure) ? CodexRunner.ErrorText(earlyFailure) : null;
                    _earlyDone[earlyId] = (CodexRunner.Status(earlyTurn), CodexRunner.FinalText(earlyTurn), earlyError);
                    return;
                }
            }
        }
        if (TurnId is null && parameters.TryGetProperty("turnId", out var pendingTurn) && pendingTurn.GetString() is { } early) {
            lock (_gate) {
                if (TurnId is null) {
                    if (method == "thread/tokenUsage/updated" && parameters.TryGetProperty("tokenUsage", out var earlyUsage) &&
                        earlyUsage.TryGetProperty("last", out var earlyLast))
                        _early[early] = (_early.GetValueOrDefault(early) ?? TurnTokens.None).Add(earlyLast);
                    else if (method == "item/completed" && parameters.TryGetProperty("item", out var earlyItem) &&
                        earlyItem.TryGetProperty("type", out var earlyType) && earlyType.GetString() == "agentMessage" &&
                        earlyItem.TryGetProperty("text", out var earlyText) && earlyText.GetString() is { Length: > 0 } message)
                        _earlyText[early] = message;
                    return;
                }
            }
        }
        switch (method) {
            case "thread/tokenUsage/updated" when Matches(parameters, "turnId") &&
                parameters.TryGetProperty("tokenUsage", out var usage) && usage.TryGetProperty("last", out var last):
                // The checkpoint is written under the same lock, so an older total can't overwrite a newer one.
                lock (_gate) {
                    _tokens = _tokens.Add(last);
                    checkpoint?.Invoke(_tokens);
                }
                break;
            case "item/completed" when Matches(parameters, "turnId") && parameters.TryGetProperty("item", out var item) &&
                item.TryGetProperty("type", out var type) && type.GetString() == "agentMessage" &&
                item.TryGetProperty("text", out var text) && !string.IsNullOrWhiteSpace(text.GetString()):
                LastMessage = text.GetString();
                break;
            case "turn/completed" when parameters.TryGetProperty("turn", out var turn) && turn.GetProperty("id").GetString() == TurnId:
                var error = turn.TryGetProperty("error", out var failure) ? CodexRunner.ErrorText(failure) : null;
                Complete(CodexRunner.Status(turn), CodexRunner.FinalText(turn), error);
                break;
        }
    }

    private bool Matches(JsonElement parameters, string name) => TurnId is not null &&
        parameters.TryGetProperty(name, out var value) && value.GetString() == TurnId;
}
