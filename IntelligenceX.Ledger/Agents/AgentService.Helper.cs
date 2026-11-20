using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using IntelligenceX.Ledger.Contracts;

namespace IntelligenceX.Ledger.Agents;

internal sealed partial class AgentService {
    private const int MaximumResultBytes = 8192;

    /// <summary>The final answer shape asked of every helper turn: a result, or one question for the requester.</summary>
    internal static readonly JsonObject HelperOutputSchema = new() {
        ["type"] = "object",
        ["properties"] = new JsonObject {
            ["outcome"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("result", "question") },
            ["text"] = new JsonObject { ["type"] = "string" }
        },
        ["required"] = new JsonArray("outcome", "text"),
        ["additionalProperties"] = false
    };

    /// <summary>The final answer of a review ask: the same result-or-question shape, plus a structured verdict with
    /// findings and the checks that were run. A check counts by its final attempt.</summary>
    internal static readonly JsonObject ReviewOutputSchema = new() {
        ["type"] = "object",
        ["properties"] = new JsonObject {
            ["outcome"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("result", "question") },
            ["text"] = new JsonObject { ["type"] = "string" },
            ["review"] = new JsonObject {
                ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["verdict"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("approve", "needs-attention") },
                    ["summary"] = new JsonObject { ["type"] = "string" },
                    ["findings"] = new JsonObject {
                        ["type"] = "array",
                        ["items"] = new JsonObject {
                            ["type"] = "object",
                            ["properties"] = new JsonObject {
                                ["severity"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("critical", "high", "medium", "low") },
                                ["title"] = new JsonObject { ["type"] = "string" },
                                ["file"] = new JsonObject { ["type"] = "string" },
                                ["line"] = new JsonObject { ["type"] = "integer" },
                                ["detail"] = new JsonObject { ["type"] = "string" },
                                ["recommendation"] = new JsonObject { ["type"] = "string" }
                            },
                            ["required"] = new JsonArray("severity", "title", "file", "line", "detail", "recommendation"),
                            ["additionalProperties"] = false
                        }
                    },
                    ["validation"] = new JsonObject {
                        ["type"] = "array",
                        ["items"] = new JsonObject {
                            ["type"] = "object",
                            ["properties"] = new JsonObject {
                                ["check"] = new JsonObject { ["type"] = "string" },
                                ["command"] = new JsonObject { ["type"] = "string" },
                                ["result"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("pass", "fail", "skipped") },
                                ["final"] = new JsonObject { ["type"] = "boolean" },
                                ["notes"] = new JsonObject { ["type"] = "string" }
                            },
                            ["required"] = new JsonArray("check", "command", "result", "final", "notes"),
                            ["additionalProperties"] = false
                        }
                    }
                },
                ["required"] = new JsonArray("verdict", "summary", "findings", "validation"),
                ["additionalProperties"] = false
            }
        },
        ["required"] = new JsonArray("outcome", "text", "review"),
        ["additionalProperties"] = false
    };

    private static bool IsReview(CoordinationWork work) => work.Request.Helper?.Output == "review";
    private static JsonObject OutputSchema(CoordinationWork work) => IsReview(work) ? ReviewOutputSchema : HelperOutputSchema;
    private string? Model(CoordinationWork work) => work.Request.Helper?.Model ?? _policy.CodexModel;
    private string? Effort(CoordinationWork work) => work.Request.Helper?.Effort ?? _policy.CodexEffort;

    /// <summary>Branch and commit as the helper should read them. An ask about another repository may leave either
    /// open (<c>HEAD</c>, all-zero commit): then the helper uses the default branch, or the latest of the branch.</summary>
    internal static string Where(WorkCheckpoint checkpoint) {
        var branch = checkpoint.Branch == "HEAD" ? "its default branch" : "branch " + checkpoint.Branch;
        return checkpoint.Commit.All(c => c == '0') ? branch + ", latest commit" : branch + ", commit " + checkpoint.Commit;
    }

    internal static string HelperPrompt(CoordinationWork work, string workspace) {
        var r = work.Request;
        var from = r.ReturnTo is { } back ? back.Provider + " session " + back.SessionId + " on " + back.MachineId : "another session";
        var prompt = new StringBuilder();
        if (r.Helper?.ContinuesRequestId is { } earlier)
            prompt.Append("This continues your earlier work on ask ").Append(earlier).AppendLine(", which is the conversation above. " +
                "This run has its own working directory, separate from the earlier one.").AppendLine();
        prompt.Append("You are a helper for ").Append(from).AppendLine(", working through the IntelligenceX Ledger.")
            .AppendLine().AppendLine("Task:").AppendLine(r.TaskBrief ?? "(no task text)").AppendLine()
            .Append("The work is in the ").Append(r.Checkpoint.Project).Append(" repository (").Append(Where(r.Checkpoint)).AppendLine(").")
            .Append("Your working directory ").Append(workspace).AppendLine(" holds only the Ledger's marker file. To work on the code, " +
                "clone the repository into a subfolder of it and check out that branch or commit. Anything you leave there is kept for " +
                "review, so remove clones and scratch you don't need.");
        if (IsReview(work))
            prompt.Append("Review exactly commit ").Append(r.Checkpoint.Commit).AppendLine(": check it out by that SHA (not the branch, " +
                "which may have moved) and confirm HEAD is that commit before you start. If it can't be fetched or HEAD differs, give " +
                "review.verdict \"needs-attention\" with a finding that says so, and don't review anything else.");
        if (!IsReview(work)) prompt.AppendLine("Push anything worth keeping before you finish.");
        if (IsReview(work))
            prompt.AppendLine().AppendLine("This is a review. Review the changes on that branch at that commit against the base the task names " +
                    "(the repository's default branch if it names none). Use the repository's own conventions and AGENTS.md. Be adversarial: look " +
                    "for correctness bugs, regressions, missing tests, unsafe changes and anything that should block a merge. Run the validation " +
                    "that matters for these changes (build, tests, analyzers) where practical and record each command in review.validation.")
                .AppendLine("Give every attempt of a check the same check name. If an attempt failed for a setup reason and you corrected and " +
                    "reran it, mark the earlier attempt final: false and the rerun final: true. A final fail blocks the merge.")
                .AppendLine("Do not modify, commit, push or delete anything in the repository, and do not merge.")
                .AppendLine("Set review.verdict to \"approve\" only if nothing should block the merge; otherwise \"needs-attention\" with " +
                    "concrete findings. text is a one-paragraph summary for the requester. For a question, fill review with " +
                    "\"needs-attention\" and empty lists; it is ignored.");
        return prompt.AppendLine().AppendLine("Finish with the JSON final answer the output schema asks for:")
            .AppendLine("- outcome \"result\": text is your answer for the requester (keep it under about 6000 characters).")
            .AppendLine("- outcome \"question\": text is one clear question only the requester can answer. The answer arrives in this conversation and you continue.")
            .ToString();
    }

    /// <summary>Turns a review answer into the published result: the verdict as JSON with pass worked out the way the merge
    /// gate always did (approve, no critical or high finding, no check whose final attempt failed). When the whole would pass
    /// the Ledger's 8 KB, superseded check attempts and then the lowest-severity findings are dropped and truncated is set.
    /// Null when the answer has no review.</summary>
    internal static string? ReviewResult(string? finalText, string threadId) {
        if (string.IsNullOrWhiteSpace(finalText)) return null;
        JsonObject review;
        try {
            if (JsonNode.Parse(finalText) is not JsonObject root || root["review"] is not JsonObject found) return null;
            review = (JsonObject)found.DeepClone();
        } catch (Exception error) when (error is JsonException or InvalidOperationException) { return null; }
        var verdict = Str(review["verdict"]);
        var findings = review["findings"] as JsonArray ?? [];
        var checks = review["validation"] as JsonArray ?? [];
        // Attempts of one check share its name (compared without case); a check without a name is keyed by its exact command,
        // then its position, and the three kinds never share a key. Each check counts by its last attempt marked final, or by
        // its last attempt when none is, so a check whose only attempt failed still fails.
        var attempts = new Dictionary<string, (string Label, JsonNode? Final, JsonNode Last)>(StringComparer.Ordinal);
        for (var i = 0; i < checks.Count; i++) {
            if (checks[i] is not JsonObject check) continue;
            var (key, label) = Str(check["check"])?.Trim() is { Length: > 0 } named ? ("check:" + named.ToLowerInvariant(), named)
                : Str(check["command"])?.Trim() is { Length: > 0 } command ? ("command:" + command, command) : ("index:" + i, "#" + i);
            var superseded = check["final"] is JsonValue final && final.TryGetValue<bool>(out var isFinal) && !isFinal;
            var known = attempts.TryGetValue(key, out var seen) ? seen : (label, null, check);
            attempts[key] = (known.Label, superseded ? known.Final : check, check);
        }
        var failed = attempts.Values.Where(a => Str((a.Final ?? a.Last)["result"]) == "fail")
            .Select(a => (JsonNode?)JsonValue.Create(a.Label)).ToArray();
        var blocking = findings.Any(f => Str(f?["severity"]) is "critical" or "high");
        var result = new JsonObject {
            ["pass"] = verdict == "approve" && !blocking && failed.Length == 0,
            ["verdict"] = verdict,
            ["failedChecks"] = new JsonArray(failed),
            ["summary"] = review["summary"]?.DeepClone(),
            ["findings"] = findings.DeepClone(),
            ["validation"] = checks.DeepClone()
        };
        bool Fits() => Encoding.UTF8.GetByteCount(result.ToJsonString()) <= MaximumResultBytes;
        if (Fits()) return result.ToJsonString();
        result["truncated"] = "The full review is in Codex thread " + threadId + ".";
        var kept = (JsonArray)result["validation"]!;
        // Superseded attempts go first, then long text is shortened, then the least severe findings are dropped.
        while (!Fits() && kept.FirstOrDefault(c => c?["final"] is JsonValue v && v.TryGetValue<bool>(out var f) && !f) is { } superseded)
            kept.Remove(superseded);
        if (!Fits()) {
            Clip(result, "summary", 2000);
            foreach (var check in kept.OfType<JsonObject>()) { Clip(check, "notes", 300); Clip(check, "command", 200); Clip(check, "check", 120); }
            foreach (var finding in ((JsonArray)result["findings"]!).OfType<JsonObject>()) {
                Clip(finding, "detail", 600); Clip(finding, "recommendation", 300); Clip(finding, "title", 160); Clip(finding, "file", 200);
            }
        }
        var order = new[] { "low", "medium", "high", "critical" };
        var list = (JsonArray)result["findings"]!;
        while (!Fits() && list.Count > 0) list.Remove(list.OrderBy(f => Array.IndexOf(order, Str(f?["severity"]) ?? "low")).First());
        return Fits() ? result.ToJsonString() : CompactReview(result, threadId);
    }

    private static void Clip(JsonObject node, string name, int length) {
        if (Str(node[name]) is { } text && text.Length > length) node[name] = text[..length] + "...";
    }

    /// <summary>The smallest review result: the decision, the failed checks and a shortened summary, still valid JSON and
    /// within the byte limit as serialized (escaped non-ASCII text takes up to six bytes a character).</summary>
    private static string CompactReview(JsonObject result, string threadId) {
        var summary = Str(result["summary"]) ?? "";
        var failed = ((JsonArray)result["failedChecks"]!).Select(Str).OfType<string>().ToArray();
        for (int length = 1000, checks = 10; ; length /= 2, checks /= 2) {
            var json = new JsonObject {
                ["pass"] = result["pass"]?.DeepClone(),
                ["verdict"] = result["verdict"]?.DeepClone(),
                ["failedChecks"] = new JsonArray(failed.Take(checks).Select(c => (JsonNode?)JsonValue.Create(Shorten(c, 100))).ToArray()),
                ["summary"] = Shorten(summary, length),
                ["findings"] = new JsonArray(),
                ["validation"] = new JsonArray(),
                ["truncated"] = "The full review is in Codex thread " + threadId + "."
            }.ToJsonString();
            if (Encoding.UTF8.GetByteCount(json) <= MaximumResultBytes || length == 0 && checks == 0) return json;
        }
    }

    private static string Shorten(string text, int length) => text.Length <= length ? text : text[..length] + "...";

    /// <summary>A review that ended without a verdict (a question it couldn't ask, or a failure) still answers in the review
    /// shape, as a non-passing result whose summary says what happened.</summary>
    internal static string ReviewWithoutVerdict(string text, string threadId) => CompactReview(new JsonObject {
        ["pass"] = false, ["verdict"] = "needs-attention", ["failedChecks"] = new JsonArray(), ["summary"] = text
    }, threadId);

    /// <summary>Result content for a helper's own text: bounded, and in the review shape for a review ask.</summary>
    private static string TextResult(CoordinationWork work, string text, string threadId, string source = "Codex thread") =>
        IsReview(work) ? ReviewWithoutVerdict(text, threadId) : Bound(text, threadId, source);

    private static string? Str(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    internal static string AnswerPrompt(CoordinationQuestion question, double waitMinutes) => question.State == "expired"
        ? "No answer to your question arrived within " + waitMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture) +
          " minutes. Continue with your best judgement and say in your final answer what you assumed. Finish with the same JSON final answer."
        : "The requester answered your question:\n\n" + question.Answer + "\n\nContinue the task and finish with the same JSON final answer.";

    private const string ResumePrompt = "Your previous turn ended before you gave a final answer. Continue the task from where you were " +
        "and finish with the JSON final answer the output schema asks for.";

    internal static (string Outcome, string Text) ParseAnswer(string? text) {
        if (string.IsNullOrWhiteSpace(text)) return ("none", "");
        try {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("outcome", out var outcome) && outcome.ValueKind == JsonValueKind.String &&
                root.TryGetProperty("text", out var body) && outcome.GetString() is "result" or "question" && body.ValueKind == JsonValueKind.String)
                return (outcome.GetString()!, body.GetString()!);
        } catch (JsonException) { }
        return ("result", text);
    }

    /// <summary>Bounds result content to the Ledger's 8 KB, pointing at the helper thread for the rest.</summary>
    internal static string Bound(string text, string threadId, string source = "Codex thread") {
        if (Encoding.UTF8.GetByteCount(text) <= MaximumResultBytes) return text;
        var note = "\n\n[Truncated. The full answer is in " + source + " " + threadId + ".]";
        var budget = MaximumResultBytes - Encoding.UTF8.GetByteCount(note);
        var builder = new StringBuilder();
        foreach (var rune in text.EnumerateRunes()) {
            if (Encoding.UTF8.GetByteCount(builder.ToString()) + rune.Utf8SequenceLength > budget) break;
            builder.Append(rune.ToString());
        }
        return builder.Append(note).ToString();
    }

    private static string RunKey(string requestId, long generation) => "run:" + requestId + ":" + generation;

    /// <summary>The temporary worker a helper thread is enrolled as.</summary>
    private CoordinationWorker HelperWorker(CoordinationWork work, string threadId) => new(work.Provider + "-" + threadId, _machineId,
        work.Provider!, true, null, DateTimeOffset.UtcNow, threadId, WorkspaceId(work), work.Request.Checkpoint, _bootId, TemporaryWorker: true);

    private static string WorkspaceId(CoordinationWork work) => "ledger-workspace-" + LocalFiles.Hash(work.Request.Id, work.Generation)[..40];

    /// <summary>Creates (once) and runs one helper thread for an admitted creation grant, through questions and answers,
    /// to a published result and terminal receipt. Every step is recorded locally first, so a restart continues the same
    /// thread and turn instead of starting new ones. The run stops when the service marks its grant for reconciliation
    /// or rejects a renewal; reconciliation then interrupts the turn and closes the grant.</summary>
    private async Task RunHelperAsync(CoordinationWork work, CancellationToken stop) {
        var id = work.Request.Id;
        var grant = work.GrantId!;
        var record = RunRecord.Load(_settings.StateDirectory, id, work.Generation) ?? new RunRecord {
            RequestId = id, Generation = work.Generation, GrantId = grant,
            Workspace = Path.Combine(_settings.WorkspaceRoot, LocalFiles.Hash(id, work.Generation)[..24])
        }.Save(_settings.StateDirectory);
        if (record.GrantId != grant || record.Phase == "done") return;
        using var run = CancellationTokenSource.CreateLinkedTokenSource(stop);
        var key = RunKey(id, work.Generation);
        _runs[key] = run;
        try {
            stop = run.Token;
            Directory.CreateDirectory(record.Workspace);
            var marker = Path.Combine(record.Workspace, ".ix-ledger-workspace.json");
            if (!File.Exists(marker)) LocalFiles.WriteAtomic(marker, JsonSerializer.Serialize(new { requestId = id, generation = work.Generation, grantId = grant }));
            var claude = work.Provider == "claude";
            var receipt = new ExecutionReceipt(grant, work.Generation, _bootId, work.Provider + "-" + LocalFiles.Hash(id, work.Generation, grant)[..40]);
            if (record.ThreadId is null && claude) {
                // A Claude session is created by its first turn, under an id chosen here and saved first.
                record = (record with { ThreadId = Guid.NewGuid().ToString() }).Save(_settings.StateDirectory);
            } else if (record.ThreadId is null) {
                var task = work.Request.TaskBrief ?? id;
                var title = "Ledger " + id + ": " + (task.Length > 60 ? task[..60] : task).ReplaceLineEndings(" ");
                var thread = work.Request.Helper?.ForkSessionId is { } source
                    ? await _codex.ForkThreadAsync(source, record.Workspace, Model(work), title, stop).ConfigureAwait(false)
                    : await _codex.StartThreadAsync(record.Workspace, Model(work), title, stop).ConfigureAwait(false);
                record = record with { ThreadId = thread };
                record.Save(_settings.StateDirectory);
            }
            var threadId = record.ThreadId!;
            if (!record.Bound) {
                var worker = HelperWorker(work, threadId);
                await _ledger.WorkerAsync(worker, stop).ConfigureAwait(false);
                await _ledger.BindSessionAsync(new CoordinationSessionBinding(id, work.Generation, grant, worker.Id, _machineId, work.Provider!, threadId,
                    WorkspaceId(work), work.Request.Checkpoint), stop).ConfigureAwait(false);
                record = (record with { Bound = true }).Save(_settings.StateDirectory);
            }
            if (!record.Submitted) {
                await _ledger.ReceiptAsync(id, "submitted", receipt, stop).ConfigureAwait(false);
                record = (record with { Submitted = true }).Save(_settings.StateDirectory);
            }
            using var renewal = CancellationTokenSource.CreateLinkedTokenSource(stop);
            var renewing = RenewAsync(id, receipt, run, renewal.Token);
            try {
                if (record.ResultRef is null) record = claude
                    ? await ConverseClaudeAsync(work, record, receipt, stop).ConfigureAwait(false)
                    : await ConverseAsync(work, record, receipt, stop).ConfigureAwait(false);
                if (record.Phase == "done") return;
                await _ledger.ReceiptAsync(id, "terminal", receipt with {
                    ResultRef = record.ResultRef, Succeeded = record.Phase == "succeeded", ExclusionVerified = true
                }, stop).ConfigureAwait(false);
                record.With(r => r with { Phase = "done" }).Save(_settings.StateDirectory);
                Log("helper-finished", id + " " + record.Phase);
            } finally {
                renewal.Cancel();
                try { await renewing.ConfigureAwait(false); } catch (OperationCanceledException) { }
            }
        } finally {
            _runs.TryRemove(new KeyValuePair<string, CancellationTokenSource>(key, run));
        }
    }

    /// <summary>Runs helper turns until a result: raises questions, waits for their answers (plain reads), feeds each
    /// answer back as the next turn, and resumes a failed turn as often as policy allows.</summary>
    private async Task<RunRecord> ConverseAsync(CoordinationWork work, RunRecord record, ExecutionReceipt receipt, CancellationToken stop) {
        var id = work.Request.Id;
        var threadId = record.ThreadId!;
        var limit = TimeSpan.FromMinutes(_policy.TurnMinutes);
        var tokensFile = RunRecord.TokensFile(_settings.StateDirectory, id, work.Generation);
        while (true) {
            if (record.PendingResult is { } pending)
                return await PublishAsync(work, record, pending, record.PendingSucceeded, stop).ConfigureAwait(false);
            var watch = new TurnWatch(threadId, RunRecord.LoadTokens(_settings.StateDirectory, id, work.Generation),
                tokens => LocalFiles.WriteAtomic(tokensFile, JsonSerializer.Serialize(tokens)));
            Task Submitting(int turns) {
                // The previous turn's usage is recorded by now; its checkpoint goes before a new turn adds to it.
                if (record.KnownTurns is null) {
                    File.Delete(tokensFile);
                    watch.Reset();
                }
                record = (record with { KnownTurns = turns }).Save(_settings.StateDirectory);
                return Task.CompletedTask;
            }
            async Task Started(string turnId) {
                record = (record with { Phase = "turn", TurnId = turnId, KnownTurns = null }).Save(_settings.StateDirectory);
                // These run while the turn is live; on Windows a failure here would close the private app server and the
                // turn with it, so a passing Ledger outage is waited out instead.
                if (!record.Started) {
                    await PersistAsync(() => _ledger.ReceiptAsync(id, "started", receipt, stop), stop).ConfigureAwait(false);
                    record = (record with { Started = true }).Save(_settings.StateDirectory);
                }
                await PersistAsync(() => AnswerSubmittedAsync(turnId), stop).ConfigureAwait(false);
            }
            // The answer's delivery is recorded as submitted once its turn exists, including after a restart in between.
            async Task AnswerSubmittedAsync(string turnId) {
                if (record.AnswerAttempt is not { } attempt) return;
                await _ledger.QuestionDeliveryAsync(id, record.QuestionNumber!.Value, new("answer", attempt, "submitted", "codex-turn-" + turnId), stop).ConfigureAwait(false);
                record = (record with { AnswerAttempt = null, AnswerPrompt = null }).Save(_settings.StateDirectory);
            }
            TurnOutcome outcome;
            if (record.TurnId is { } running) {
                await AnswerSubmittedAsync(running).ConfigureAwait(false);
                outcome = await _codex.FollowTurnAsync(threadId, running, record.Workspace, limit, watch, stop).ConfigureAwait(false);
            } else if (record.Phase == "question") {
                if (record.AnswerAttempt is null) {
                    var question = await WaitForAnswerAsync(id, record.QuestionNumber!.Value, stop).ConfigureAwait(false);
                    if (question is null) return record with { Phase = "done" };
                    var attempt = "answer-" + LocalFiles.Hash(id, question.Number, question.AnswerDelivery?.AttemptId)[..32];
                    record = (record with { AnswerAttempt = attempt, AnswerPrompt = AnswerPrompt(question, _policy.QuestionWaitMinutes), KnownTurns = null })
                        .Save(_settings.StateDirectory);
                }
                // Repeating the same sending attempt is accepted, so a restart right after recording it locally is safe.
                await _ledger.QuestionDeliveryAsync(id, record.QuestionNumber!.Value, new("answer", record.AnswerAttempt!, "sending"), stop).ConfigureAwait(false);
                try {
                    outcome = await _codex.RunTurnAsync(threadId, record.Workspace, record.AnswerPrompt!, OutputSchema(work), Model(work),
                        Effort(work), limit, record.KnownTurns, Submitting, Started, watch, stop).ConfigureAwait(false);
                } catch (Exception error) when (record.KnownTurns is null && record.TurnId is null && record.AnswerAttempt is { } attempt &&
                    error is IOException or TimeoutException or InvalidOperationException or System.ComponentModel.Win32Exception or
                        System.Net.WebSockets.WebSocketException) {
                    // Nothing was submitted to the thread: the answer goes back to pending and is delivered on the next try.
                    await _ledger.QuestionDeliveryAsync(id, record.QuestionNumber!.Value, new("answer", attempt, "not-submitted", "codex-thread-unreachable"),
                        CancellationToken.None).ConfigureAwait(false);
                    record = (record with { AnswerAttempt = null, AnswerPrompt = null }).Save(_settings.StateDirectory);
                    throw;
                }
            } else {
                if (record.LimitRetryAtUtc is { } retry && retry > DateTimeOffset.UtcNow)
                    await Task.Delay(retry - DateTimeOffset.UtcNow, stop).ConfigureAwait(false);
                var prompt = record.Resumes > 0 || record.LimitWaits > 0 ? ResumePrompt : HelperPrompt(work, record.Workspace);
                outcome = await _codex.RunTurnAsync(threadId, record.Workspace, prompt, OutputSchema(work), Model(work),
                    Effort(work), limit, record.KnownTurns, Submitting, Started, watch, stop).ConfigureAwait(false);
            }
            if (outcome.Status == "rejected") {
                // Nothing reached the thread, so the same input (task, resume prompt or answer) is sent again unchanged, a
                // bounded number of times; then the requester gets a failure result naming the reason.
                if (record.Rejections < _policy.ResumeAttempts) {
                    record = (record with { Rejections = record.Rejections + 1, KnownTurns = null }).Save(_settings.StateDirectory);
                    Log("helper-turn-rejected", id + " " + outcome.Error);
                    await Task.Delay(TimeSpan.FromSeconds(30), stop).ConfigureAwait(false);
                    continue;
                }
                return await PublishAsync(work, record, TextResult(work, "The helper on " + _machineId + " could not start a turn: " +
                    outcome.Error + ". Codex thread " + threadId + " has the details.", threadId), succeeded: false, stop).ConfigureAwait(false);
            }
            // A turn stopped at the usage limit before spending anything isn't recorded, so long waits don't fill the usage log.
            if (!(outcome.UsageLimited && outcome.Tokens.Total == 0)) {
                if (record.UsageTurn != outcome.TurnId)
                    record = (record with { UsageTurn = outcome.TurnId, UsageAtUtc = DateTimeOffset.UtcNow }).Save(_settings.StateDirectory);
                await RecordUsageAsync(id, "worker", "codex", outcome.Model, outcome.Tokens, null, threadId, stop, record.UsageAtUtc).ConfigureAwait(false);
            }
            if (outcome.UsageLimited) {
                // Nothing about the task failed: the account ran out of Codex usage. The user is asked to sign in with another
                // account, the grant stays renewed, and the same thread is tried again each probe interval without using up
                // a resume. The time of the next try is saved, so a restart waits out the rest of the interval.
                Observe("codex", false, "usage-limit");
                record = (record with { LimitWaits = record.LimitWaits + 1, TurnId = null, Phase = "turn",
                    LimitRetryAtUtc = DateTimeOffset.UtcNow.AddMinutes(_policy.UsageLimitProbeMinutes) }).Save(_settings.StateDirectory);
                Log("helper-usage-limit", id + " " + outcome.Error);
                continue;
            }
            if (outcome.Status == "completed") Observe("codex", true, "turn");
            var (kind, text) = ParseAnswer(outcome.FinalText);
            if (kind == "question" && (string.IsNullOrWhiteSpace(text) || System.Text.Encoding.UTF8.GetByteCount(text) > MaximumResultBytes)) kind = "none";
            if (outcome.Status == "completed" && kind == "question") {
                try {
                    var question = await _ledger.RaiseQuestionAsync(id, new(work.Generation, record.GrantId, _bootId, text), stop).ConfigureAwait(false);
                    record = (record with { Phase = "question", QuestionNumber = question.Number, TurnId = null }).Save(_settings.StateDirectory);
                    Log("helper-question", id + "#" + question.Number);
                    continue;
                } catch (CoordinationCommandRejectedException error) when (error.ErrorCode == "question-limit-reached") {
                    return await PublishAsync(work, record, TextResult(work, "The helper reached the question limit and stopped with " +
                        "this open question:\n\n" + text, threadId), succeeded: true, stop).ConfigureAwait(false);
                }
            }
            // A review is judged by its structured verdict, which the schema allows next to an empty text.
            if (outcome.Status == "completed" && kind == "result" && IsReview(work) && ReviewResult(outcome.FinalText, threadId) is { } review)
                return await PublishAsync(work, record, review, succeeded: true, stop).ConfigureAwait(false);
            if (outcome.Status == "completed" && kind == "result" && !string.IsNullOrWhiteSpace(text))
                return await PublishAsync(work, record, IsReview(work) ? TextResult(work, text, threadId) : Bound(text, threadId),
                    succeeded: true, stop).ConfigureAwait(false);
            if (record.Resumes < _policy.ResumeAttempts) {
                record = (record with { Resumes = record.Resumes + 1, TurnId = null, Phase = "turn" }).Save(_settings.StateDirectory);
                Log("helper-resumed", id + " " + outcome.Status);
                continue;
            }
            var failure = "The helper on " + _machineId + " stopped without an answer (turn " + outcome.Status +
                (outcome.Error is null ? "" : ": " + outcome.Error) + "). Codex thread " + threadId + " has the details." +
                (string.IsNullOrWhiteSpace(outcome.FinalText) ? "" : "\n\nIts last message:\n" + outcome.FinalText);
            return await PublishAsync(work, record, TextResult(work, failure, threadId), succeeded: false, stop).ConfigureAwait(false);
        }
    }

    /// <summary>Retries a Ledger write through transient failures (network, timeout, a restarting service) for up to five
    /// minutes. A rejection is not transient and is thrown at once.</summary>
    private static async Task PersistAsync(Func<Task> write, CancellationToken stop) {
        var deadline = DateTimeOffset.UtcNow.AddMinutes(5);
        while (true) {
            try { await write().ConfigureAwait(false); return; }
            catch (Exception error) when (error is HttpRequestException or IOException ||
                error is TaskCanceledException && !stop.IsCancellationRequested) {
                if (DateTimeOffset.UtcNow > deadline) throw;
                Log("ledger-write-retry", error.Message);
                await Task.Delay(TimeSpan.FromSeconds(10), stop).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Publishes the result content, recording it first: the service binds each assignment to one immutable
    /// content, so a retry or reconciliation must publish exactly the same text.</summary>
    private async Task<RunRecord> PublishAsync(CoordinationWork work, RunRecord record, string content, bool succeeded, CancellationToken stop) {
        record = (record with { PendingResult = content, PendingSucceeded = succeeded }).Save(_settings.StateDirectory);
        var artifact = await _ledger.PublishResultAsync(work.Request.Id, new CoordinationArtifactCommand(work.Generation, record.GrantId,
            _bootId, content), stop).ConfigureAwait(false);
        return (record with { ResultRef = artifact.ResultRef, Phase = succeeded ? "succeeded" : "failed" }).Save(_settings.StateDirectory);
    }

    /// <summary>Waits for the answer to one question with plain Ledger reads at the sweep interval. Returns null when the
    /// request left the question state some other way (cancelled or reconciled).</summary>
    private async Task<CoordinationQuestion?> WaitForAnswerAsync(string id, int number, CancellationToken stop) {
        while (true) {
            var view = await _ledger.RequestAsync(id, stop).ConfigureAwait(false);
            var question = view.Questions.FirstOrDefault(q => q.Number == number);
            if (question is null || view.Work.State is not ("question" or "started")) return null;
            if (question.State is "answered" or "expired" && question.AnswerDelivery is null or { Stage: "held" }) return question;
            if (question.State == "resumed") return null;
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, _settings.SweepSeconds)), stop).ConfigureAwait(false);
        }
    }

    /// <summary>Keeps the grant alive while the helper works or waits for an answer. A renewal the service rejects means
    /// the grant is gone, so the run stops and reconciliation takes over.</summary>
    private async Task RenewAsync(string id, ExecutionReceipt receipt, CancellationTokenSource run, CancellationToken token) {
        while (!token.IsCancellationRequested) {
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(5, _grantSeconds / 3.0)), token).ConfigureAwait(false);
            try { await _ledger.ReceiptAsync(id, "renew", receipt, token).ConfigureAwait(false); }
            catch (CoordinationCommandRejectedException error) {
                Log("renew-rejected", id + " " + error.Message);
                await run.CancelAsync().ConfigureAwait(false);
                return;
            } catch (Exception error) when (error is HttpRequestException or IOException ||
                error is TaskCanceledException && !token.IsCancellationRequested) {
                Log("renew-unavailable", id + " " + error.Message);
            }
        }
    }

    /// <summary>Closes a grant the service marked for reconciliation (lapsed, stopped or from an earlier boot). A running
    /// helper is stopped first; its turn is interrupted and confirmed stopped, what it had is published as the result (the
    /// content it already chose, if any), and the requester is told.</summary>
    private async Task ReconcileAsync(CoordinationWork work, RunRecord? record, CancellationToken stop) {
        if (_runs.TryGetValue(RunKey(work.Request.Id, work.Generation), out var run)) {
            await run.CancelAsync().ConfigureAwait(false);
            return;
        }
        if (_active.ContainsKey(RunKey(work.Request.Id, work.Generation))) return;
        var view = await _ledger.RequestAsync(work.Request.Id, stop).ConfigureAwait(false);
        var binding = view.Attempt;
        string content, disposition;
        if (record?.ThreadId is null && binding is null) {
            disposition = "never-created";
            content = "Request " + work.Request.Id + " was not started on " + _machineId + " (" + (work.Reason ?? "reconcile") +
                "). Ask again if it is still needed.";
        } else {
            var threadId = binding?.SessionId ?? record!.ThreadId!;
            // A Claude turn is a process this service runs, already stopped with its run; there is nothing else to close.
            var last = work.Provider == "claude" ? null : await _codex.CloseAsync(threadId, stop).ConfigureAwait(false);
            disposition = "execution-excluded";
            if (binding is null) {
                // Stopped between the thread's creation and its binding: enroll its worker again so the binding can be recovered.
                var worker = HelperWorker(work, threadId);
                await _ledger.WorkerAsync(worker, stop).ConfigureAwait(false);
                binding = new CoordinationSessionBinding(work.Request.Id, work.Generation, work.GrantId!, worker.Id, _machineId,
                    work.Provider!, threadId, WorkspaceId(work), work.Request.Checkpoint);
            }
            var source = work.Provider == "claude" ? ClaudeSource : "Codex thread";
            content = record?.PendingResult ?? Bound("The helper on " + _machineId + " was stopped before it finished (" + (work.Reason ?? "reconcile") +
                "). " + source + " " + threadId + " has its work so far." + (string.IsNullOrWhiteSpace(last) ? "" : "\n\nIts last message:\n" + last),
                threadId, source);
        }
        record = record?.With(r => r with { PendingResult = content }).Save(_settings.StateDirectory);
        var artifact = await _ledger.PublishResultAsync(work.Request.Id, new CoordinationArtifactCommand(work.Generation, work.GrantId!,
            _bootId, content), stop).ConfigureAwait(false);
        await _ledger.ReconcileAsync(new CoordinationReconciliation(work.Request.Id, work.Generation, work.GrantId!, work.BootId!, _bootId,
            DateTimeOffset.UtcNow, disposition, "agent-" + disposition + "-" + LocalFiles.Hash(work.Request.Id, work.Generation)[..24],
            artifact.ResultRef, true, disposition == "never-created" ? null : binding, work.ExecutionId), stop).ConfigureAwait(false);
        record?.With(r => r with { Phase = "done", ResultRef = artifact.ResultRef }).Save(_settings.StateDirectory);
        Log("helper-reconciled", work.Request.Id + " " + disposition);
    }
}

internal static class RecordExtensions {
    public static RunRecord With(this RunRecord record, Func<RunRecord, RunRecord> change) => change(record);
}
