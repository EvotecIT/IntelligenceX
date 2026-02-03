using System;
using IntelligenceX.Json;

namespace IntelligenceX.OpenAI.Tools;

/// <summary>
/// Defines a tool that can be invoked by the model.
/// </summary>
public sealed class ToolDefinition {
    /// <summary>
    /// Initializes a new tool definition.
    /// </summary>
    /// <param name="name">Tool name.</param>
    /// <param name="description">Tool description.</param>
    /// <param name="parameters">JSON schema for tool parameters.</param>
    public ToolDefinition(string name, string? description = null, JsonObject? parameters = null) {
        if (string.IsNullOrWhiteSpace(name)) {
            throw new ArgumentException("Tool name cannot be empty.", nameof(name));
        }
        Name = name;
        Description = description;
        Parameters = parameters;
    }

    /// <summary>
    /// Gets the tool name.
    /// </summary>
    public string Name { get; }
    /// <summary>
    /// Gets the tool description.
    /// </summary>
    public string? Description { get; }
    /// <summary>
    /// Gets the JSON schema for tool parameters.
    /// </summary>
    public JsonObject? Parameters { get; }

    internal JsonObject ToJson() {
        var obj = new JsonObject()
            .Add("type", "custom")
            .Add("name", Name);
        if (!string.IsNullOrWhiteSpace(Description)) {
            obj.Add("description", Description);
        }
        if (Parameters is not null) {
            obj.Add("parameters", Parameters);
        }
        return obj;
    }
}
