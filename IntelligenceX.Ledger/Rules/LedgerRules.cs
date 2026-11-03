using IntelligenceX.Ledger.Contracts;

namespace IntelligenceX.Ledger.Rules;

/// <summary>Rules version 1. First matching verdict wins; clock advancement is not work progress.</summary>
public sealed class LedgerRules(RuleSettings settings) {
    public const int Version = 1;
    internal WaitFact ReviewWait(string condition, DateTimeOffset recorded) => new("review", condition, recorded, recorded + settings.ReviewWait);
    internal TimeSpan EvidenceMaxAge => settings.EvidenceMaxAge;

    public Verdict Evaluate(LedgerItem item, DateTimeOffset now, bool reporterSilent = false,
        bool accountLimited = false) {
        var matches = new List<RuleMatch>();
        var session = item.Session;
        var work = item.Work;
        void Add(bool condition, string verdict, string reason, DateTimeOffset? since, string basis = "source-event") {
            if (condition) matches.Add(new(verdict, reason, since ?? item.ConditionFirstObservedAtUtc.GetValueOrDefault(reason, item.FirstObservedAtUtc),
                since.HasValue ? basis : "first-observation"));
        }
        // Needs you, in published rules order.
        Add(session?.Question == true, "needs-you", "question", session?.QuestionAtUtc);
        Add(session?.InputUnconfirmed == true, "needs-you", "input-unconfirmed", null, "first-observation");
        Add(session?.UsageLimit == true, "needs-you", "usage-limit", session?.TurnEndedAtUtc);
        var queued = work?.QueuedInputs ?? 0;
        Add(work?.GoalHeld == true && (queued > 0 || work.ActiveClaim == true ||
            item.Deliveries.Values.Any(IsPending)), "needs-you", "goal-held", work?.GoalHeldAtUtc);
        foreach (var delivery in item.Deliveries.Values.OrderBy(x => x.Id, StringComparer.Ordinal)) {
            var submitted = delivery.Stages["submitted"];
            Add(submitted.Knowledge == "known" && delivery.Stages["started"].Knowledge == "unknown" &&
                delivery.Stages["terminal"].Knowledge == "unknown" && now - submitted.AtUtc >= settings.AdmissionGrace,
                "needs-you", "not-accepted", submitted.AtUtc);
        }
        Add(work?.NeedsApp == true && session?.AppOwned == true, "needs-you", "needs-app", work?.NeedsAppAtUtc);
        // Stuck. A coverage note by itself is not a decisive missing fact.
        var wait = work?.Wait ?? item.ImplicitReviewWait ?? (session?.Outcome == "completed" && work?.OpenPullRequests.Length > 0
            ? ReviewWait(work.OpenPullRequests.Order(StringComparer.Ordinal).First(), item.WorkEvidence?.ObservedAtUtc ?? item.FirstObservedAtUtc) : null);
        Add(wait is not null && now > wait.DueAtUtc, "stuck", "wait-overdue", wait?.DueAtUtc, "time-threshold");
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
                delivery.LastStageTimeIsInferred ? "first-observation" : "source-event");
        }
        Add(queued >= 2 || queued > 0 && (work?.Stalled == true || work?.GoalHeld == true),
            "stuck", "queue-behind-stall", work?.OldestQueuedAtUtc);
        Add(session?.TurnOpen == true && session.LastActivityAtUtc is { } activity &&
            now - activity >= settings.NoProgressAfter, "stuck", "no-progress", session?.LastActivityAtUtc);

        // Missing completion gates decide Done, but cannot suppress a known question/wait/open turn.
        var knownPrimary = matches.Count > 0 || wait is not null || session?.TurnOpen == true || work?.OpenPullRequests.Length > 0;
        var completed = session?.Outcome == "completed";
        var unknown = work?.NeedsApp == true && session?.AppOwned is null && !matches.Any(m => m.Verdict == "needs-you") ||
            !knownPrimary && !(completed && work?.ActiveClaim is not null && work.PullRequestsKnown == true);
        Add(unknown, "stuck", "unknown", item.Current?.ReasonCode == "unknown" ? item.Current.SinceUtc : now, "first-observation");

        if (wait is not null) Add(now <= wait.DueAtUtc, "waiting", "waiting-" + wait.Kind, wait.RecordedAtUtc);
        if (session?.TurnOpen == true && session.LastActivityAtUtc is { } last && now - last < settings.NoProgressAfter)
            Add(true, "working", now - last < settings.RecentActivity ? "active" : "quiet", session.TurnStartedAtUtc);
        Add(completed && work?.ActiveClaim == false && work.PullRequestsKnown == true &&
            work.OpenPullRequests.Length == 0 && wait is null && queued == 0 &&
            !item.Deliveries.Values.Any(IsPending), "done", work?.HadPullRequests == true ? "complete" : "idle", session?.TurnEndedAtUtc);

        if (matches.Count == 0) matches.Add(new("stuck", "unknown", item.FirstObservedAtUtc, "first-observation"));
        var selected = matches[0];
        var previous = item.Current;
        if (reporterSilent && previous is not null)
            selected = new(previous.Value, previous.ReasonCode, previous.SinceUtc, previous.SinceBasis);
        // Done remains sticky until explicit new work appears.
        if (previous?.Value == "done" && session?.TurnOpen != true && work?.ActiveClaim != true &&
            wait is null && queued == 0 && !item.Deliveries.Values.Any(IsPending) &&
            !(session?.TurnStartedAtUtc > previous.SinceUtc || session?.TurnEndedAtUtc > previous.SinceUtc) &&
            matches.All(x => x.Verdict == "done" || x.ReasonCode == "unknown"))
            selected = new(previous.Value, previous.ReasonCode, previous.SinceUtc, previous.SinceBasis);
        // Silent reporters retain each item's last known verdict, with machine-level Stuck.
        var also = matches.Skip(1).Select(x => x.ReasonCode).ToList();
        if (reporterSilent) also.Add("reporter-silent");
        if (accountLimited && selected.ReasonCode != "usage-limit") also.Add("usage-limit");
        return new(selected.Verdict, selected.ReasonCode,
            previous?.Value == selected.Verdict && previous.ReasonCode == selected.ReasonCode ? previous.SinceUtc : selected.SinceUtc,
            previous?.Value == selected.Verdict && previous.ReasonCode == selected.ReasonCode ? previous.SinceBasis : selected.SinceBasis,
            now, Version, also.Distinct(StringComparer.Ordinal).Where(x => x != selected.ReasonCode).ToArray(), reporterSilent,
            selected.ReasonCode == "unknown" ? MissingFacts(item) : []);
    }

    private static string[] MissingFacts(LedgerItem item) {
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
