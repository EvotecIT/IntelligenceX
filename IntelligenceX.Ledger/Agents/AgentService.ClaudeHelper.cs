using IntelligenceX.Ledger.Contracts;

namespace IntelligenceX.Ledger.Agents;

internal sealed partial class AgentService {
    private const string ClaudeSource = "Claude session";

    /// <summary>Runs Claude helper turns until a result, the same way as a Codex helper: questions go to the requester, the
    /// answer continues the same Claude session, a failed turn is resumed as often as policy allows, and a usage limit waits
    /// without using up a resume. A turn that a restart cut short is continued with the resume prompt. Every step is saved
    /// first, so a restart picks up where the run was.</summary>
    private async Task<RunRecord> ConverseClaudeAsync(CoordinationWork work, RunRecord record, ExecutionReceipt receipt, CancellationToken stop) {
        var id = work.Request.Id;
        var session = record.ThreadId!;
        var limit = TimeSpan.FromMinutes(_policy.TurnMinutes);
        while (true) {
            if (record.PendingResult is { } pending)
                return await PublishAsync(work, record, pending, record.PendingSucceeded, stop).ConfigureAwait(false);
            string prompt;
            var answering = record.Phase == "question";
            if (answering) {
                if (record.AnswerAttempt is null) {
                    var question = await WaitForAnswerAsync(id, record.QuestionNumber!.Value, stop).ConfigureAwait(false);
                    if (question is null) return record with { Phase = "done" };
                    var attempt = "answer-" + LocalFiles.Hash(id, question.Number, question.AnswerDelivery?.AttemptId)[..32];
                    record = (record with { AnswerAttempt = attempt, AnswerPrompt = AnswerPrompt(question, _policy.QuestionWaitMinutes) })
                        .Save(_settings.StateDirectory);
                }
                await _ledger.QuestionDeliveryAsync(id, record.QuestionNumber!.Value, new("answer", record.AnswerAttempt!, "sending"), stop).ConfigureAwait(false);
                prompt = record.AnswerPrompt!;
            } else {
                if (record.LimitRetryAtUtc is { } retry && retry > DateTimeOffset.UtcNow)
                    await Task.Delay(retry - DateTimeOffset.UtcNow, stop).ConfigureAwait(false);
                prompt = record.TurnId is not null || record.Resumes > 0 || record.LimitWaits > 0 ? ResumePrompt : HelperPrompt(work, record.Workspace);
            }
            // The session exists once its first turn has started; until then the next turn creates it under the same id.
            var resume = record.Started;
            record = (record with { TurnId = "claude-" + session }).Save(_settings.StateDirectory);
            async Task Launched() {
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
            var outcome = await _claudeHelper.RunTurnAsync(session, resume, record.Workspace, prompt, OutputSchema(work), null, limit,
                Launched, stop).ConfigureAwait(false);
            record = (record with { TurnId = null, Phase = "turn" }).Save(_settings.StateDirectory);
            if (outcome.Status == "rejected") {
                if (answering && record.AnswerAttempt is { } unsent) {
                    // Claude never started, so the answer wasn't given to the session: it goes back to pending.
                    await _ledger.QuestionDeliveryAsync(id, record.QuestionNumber!.Value, new("answer", unsent, "not-submitted", "claude-not-started"),
                        CancellationToken.None).ConfigureAwait(false);
                    record = (record with { AnswerAttempt = null, AnswerPrompt = null, Phase = "question" }).Save(_settings.StateDirectory);
                }
                if (record.Rejections < _policy.ResumeAttempts) {
                    record = (record with { Rejections = record.Rejections + 1 }).Save(_settings.StateDirectory);
                    Log("helper-turn-rejected", id + " " + outcome.Error);
                    await Task.Delay(TimeSpan.FromSeconds(30), stop).ConfigureAwait(false);
                    continue;
                }
                return await PublishAsync(work, record, TextResult(work, "The Claude helper on " + _machineId + " could not start: " +
                    outcome.Error + ".", session, ClaudeSource), succeeded: false, stop).ConfigureAwait(false);
            }
            if (!(outcome.UsageLimited && outcome.Tokens.Total == 0)) {
                var turn = "claude-" + LocalFiles.Hash(session, record.Resumes, record.LimitWaits, record.QuestionNumber)[..24];
                if (record.UsageTurn != turn)
                    record = (record with { UsageTurn = turn, UsageAtUtc = DateTimeOffset.UtcNow }).Save(_settings.StateDirectory);
                await RecordUsageAsync(id, "worker", "claude", outcome.Model, outcome.Tokens, null, session, stop, record.UsageAtUtc).ConfigureAwait(false);
            }
            if (outcome.UsageLimited) {
                Observe("claude", false, "usage-limit");
                record = (record with { LimitWaits = record.LimitWaits + 1,
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
                    record = (record with { Phase = "question", QuestionNumber = question.Number }).Save(_settings.StateDirectory);
                    Log("helper-question", id + "#" + question.Number);
                    continue;
                } catch (CoordinationCommandRejectedException error) when (error.ErrorCode == "question-limit-reached") {
                    return await PublishAsync(work, record, TextResult(work, "The helper reached the question limit and stopped with " +
                        "this open question:\n\n" + text, session, ClaudeSource), succeeded: true, stop).ConfigureAwait(false);
                }
            }
            if (outcome.Status == "completed" && kind == "result" && !string.IsNullOrWhiteSpace(text))
                return await PublishAsync(work, record, TextResult(work, text, session, ClaudeSource), succeeded: true, stop).ConfigureAwait(false);
            if (record.Resumes < _policy.ResumeAttempts) {
                record = (record with { Resumes = record.Resumes + 1 }).Save(_settings.StateDirectory);
                Log("helper-resumed", id + " " + outcome.Status);
                continue;
            }
            var failure = "The Claude helper on " + _machineId + " stopped without an answer (turn " + outcome.Status +
                (outcome.Error is null ? "" : ": " + outcome.Error) + "). Claude session " + session + " has the details." +
                (string.IsNullOrWhiteSpace(outcome.FinalText) ? "" : "\n\nIts last message:\n" + outcome.FinalText);
            return await PublishAsync(work, record, TextResult(work, failure, session, ClaudeSource), succeeded: false, stop).ConfigureAwait(false);
        }
    }
}
