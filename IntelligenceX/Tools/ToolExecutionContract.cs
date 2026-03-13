using System;
using System.Collections.Generic;

namespace IntelligenceX.Tools;

/// <summary>
/// Declares execution-locality metadata for a tool.
/// </summary>
public sealed class ToolExecutionContract {
    /// <summary>
    /// Default contract id for IX execution metadata.
    /// </summary>
    public const string DefaultContractId = "ix.tool-execution.v1";

    /// <summary>
    /// True when this definition participates in structured execution metadata.
    /// </summary>
    public bool IsExecutionAware { get; set; } = true;

    /// <summary>
    /// Stable execution contract identifier.
    /// </summary>
    public string ExecutionContractId { get; set; } = DefaultContractId;

    /// <summary>
    /// Human-readable execution locality classification.
    /// </summary>
    public string ExecutionScope { get; set; } = ToolExecutionScopes.LocalOnly;

    /// <summary>
    /// Optional canonical target-scope arguments explicitly declared by the tool.
    /// </summary>
    public IReadOnlyList<string> TargetScopeArguments { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Optional canonical remote-host arguments explicitly declared by the tool.
    /// </summary>
    public IReadOnlyList<string> RemoteHostArguments { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Validates the contract and throws when invalid.
    /// </summary>
    public void Validate() {
        if (!IsExecutionAware) {
            return;
        }

        if (string.IsNullOrWhiteSpace(ExecutionContractId)) {
            throw new InvalidOperationException("ExecutionContractId is required when IsExecutionAware is enabled.");
        }

        var normalizedScope = ToolExecutionScopes.Normalize(ExecutionScope);
        if (!ToolExecutionScopes.IsAllowed(normalizedScope)) {
            throw new InvalidOperationException(
                $"ExecutionScope must be one of: {string.Join(", ", ToolExecutionScopes.AllowedScopes)}.");
        }

        ValidateArgumentList(TargetScopeArguments, nameof(TargetScopeArguments));
        ValidateArgumentList(RemoteHostArguments, nameof(RemoteHostArguments));

        if (string.Equals(normalizedScope, ToolExecutionScopes.LocalOnly, StringComparison.Ordinal)
            && RemoteHostArguments.Count > 0) {
            throw new InvalidOperationException("RemoteHostArguments cannot be declared when ExecutionScope is local_only.");
        }
    }

    private static void ValidateArgumentList(IReadOnlyList<string>? values, string propertyName) {
        if (values is null) {
            throw new InvalidOperationException($"{propertyName} cannot be null when IsExecutionAware is enabled.");
        }

        for (var i = 0; i < values.Count; i++) {
            if (string.IsNullOrWhiteSpace(values[i])) {
                throw new InvalidOperationException($"{propertyName} cannot contain empty entries.");
            }
        }
    }
}
