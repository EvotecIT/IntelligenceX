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

    internal static string Json(LedgerUnreachableException error, string? id = null, string? tool = null) =>
        JsonSerializer.Serialize(new UnreachableOutcome("unreachable", false, error.Reason, error.Endpoint, id,
            id is null ? null : "Nothing reached the Ledger. Once it answers, call " + (tool ?? "ask") + " again with this id.",
            error.Fallback), Options);

    internal static McpException Exception(LedgerUnreachableException error, string? id = null, string? tool = null) =>
        new(Json(error, id, tool), error);

    private sealed record UnreachableOutcome(string Outcome, bool Sent, string Reason, string Endpoint, string? Id, string? Retry, string? Fallback);
}
