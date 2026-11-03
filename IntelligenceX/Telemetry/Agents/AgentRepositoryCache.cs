using System;
using System.Collections.Generic;
using System.Linq;

namespace IntelligenceX.Telemetry.Agents;

// Owned by one collection, never shared across consumers. Reconciliation invalidates both
// positive and negative results so branch switches and repositories created later are observed.
internal sealed class AgentRepositoryCache {
    private readonly Dictionary<string, AgentSessionState> _identities = new(StringComparer.Ordinal);

    internal AgentSessionState Enrich(AgentSessionState session) {
        if (string.IsNullOrEmpty(session.WorkingDirectory) || (session.Repository is not null && session.Branch is not null)) return session;
        if (!_identities.TryGetValue(session.WorkingDirectory!, out var identity)) {
            identity = AgentRepositoryIdentity.Enrich(new AgentSessionState { WorkingDirectory = session.WorkingDirectory });
            _identities[session.WorkingDirectory!] = identity;
        }
        return session with { Repository = session.Repository ?? identity.Repository, Branch = session.Branch ?? identity.Branch };
    }

    internal bool Refresh(IEnumerable<AgentSessionState> sessions) {
        var needed = new HashSet<string>(sessions.Where(static session => session.WorkingDirectory is not null
            && (session.Repository is null || session.Branch is null)).Select(static session => session.WorkingDirectory!), StringComparer.Ordinal);
        foreach (var cwd in _identities.Keys.Where(cwd => !needed.Contains(cwd)).ToArray()) _identities.Remove(cwd);
        var changed = false;
        foreach (var cwd in new List<string>(_identities.Keys)) {
            var current = AgentRepositoryIdentity.Enrich(new AgentSessionState { WorkingDirectory = cwd });
            var previous = _identities[cwd];
            changed |= previous.Repository != current.Repository || previous.Branch != current.Branch;
            _identities[cwd] = current;
        }
        return changed;
    }
}
