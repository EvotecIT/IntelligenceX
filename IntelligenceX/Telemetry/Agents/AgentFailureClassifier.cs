using System.Text.Json;
using static IntelligenceX.Telemetry.Agents.AgentSessionLogReader;

namespace IntelligenceX.Telemetry.Agents;

// Exact protocol identifiers only. Unknown codes and human-readable error messages never escape.
internal static class AgentFailureClassifier {
    internal static AgentSessionState Failed(AgentSessionState session, JsonElement error, bool codex,
        AgentTurnOutcome outcome = AgentTurnOutcome.Failed) {
        var code = codex ? Object(error, "codex_error_info")
            : error.ValueKind == JsonValueKind.String ? error : Object(error, "type");
        var cause = AgentFailureCause.Unknown;
        if (code.ValueKind == JsonValueKind.String) cause = Map(code.GetString(), codex);
        else if (codex && code.ValueKind == JsonValueKind.Object) {
            foreach (var property in code.EnumerateObject()) {
                cause = Map(property.Name, true);
                break; // Externally tagged provider enum; never search nested payloads for words.
            }
        }
        return session with { State = AgentWorkState.Idle, StateIsInferred = false, LastTurnOutcome = outcome,
            FailureCause = cause, FailureEvidence = cause == AgentFailureCause.Unknown
                ? AgentFailureEvidence.TerminalEvent : AgentFailureEvidence.ProviderCode };
    }

    internal static bool IsNonTurnCodexError(JsonElement payload) => Text(payload, "codex_error_info") is "ThreadRollbackFailed" or "thread_rollback_failed"
        || Object(payload, "codex_error_info").ValueKind == JsonValueKind.Object
            && (Object(payload, "codex_error_info").TryGetProperty("ActiveTurnNotSteerable", out _)
                || Object(payload, "codex_error_info").TryGetProperty("active_turn_not_steerable", out _));

    private static AgentFailureCause Map(string? code, bool codex) => codex ? code switch {
        "ServerOverloaded" or "server_overloaded" or "FlexUnavailable" or "flex_unavailable" or "PolicyCapacityUnavailable" or "policy_capacity_unavailable" => AgentFailureCause.Capacity,
        "UsageLimitExceeded" or "usage_limit_exceeded" or "SessionBudgetExceeded" or "session_budget_exceeded" => AgentFailureCause.UsageLimit,
        "RateLimitExceeded" or "rate_limit_exceeded" => AgentFailureCause.RateLimit,
        "Unauthorized" or "unauthorized" => AgentFailureCause.Authentication,
        "HttpConnectionFailed" or "http_connection_failed" or "ResponseStreamConnectionFailed" or "response_stream_connection_failed"
            or "ResponseStreamDisconnected" or "response_stream_disconnected" or "ResponseTooManyFailedAttempts" or "response_too_many_failed_attempts" => AgentFailureCause.Transport,
        "ContextWindowExceeded" or "context_window_exceeded" => AgentFailureCause.ContextLimit,
        _ => AgentFailureCause.Unknown
    } : ClaudeCause(code);

    internal static AgentFailureCause ClaudeCause(string? code) => code switch {
        "overloaded_error" => AgentFailureCause.Capacity,
        "rate_limit" or "rate_limit_error" => AgentFailureCause.RateLimit,
        "authentication_error" => AgentFailureCause.Authentication,
        _ => AgentFailureCause.Unknown
    };
}
