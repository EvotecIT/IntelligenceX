using System.Text.Json;
using System.Text.Json.Serialization;
using IntelligenceX.Ledger.Contracts;
using ModelContextProtocol;

namespace IntelligenceX.Ledger.Mcp;

/// <summary>The tool result for a Ledger the client could not connect to. Unlike "uncertain", it says the request never
/// reached the service. Only a fresh ask or wake, whose id this attempt generated, is offered the configured fallback:
/// that one is known to be absent from the Ledger. A retry, an answer or another command may repeat an earlier attempt
/// that did arrive, so sending it another way could do the same thing twice.</summary>
internal static class Unreachable {
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <param name="retried">True when the caller passed the id: an earlier attempt with it may have reached the Ledger,
    /// so only this attempt is known not to have, and the work must not go another way.</param>
    /// <param name="local">Why this machine's local store didn't take the ask instead, when it was tried.</param>
    internal static string Json(LedgerUnreachableException error, string? id = null, string? tool = null, bool retried = false,
        string? local = null) =>
        JsonSerializer.Serialize(new UnreachableOutcome("unreachable", false, error.Reason, error.Endpoint, id,
            id is null ? null : retried
                ? "This attempt didn't reach the Ledger, but an earlier one with this id may have. Once it answers, call " +
                  (tool ?? "ask") + " again with this id, and don't send this work another way."
                : "Nothing reached the Ledger. Either use the fallback, or call " + (tool ?? "ask") +
                  " again with this id once the Ledger answers, not both.",
            id is null || retried ? null : error.Fallback, local), Options);

    internal static McpException Exception(LedgerUnreachableException error, string? id = null, string? tool = null, bool retried = false,
        string? local = null) => new(Json(error, id, tool, retried, local), error);

    private sealed record UnreachableOutcome(string Outcome, bool Sent, string Reason, string Endpoint, string? Id, string? Retry,
        string? Fallback, string? Local);
}
