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

    internal static string HelperPrompt(CoordinationWork work, string workspace) {
        var r = work.Request;
        var from = r.ReturnTo is { } back ? back.Provider + " session " + back.SessionId + " on " + back.MachineId : "another session";
        return new StringBuilder()
            .Append("You are a helper for ").Append(from).AppendLine(", working through the IntelligenceX Ledger.")
            .AppendLine().AppendLine("Task:").AppendLine(r.TaskBrief ?? "(no task text)").AppendLine()
            .Append("The requester is working on ").Append(r.Checkpoint.Project).Append(" (branch ").Append(r.Checkpoint.Branch)
            .Append(", commit ").Append(r.Checkpoint.Commit).AppendLine(").")
            .Append("Your working directory ").Append(workspace).AppendLine(" starts empty. Anything you leave there is kept for review, " +
                "so remove scratch you don't need and push anything worth keeping.")
            .AppendLine().AppendLine("Finish with the JSON final answer the output schema asks for:")
            .AppendLine("- outcome \"result\": text is your answer for the requester (keep it under about 6000 characters).")
            .AppendLine("- outcome \"question\": text is one clear question only the requester can answer. The answer arrives in this conversation and you continue.")
            .ToString();
    }

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
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("outcome", out var outcome) &&
                root.TryGetProperty("text", out var body) && outcome.GetString() is "result" or "question" && body.ValueKind == JsonValueKind.String)
                return (outcome.GetString()!, body.GetString()!);
        } catch (JsonException) { }
        return ("result", text);
    }

    /// <summary>Bounds result content to the Ledger's 8 KB, pointing at the helper thread for the rest.</summary>
    internal static string Bound(string text, string threadId) {
        if (Encoding.UTF8.GetByteCount(text) <= MaximumResultBytes) return text;
        var note = "\n\n[Truncated. The full answer is in Codex thread " + threadId + ".]";
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
    private CoordinationWorker HelperWorker(CoordinationWork work, string threadId) => new("codex-" + threadId, _machineId, "codex", true,
        null, DateTimeOffset.UtcNow, threadId, WorkspaceId(work), work.Request.Checkpoint, _bootId, TemporaryWorker: true);

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
            var receipt = new ExecutionReceipt(grant, work.Generation, _bootId, "codex-" + LocalFiles.Hash(id, work.Generation, grant)[..40]);
            if (record.ThreadId is null) {
                var task = work.Request.TaskBrief ?? id;
                var title = "Ledger " + id + ": " + (task.Length > 60 ? task[..60] : task).ReplaceLineEndings(" ");
                record = record with { ThreadId = await _codex.StartThreadAsync(record.Workspace, _policy.CodexModel, title, stop).ConfigureAwait(false) };
                record.Save(_settings.StateDirectory);
            }
            var threadId = record.ThreadId!;
            if (!record.Bound) {
                var worker = HelperWorker(work, threadId);
                await _ledger.WorkerAsync(worker, stop).ConfigureAwait(false);
                await _ledger.BindSessionAsync(new CoordinationSessionBinding(id, work.Generation, grant, worker.Id, _machineId, "codex", threadId,
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
                if (record.ResultRef is null) record = await ConverseAsync(work, record, receipt, stop).ConfigureAwait(false);
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
                    outcome = await _codex.RunTurnAsync(threadId, record.Workspace, record.AnswerPrompt!, HelperOutputSchema, _policy.CodexModel,
                        _policy.CodexEffort, limit, record.KnownTurns, Submitting, Started, watch, stop).ConfigureAwait(false);
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
                var prompt = record.Resumes > 0 ? ResumePrompt : HelperPrompt(work, record.Workspace);
                outcome = await _codex.RunTurnAsync(threadId, record.Workspace, prompt, HelperOutputSchema, _policy.CodexModel,
                    _policy.CodexEffort, limit, record.KnownTurns, Submitting, Started, watch, stop).ConfigureAwait(false);
            }
            if (record.UsageTurn != outcome.TurnId)
                record = (record with { UsageTurn = outcome.TurnId, UsageAtUtc = DateTimeOffset.UtcNow }).Save(_settings.StateDirectory);
            await RecordUsageAsync(id, "worker", "codex", outcome.Model, outcome.Tokens, null, threadId, stop, record.UsageAtUtc).ConfigureAwait(false);
            File.Delete(tokensFile);
            var (kind, text) = ParseAnswer(outcome.FinalText);
            if (kind == "question" && (string.IsNullOrWhiteSpace(text) || System.Text.Encoding.UTF8.GetByteCount(text) > MaximumResultBytes)) kind = "none";
            if (outcome.Status == "completed" && kind == "question") {
                try {
                    var question = await _ledger.RaiseQuestionAsync(id, new(work.Generation, record.GrantId, _bootId, text), stop).ConfigureAwait(false);
                    record = (record with { Phase = "question", QuestionNumber = question.Number, TurnId = null }).Save(_settings.StateDirectory);
                    Log("helper-question", id + "#" + question.Number);
                    continue;
                } catch (CoordinationCommandRejectedException error) when (error.ErrorCode == "question-limit-reached") {
                    text = "The helper reached the question limit and stopped with this open question:\n\n" + text;
                    kind = "result";
                }
            }
            if (outcome.Status == "completed" && kind == "result" && !string.IsNullOrWhiteSpace(text))
                return await PublishAsync(work, record, Bound(text, threadId), succeeded: true, stop).ConfigureAwait(false);
            if (record.Resumes < _policy.ResumeAttempts) {
                record = (record with { Resumes = record.Resumes + 1, TurnId = null, Phase = "turn" }).Save(_settings.StateDirectory);
                Log("helper-resumed", id + " " + outcome.Status);
                continue;
            }
            var failure = "The helper on " + _machineId + " stopped without an answer (turn " + outcome.Status +
                (outcome.Error is null ? "" : ": " + outcome.Error) + "). Codex thread " + threadId + " has the details." +
                (string.IsNullOrWhiteSpace(outcome.FinalText) ? "" : "\n\nIts last message:\n" + outcome.FinalText);
            return await PublishAsync(work, record, Bound(failure, threadId), succeeded: false, stop).ConfigureAwait(false);
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
            var last = await _codex.CloseAsync(threadId, stop).ConfigureAwait(false);
            disposition = "execution-excluded";
            if (binding is null) {
                // Stopped between the thread's creation and its binding: enroll its worker again so the binding can be recovered.
                var worker = HelperWorker(work, threadId);
                await _ledger.WorkerAsync(worker, stop).ConfigureAwait(false);
                binding = new CoordinationSessionBinding(work.Request.Id, work.Generation, work.GrantId!, worker.Id, _machineId,
                    "codex", threadId, WorkspaceId(work), work.Request.Checkpoint);
            }
            content = record?.PendingResult ?? Bound("The helper on " + _machineId + " was stopped before it finished (" + (work.Reason ?? "reconcile") +
                "). Codex thread " + threadId + " has its work so far." + (string.IsNullOrWhiteSpace(last) ? "" : "\n\nIts last message:\n" + last), threadId);
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
