using System;

namespace IntelligenceX.OpenAI.ToolCalling;

/// <summary>
/// Represents the tool selection strategy for a request.
/// </summary>
public sealed class ToolChoice {
    private ToolChoice(string type, string? name) {
        Type = type;
        Name = name;
    }

    /// <summary>
    /// Gets the tool choice type.
    /// </summary>
    public string Type { get; }
    /// <summary>
    /// Gets the tool name when a specific tool is required.
    /// </summary>
    public string? Name { get; }

    /// <summary>
    /// Allows the model to decide which tool to call.
    /// </summary>
    public static ToolChoice Auto { get; } = new("auto", null);
    /// <summary>
    /// Disables tool calling.
    /// </summary>
    public static ToolChoice None { get; } = new("none", null);

    /// <summary>
    /// Forces the model to call a specific tool.
    /// </summary>
    /// <param name="name">Tool name.</param>
    public static ToolChoice Custom(string name) {
        if (string.IsNullOrWhiteSpace(name)) {
            throw new ArgumentException("Tool name cannot be empty.", nameof(name));
        }
        return new ToolChoice("custom", name);
    }
}
