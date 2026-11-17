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

    /// <summary>Creates (once) and runs one helper thread for an admitted creation grant, through questions and answers,
    /// to a published result and terminal receipt. Every step is recorded locally first, so a restart continues the same
    /// thread and turn instead of starting new ones.</summary>
    private async Task RunHelperAsync(CoordinationWork work, CancellationToken stop) {
        var id = work.Request.Id;
        var grant = work.GrantId!;
        var record = RunRecord.Load(_settings.StateDirectory, id, work.Generation) ?? new RunRecord {
            RequestId = id, Generation = work.Generation, GrantId = grant,
            Workspace = Path.Combine(_settings.WorkspaceRoot, LocalFiles.Hash(id, work.Generation)[..24])
        }.Save(_settings.StateDirectory);
        if (record.GrantId != grant || record.Phase == "done") return;
        Directory.CreateDirectory(record.Workspace);
        var marker = Path.Combine(record.Workspace, ".ix-ledger-workspace.json");
        if (!File.Exists(marker)) LocalFiles.WriteAtomic(marker, JsonSerializer.Serialize(new { requestId = id, generation = work.Generation, grantId = grant }));
        var workspaceId = "ledger-workspace-" + LocalFiles.Hash(id, work.Generation)[..40];
        var receipt = new ExecutionReceipt(grant, work.Generation, _bootId, "codex-" + LocalFiles.Hash(id, work.Generation, grant)[..40]);
        if (record.ThreadId is null) {
            var task = work.Request.TaskBrief ?? id;
            var title = "Ledger " + id + ": " + (task.Length > 60 ? task[..60] : task).ReplaceLineEndings(" ");
            record = record with { ThreadId = await _codex.StartThreadAsync(record.Workspace, _policy.CodexModel, title, stop).ConfigureAwait(false) };
            record.Save(_settings.StateDirectory);
        }
        var threadId = record.ThreadId!;
        if (!record.Bound) {
            var workerId = "codex-" + threadId;
            await _ledger.WorkerAsync(new CoordinationWorker(workerId, _machineId, "codex", true, null, DateTimeOffset.UtcNow, threadId,
                workspaceId, work.Request.Checkpoint, _bootId, TemporaryWorker: true), stop).ConfigureAwait(false);
            await _ledger.BindSessionAsync(new CoordinationSessionBinding(id, work.Generation, grant, workerId, _machineId, "codex", threadId,
                workspaceId, work.Request.Checkpoint), stop).ConfigureAwait(false);
            record = (record with { Bound = true }).Save(_settings.StateDirectory);
        }
        if (!record.Submitted) {
            await _ledger.ReceiptAsync(id, "submitted", receipt, stop).ConfigureAwait(false);
            record = (record with { Submitted = true }).Save(_settings.StateDirectory);
        }
        using var renewal = CancellationTokenSource.CreateLinkedTokenSource(stop);
        var renewing = RenewAsync(id, receipt, renewal.Token);
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
    }

    /// <summary>Runs helper turns until a result: raises questions, waits for their answers (plain reads), feeds each
    /// answer back as the next turn, and resumes a failed turn as often as policy allows.</summary>
    private async Task<RunRecord> ConverseAsync(CoordinationWork work, RunRecord record, ExecutionReceipt receipt, CancellationToken stop) {
        var id = work.Request.Id;
        var threadId = record.ThreadId!;
        var limit = TimeSpan.FromMinutes(_policy.TurnMinutes);
        while (true) {
            TurnOutcome outcome;
            async Task Started(string turnId) {
                record = (record with { Phase = "turn", TurnId = turnId }).Save(_settings.StateDirectory);
                if (!record.Started) {
                    await _ledger.ReceiptAsync(id, "started", receipt, stop).ConfigureAwait(false);
                    record = (record with { Started = true }).Save(_settings.StateDirectory);
                }
            }
            if (record.Phase == "question") {
                var question = await WaitForAnswerAsync(id, record.QuestionNumber!.Value, stop).ConfigureAwait(false);
                if (question is null) return record with { Phase = "done" };
                var attempt = "answer-" + LocalFiles.Hash(id, question.Number, question.AnswerDelivery?.AttemptId)[..32];
                await _ledger.QuestionDeliveryAsync(id, question.Number, new("answer", attempt, "sending"), stop).ConfigureAwait(false);
                outcome = await _codex.RunTurnAsync(threadId, record.Workspace, AnswerPrompt(question, _policy.QuestionWaitMinutes),
                    HelperOutputSchema, _policy.CodexModel, _policy.CodexEffort, limit, async turnId => {
                        await Started(turnId).ConfigureAwait(false);
                        await _ledger.QuestionDeliveryAsync(id, question.Number, new("answer", attempt, "submitted", "codex-turn-" + turnId), stop).ConfigureAwait(false);
                    }, stop).ConfigureAwait(false);
            } else if (record.TurnId is { } running) {
                outcome = await _codex.FollowTurnAsync(threadId, running, record.Workspace, limit, stop).ConfigureAwait(false);
            } else {
                var prompt = record.Resumes > 0 ? ResumePrompt : HelperPrompt(work, record.Workspace);
                outcome = await _codex.RunTurnAsync(threadId, record.Workspace, prompt, HelperOutputSchema, _policy.CodexModel,
                    _policy.CodexEffort, limit, Started, stop).ConfigureAwait(false);
            }
            await RecordUsageAsync(id, "worker", "codex", outcome.Model, outcome.Tokens, null, threadId, stop).ConfigureAwait(false);
            var (kind, text) = ParseAnswer(outcome.FinalText);
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
            if (outcome.Status == "completed" && kind == "result" && text.Length > 0)
                return await PublishAsync(work, record, receipt, Bound(text, threadId), succeeded: true, stop).ConfigureAwait(false);
            if (record.Resumes < _policy.ResumeAttempts) {
                record = (record with { Resumes = record.Resumes + 1, TurnId = null, Phase = "turn" }).Save(_settings.StateDirectory);
                Log("helper-resumed", id + " " + outcome.Status);
                continue;
            }
            var failure = "The helper on " + _machineId + " stopped without an answer (turn " + outcome.Status +
                (outcome.Error is null ? "" : ": " + outcome.Error) + "). Codex thread " + threadId + " has the details." +
                (string.IsNullOrWhiteSpace(outcome.FinalText) ? "" : "\n\nIts last message:\n" + outcome.FinalText);
            return await PublishAsync(work, record, receipt, Bound(failure, threadId), succeeded: false, stop).ConfigureAwait(false);
        }
    }

    private async Task<RunRecord> PublishAsync(CoordinationWork work, RunRecord record, ExecutionReceipt receipt, string content,
        bool succeeded, CancellationToken stop) {
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

    /// <summary>Keeps the grant alive while the helper works or waits for an answer.</summary>
    private async Task RenewAsync(string id, ExecutionReceipt receipt, CancellationToken token) {
        while (!token.IsCancellationRequested) {
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(5, _grantSeconds / 3.0)), token).ConfigureAwait(false);
            try { await _ledger.ReceiptAsync(id, "renew", receipt, token).ConfigureAwait(false); }
            catch (Exception error) when (error is HttpRequestException or IOException or CoordinationCommandRejectedException ||
                error is TaskCanceledException && !token.IsCancellationRequested) {
                Log("renew-unavailable", id + " " + error.Message);
            }
        }
    }

    /// <summary>Closes a grant the service marked for reconciliation (lapsed or from an earlier boot). The helper's
    /// running turn is interrupted, what it had is published as a failed result, and the requester is told.</summary>
    private async Task ReconcileAsync(CoordinationWork work, RunRecord? record, CancellationToken stop) {
        if (_active.ContainsKey("run:" + work.Request.Id + ":" + work.Generation)) return;
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
            binding ??= new CoordinationSessionBinding(work.Request.Id, work.Generation, work.GrantId!, "codex-" + threadId, _machineId,
                "codex", threadId, "ledger-workspace-" + LocalFiles.Hash(work.Request.Id, work.Generation)[..40], work.Request.Checkpoint);
            content = Bound("The helper on " + _machineId + " was stopped before it finished (" + (work.Reason ?? "reconcile") +
                "). Codex thread " + threadId + " has its work so far." + (string.IsNullOrWhiteSpace(last) ? "" : "\n\nIts last message:\n" + last), threadId);
        }
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
