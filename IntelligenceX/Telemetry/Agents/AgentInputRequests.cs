using System;
using System.Linq;

namespace IntelligenceX.Telemetry.Agents;

// Keep request identity in the metadata checkpoint, not in message bodies or one mutable pending slot.
internal static class AgentInputRequests {
    internal static bool IsUnresolved(AgentSessionState session) => !session.IsArchived && session.State != AgentWorkState.Finished
        && (session.PendingInputs.Count > 0 || session.State == AgentWorkState.WaitingOnPerson);

    internal static AgentSessionState Add(AgentSessionState session, string? id, AgentInputKind kind,
        bool blocking, DateTimeOffset? timestamp) {
        if (session.PendingInputs.Any(input => input.Id == id && input.Kind == kind)) return session;
        return session with { PendingInputs = session.PendingInputs.Concat(new[] { new AgentPendingInput {
            Id = id, Kind = kind, IsBlocking = blocking, AnswerIsUnconfirmed = !blocking || id is null, RequestedAtUtc = timestamp
        }}).ToArray() };
    }

    internal static AgentSessionState CompleteBlocking(AgentSessionState session, string? id) {
        if (id is null || !session.PendingInputs.Any(input => input.Id == id && input.IsBlocking)) return session;
        return session with { State = AgentWorkState.Idle,
            PendingInputs = session.PendingInputs.Where(input => input.Id != id || !input.IsBlocking).ToArray() };
    }

    internal static AgentSessionState CompleteAsync(AgentSessionState session, string id) =>
        session with { PendingInputs = session.PendingInputs.Where(input => input.Id != id || input.IsBlocking).ToArray() };
}
