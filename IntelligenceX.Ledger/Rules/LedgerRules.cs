using IntelligenceX.Ledger.Contracts;

namespace IntelligenceX.Ledger.Rules;

/// <summary>Rules version 2. Scope and native controls precede verdict priority. Clock advancement is not progress.</summary>
public sealed partial class LedgerRules(RuleSettings settings) {
    public const int Version = 2;
    internal WaitFact ReviewWait(string condition, DateTimeOffset recorded, TaskScope? scope = null) =>
        new("review", condition, recorded, ReviewAfterUtc: recorded + settings.ReviewWait, Inferred: true, Scope: scope);
    internal TimeSpan EvidenceMaxAge => settings.EvidenceMaxAge;

    public Verdict Evaluate(LedgerItem item, DateTimeOffset now, bool reporterSilent = false) {
        item = ApplyScope(item);
        var matches = new List<RuleMatch>();
        var session = item.Session;
        var work = item.Work;
        TaskScope? WorkScope(string dimension) => item.WorkDimensionScopes.TryGetValue(dimension, out var dimensionScope) ? dimensionScope : work?.Scope;
        var goalScope = WorkScope("goal");
        void Add(bool condition, string verdict, string reason, DateTimeOffset? since, string basis = "source-event",
            string? identity = null, EvidenceRef? evidence = null, WaitFact? waitFact = null) {
            if (condition) matches.Add(Match(item, verdict, reason, since, basis, identity, evidence, waitFact));
        }
        if (item.Supersession is { } superseded) {
            if (reporterSilent && item.Current is not null) return LastKnown(item.Current, now);
            var cancelled = Match(item, "done", "cancelled", superseded.AtUtc, superseded.SinceBasis,
                "parent:" + superseded.Scope?.TaskId + ":" + superseded.Scope?.Generation + ":" + superseded.TurnId, superseded.Evidence);
            return new("done", "cancelled", cancelled.SinceUtc, cancelled.SinceBasis, now, Version, reporterSilent ? ["reporter-silent"] : [], reporterSilent) {
                EpisodeId = cancelled.EpisodeId, Matches = [cancelled], LastProgressAtUtc = session?.LastActivityAtUtc,
                WakeEligibility = new("suppressed", "superseded", null, [superseded.Evidence], [])
            };
        }
        var pause = item.Control?.State is "paused" or "stopped" or "budget";
        var declarations = item.Declarations.Values.OrderBy(d => d.Fact.RecordedAtUtc).ToArray();
        var hasDeclaredCondition = declarations.Any(d => !d.Fact.Resolved && (d.Fact.Blocking || item.Kind == "input"));
        var operation = declarations.LastOrDefault(d => !d.Fact.Resolved && d.Fact.Blocking && d.Fact.Kind == "operation");
        Add(item.Safety is { Resolved: false }, "needs-you", "urgent-safety", item.Safety?.AtUtc,
            identity: item.Safety?.EpisodeId, evidence: item.SafetyEvidence);
        if (pause) return Paused(item, now, matches, reporterSilent);
        // Needs you, in published rules order.
        Add(session?.Question == true && session.InputId is not null && session.QuestionBlocking,
            "needs-you", "question", session?.QuestionAtUtc, identity: session?.InputId, evidence: item.SessionEvidence);
        Add(session?.UsageLimit == true, "needs-you", "usage-limit", session?.TurnEndedAtUtc);
        var queued = work?.QueuedInputs ?? 0;
        var heldGoal = work?.GoalHeld == true && (queued > 0 || work.ActiveClaim == true || item.Deliveries.Values.Any(IsPending));
        Add(heldGoal && LedgerScope.SameTask(goalScope, item.Scope), "needs-you", "goal-held", work?.GoalHeldAtUtc);
        foreach (var declaration in declarations.Where(d => !d.Fact.Resolved && (d.Fact.Blocking || item.Kind == "input") &&
                     (d.Fact.Kind is "question" or "handoff")))
            Add(true, declaration.Fact.NextActor == "human" ? "needs-you" : "waiting",
                declaration.Fact.Kind == "question" ? "question" : "waiting-acceptance",
                declaration.EpisodeStartedAtUtc ?? declaration.Fact.RecordedAtUtc, identity: DeclarationEpisode(declaration.Fact), evidence: declaration.Evidence);
        foreach (var delivery in item.Deliveries.Values.OrderBy(x => x.Id, StringComparer.Ordinal)) {
            var submitted = delivery.Stages["submitted"];
            Add(submitted.Knowledge == "known" && delivery.Stages["started"].Knowledge == "unknown" &&
                delivery.Stages["terminal"].Knowledge == "unknown" && now - submitted.AtUtc >= settings.AdmissionGrace,
                "stuck", "delivery-uncertain", submitted.AtUtc, identity: delivery.Id, evidence: submitted.Evidence);
        }
        var appScopeUnknown = item.Scope is not null && work?.NeedsApp == true && WorkScope("app") is null;
        Add(!appScopeUnknown && work?.NeedsApp == true && session?.AppOwned == true, "needs-you", "needs-app", work?.NeedsAppAtUtc);
        // Stuck. A coverage note by itself is not a decisive missing fact.
        var declaredWait = declarations.LastOrDefault(d => !d.Fact.Resolved && d.Fact.Blocking && d.Fact.Kind == "wait");
        var declared = operation ?? declaredWait;
        var waitScopeUnknown = item.Scope is not null && work?.Wait is not null && (work.Wait.Scope ?? WorkScope("wait")) is null;
        var owned = work?.OwnedPullRequests.Intersect(work.OpenPullRequests).Order(StringComparer.Ordinal).FirstOrDefault();
        var wait = declared is null ? (waitScopeUnknown ? null : work?.Wait) ?? item.ImplicitReviewWait ?? (session?.Outcome == "completed" && owned is not null
            ? ReviewWait(owned, item.WorkEvidence?.ObservedAtUtc ?? session.TurnEndedAtUtc ?? item.FirstObservedAtUtc, item.Scope) : null)
            : new WaitFact(declared.Fact.Kind == "operation" ? "operation" : "task", declared.Fact.ConditionRef,
                declared.EpisodeStartedAtUtc ?? declared.Fact.RecordedAtUtc, declared.Fact.DueAtUtc, ReviewAfterUtc: declared.Fact.ReviewAfterUtc,
                Scope: declared.Fact.Scope, CandidateRef: declared.Fact.CandidateRef);
        var waitIdentity = declared is null ? wait?.ConditionId + ":" + wait?.CandidateRef + ":" + wait?.RecordedAtUtc.ToString("O") : DeclarationEpisode(declared.Fact);
        Add(wait is not null && now > wait.DueAtUtc, "stuck", "wait-overdue", wait?.DueAtUtc, "time-threshold", waitIdentity, declared?.Evidence);
        Add(wait?.ReviewAfterUtc is { } review && now > review && !(wait.DueAtUtc is { } due && now > due),
            "stuck", "wait-review", wait?.ReviewAfterUtc, "time-threshold", waitIdentity, declared?.Evidence);
        foreach (var other in declarations.Where(d => d != declared && !d.Fact.Resolved && d.Fact.Blocking &&
                     (d.Fact.Kind is "operation" or "wait"))) AddDeclarationMatch(item, other, now, matches);
        foreach (var handoff in declarations.Where(d => !d.Fact.Resolved && (d.Fact.Blocking || item.Kind == "input") &&
                     (d.Fact.Kind is "question" or "handoff") &&
                     (now > d.Fact.DueAtUtc || now > d.Fact.ReviewAfterUtc))) AddDeclarationMatch(item, handoff, now, matches);
        foreach (var other in item.InferredReviewWaits.Values.Where(w => w.ConditionId != wait?.ConditionId &&
                     session?.Outcome == "completed" && work?.Wait is null && work?.OpenPullRequests.Contains(w.ConditionId) == true)) {
            var identity = other.ConditionId + ":" + other.RecordedAtUtc.ToString("O");
            var expired = now > other.ReviewAfterUtc;
            Add(true, expired ? "stuck" : "waiting", expired ? "wait-review" : "waiting-review",
                expired ? other.ReviewAfterUtc : other.RecordedAtUtc, expired ? "time-threshold" : "first-observation", identity,
                item.PositivePullRequests.GetValueOrDefault(other.ConditionId)?.Evidence ?? item.PullRequestEvidence, other);
        }
        Add(session?.Outcome is "failed" or "interrupted" && session.TurnOpen != true &&
            session.TurnEndedAtUtc is { } ended && now >= ended + settings.InterruptionGrace,
            "stuck", "interrupted", session?.TurnEndedAtUtc + settings.InterruptionGrace, "time-threshold");
        foreach (var delivery in item.Deliveries.Values) {
            var held = delivery.Stages["held"];
            var uncertain = delivery.Stages["uncertain"];
            var started = delivery.Stages["started"].AtUtc;
            var holdIsCurrent = delivery.LastStage is "held" or "uncertain" || delivery.LastStage is null &&
                (started is null || held.AtUtc >= started || uncertain.AtUtc >= started);
            Add(holdIsCurrent && delivery.Stages["terminal"].Knowledge == "unknown" &&
                (held.Knowledge == "known" || uncertain.Knowledge == "known"), "stuck", "wake-held",
                delivery.LastStage is "held" or "uncertain" ? delivery.LastStageAtUtc : held.AtUtc ?? uncertain.AtUtc,
                delivery.LastStageTimeIsInferred ? "first-observation" : "source-event",
                delivery.Id + ":" + delivery.LastStageOrderAtUtc?.ToString("O"),
                (delivery.LastStage == "uncertain" ? uncertain : held).Evidence);
        }
        Add(queued >= 2 || queued > 0 && (work?.Stalled == true || work?.GoalHeld == true),
            "stuck", "queue-behind-stall", work?.OldestQueuedAtUtc);
        Add(wait is null && !hasDeclaredCondition && session?.TurnOpen == true && session.LastActivityAtUtc is { } activity &&
            now - activity >= settings.NoProgressAfter, "stuck", "no-progress", session?.LastActivityAtUtc);

        // Missing completion gates decide Done, but cannot suppress a known question/wait/open turn.
        var knownPrimary = matches.Count > 0 || wait is not null || session?.TurnOpen == true || work?.OpenPullRequests.Length > 0;
        var completed = session?.Outcome == "completed";
        var unknown = heldGoal && !LedgerScope.SameTask(goalScope, item.Scope) || appScopeUnknown || waitScopeUnknown || work?.NeedsApp == true && session?.AppOwned is null && !matches.Any(m => m.Verdict == "needs-you") ||
            !knownPrimary && !(completed && work?.ActiveClaim is not null && work.PullRequestsKnown == true);
        Add(unknown, "stuck", "unknown", now, "first-observation", "missing:" + string.Join("-", MissingFacts(item)));

        if (wait is not null) Add(!(wait.DueAtUtc is { } deadline && now > deadline) && !(wait.ReviewAfterUtc is { } reviewAt && now > reviewAt),
            operation is null ? "waiting" : "working", operation is null ? "waiting-" + wait.Kind : "active", wait.RecordedAtUtc,
            wait.Inferred ? "first-observation" : "source-event",
            identity: waitIdentity, evidence: declared?.Evidence);
        if (wait is null && !hasDeclaredCondition && session?.TurnOpen == true && session.LastActivityAtUtc is { } last && now - last < settings.NoProgressAfter)
            Add(true, "working", now - last < settings.RecentActivity ? "active" : "quiet", session.TurnStartedAtUtc);
        Add(completed && work?.ActiveClaim == false && work.PullRequestsKnown == true &&
            work.OpenPullRequests.Length == 0 && wait is null && !hasDeclaredCondition && queued == 0 &&
            !item.Deliveries.Values.Any(IsPending), "done", work?.CancelledPullRequests.Length > 0 ? "cancelled" : work?.HadPullRequests == true ? "complete" : "idle", session?.TurnEndedAtUtc);

        if (matches.Count == 0) Add(true, "stuck", "unknown", null, identity: "decisive:" + string.Join("-", MissingFacts(item)));
        matches = matches.OrderBy(m => m.Verdict switch { "needs-you" => 0, "stuck" => 1, "waiting" => 2, "working" => 3, _ => 4 }).ToList();
        var selected = matches[0];
        var previous = item.Current;
        if (reporterSilent && previous is not null) return LastKnown(previous, now);
        // Done remains sticky until explicit new work appears.
        if (previous?.Value == "done" && previous.RulesVersion == Version &&
            (item.Scope is null && previous.Matches.FirstOrDefault()?.Scope is null || LedgerScope.SameTask(item.Scope, previous.Matches.FirstOrDefault()?.Scope)) &&
            session?.TurnOpen != true && work?.ActiveClaim != true &&
            wait is null && queued == 0 && !item.Deliveries.Values.Any(IsPending) &&
            !(session?.TurnStartedAtUtc > previous.SinceUtc || session?.TurnEndedAtUtc > previous.SinceUtc) &&
            matches.All(x => x.ReasonCode == "unknown")) {
            selected = new(previous.Value, previous.ReasonCode, previous.SinceUtc, previous.SinceBasis) { EpisodeId = previous.EpisodeId,
                Scope = previous.Matches.FirstOrDefault()?.Scope, Evidence = previous.Matches.FirstOrDefault()?.Evidence ?? [] };
            matches = previous.Matches.Length == 0 ? [selected] : previous.Matches.ToList();
        }
        // Silent reporters retain each item's last known verdict, with machine-level Stuck.
        var also = matches.Skip(1).Select(x => x.ReasonCode).ToList();
        if (reporterSilent) also.Add("reporter-silent");
        var retainSince = previous?.EpisodeId == selected.EpisodeId && previous.Value == selected.Verdict && previous.ReasonCode == selected.ReasonCode &&
            previous.SinceUtc <= selected.SinceUtc;
        return new(selected.Verdict, selected.ReasonCode,
            retainSince ? previous!.SinceUtc : selected.SinceUtc,
            retainSince ? previous!.SinceBasis : selected.SinceBasis,
            now, Version, also.Distinct(StringComparer.Ordinal).Where(x => x != selected.ReasonCode).ToArray(), reporterSilent,
            selected.ReasonCode == "unknown" ? MissingFacts(item) : []) {
                EpisodeId = selected.EpisodeId, Matches = matches.ToArray(), LastProgressAtUtc = session?.LastActivityAtUtc,
                Hold = Hold(item, selected), WakeEligibility = Eligibility(item, selected, now, settings.EvidenceMaxAge)
            };
    }

    private static string[] MissingFacts(LedgerItem item) {
        if (item.Kind == "input" && item.Session?.InputUnconfirmed == true) return ["input-status"];
        TaskScope? WorkScope(string dimension) => item.WorkDimensionScopes.TryGetValue(dimension, out var scope) ? scope : item.Work?.Scope;
        if (item.Work?.GoalHeld == true && WorkScope("goal") is null) return ["goal-task-scope"];
        if (item.Scope is not null && item.Work?.NeedsApp == true && WorkScope("app") is null) return ["app-task-scope"];
        if (item.Scope is not null && item.Work?.Wait is { Scope: null } && WorkScope("wait") is null) return ["wait-task-scope"];
        if (item.Work?.NeedsApp == true && item.Session?.AppOwned is null) return ["app-ownership"];
        if (item.Deliveries.Values.Any(d => d.Stages["submitted"] is { Knowledge: "known", AtUtc: null } &&
            d.Stages["started"].Knowledge == "unknown")) return ["submission-time"];
        if (item.Kind == "delivery" && item.Session is null) return ["delivery-progress-or-terminal-result"];
        if (item.Session is null || item.Session.TurnOpen is null) return ["turn-state"];
        if (item.Session.TurnOpen == true && item.Session.LastActivityAtUtc is null) return ["turn-activity"];
        if (item.Session.Outcome != "completed") return ["terminal-result-or-successor"];
        var missing = new List<string>();
        if (item.Work?.ActiveClaim is null) missing.Add("claim");
        if (item.Work?.PullRequestsKnown != true) missing.Add("pull-requests");
        return missing.Count == 0 ? ["next-step"] : missing.ToArray();
    }

    private static bool IsPending(DeliveryState delivery) => delivery.Stages["terminal"].Knowledge == "unknown" &&
        delivery.Stages["accepted"].Knowledge == "unknown";
}

public sealed record RuleSettings {
    public TimeSpan RecentActivity { get; init; } = TimeSpan.FromMinutes(10);
    public TimeSpan AdmissionGrace { get; init; } = TimeSpan.FromMinutes(10);
    public TimeSpan InterruptionGrace { get; init; } = TimeSpan.FromMinutes(10);
    public TimeSpan NoProgressAfter { get; init; } = TimeSpan.FromMinutes(30);
    public TimeSpan ReporterTimeout { get; init; } = TimeSpan.FromMinutes(10);
    public TimeSpan ReviewWait { get; init; } = TimeSpan.FromHours(24);
    public TimeSpan EvidenceMaxAge { get; init; } = TimeSpan.FromMinutes(10);
}
