using System;
using IntelligenceX.Json;

namespace IntelligenceX.OpenAI.Tools;

/// <summary>
/// Represents a tool call requested by the model.
/// </summary>
public sealed class ToolCall {
    /// <summary>
    /// Initializes a new tool call instance.
    /// </summary>
    public ToolCall(string callId, string name, string? input, JsonObject? arguments, JsonObject raw) {
        CallId = callId;
        Name = name;
        Input = input;
        Arguments = arguments;
        Raw = raw;
    }

    /// <summary>
    /// Gets the tool call identifier.
    /// </summary>
    public string CallId { get; }
    /// <summary>
    /// Gets the tool name.
    /// </summary>
    public string Name { get; }
    /// <summary>
    /// Gets the raw input string.
    /// </summary>
    public string? Input { get; }
    /// <summary>
    /// Gets the parsed argument object when available.
    /// </summary>
    public JsonObject? Arguments { get; }
    /// <summary>
    /// Gets the raw JSON payload.
    /// </summary>
    public JsonObject Raw { get; }

    internal static ToolCall? FromJson(JsonObject obj) {
        var type = obj.GetString("type");
        if (!string.Equals(type, "custom_tool_call", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(type, "tool_call", StringComparison.OrdinalIgnoreCase)) {
            return null;
        }

        var callId = obj.GetString("call_id") ?? obj.GetString("tool_call_id") ?? obj.GetString("id");
        var name = obj.GetString("name");
        if (string.IsNullOrWhiteSpace(callId) || string.IsNullOrWhiteSpace(name)) {
            return null;
        }

        var input = obj.GetString("input") ?? obj.GetString("arguments");
        JsonObject? arguments = null;
        if (!string.IsNullOrWhiteSpace(input)) {
            try {
                var parsed = JsonLite.Parse(input!);
                arguments = parsed?.AsObject();
            } catch {
                arguments = null;
            }
        } else {
            arguments = obj.GetObject("input") ?? obj.GetObject("arguments");
        }

        return new ToolCall(callId!, name!, input, arguments, obj);
    }
}
