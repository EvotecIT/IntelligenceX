using System;
using System.Collections.Generic;
using System.Linq;

namespace IntelligenceX.OpenAI.Tools;

/// <summary>
/// Registry for tools available to the model.
/// </summary>
public sealed class ToolRegistry {
    private readonly Dictionary<string, ITool> _tools = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registers a tool.
    /// </summary>
    /// <param name="tool">Tool instance.</param>
    public void Register(ITool tool) {
        if (tool is null) {
            throw new ArgumentNullException(nameof(tool));
        }
        _tools[tool.Definition.Name] = tool;
    }

    /// <summary>
    /// Gets a tool by name.
    /// </summary>
    public bool TryGet(string name, out ITool tool) => _tools.TryGetValue(name, out tool!);

    /// <summary>
    /// Returns tool definitions for the registry.
    /// </summary>
    public IReadOnlyList<ToolDefinition> GetDefinitions()
        => _tools.Values.Select(tool => tool.Definition).ToList();
}
