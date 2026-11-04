using IntelligenceX.Ledger.Contracts;

namespace IntelligenceX.Ledger.Rules;

public sealed partial class LedgerRules {
    private static string DeclarationEpisode(WorkerDeclaration fact) => LedgerScope.Id(fact.Scope.TurnId!,
        fact.CandidateRef ?? "", fact.ConditionRef, fact.EpisodeId);

    private static void AddDeclarationMatch(LedgerItem item, DeclarationObservation declaration, DateTimeOffset now, List<RuleMatch> matches) {
        var fact = declaration.Fact;
        var identity = DeclarationEpisode(fact);
        if (fact.DueAtUtc is { } due && now > due) {
            matches.Add(Match(item, "stuck", "wait-overdue", due, "time-threshold", identity, declaration.Evidence));
        } else if (fact.ReviewAfterUtc is { } review && now > review) {
            matches.Add(Match(item, "stuck", "wait-review", review, "time-threshold", identity, declaration.Evidence));
        } else {
            matches.Add(Match(item, fact.Kind == "operation" ? "working" : "waiting", fact.Kind == "operation" ? "active" : "waiting-task",
                declaration.EpisodeStartedAtUtc ?? fact.RecordedAtUtc, "source-event", identity, declaration.Evidence));
        }
    }

    private static LedgerItem ApplyScope(LedgerItem item) {
        var scope = item.Scope ?? item.Session?.Scope;
        var turn = item.Session?.TurnId ?? scope?.TurnId;
        bool Current(TaskScope? fact) => LedgerScope.Applies(fact, scope, turn);
        var work = item.Work;
        if (work?.Scope is not null && !LedgerScope.SameTask(work.Scope, scope)) work = work with {
            GoalHeld = null, GoalHeldAtUtc = null, NeedsApp = null, NeedsAppAtUtc = null,
            QueuedInputs = null, OldestQueuedAtUtc = null, Stalled = null, Wait = null
        };
        if (work?.Wait?.Scope is { } waitScope && !Current(waitScope)) work = work with { Wait = null };
        TaskScope? Dimension(string name) => item.WorkDimensionScopes.TryGetValue(name, out var observed) ? observed : work?.Scope;
        if (Dimension("goal") is { } goal && !LedgerScope.SameTask(goal, scope)) work = work is null ? null : work with { GoalHeld = null };
        if (Dimension("app") is { } app && !LedgerScope.SameTask(app, scope)) work = work is null ? null : work with { NeedsApp = null };
        if (Dimension("wait") is { } waitTask && !Current(waitTask)) work = work is null ? null : work with { Wait = null };
        return item with {
            Scope = scope, Work = work,
            Control = item.Control is { } control && LedgerScope.SameTask(control.Scope, scope) ? control : null,
            Safety = item.Safety is { } safety && LedgerScope.SameTask(safety.Scope, scope) ? safety : null,
            ImplicitReviewWait = item.ImplicitReviewWait is { } inferred &&
                (inferred.Scope is null || Current(inferred.Scope)) && work?.OwnedPullRequests.Contains(inferred.ConditionId) == true ? inferred : null,
            Declarations = LedgerScope.CurrentDeclarations(item.Declarations.Values.Where(d => Current(d.Fact.Scope) &&
                (d.Fact.CandidateRef is null || item.CandidateRef is null || d.Fact.CandidateRef == item.CandidateRef)))
                .ToDictionary(d => LedgerScope.DeclarationCondition(d.Fact), d => d),
            Deliveries = item.Deliveries.Where(d => d.Value.Scope is null || Current(d.Value.Scope)).ToDictionary(d => d.Key, d => d.Value)
        };
    }

    private static RuleMatch Match(LedgerItem item, string verdict, string reason, DateTimeOffset? at, string basis,
        string? identity, EvidenceRef? evidence, WaitFact? waitOverride = null) {
        var session = item.Session;
        var wait = waitOverride ?? item.Work?.Wait ?? item.ImplicitReviewWait;
        var turn = session?.TurnId ?? session?.TurnStartedAtUtc?.ToString("O") ?? session?.TurnEndedAtUtc?.ToString("O") ?? "turn-unknown";
        identity ??= reason switch {
            "question" => session?.InputId,
            "no-progress" => session?.LastActivityAtUtc?.ToString("O"),
            "goal-held" => item.Work?.GoalHeldAtUtc?.ToString("O"),
            "needs-app" => item.Work?.NeedsAppAtUtc?.ToString("O"),
            "queue-behind-stall" => item.Work?.OldestQueuedAtUtc?.ToString("O"),
            "wait-overdue" or "wait-review" => wait?.ConditionId + ":" + wait?.RecordedAtUtc.ToString("O"),
            _ when reason.StartsWith("waiting-", StringComparison.Ordinal) => wait?.ConditionId + ":" + wait?.RecordedAtUtc.ToString("O"),
            _ => turn
        };
        evidence ??= reason switch {
            "goal-held" => item.WorkDimensionEvidence.GetValueOrDefault("goal") ?? item.WorkEvidence,
            "needs-app" => item.WorkDimensionEvidence.GetValueOrDefault("app") ?? item.WorkEvidence,
            "queue-behind-stall" => item.WorkDimensionEvidence.GetValueOrDefault("queue") ?? item.WorkEvidence,
            _ when reason.StartsWith("wait", StringComparison.Ordinal) => item.WorkDimensionEvidence.GetValueOrDefault("wait") ?? item.PullRequestEvidence ?? item.WorkEvidence,
            _ => item.SessionEvidence
        };
        var declared = item.Declarations.Values.FirstOrDefault(d => d.Evidence.EventId == evidence?.EventId);
        var declaration = declared?.Fact;
        if (declaration is not null) {
            wait = new(declaration.Kind == "operation" ? "operation" : "task", declaration.ConditionRef,
                declared!.EpisodeStartedAtUtc ?? declaration.RecordedAtUtc, declaration.DueAtUtc, ReviewAfterUtc: declaration.ReviewAfterUtc,
                Scope: declaration.Scope, CandidateRef: declaration.CandidateRef);
        }
        var matchScope = declaration?.Scope ?? (reason == "paused-by-user" ? item.Control?.Scope :
            reason == "urgent-safety" ? item.Safety?.Scope :
            item.Scope is null ? null : item.Scope with { TurnId = session?.TurnId ?? item.Scope.TurnId });
        return new(verdict, reason, at ?? item.ConditionFirstObservedAtUtc.GetValueOrDefault(reason, item.FirstObservedAtUtc),
            at.HasValue ? basis : "first-observation") {
                EpisodeId = LedgerScope.Id(item.Id, item.Scope?.TaskId ?? "unscoped", item.Scope?.Generation.ToString() ?? "unknown",
                    reason, identity ?? item.ConditionFirstObservedAtUtc.GetValueOrDefault(reason, item.FirstObservedAtUtc).ToString("O")),
                Scope = matchScope, Evidence = new[] { evidence, declared?.EpisodeEvidence }.OfType<EvidenceRef>().DistinctBy(e => e.EventId).ToArray(),
                Wait = verdict == "waiting" || reason.StartsWith("wait", StringComparison.Ordinal) || declaration?.Kind == "operation" ? wait : null
            };
    }

    private static HoldView? Hold(LedgerItem item, RuleMatch match, bool quiet = false) {
        var declaration = item.Declarations.Values.FirstOrDefault(d => match.Evidence.Any(e => e.EventId == d.Evidence.EventId))?.Fact;
        var type = declaration?.HoldType ?? match.ReasonCode switch {
            "paused-by-user" => "paused-by-user",
            "urgent-safety" => "urgent-safety",
            "question" or "goal-held" or "needs-app" => "waiting-decision",
            "waiting-acceptance" => "waiting-acceptance",
            "delivery-uncertain" => "delivery-uncertain",
            "wake-held" or "queue-behind-stall" => "delivery-backpressure",
            "unknown" => "observation-gap",
            _ when match.Verdict == "waiting" || match.ReasonCode is "wait-review" or "wait-overdue" or "usage-limit" => "waiting-resource",
            _ => null
        };
        return type is null ? null : new(type, match.EpisodeId, match.Scope,
            declaration?.NextActor, declaration?.AcceptanceBy, declaration?.ResultRef, quiet);
    }

    private static WakeEligibilityView Eligibility(LedgerItem item, RuleMatch match, DateTimeOffset now, TimeSpan evidenceMaxAge) {
        WakeEligibilityView Result(string state, string reason, string[]? missing = null, DeclarationObservation? declaration = null) =>
            new(state, reason, declaration?.Fact.ConditionRef, declaration is null ? match.Evidence : [declaration.Evidence], missing ?? []);
        var hold = Hold(item, match);
        if (hold?.Type == "observation-gap") return Result("unknown", "observation-gap", MissingFacts(item));
        if (hold is not null) return Result("suppressed", hold.Type);
        if (item.Session?.TurnOpen == true) return Result("suppressed", "turn-open");
        if (item.Scope is null) return Result("unknown", "observation-gap", ["task-scope"]);
        if (item.Control is null) return Result("unknown", "observation-gap", ["native-controls"]);
        if (item.ControlEvidence is null || now - item.ControlEvidence.ObservedAtUtc > evidenceMaxAge)
            return Result("unknown", "observation-gap", ["native-controls-freshness"]);
        if (item.Control.State != "active") return Result("suppressed", "native-control-hold");
        if (item.Session?.TurnOpen is null) return Result("unknown", "observation-gap", ["turn-state"]);
        if (item.Work?.QueuedInputs > 0 || item.Deliveries.Values.Any(IsPending)) return Result("suppressed", "delivery-outstanding");
        var resolved = item.Declarations.Values.Where(d => d.Fact.Resolved && d.Fact.Blocking).MaxBy(d => d.Fact.RecordedAtUtc);
        if (resolved is null) return Result("suppressed", "no-resolved-condition");
        if (resolved.Fact.NextActor != "worker") return Result("suppressed", "next-actor", declaration: resolved);
        if (item.Session?.AppOwned == true) return Result("suppressed", "needs-app", declaration: resolved);
        if (item.Session?.AppOwned is null) return Result("unknown", "observation-gap", ["app-ownership"], resolved);
        return Result("eligible", "condition-resolved", declaration: resolved);
    }

    private static Verdict Paused(LedgerItem item, DateTimeOffset now, List<RuleMatch> matches, bool reporterSilent) {
        var control = item.Control!;
        var queue = item.Work?.QueuedInputs > 0;
        if (queue) matches.Add(Match(item, "stuck", "queue-behind-stall", item.Work?.OldestQueuedAtUtc,
            "source-event", control.EpisodeId + ":queue", item.WorkDimensionEvidence.GetValueOrDefault("queue") ?? item.WorkEvidence));
        matches.Add(Match(item, "waiting", "paused-by-user", control.AtUtc, "source-event", control.EpisodeId, item.ControlEvidence));
        var selected = matches[0];
        if (reporterSilent && item.Current is { } previous) return LastKnown(previous, now);
        var hold = Hold(item, selected, selected.ReasonCode == "paused-by-user");
        if (queue && selected.ReasonCode == "queue-behind-stall") hold = new("paused-by-user", selected.EpisodeId, item.Scope);
        return new(selected.Verdict, selected.ReasonCode, selected.SinceUtc, selected.SinceBasis, now, Version,
            matches.Skip(1).Select(m => m.ReasonCode).ToArray()) {
                EpisodeId = selected.EpisodeId, Matches = matches.ToArray(), Hold = hold,
                LastProgressAtUtc = item.Session?.LastActivityAtUtc,
                WakeEligibility = new("suppressed", hold?.Type ?? selected.ReasonCode, null, selected.Evidence, [])
            };
    }

    private static Verdict LastKnown(Verdict previous, DateTimeOffset now) => previous with {
        EvaluatedAtUtc = now, LastKnown = true,
        AlsoMatched = previous.AlsoMatched.Where(r => r != "reporter-silent").Append("reporter-silent").ToArray(),
        WakeEligibility = new("suppressed", "reporter-silent", null, [], [])
    };
}
