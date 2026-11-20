using IntelligenceX.Ledger.Context;
using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Rules;
using WorkAsk = IntelligenceX.Ledger.Coordination.CoordinationStore.WorkAsk;

namespace IntelligenceX.Ledger.Hosting;

internal sealed partial class LedgerRuntime {
    private static LedgerItem ApplyAskVerdict(LedgerItem item, WorkAsk ask, DateTimeOffset now) {
        // Coordination overrides transcript liveness. Qualified current holds still apply to a running helper.
        if (!ask.Finished && item.Current is { } observed &&
            (observed.Hold?.Type == "paused-by-user" || observed.ReasonCode is "urgent-safety" or "goal-held" or "needs-app" ||
             observed.UsageLimit is { InForce: true } && observed.ReasonCode == "usage-limit"))
            return item;
        var step = ask.Step;
        var (verdict, reason, stage) = ask.Finished
            ? ("done", step.State == "cancelled" ? "cancelled" : "complete", "terminal")
            : step.State switch {
                "started" => ("working", "active", "started"),
                "question" => ("waiting", "question", "question-asked"),
                _ => ("waiting", "waiting-task", step.State)
            };
        var since = step.Stages.GetValueOrDefault(stage, step.Stages.GetValueOrDefault("session-bound", step.RequestedAtUtc));
        if (step.State == "question" && ask.QuestionAtUtc is { } asked) since = asked;
        var resumed = ask.ResumedAtUtc ?? step.Stages.GetValueOrDefault("question-resumed");
        if (step.State == "started" && resumed > since) since = resumed;
        var episode = LedgerScope.Id(item.Id, step.RequestId, verdict, reason, since.ToString("O"));
        return item with { Current = new Verdict(verdict, reason, since, "source-event", now, LedgerRules.Version, []) {
            EpisodeId = episode, LastProgressAtUtc = item.Session?.LastActivityAtUtc,
            UsageLimit = item.Current?.UsageLimit,
            Matches = [new(verdict, reason, since, "source-event") { EpisodeId = episode }],
            Hold = reason == "question" ? new("waiting-decision", episode, item.Scope, "worker") : null,
            WakeEligibility = new("suppressed", "ix-ask", step.RequestId, [], [])
        }};
    }

    private sealed class WorkRelationships(LedgerItem[] items, WorkAsk[] asks, SessionLink[] nativeParents) {
        private sealed record SessionName(string Id, string Machine, string? Name);

        private readonly ILookup<(string Provider, Guid Session), SessionName> _sessions = items
            .Where(item => item is { Kind: "session", Session.SourceSessionId: not null })
            .ToLookup(item => (item.Provider, item.Session!.SourceSessionId!.Value),
                item => new SessionName(item.Id, item.MachineId, SessionTitle(item)));

        private readonly Dictionary<(string Machine, string Provider, Guid Session), WorkAsk> _asks = asks
            .Where(ask => Guid.TryParse(ask.Step.HelperSessionId, out _))
            .GroupBy(ask => (ask.HelperMachine, ask.Step.HelperProvider!, Guid.Parse(ask.Step.HelperSessionId!)))
            .ToDictionary(group => group.Key, group => group.MaxBy(ask => ask.Step.RequestedAtUtc)!);

        private readonly Dictionary<(string Provider, Guid Session), SessionLink> _nativeParents = nativeParents
            .ToDictionary(link => (link.ChildProvider, Guid.Parse(link.ChildSessionId)));

        internal WorkAsk? Ask(LedgerItem item) => item is { Kind: "session", Session.SourceSessionId: { } session }
            ? _asks.GetValueOrDefault((item.MachineId, item.Provider, session))
            : null;

        internal LedgerWorkRow Row(LedgerItem item, IReadOnlyDictionary<string, WorkLabel> labels, bool open) {
            labels.TryGetValue(item.Id, out var display);
            var label = SessionTitle(item) ?? Name(display?.Label);
            WorkHelperView? helper = null;
            WorkStartedByView? startedBy = null;
            if (Ask(item) is { } ask && Guid.TryParse(ask.Step.RequesterSessionId, out var requester)) {
                var parent = Find(ask.Step.RequesterProvider, requester, ask.RequesterMachine, allowOtherMachine: false);
                var parentName = Label(parent, labels);
                var requesterLabel = parentName ?? Fallback(ask.Step.RequesterProvider, "session", requester.ToString());
                helper = new(ask.Step.RequestId, ask.Step.State, ask.Step.RequesterProvider, requester.ToString(), parent?.Id, requesterLabel);
                if (ask.Title is { } title) label ??= $"Helper for {requesterLabel}: {title}";
                else if (parentName is not null) label ??= "Helper for " + parentName;
            }
            if (Parent(item) is { } parentLink) {
                var (provider, session, via) = parentLink;
                var parent = Find(provider, session, item.MachineId, allowOtherMachine: true);
                var parentName = Label(parent, labels);
                startedBy = new(provider, session.ToString(), parent?.Id,
                    parentName ?? Fallback(provider, "session", session.ToString()), via);
                label ??= parentName;
            }
            return new(label ?? Fallback(item.Provider, item.Kind, item.Id), display?.ProjectId, LedgerItemView.From(item)) {
                Helper = helper, StartedBy = startedBy, Open = open
            };
        }

        private (string Provider, Guid Session, string Via)? Parent(LedgerItem item) {
            if (item.Session is { StartedByProvider: { } provider, StartedBySessionId: { } session,
                StartedVia: "codex-parent-thread" or "claude-parent-session" or "desktop-side-session" } fact)
                return (provider, session, fact.StartedVia!);
            return item.Session?.SourceSessionId is { } native && _nativeParents.GetValueOrDefault((item.Provider, native)) is { } link
                ? (link.ParentProvider, Guid.Parse(link.ParentSessionId), link.Via)
                : null;
        }

        private SessionName? Find(string provider, Guid session, string machine, bool allowOtherMachine) {
            var candidates = _sessions[(provider, session)];
            return candidates.FirstOrDefault(candidate => candidate.Machine == machine) ??
                (allowOtherMachine && candidates.Take(2).Count() == 1 ? candidates.First() : null);
        }

        private static string? Label(SessionName? parent, IReadOnlyDictionary<string, WorkLabel> labels) =>
            parent is null ? null : parent.Name ?? Name(labels.GetValueOrDefault(parent.Id)?.Label);

        private static string? SessionTitle(LedgerItem item) => Name(item.Session?.Title) ?? Name(item.Session?.Repository?.Title);
        private static string? Name(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
        private static string Fallback(string provider, string kind, string id) => provider + " " + kind + " " + id[..Math.Min(10, id.Length)];
    }
}
