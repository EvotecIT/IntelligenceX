using System;
using System.Collections.Generic;

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

    internal void Clear() => _identities.Clear();
}
