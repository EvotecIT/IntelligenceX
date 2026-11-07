using IntelligenceX.Ledger.Contracts;

namespace IntelligenceX.Ledger.Hosting;

internal static class EventAdmission {
    internal static bool Valid(LedgerEvent? e, LedgerSettings settings, DateTimeOffset now) {
        static bool Id(string? id) => id is { Length: > 0 and <= 128 } && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or ':');
        bool Time(DateTimeOffset? value) => value is null || value.Value.Offset == TimeSpan.Zero && value <= now.AddMinutes(2);
        if (e is null || e.SchemaVersion != 1 || e.EventId == Guid.Empty || e.Epoch == Guid.Empty || e.Sequence < 1 ||
            !Id(e.ItemId) || !Id(e.MachineId) || !Id(e.Provider) || !Id(e.SourceVersion) || !Id(e.ReporterId) ||
            e.ParentItemId is not null && (!Id(e.ParentItemId) || e.ParentItemId == e.ItemId) || e.ItemKind is not (null or "session" or "delivery" or "input") ||
            !settings.ExpectedReporters.TryGetValue(e.MachineId, out var reporters) || !reporters.Contains(e.ReporterId) ||
            e.Source is not ("watcher" or "hook" or "notify" or "evidence") ||
            e.ObservedAtUtc.Offset != TimeSpan.Zero || e.ObservedAtUtc > now.AddMinutes(2) ||
            new[] { e.Session is not null, e.Work is not null, e.Declaration is not null, e.Control is not null, e.Safety is not null,
                e.Delivery is not null, e.GlobalWork is not null, e.ReviewMark is not null, e.Heartbeat, e.Coverage is not null }.Count(x => x) != 1)
            return false;
        if (e.Session is { } s && (s.Outcome is not ("unknown" or "completed" or "interrupted" or "failed" or "incomplete") ||
            !DeclarationValidation.ValidScope(s.Scope) || s.TurnId is not null && !Id(s.TurnId) || s.InputId is not null && !Id(s.InputId) ||
            s.AccountId is not null && !Id(s.AccountId) || s.RepositoryBranchKey is not null && !Id(s.RepositoryBranchKey) ||
            !Time(s.LastActivityAtUtc) || !Time(s.TurnStartedAtUtc) || !Time(s.TurnEndedAtUtc) || !Time(s.QuestionAtUtc) ||
            !Time(s.ArchiveObservedAtUtc) || (s.Archived is null) != (s.ArchiveObservedAtUtc is null))) return false;
        if (e.Work is { } w && (!DeclarationValidation.ValidScope(w.Scope) || w.QueuedInputs is < 0 or > 10000 || w.OpenPullRequests is null || w.OpenPullRequests.Length > 100 ||
            w.OwnedPullRequests is null || w.OwnedPullRequests.Length > 100 || w.OwnedPullRequests.Any(x => !Id(x)) ||
            w.CancelledPullRequests is null || w.CancelledPullRequests.Length > 100 || w.CancelledPullRequests.Any(x => !Id(x)) ||
            !Time(w.ClaimsAtUtc) || !Time(w.PullRequestsAtUtc) || !Time(w.GoalHeldAtUtc) || !Time(w.NeedsAppAtUtc) || !Time(w.OldestQueuedAtUtc) ||
            w.OpenPullRequests.Any(x => !Id(x)) || w.Wait is { } wait &&
            (wait.Kind is not ("ci" or "review" or "publish" or "task" or "date") || !Id(wait.ConditionId) || !Time(wait.RecordedAtUtc) ||
                !DeclarationValidation.ValidScope(wait.Scope) || wait.CandidateRef is not null && !Id(wait.CandidateRef) ||
                !DeclarationValidation.ValidBoundary(wait.RecordedAtUtc, wait.DueAtUtc, wait.ReviewAfterUtc, wait.Resolved)))) return false;
        if (e.Declaration is { } declaration && (!DeclarationValidation.Valid(declaration, now) ||
            declaration.Kind == "question" && !declaration.Blocking && (e.ItemKind != "input" || e.ParentItemId is null))) return false;
        if (e.Control is { } control && (control.Scope is null || !DeclarationValidation.ValidScope(control.Scope) || !Id(control.EpisodeId) ||
            control.State is not ("active" or "paused" or "stopped" or "budget") || !Time(control.AtUtc))) return false;
        if (e.Safety is { } safety && (safety.Scope is null || !DeclarationValidation.ValidScope(safety.Scope) || !Id(safety.EpisodeId) ||
            safety.Case is not ("production-outside-authority" or "destructive-outside-scope" or "credential-exposure" or "human-stop") ||
            safety.PolicyId != "urgent-safety" || safety.PolicyVersion != 1 || !Id(safety.EvidenceRef) || !Time(safety.AtUtc) ||
            safety.ManualAction is not (null or "press-stop" or "press-app-button"))) return false;
        if (e.Delivery is { } d && (!DeclarationValidation.ValidScope(d.Scope) || !Id(d.DeliveryId) ||
            new[] { d.NextActor, d.AcceptanceBy, d.ResultRef }.Any(x => x is not null && !Id(x)) ||
            d.CauseCode is not null && !Id(d.CauseCode) || d.TargetItemId is not null && !Id(d.TargetItemId) || d.Stage is not
            ("requested" or "admitted" or "submitted" or "started" or "terminal" or "accepted" or "held" or "uncertain") ||
            d.Outcome is not (null or "completed" or "failed" or "cancelled") || !Time(d.AtUtc))) return false;
        if (e.ReviewMark is { } review && (review.TransitionId < 1 || review.Mark is not ("right" or "wrong"))) return false;
        if (e.GlobalWork is { } global && (e.ReporterId != settings.ProjectEvidenceReporterId ||
            !Time(global.ClaimsAtUtc) || !Time(global.PullRequestsAtUtc) || global.ActiveClaimItems is null || global.OpenPullRequestsByBranch is null || global.ActiveClaimItems.Length > 10000 || global.OpenPullRequestsByBranch.Count > 5000 ||
            global.ActiveClaimItems.Any(x => !Id(x)) || global.OpenPullRequestsByBranch.Any(x => !Id(x.Key) || x.Value is null || x.Value.Length > 100 || x.Value.Any(v => !Id(v))))) return false;
        if (e.GlobalWork is { } evidence && (evidence.PullRequestProofs?.Length > 1000 || evidence.LinksByItem?.Count > 10000 ||
            evidence.PullRequestProofs?.Any(p => p is null || !Id(p.Id) || p.State is not ("open" or "closed" or "merged") || !Time(p.CheckedAtUtc)) == true ||
            evidence.LinksByItem?.Any(p => !Id(p.Key) || p.Value is null || p.Value.Ids is null || p.Value.Ids.Length > 100 ||
                p.Value.Ids.Any(id => !Id(id)) || !Time(p.Value.CheckedAtUtc) || !DeclarationValidation.ValidScope(p.Value.Scope)) == true)) return false;
        return e.Coverage is null || e.Coverage.Length <= 32 && e.Coverage.All(Id);
    }
}
