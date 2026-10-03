using System;
using System.Collections.Generic;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.Tools;

namespace IntelligenceX.OpenAI.ToolCalling;

/// <summary>
/// Helper for extracting tool calls from a turn response.
/// </summary>
public static class ToolCallParser {
    /// <summary>
    /// Extracts tool calls from a turn.
    /// </summary>
    /// <remarks>
    /// Explicitly unfinished turns and output items are not executable calls. Their raw outputs remain
    /// available on the turn for diagnostics. Transports that omit completion status retain legacy parsing.
    /// </remarks>
    /// <param name="turn">Turn info.</param>
    public static IReadOnlyList<ToolCall> Extract(TurnInfo turn) {
        if (turn is null) {
            throw new ArgumentNullException(nameof(turn));
        }
        var calls = new List<ToolCall>();
        if (!IsCompletedOrUnspecified(turn.Status)) {
            return calls;
        }
        foreach (var output in turn.Outputs) {
            if (!IsCompletedOrUnspecified(output.Raw.GetString("status"))) {
                continue;
            }
            var call = ToolCall.FromJson(output.Raw);
            if (call is not null) {
                calls.Add(call);
            }
        }
        return calls;
    }

    private static bool IsCompletedOrUnspecified(string? status) =>
        string.IsNullOrWhiteSpace(status) || string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase);
}
