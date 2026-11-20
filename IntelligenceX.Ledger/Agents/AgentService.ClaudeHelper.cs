using IntelligenceX.Ledger.Contracts;

namespace IntelligenceX.Ledger.Agents;

internal sealed partial class AgentService {
    private const string ClaudeSource = "Claude session";

    /// <summary>Usage-limit waits a Claude helper sits through before it reports the limit as its result (two days at the
    /// default fifteen-minute probe interval).</summary>
    internal const int MaximumClaudeLimitWaits = 192;

    /// <summary>Runs Claude helper turns until a result, the same way as a Codex helper: questions go to the requester, the
    /// answer continues the same Claude session, a failed turn is resumed as often as policy allows, and a usage limit waits
    /// without using up a resume. Each step is saved before the next one, so after a restart:
    /// a finished turn's saved outcome is handled without running it again; a turn that may have started continues the
    /// session (or, if Claude has no such session, starts it with the task); an answer the Ledger already shows as delivered
    /// isn't sent again.</summary>
    private async Task<RunRecord> ConverseClaudeAsync(CoordinationWork work, RunRecord record, ExecutionReceipt receipt, CancellationToken stop) {
        var id = work.Request.Id;
        var session = record.ThreadId!;
        var limit = TimeSpan.FromMinutes(_policy.TurnMinutes);
        while (true) {
            if (record.PendingResult is { } pending)
                return await PublishAsync(work, record, pending, record.PendingSucceeded, stop).ConfigureAwait(false);
            if (record.LastTurn is null) record = await RunClaudeTurnAsync(work, record, receipt, limit, stop).ConfigureAwait(false);
            if (record.LastTurn is not { } outcome) {
                if (record.Phase == "done") return record;
                continue;
            }
            // From here on the turn is handled from its saved outcome; it is cleared only together with the next state.
            if (outcome.Status == "rejected") {
                if (record.Rejections < _policy.ResumeAttempts) {
                    record = (record with { Rejections = record.Rejections + 1, LastTurn = null }).Save(_settings.StateDirectory);
                    Log("helper-turn-rejected", id + " " + outcome.Error);
                    await Task.Delay(TimeSpan.FromSeconds(30), stop).ConfigureAwait(false);
                    continue;
                }
                return await PublishAsync(work, record with { LastTurn = null }, TextResult(work, "The Claude helper on " + _machineId +
                    " could not start: " + outcome.Error + ".", session, ClaudeSource), succeeded: false, stop).ConfigureAwait(false);
            }
            if (!(outcome.UsageLimited && outcome.Tokens.Total == 0)) {
                var turn = "claude-" + LocalFiles.Hash(session, record.Resumes, record.LimitWaits, record.QuestionNumber, record.Rejections)[..24];
                if (record.UsageTurn != turn)
                    record = (record with { UsageTurn = turn, UsageAtUtc = DateTimeOffset.UtcNow }).Save(_settings.StateDirectory);
                await RecordUsageAsync(id, "worker", "claude", outcome.Model, outcome.Tokens, null, session, stop, record.UsageAtUtc).ConfigureAwait(false);
            }
            if (outcome.UsageLimited) {
                Observe("claude", false, "usage-limit");
                if (record.LimitWaits >= MaximumClaudeLimitWaits)
                    return await PublishAsync(work, record with { LastTurn = null }, TextResult(work, "The Claude helper on " + _machineId +
                        " stayed at its usage limit for " + record.LimitWaits + " tries and stopped. Claude session " + session +
                        " has its work so far; ask again once the limit has passed.", session, ClaudeSource), succeeded: false, stop).ConfigureAwait(false);
                record = (record with { LimitWaits = record.LimitWaits + 1, LastTurn = null,
                    LimitRetryAtUtc = DateTimeOffset.UtcNow.AddMinutes(_policy.UsageLimitProbeMinutes) }).Save(_settings.StateDirectory);
                Log("helper-usage-limit", id + " " + outcome.Error);
                continue;
            }
            if (outcome.Status == "completed") Observe("claude", true, "turn");
            var (kind, text) = ParseAnswer(outcome.FinalText);
            if (kind == "question" && (string.IsNullOrWhiteSpace(text) || System.Text.Encoding.UTF8.GetByteCount(text) > MaximumResultBytes)) kind = "none";
            if (outcome.Status == "completed" && kind == "question") {
                try {
                    var question = await _ledger.RaiseQuestionAsync(id, new(work.Generation, record.GrantId, _bootId, text), stop).ConfigureAwait(false);
                    record = (record with { Phase = "question", QuestionNumber = question.Number, LastTurn = null }).Save(_settings.StateDirectory);
                    Log("helper-question", id + "#" + question.Number);
                    continue;
                } catch (CoordinationCommandRejectedException error) when (error.ErrorCode == "question-limit-reached") {
                    return await PublishAsync(work, record with { LastTurn = null }, TextResult(work, "The helper reached the question limit " +
                        "and stopped with this open question:\n\n" + text, session, ClaudeSource), succeeded: true, stop).ConfigureAwait(false);
                }
            }
            // A review is judged by its structured verdict, which the schema allows next to an empty text.
            if (outcome.Status == "completed" && kind == "result" && IsReview(work) && ReviewResult(outcome.FinalText, session, ClaudeSource) is { } review)
                return await PublishAsync(work, record with { LastTurn = null }, review, succeeded: true, stop).ConfigureAwait(false);
            if (outcome.Status == "completed" && kind == "result" && !string.IsNullOrWhiteSpace(text))
                return await PublishAsync(work, record with { LastTurn = null }, TextResult(work, text, session, ClaudeSource), succeeded: true, stop).ConfigureAwait(false);
            if (record.Resumes < _policy.ResumeAttempts) {
                record = (record with { Resumes = record.Resumes + 1, LastTurn = null }).Save(_settings.StateDirectory);
                Log("helper-resumed", id + " " + outcome.Status);
                continue;
            }
            var failure = "The Claude helper on " + _machineId + " stopped without an answer (turn " + outcome.Status +
                (outcome.Error is null ? "" : ": " + outcome.Error) + "). Claude session " + session + " has the details." +
                (string.IsNullOrWhiteSpace(outcome.FinalText) ? "" : "\n\nIts last message:\n" + outcome.FinalText);
            return await PublishAsync(work, record with { LastTurn = null }, TextResult(work, failure, session, ClaudeSource), succeeded: false, stop).ConfigureAwait(false);
        }
    }

    /// <summary>Runs one turn and saves its outcome as <see cref="RunRecord.LastTurn"/>. Returns the record without an
    /// outcome when there was nothing to run (the question was settled another way, or the answer had already reached the
    /// session and the next pass continues it).</summary>
    private async Task<RunRecord> RunClaudeTurnAsync(CoordinationWork work, RunRecord record, ExecutionReceipt receipt, TimeSpan limit, CancellationToken stop) {
        var id = work.Request.Id;
        var session = record.ThreadId!;
        // A process left by an earlier run of this service is stopped before another turn uses the session.
        if (record.ProcessId is { } leftover && record.ProcessStartedUtc is { } leftoverStart) {
            if (!ClaudeRunner.StopLeftover(leftover, leftoverStart)) throw new InvalidOperationException("Claude process " + leftover + " did not stop.");
            record = (record with { ProcessId = null, ProcessStartedUtc = null }).Save(_settings.StateDirectory);
        }
        // Any earlier launch may have created the session, even if its receipt or start was never recorded.
        var mayExist = record.Started || record.TurnId is not null;
        string prompt;
        if (record.Phase == "question") {
            if (record.AnswerAttempt is null) {
                var question = await WaitForAnswerAsync(id, record.QuestionNumber!.Value, stop).ConfigureAwait(false);
                if (question is null) return record with { Phase = "done" };
                var attempt = "answer-" + LocalFiles.Hash(id, question.Number, question.AnswerDelivery?.AttemptId)[..32];
                record = (record with { AnswerAttempt = attempt, AnswerPrompt = AnswerPrompt(question, _policy.QuestionWaitMinutes) })
                    .Save(_settings.StateDirectory);
            }
            var view = await _ledger.RequestAsync(id, stop).ConfigureAwait(false);
            if (view.Questions.FirstOrDefault(q => q.Number == record.QuestionNumber)?.AnswerDelivery is { Stage: "submitted" } delivered &&
                delivered.AttemptId == record.AnswerAttempt) {
                // The answer reached the session before a restart; the session is continued, not answered again.
                return (record with { AnswerAttempt = null, AnswerPrompt = null, Phase = "turn", TurnId = "claude-" + session }).Save(_settings.StateDirectory);
            }
            await _ledger.QuestionDeliveryAsync(id, record.QuestionNumber!.Value, new("answer", record.AnswerAttempt!, "sending"), stop).ConfigureAwait(false);
            prompt = record.AnswerPrompt!;
        } else {
            if (record.LimitRetryAtUtc is { } retry && retry > DateTimeOffset.UtcNow)
                await Task.Delay(retry - DateTimeOffset.UtcNow, stop).ConfigureAwait(false);
            prompt = mayExist ? ResumePrompt : HelperPrompt(work, record.Workspace);
        }
        record = (record with { TurnId = "claude-" + session }).Save(_settings.StateDirectory);
        // Kept here as well, so a session that has to be created after all still gets the requester's answer.
        var answer = record.AnswerPrompt;
        Task Launched(int processId, DateTimeOffset startedUtc) {
            record = (record with { ProcessId = processId, ProcessStartedUtc = startedUtc }).Save(_settings.StateDirectory);
            return Task.CompletedTask;
        }
        // Claude has opened the session with the prompt: the turn has started and any answer has reached the session.
        async Task Accepted() {
            if (!record.Started) {
                await PersistAsync(() => _ledger.ReceiptAsync(id, "started", receipt, stop), stop).ConfigureAwait(false);
                record = (record with { Started = true }).Save(_settings.StateDirectory);
            }
            if (record.AnswerAttempt is { } attempt) {
                await PersistAsync(() => _ledger.QuestionDeliveryAsync(id, record.QuestionNumber!.Value,
                    new("answer", attempt, "submitted", "claude-turn-" + LocalFiles.Hash(session, attempt)[..24]), stop), stop).ConfigureAwait(false);
                // The answer is in the session now; after a restart this turn is continued, not answered again.
                record = (record with { AnswerAttempt = null, AnswerPrompt = null, Phase = "turn" }).Save(_settings.StateDirectory);
            }
        }
        var outcome = await _claudeHelper.RunTurnAsync(session, mayExist, record.Workspace, prompt, OutputSchema(work), null, limit,
            Launched, Accepted, stop).ConfigureAwait(false);
        if (outcome is { Status: "rejected", Error: ClaudeRunner.NoSession }) {
            // No earlier launch got as far as creating the session: start it now, with the task (and any answer given).
            outcome = await _claudeHelper.RunTurnAsync(session, false, record.Workspace,
                answer is not null ? HelperPrompt(work, record.Workspace) + "\n\n" + answer : HelperPrompt(work, record.Workspace),
                OutputSchema(work), null, limit, Launched, Accepted, stop).ConfigureAwait(false);
        }
        if (record.AnswerAttempt is { } unsent) {
            // Claude never opened the session with the answer, so it didn't arrive: it goes back to pending, and the turn
            // counts as not started.
            await _ledger.QuestionDeliveryAsync(id, record.QuestionNumber!.Value, new("answer", unsent, "not-submitted", "claude-not-started"),
                CancellationToken.None).ConfigureAwait(false);
            record = (record with { AnswerAttempt = null, AnswerPrompt = null }).Save(_settings.StateDirectory);
            if (outcome.Status != "rejected") outcome = outcome with { Status = "rejected", Error = outcome.Error ?? "Claude did not open the session." };
        }
        return (record with { LastTurn = outcome, TurnId = null, ProcessId = null, ProcessStartedUtc = null }).Save(_settings.StateDirectory);
    }
}
