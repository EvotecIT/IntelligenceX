using System;
using System.Collections.Generic;
using System.Linq;

namespace IntelligenceX.Tools;

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
        Register(tool, replaceExisting: false);
    }

    /// <summary>
    /// Registers a tool with optional replacement.
    /// </summary>
    /// <param name="tool">Tool instance.</param>
    /// <param name="replaceExisting">Replace an existing tool with the same name.</param>
    public void Register(ITool tool, bool replaceExisting) {
        if (tool is null) {
            throw new ArgumentNullException(nameof(tool));
        }
        if (!replaceExisting && _tools.ContainsKey(tool.Definition.Name)) {
            throw new InvalidOperationException($"Tool '{tool.Definition.Name}' is already registered.");
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
        => _tools.Values
            .Select(tool => tool.Definition)
            .OrderBy(definition => definition.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
}
