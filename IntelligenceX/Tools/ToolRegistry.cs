using System;
using System.Collections.Generic;
using System.Linq;

namespace IntelligenceX.Tools;

/// <summary>
/// Registry for tools available to the model.
/// </summary>
public sealed class ToolRegistry {
    private readonly Dictionary<string, ITool> _tools = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ToolDefinition> _definitions = new(StringComparer.OrdinalIgnoreCase);

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

        var definition = tool.Definition;
        if (replaceExisting) {
            RemoveCanonicalEntries(definition.CanonicalName);
        }

        RegisterEntry(tool, definition, replaceExisting);
        foreach (var alias in definition.Aliases) {
            var aliasDefinition = definition.CreateAliasDefinition(alias.Name, alias.Description, alias.Tags);
            RegisterEntry(tool, aliasDefinition, replaceExisting);
        }
    }

    /// <summary>
    /// Gets a tool by name.
    /// </summary>
    public bool TryGet(string name, out ITool tool) => _tools.TryGetValue(name, out tool!);

    /// <summary>
    /// Registers an alias for an already-registered tool.
    /// </summary>
    /// <param name="aliasName">Alias name.</param>
    /// <param name="targetToolName">Existing canonical or alias tool name to map to.</param>
    /// <param name="description">Optional alias-specific description override.</param>
    /// <param name="tags">Optional alias tags merged with canonical tags.</param>
    /// <param name="replaceExisting">Replace an existing registration that uses <paramref name="aliasName"/>.</param>
    public void RegisterAlias(
        string aliasName,
        string targetToolName,
        string? description = null,
        IReadOnlyList<string>? tags = null,
        bool replaceExisting = false) {
        if (string.IsNullOrWhiteSpace(aliasName)) {
            throw new ArgumentException("Alias name cannot be empty.", nameof(aliasName));
        }
        if (string.IsNullOrWhiteSpace(targetToolName)) {
            throw new ArgumentException("Target tool name cannot be empty.", nameof(targetToolName));
        }

        if (!_tools.TryGetValue(targetToolName, out var tool) || !_definitions.TryGetValue(targetToolName, out var targetDefinition)) {
            throw new InvalidOperationException($"Tool '{targetToolName}' is not registered.");
        }

        var aliasDefinition = targetDefinition.CreateAliasDefinition(aliasName, description, tags);
        RegisterEntry(tool, aliasDefinition, replaceExisting);
    }

    /// <summary>
    /// Returns tool definitions for the registry.
    /// </summary>
    public IReadOnlyList<ToolDefinition> GetDefinitions()
        => _definitions.Values
            .OrderBy(definition => definition.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private void RegisterEntry(ITool tool, ToolDefinition definition, bool replaceExisting) {
        if (!replaceExisting && _tools.ContainsKey(definition.Name)) {
            throw new InvalidOperationException($"Tool '{definition.Name}' is already registered.");
        }

        _tools[definition.Name] = tool;
        _definitions[definition.Name] = definition;
    }

    private void RemoveCanonicalEntries(string canonicalName) {
        if (string.IsNullOrWhiteSpace(canonicalName)) {
            return;
        }

        var toRemove = _definitions
            .Where(static kv => !string.IsNullOrWhiteSpace(kv.Key))
            .Where(kv => string.Equals(kv.Value.CanonicalName, canonicalName, StringComparison.OrdinalIgnoreCase))
            .Select(kv => kv.Key)
            .ToArray();
        foreach (var key in toRemove) {
            _definitions.Remove(key);
            _tools.Remove(key);
        }
    }
}
