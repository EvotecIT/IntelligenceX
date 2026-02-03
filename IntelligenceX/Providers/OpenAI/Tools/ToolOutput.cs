using System;
using IntelligenceX.Json;

namespace IntelligenceX.OpenAI.Tools;

/// <summary>
/// Represents the output for a tool call.
/// </summary>
public sealed class ToolOutput {
    /// <summary>
    /// Initializes a new tool output instance.
    /// </summary>
    public ToolOutput(string callId, string output) {
        if (string.IsNullOrWhiteSpace(callId)) {
            throw new ArgumentException("Call id cannot be empty.", nameof(callId));
        }
        CallId = callId;
        Output = output ?? string.Empty;
    }

    /// <summary>
    /// Gets the call id this output belongs to.
    /// </summary>
    public string CallId { get; }
    /// <summary>
    /// Gets the output text.
    /// </summary>
    public string Output { get; }

    internal JsonObject ToInputItem() {
        return new JsonObject()
            .Add("type", "custom_tool_call_output")
            .Add("call_id", CallId)
            .Add("output", Output);
    }
}
