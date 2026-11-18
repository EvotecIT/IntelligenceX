using IntelligenceX.Telemetry.Agents;

namespace IntelligenceX.Ledger.Reporting;

/// <summary>Conservative creation provenance. Missing evidence receives human-session protections.</summary>
internal static class SessionOrigin {
    internal static string? Title(string? title) {
        if (string.IsNullOrWhiteSpace(title)) return null;
        var value = string.Join(" ", title.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return value.Length > 240 ? value[..240] : value;
    }

    /// <summary>The native parent a reader saw: Codex <c>parent_thread_id</c> or Claude <c>parentSessionId</c>. A parent
    /// that isn't a session id is left out. It records lineage only: a thread a person forked in the app has a parent too,
    /// so creation provenance stays with <see cref="For"/>.</summary>
    internal static (Guid Session, string Provider, string Via)? Parent(AgentSessionState session) =>
        session.ParentSessionId is { } parent && Guid.TryParse(parent, out var id) && session.Provider is "codex" or "claude" &&
        !string.Equals(parent, session.Id, StringComparison.OrdinalIgnoreCase)
            ? (id, session.Provider, session.Provider == "codex" ? "codex-parent-thread" : "claude-parent-session") : null;

    internal static (string Origin, string Evidence) For(AgentSessionState session) {
        if (session.Provider == "codex" && session.SessionSource == "subagent") return ("agent", "native-subagent-source");
        if (session.Provider == "claude" && session.ParentSessionId is not null) return ("agent", "native-parent-session");
        if (session.Title is { } title && (title.StartsWith("Auto:", StringComparison.OrdinalIgnoreCase) ||
            title.StartsWith("Claude:", StringComparison.Ordinal))) return ("agent", "worker-title-prefix");
        if (session.SessionSource is "vscode" or "desktop" or "app") return ("human", "native-interactive-launcher");
        return ("unknown", "creation-unobserved");
    }
}
