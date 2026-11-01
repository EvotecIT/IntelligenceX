using System;
using System.Collections.Generic;
using System.Linq;

namespace IntelligenceX.Telemetry.Agents;

/// <summary>Pure association and attention rules, with caller-supplied time for deterministic evaluation.</summary>
public static class AgentWorkStateRules {
    /// <summary>Links sessions to PRs and evaluates attention. No files, network or process state are read.</summary>
    /// <param name="sessions">Local provider metadata.</param>
    /// <param name="pullRequests">GitHub metadata for the selected user.</param>
    /// <param name="nowUtc">Snapshot time.</param>
    /// <param name="staleAfter">Inactivity threshold for sessions with an open PR; defaults to seven days.</param>
    /// <param name="diagnostics">Optional source-read diagnostics.</param>
    public static AgentWorkSnapshot Evaluate(IEnumerable<AgentSessionState> sessions,
        IEnumerable<AgentPullRequestState> pullRequests, DateTimeOffset nowUtc, TimeSpan? staleAfter = null,
        IReadOnlyList<AgentSourceDiagnostic>? diagnostics = null) {
        if (sessions is null) throw new ArgumentNullException(nameof(sessions));
        if (pullRequests is null) throw new ArgumentNullException(nameof(pullRequests));
        var threshold = staleAfter ?? TimeSpan.FromDays(7);
        if (threshold <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(staleAfter));
        var prs = pullRequests.Where(static pr => pr.IsOpen)
            .GroupBy(static pr => pr.Url, StringComparer.OrdinalIgnoreCase).Select(static group => group.First())
            .OrderBy(static pr => pr.Url, StringComparer.Ordinal).ToArray();
        var attention = new List<AgentAttentionItem>();
        var linked = new List<AgentSessionState>();
        foreach (var session in sessions) {
            var baseState = session.State == AgentWorkState.Stale ? AgentWorkState.Idle : session.State;
            var matches = prs.Length == 0 ? Array.Empty<AgentPullRequestState>() : prs.Where(pr => !string.IsNullOrEmpty(session.Repository) && !string.IsNullOrEmpty(session.Branch)
                && string.Equals(pr.HeadRepository, session.Repository, StringComparison.OrdinalIgnoreCase)
                && string.Equals(pr.Branch, session.Branch, StringComparison.Ordinal)).ToArray();
            var stale = session.State != AgentWorkState.Running && session.State != AgentWorkState.Finished
                && matches.Length > 0 && session.LastActivityAtUtc.HasValue
                && nowUtc - session.LastActivityAtUtc.Value >= threshold;
            linked.Add(session with { PullRequests = matches, State = stale && baseState == AgentWorkState.Idle
                ? AgentWorkState.Stale : baseState });
            void Add(AgentAttentionKind kind) => attention.Add(new AgentAttentionItem {
                Kind = kind, Provider = session.Provider, SessionId = session.Id, Title = session.Title,
                Url = matches.FirstOrDefault()?.Url
            });
            if (session.State == AgentWorkState.WaitingOnPerson) Add(AgentAttentionKind.WaitingOnPerson);
            if (stale) Add(AgentAttentionKind.Stale);
            if (session.State != AgentWorkState.Finished) {
                switch (session.LastTurnOutcome) {
                    case AgentTurnOutcome.Failed: Add(AgentAttentionKind.FailedTurn); break;
                    case AgentTurnOutcome.Interrupted: Add(AgentAttentionKind.InterruptedTurn); break;
                    case AgentTurnOutcome.Incomplete: Add(AgentAttentionKind.IncompleteTurn); break;
                }
            }
        }
        foreach (var pr in prs) {
            if (pr.IsAuthor && pr.Checks == AgentCheckState.Failure) attention.Add(new AgentAttentionItem {
                Kind = AgentAttentionKind.FailedChecks, Title = pr.Title, Url = pr.ChecksUrl ?? pr.Url
            });
            if (pr.ReviewRequested) attention.Add(new AgentAttentionItem {
                Kind = AgentAttentionKind.ReviewRequested, Title = pr.Title, Url = pr.Url
            });
        }
        return new AgentWorkSnapshot {
            CapturedAtUtc = nowUtc, Sessions = linked.OrderByDescending(static s => s.LastActivityAtUtc)
                .ThenBy(static s => s.Provider, StringComparer.Ordinal).ThenBy(static s => s.Id, StringComparer.Ordinal).ToArray(),
            PullRequests = prs, NeedsAttention = attention, Diagnostics = diagnostics ?? Array.Empty<AgentSourceDiagnostic>()
        };
    }
}
