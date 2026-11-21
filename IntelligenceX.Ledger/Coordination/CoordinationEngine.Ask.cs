using System.Text;
using IntelligenceX.Ledger.Contracts;
using static IntelligenceX.Ledger.Coordination.CoordinationValidation;

namespace IntelligenceX.Ledger.Coordination;

public sealed partial class CoordinationEngine {
    internal const string AskOrigin = "ask";
    private const int MaximumTextBytes = 8192;
    private const int MaximumUsagePerRequest = 256;

    private static bool IsAsk(CoordinationWork work) => work.Origin == AskOrigin;
    private static bool Text(string? value) => !string.IsNullOrWhiteSpace(value) && Encoding.UTF8.GetByteCount(value) <= MaximumTextBytes;
    private static string QuestionKey(string requestId, int number) => requestId + "#" + number.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Questions and notices for one request, oldest first.</summary>
    public CoordinationQuestion[] QuestionsFor(string requestId) =>
        state.Questions.Values.Where(q => q.RequestId == requestId).OrderBy(q => q.Number).ToArray();

    /// <summary>Creates the goal and the help request for one ask in a single command. The caller is the machine's
    /// session client; the asking session must be on that machine. Provider and capability fall back to policy routing
    /// and the target machine. An identical retry returns the existing work, live or <paramref name="archived"/>; any
    /// other reuse of the id is rejected.</summary>
    public CoordinationWork Ask(CoordinationAsk ask, CoordinationPrincipal caller, CoordinationArchiveEntry? archived = null,
        string? previousResult = null) {
        Require(caller.Role == "client" && Id(caller.MachineId) && caller.RequesterId is null, "ask-role-denied");
        Require(ask is not null && Id(ask.Id) && ask.Id.Length <= 120 && Text(ask.Task) && Id(ask.ReturnProvider) &&
            Id(ask.ReturnSessionId) && (ask.MachineId is null || Id(ask.MachineId)) && (ask.Provider is null || Id(ask.Provider)) &&
            (ask.Kind is null || Id(ask.Kind)) && (ask.Capability is null || Id(ask.Capability)) &&
            (ask.ReturnAddress is null || ask.ReturnAddress.Length is > 0 and <= 300 && !ask.ReturnAddress.Any(char.IsControl)) &&
            (ask.Continues is null || Id(ask.Continues) && ask.Continues != ask.Id) &&
            (ask.Images is null || ask.Images.Length is > 0 and <= CoordinationImage.MaximumPerMessage &&
                ask.Images.All(CoordinationImage.ValidId) && ask.Images.Distinct(StringComparer.Ordinal).Count() == ask.Images.Length) &&
            (previousResult is null || Encoding.UTF8.GetByteCount(previousResult) <= MaximumTextBytes) &&
            (ask.PullRequest is null || CoordinationAsk.PullRequestReference(ask.PullRequest) is not null), "invalid-ask");
        // Only a visual review posts to a pull request; elsewhere the asker decides what to publish.
        Require(ask.PullRequest is null || ask.Output == "visual-review", "ask-pull-request-requires-visual-review");
        var pullRequest = CoordinationAsk.PullRequestReference(ask.PullRequest);
        bool Same(CoordinationWork work) => work.Requester == caller.Id && IsAsk(work) && work.Request.TaskBrief == ask.Task &&
            work.Request.ReturnTo is { } back && back.Provider == ask.ReturnProvider && back.SessionId == ask.ReturnSessionId &&
            (work.Request.MachineId == ask.MachineId || ask.Continues is not null && ask.MachineId is null) &&
            work.Request.Checkpoint.Project == ask.Project &&
            work.Request.Checkpoint.Branch == ask.Branch && work.Request.Checkpoint.Commit == ask.Commit &&
            (ask.Provider is null || work.Request.RequiredProvider == ask.Provider) &&
            (ask.Capability is null || work.Request.Capability == ask.Capability) &&
            // Model and effort the policy chose are recorded on the work, so a retry that left them out still matches.
            work.Request.Helper?.ContinuesRequestId == ask.Continues && (ask.Model is null || work.Request.Helper?.Model == ask.Model) &&
            (ask.Effort is null || work.Request.Helper?.Effort == ask.Effort) && work.Request.Helper?.Output == ask.Output &&
            (work.Request.Helper?.Images ?? []).SequenceEqual(ask.Images ?? [], StringComparer.Ordinal) &&
            work.Request.Helper?.PullRequest == pullRequest;
        if (state.Work.TryGetValue(ask.Id, out var existing) || archived?.RequestId == ask.Id && (existing = archived.Work) is not null) {
            Require(Same(existing), "request-id-conflict");
            return existing;
        }
        // A session already enrolled under another worker id keeps it, so the result can be delivered to that worker.
        var returnWorker = state.Workers.Values.FirstOrDefault(w => w.MachineId == caller.MachineId && w.Provider == ask.ReturnProvider &&
            w.SessionId == ask.ReturnSessionId)?.Id ?? ask.ReturnProvider + "-" + ask.ReturnSessionId;
        var visual = ask.Output == "visual-review";
        // A follow-up of a Codex ask carries on the earlier helper's conversation, so it runs where that helper ran, with the
        // same provider. Any other follow-up (a Claude helper, whose conversation can't move to a new workspace, or a switch
        // of provider) gives the new helper the earlier result instead, with the images it refers to.
        string? forkSession = null, carried = null;
        if (ask.Continues is not null) {
            CoordinationSessionBinding? priorBinding = null;
            Require(state.Work.TryGetValue(ask.Continues, out var prior) && IsAsk(prior) && prior.Requester == caller.Id &&
                Terminal.Contains(prior.State), "ask-continues-unavailable");
            // The earlier work is the caller's own, so the caller must still be allowed that project.
            Require(caller.Projects.Contains(prior!.Request.Checkpoint.Project, StringComparer.Ordinal), "ask-continues-project-denied");
            var fork = prior.Provider == "codex" && ask.Provider is null or "codex" && !visual && prior.Machine is not null &&
                state.Bindings.TryGetValue(prior.Request.Id, out priorBinding) && priorBinding.Provider == "codex";
            if (fork) {
                Require(ask.MachineId is null || ask.MachineId == prior.Machine, "ask-continues-machine-conflict");
                forkSession = priorBinding!.SessionId;
                ask = ask with { MachineId = prior.Machine, Provider = "codex", Capability = ask.Capability ?? prior.Request.Capability };
            } else {
                Require(previousResult is not null, "ask-continues-unavailable");
                carried = previousResult;
                ask = ask with { Provider = ask.Provider ?? (visual ? "claude" : prior.Provider),
                    Capability = ask.Capability ?? (ask.MachineId is null ? prior.Request.Capability : null) };
            }
        }
        var machine = ask.MachineId is null ? null : state.Policy.Machines.FirstOrDefault(m => m.Id == ask.MachineId);
        Require(ask.MachineId is null || machine is not null, "ask-machine-unknown");
        var capability = ask.Capability ?? machine?.Capabilities.FirstOrDefault();
        Require(capability is not null, "ask-machine-or-capability-required");
        // The provider has to be one a machine with the capability (the named one, if any) may create.
        bool Offers(MachineCapacity m, string p) => m.Enabled && (machine is null || m.Id == machine.Id) &&
            m.Capabilities.Contains(capability!) && (m.AllowedCreateProviders ?? []).Contains(p, StringComparer.Ordinal);
        // A visual review is Claude's and a fork of a Codex conversation stays with Codex. Model, effort and the other outputs
        // go with either provider.
        Require(!visual || ask.Provider is null or "claude", "ask-visual-review-requires-claude");
        Require(forkSession is null || ask.Provider is null or "codex", "ask-options-require-codex");
        var provider = visual ? "claude" : forkSession is not null ? "codex" : ask.Provider;
        // The kind picks the provider (when the ask names none) and the policy's model and effort for it.
        var kind = ask.Kind ?? ask.Output switch { "review" => "review", "visual-review" => "visual", _ => state.Policy.Ask.DefaultKind };
        if (provider is null) {
            Require(state.Policy.Routing.TryGetValue(kind, out var route), "ask-kind-not-routed");
            provider = route.FirstOrDefault(p => state.Policy.Machines.Any(m => Offers(m, p)));
        }
        Require(provider is not null && state.Policy.Machines.Any(m => Offers(m, provider)), "ask-provider-unavailable");
        Require(provider != "claude" || ask.Effort != "ultra", "ask-effort-unsupported");
        var choice = state.Policy.Ask.Models.TryGetValue(kind, out var byProvider) && byProvider.TryGetValue(provider!, out var chosen) ? chosen : null;
        var model = ask.Model ?? choice?.Model;
        var effort = ask.Effort ?? choice?.Effort;
        var checkpoint = new WorkCheckpoint(ask.Project, ask.Branch, ask.Commit, null, AskOrigin, AskOrigin);
        var goalId = "ask-" + ask.Id;
        var request = new CoordinationRequest(ask.Id, goalId, "help", capability, ask.MachineId, null, new(AgentSlots: 1),
            checkpoint, [], 1, AskOrigin, provider, ReturnTo: new(returnWorker, caller.MachineId!, ask.ReturnProvider,
                ask.ReturnSessionId, checkpoint) { Address = ask.ReturnAddress }, TaskBrief: ask.Task,
            Helper: ask.Continues is null && model is null && effort is null && ask.Output is null && ask.Images is null ? null
                : new(model, effort, ask.Output, ask.Continues, forkSession, ask.Images, carried, pullRequest));
        Request(request, caller);
        Require(!state.Goals.ContainsKey(goalId), "ask-goal-conflict");
        Require(state.Work.Values.Count(w => w.State == "queued") < state.Policy.MaximumQueued, "queue-full");
        Require(state.Policy.Machines.Any(m => (request.MachineId is null || request.MachineId == m.Id) &&
            m.Capabilities.Contains(request.Capability) && Fits(request.Resources, m, new())), "no-capable-machine");
        var goal = new CoordinationGoal(goalId, caller.Id, 1, false, false, AskOrigin, ["result-delivered"], ControlRevision: 1);
        state.Goals[goalId] = goal;
        Note(goalId, "goal-updated", caller.Id, goal.Revision);
        Put(new CoordinationWork(request, caller.Id, state.Sequence + 1, "queued", now) { Origin = AskOrigin }, "requested", caller.Id);
        Schedule();
        return state.Work[ask.Id];
    }

    /// <summary>Records a helper's question for its exact running grant. The work waits in the question state, with its
    /// grant renewed by the host, until the answer (or the policy's expiry) is delivered back into the helper session.</summary>
    public CoordinationQuestion RaiseQuestion(string id, CoordinationQuestionCommand command, CoordinationPrincipal caller) {
        Require(state.Work.TryGetValue(id, out var work) && caller.Role == "host" && work.Machine == caller.MachineId &&
            command.Generation == work.Generation && command.GrantId == work.GrantId && command.BootId == work.BootId &&
            work.Request.ReturnTo is not null, "question-owner-conflict");
        Require(Text(command.Text), "invalid-question");
        var prior = QuestionsFor(id).Where(q => q.Kind == "question" && q.Generation == work.Generation).ToArray();
        var open = prior.LastOrDefault(q => q.State != "resumed");
        if (open is not null) {
            Require(open.Text == command.Text, "question-already-open");
            return open;
        }
        Require(work.State is "submitted" or "started", "question-state-conflict");
        Require(prior.Length < state.Policy.Ask.MaximumQuestions, "question-limit-reached");
        var question = AddQuestion(work, "question", command.Text);
        Put(state.Work[id] with { State = "question", RenewByUtc = now.AddSeconds(state.Policy.GrantSeconds) }, "question-asked",
            caller.Id, QuestionKey(id, question.Number));
        return question;
    }

    private CoordinationQuestion AddQuestion(CoordinationWork work, string kind, string text) {
        var number = state.Questions.Values.Count(q => q.RequestId == work.Request.Id) + 1;
        var question = new CoordinationQuestion(work.Request.Id, work.Generation, number, kind, text, now, Order: state.Sequence + 1);
        state.Questions[QuestionKey(work.Request.Id, number)] = question;
        if (kind == "notice") Note(work.Request.Id, "notice", "service", work.Generation, QuestionKey(work.Request.Id, number));
        return question;
    }

    /// <summary>Records the original requester's answer. Only an open question accepts one answer; identical retries
    /// return the stored answer.</summary>
    public CoordinationQuestion Answer(string id, int number, CoordinationAnswerCommand command, CoordinationPrincipal caller) {
        Require(state.Work.TryGetValue(id, out var work), "answer-owner-conflict");
        Require(state.Questions.TryGetValue(QuestionKey(id, number), out var question) &&
            (caller.Role == "operator" || caller.Role == "client" && work.Requester == caller.Id), "answer-owner-conflict");
        Require(question.Kind == "question" && Text(command.Text), "invalid-answer");
        if (question.Answer is not null) {
            Require(question.Answer == command.Text, "question-already-answered");
            return question;
        }
        Require(question.State == "asked", "question-closed");
        question = question with { State = "answered", Answer = command.Text, AnsweredAtUtc = now };
        state.Questions[QuestionKey(id, number)] = question;
        Note(id, "question-answered", caller.Id, question.Generation, QuestionKey(id, number));
        return question;
    }

    /// <summary>Starts or records one question-side or answer-side delivery. Question-side deliveries belong to the host on
    /// the requester's machine, answer-side ones to the host running the helper. A sending or uncertain attempt is never
    /// restarted; only a negative observation (not-submitted) or a pre-send hold permits a new attempt id.</summary>
    public CoordinationQuestion RecordQuestionDelivery(string id, int number, CoordinationQuestionDeliveryCommand command,
        CoordinationPrincipal caller) {
        Require(state.Work.TryGetValue(id, out var work), "question-delivery-owner-conflict");
        Require(state.Questions.TryGetValue(QuestionKey(id, number), out var question) &&
            command.Target is "question" or "answer" && caller.Role == "host" &&
            (command.Target == "question" ? work.Request.ReturnTo?.MachineId == caller.MachineId : work.Machine == caller.MachineId),
            "question-delivery-owner-conflict");
        Require(Id(command.AttemptId) && (command.EvidenceRef is null || Id(command.EvidenceRef)), "invalid-question-delivery");
        var answerSide = command.Target == "answer";
        if (answerSide) Require(question.Kind == "question" && question.State is "answered" or "expired" or "resumed" &&
            question.Generation == work.Generation, "question-not-answered");
        var current = answerSide ? question.AnswerDelivery : question.Delivery;
        CoordinationDelivery? next;
        if (command.Stage == "sending") {
            if (current is { Stage: "sending" } && current.AttemptId == command.AttemptId && current.Host == caller.Id) return question;
            Require(current is null || current.Stage == "held", "delivery-needs-reconciliation");
            Require(current?.AttemptId != command.AttemptId, "delivery-needs-reconciliation");
            next = new(command.AttemptId, "sending", caller.Id, AtUtc: now);
        } else if (command.Stage == "held") {
            if (current is { Stage: "held" } && current.EvidenceRef == command.EvidenceRef) return question;
            Require(current is null || current.Stage == "held", "delivery-needs-reconciliation");
            Require(command.EvidenceRef is not null, "invalid-question-delivery");
            next = new(command.AttemptId, "held", caller.Id, command.EvidenceRef);
        } else {
            Require(command.Stage is "submitted" or "uncertain" or "not-submitted" && command.EvidenceRef is not null,
                "invalid-delivery-stage");
            if (current is not null && current.AttemptId == command.AttemptId && current.Stage == command.Stage) return question;
            Require(current is { Stage: "sending" or "uncertain" } && current.AttemptId == command.AttemptId &&
                current.Host == caller.Id, "delivery-attempt-conflict");
            next = command.Stage == "not-submitted" ? null : new(command.AttemptId, command.Stage, caller.Id, command.EvidenceRef);
        }
        question = answerSide ? question with { AnswerDelivery = next } : question with { Delivery = next };
        if (answerSide && next?.Stage == "submitted") {
            question = question with { State = "resumed" };
            if (work.State == "question")
                Put(work with { State = "started", RenewByUtc = now.AddSeconds(state.Policy.GrantSeconds) }, "question-resumed",
                    caller.Id, QuestionKey(id, number));
        }
        state.Questions[QuestionKey(id, number)] = question;
        Note(id, (answerSide ? "answer-" : command.Target + "-") + command.Stage, caller.Id, question.Generation, command.EvidenceRef);
        return question;
    }

    /// <summary>Questions and notices the calling host has to deliver: to requesters on its machine, and answers to helpers
    /// it runs. Sending and uncertain deliveries are excluded so they are never repeated.</summary>
    public CoordinationQuestionPage QuestionsToDeliver(CoordinationPrincipal caller, long afterOrder) {
        Require(caller.Role == "host", "questions-role-denied");
        var items = new List<CoordinationQuestionItem>();
        foreach (var q in state.Questions.Values.Where(q => q.Order > afterOrder).OrderBy(q => q.Order)) {
            if (!state.Work.TryGetValue(q.RequestId, out var work) || work.Request.ReturnTo is not { } returnTo) continue;
            // A question reaches the requester only while the helper still waits on it; notices always do.
            if (returnTo.MachineId == caller.MachineId && q.Delivery is null or { Stage: "held" } &&
                (q.Kind != "question" || q.State == "asked" && q.Generation == work.Generation && work.State == "question"))
                items.Add(new(q, "question", returnTo, work.Machine, work.Provider));
            else if (work.Machine == caller.MachineId && q.Kind == "question" && q.Generation == work.Generation &&
                q.State is "answered" or "expired" && q.AnswerDelivery is null or { Stage: "held" })
                items.Add(new(q, "answer", returnTo, work.Machine, work.Provider));
            if (items.Count > 16) break;
        }
        return new(items.Take(16).ToArray(), items.Count > 16 ? items[15].Question.Order : null);
    }

    /// <summary>Applies the ask policy's waits: expired questions release the helper, overdue asks tell the requester, and
    /// a delivery left sending (its host stopped before recording what happened) becomes uncertain. For a result the
    /// requester is told where to read it, since it may never have arrived.</summary>
    private void ApplyAskWaits() {
        var ask = state.Policy.Ask;
        var unconfirmed = TimeSpan.FromMinutes(ask.DeliveryUnconfirmedMinutes);
        foreach (var result in state.ResultOutbox.Values.Where(r => r.State == "sending" && r.ConsumedAtUtc is null &&
            r.SendingAtUtc is { } since && now - since > unconfirmed).ToArray()) {
            state.ResultOutbox[result.RequestId] = result with { State = "uncertain", EvidenceRef = "delivery-unconfirmed" };
            Note(result.RequestId, "result-uncertain", "service", result.Generation, "delivery-unconfirmed");
            NoticeUncertainResult(result.RequestId);
        }
        foreach (var question in state.Questions.Values.Where(q => q.Delivery is { Stage: "sending", AtUtc: { } since } &&
            now - since > unconfirmed).ToArray()) {
            state.Questions[QuestionKey(question.RequestId, question.Number)] = question with {
                Delivery = question.Delivery! with { Stage = "uncertain", EvidenceRef = "delivery-unconfirmed" } };
            Note(question.RequestId, "question-uncertain", "service", question.Generation, "delivery-unconfirmed");
        }
        foreach (var question in state.Questions.Values.Where(q => q.Kind == "question" && q.State == "asked" &&
            now - q.AskedAtUtc > TimeSpan.FromMinutes(ask.QuestionWaitMinutes)).ToArray()) {
            state.Questions[QuestionKey(question.RequestId, question.Number)] = question with { State = "expired" };
            Note(question.RequestId, "question-expired", "service", question.Generation, QuestionKey(question.RequestId, question.Number));
        }
        foreach (var work in state.Work.Values.Where(w => IsAsk(w) && w.State == "queued" &&
            now - w.RequestedAtUtc > TimeSpan.FromMinutes(ask.AdmissionMinutes) &&
            !state.Questions.Values.Any(q => q.RequestId == w.Request.Id && q.Kind == "notice")).ToArray())
            AddQuestion(work, "notice", "No machine has taken request " + work.Request.Id + " after " +
                ask.AdmissionMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture) + " minutes (" +
                (work.Reason ?? "waiting") + "). It stays queued; cancel it if it is no longer needed.");
    }

    /// <summary>Tells an asker, once, that its result may not have arrived and where to read it.</summary>
    private void NoticeUncertainResult(string requestId) {
        if (!state.Work.TryGetValue(requestId, out var asked) || !IsAsk(asked)) return;
        var text = "The result of your ask " + requestId + " may not have reached this session. Read it with request_result (requestId " + requestId + ").";
        if (state.Questions.Values.Any(q => q.RequestId == requestId && q.Kind == "notice" && q.Text == text)) return;
        AddQuestion(asked, "notice", text);
    }

    /// <summary>Records provider, model and tokens spent on behalf of one request by the host running or delivering it.
    /// An identical retry is ignored.</summary>
    public CoordinationUsageView RecordUsage(string id, CoordinationUsage usage, CoordinationPrincipal caller) {
        Require(state.Work.TryGetValue(id, out var work) && caller.Role == "host" && usage.MachineId == caller.MachineId &&
            (work.Machine == caller.MachineId || work.Request.ReturnTo?.MachineId == caller.MachineId), "usage-owner-conflict");
        Require(usage.Kind is "worker" or "wake" or "delivery" && Id(usage.Provider) && Id(usage.Model) &&
            usage.InputTokens >= 0 && usage.CachedInputTokens >= 0 && usage.OutputTokens >= 0 && usage.TotalTokens >= 0 &&
            (usage.CostUsd is null || double.IsFinite(usage.CostUsd.Value) && usage.CostUsd >= 0) &&
            (usage.SessionId is null || Id(usage.SessionId)) && usage.AtUtc <= now.AddMinutes(2), "invalid-usage");
        state.Usage.TryGetValue(id, out var items);
        items ??= [];
        if (!items.Any(item => Same(item, usage))) {
            Require(items.Count < MaximumUsagePerRequest, "usage-limit-reached");
            items.Add(usage);
            state.Usage[id] = items;
            Note(id, "usage-" + usage.Kind, caller.Id, work.Generation);
        }
        return new(id, items.ToArray());
    }

    /// <summary>Totals for one UTC day by request and by kind/provider/model, limited to what the caller may see.</summary>
    public CoordinationUsageReport UsageReport(DateOnly day, Func<CoordinationWork, bool> visible,
        IEnumerable<CoordinationArchiveEntry>? archived = null) {
        var rows = state.Usage.Where(pair => state.Work.TryGetValue(pair.Key, out var work) && visible(work))
            .Select(pair => (Key: pair.Key, Items: (IEnumerable<CoordinationUsage>)pair.Value))
            .Concat((archived ?? []).Where(entry => !state.Work.ContainsKey(entry.RequestId) && visible(entry.Work))
                .Select(entry => (Key: entry.RequestId, Items: (IEnumerable<CoordinationUsage>)entry.Usage)))
            .SelectMany(pair => pair.Items.Where(u => DateOnly.FromDateTime(u.AtUtc.UtcDateTime) == day).Select(u => (pair.Key, u)))
            .ToArray();
        static CoordinationUsageTotal Total(string key, IEnumerable<CoordinationUsage> items) {
            var list = items.ToArray();
            return new(key, list.Length, list.Sum(u => u.InputTokens), list.Sum(u => u.CachedInputTokens),
                list.Sum(u => u.OutputTokens), list.Sum(u => u.TotalTokens), list.Sum(u => u.CostUsd ?? 0));
        }
        return new(day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            rows.GroupBy(r => r.Key, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => Total(g.Key, g.Select(r => r.u))).ToArray(),
            rows.GroupBy(r => r.u.Kind + "/" + r.u.Provider + "/" + r.u.Model, StringComparer.Ordinal)
                .OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => Total(g.Key, g.Select(r => r.u))).ToArray());
    }
}
