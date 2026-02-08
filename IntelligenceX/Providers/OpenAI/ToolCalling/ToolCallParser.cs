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
    /// <param name="turn">Turn info.</param>
    public static IReadOnlyList<ToolCall> Extract(TurnInfo turn) {
        if (turn is null) {
            throw new ArgumentNullException(nameof(turn));
        }
        var calls = new List<ToolCall>();
        foreach (var output in turn.Outputs) {
            var call = ToolCall.FromJson(output.Raw);
            if (call is not null) {
                calls.Add(call);
            }
        }
        return calls;
    }
}
