using System;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Tools;

namespace IntelligenceX.Tools.Common;

/// <summary>
/// Provides helpers for wrapping a tool with an updated immutable definition.
/// </summary>
public static class ToolDefinitionOverlay {
    /// <summary>
    /// Creates a forwarding wrapper that uses the supplied definition while preserving runtime behavior.
    /// </summary>
    public static ITool WithDefinition(ITool tool, ToolDefinition definition) {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(definition);
        return new ForwardingTool(tool, definition);
    }

    /// <summary>
    /// Creates a new definition derived from an existing one with contract overrides.
    /// </summary>
    public static ToolDefinition WithContracts(
        ToolDefinition definition,
        ToolRoutingContract? routing = null,
        ToolSetupContract? setup = null,
        ToolHandoffContract? handoff = null,
        ToolRecoveryContract? recovery = null) {
        ArgumentNullException.ThrowIfNull(definition);

        return new ToolDefinition(
            name: definition.Name,
            description: definition.Description,
            parameters: definition.Parameters,
            displayName: definition.DisplayName,
            category: definition.Category,
            tags: definition.Tags,
            writeGovernance: definition.WriteGovernance,
            aliases: definition.Aliases,
            aliasOf: definition.AliasOf,
            authentication: definition.Authentication,
            routing: routing ?? definition.Routing,
            setup: setup ?? definition.Setup,
            handoff: handoff ?? definition.Handoff,
            recovery: recovery ?? definition.Recovery);
    }

    private sealed class ForwardingTool : ITool {
        private readonly ITool _inner;
        private readonly ToolDefinition _definition;

        public ForwardingTool(ITool inner, ToolDefinition definition) {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _definition = definition ?? throw new ArgumentNullException(nameof(definition));
        }

        public ToolDefinition Definition => _definition;

        public Task<string> InvokeAsync(JsonObject? arguments, CancellationToken cancellationToken) {
            return _inner.InvokeAsync(arguments, cancellationToken);
        }
    }
}
