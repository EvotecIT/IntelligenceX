using IntelligenceX.Telemetry.Agents;

namespace IntelligenceX.Ledger.Reporting;

/// <summary>Conservative creation provenance. Missing evidence receives human-session protections.</summary>
internal static class SessionOrigin {
    internal static string? Title(string? title) {
        if (string.IsNullOrWhiteSpace(title)) return null;
        var value = string.Join(" ", title.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return value.Length > 240 ? value[..240] : value;
    }

    internal static (string Origin, string Evidence) For(AgentSessionState session) {
        if (session.Provider == "codex" && session.SessionSource == "subagent") return ("agent", "native-subagent-source");
        if (session.Provider == "claude" && session.ParentSessionId is not null) return ("agent", "native-parent-session");
        if (session.Title is { } title && (title.StartsWith("Auto:", StringComparison.OrdinalIgnoreCase) ||
            title.StartsWith("Claude:", StringComparison.Ordinal))) return ("agent", "worker-title-prefix");
        if (session.SessionSource is "vscode" or "desktop" or "app") return ("human", "native-interactive-launcher");
        return ("unknown", "creation-unobserved");
    }
}
