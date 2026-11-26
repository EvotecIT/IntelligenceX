using System.Text.Json;
using System.Text.Json.Serialization;
using IntelligenceX.Ledger.Contracts;
using ModelContextProtocol;

namespace IntelligenceX.Ledger.Mcp;

/// <summary>The tool result for a Ledger the client could not connect to. Unlike "uncertain", it says the request never
/// reached the service, so the caller can use the configured fallback without risking the same work twice.</summary>
internal static class Unreachable {
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <param name="retried">True when the caller passed the id: an earlier attempt with it may have reached the Ledger,
    /// so only this attempt is known not to have, and the work must not go another way.</param>
    internal static string Json(LedgerUnreachableException error, string? id = null, string? tool = null, bool retried = false) =>
        JsonSerializer.Serialize(new UnreachableOutcome("unreachable", false, error.Reason, error.Endpoint, id,
            id is null ? null : retried
                ? "This attempt didn't reach the Ledger, but an earlier one with this id may have. Once it answers, call " +
                  (tool ?? "ask") + " again with this id, and don't send this work another way."
                : "Nothing reached the Ledger. Either use the fallback, or call " + (tool ?? "ask") +
                  " again with this id once the Ledger answers, not both.",
            retried ? null : error.Fallback), Options);

    internal static McpException Exception(LedgerUnreachableException error, string? id = null, string? tool = null, bool retried = false) =>
        new(Json(error, id, tool, retried), error);

    private sealed record UnreachableOutcome(string Outcome, bool Sent, string Reason, string Endpoint, string? Id, string? Retry, string? Fallback);
}
