using System;
using System.Collections.Generic;

namespace IntelligenceX.Tools;

/// <summary>
/// Declares outbound handoff mappings from one tool/pack boundary to another.
/// </summary>
public sealed class ToolHandoffContract {
    /// <summary>
    /// Default contract id for IX tool handoff metadata.
    /// </summary>
    public const string DefaultContractId = "ix.tool-handoff.v1";

    /// <summary>
    /// True when the tool declares outbound handoff routes.
    /// </summary>
    public bool IsHandoffAware { get; set; }

    /// <summary>
    /// Stable handoff contract identifier.
    /// </summary>
    public string HandoffContractId { get; set; } = DefaultContractId;

    /// <summary>
    /// Outbound handoff routes available from this tool.
    /// </summary>
    public IReadOnlyList<ToolHandoffRoute> OutboundRoutes { get; set; } = Array.Empty<ToolHandoffRoute>();

    /// <summary>
    /// Validates the contract and throws when invalid.
    /// </summary>
    public void Validate() {
        if (!IsHandoffAware) {
            return;
        }

        if (string.IsNullOrWhiteSpace(HandoffContractId)) {
            throw new InvalidOperationException("HandoffContractId is required when IsHandoffAware is enabled.");
        }

        if (OutboundRoutes is null || OutboundRoutes.Count == 0) {
            throw new InvalidOperationException(
                "OutboundRoutes must include at least one route when IsHandoffAware is enabled.");
        }

        for (var i = 0; i < OutboundRoutes.Count; i++) {
            var route = OutboundRoutes[i];
            if (route is null) {
                throw new InvalidOperationException("OutboundRoutes cannot contain null entries.");
            }

            route.Validate();
        }
    }
}

/// <summary>
/// Typed outbound handoff route descriptor.
/// </summary>
public sealed class ToolHandoffRoute {
    /// <summary>
    /// Target pack id that can receive this handoff.
    /// </summary>
    public string TargetPackId { get; set; } = string.Empty;

    /// <summary>
    /// Optional target tool name within the destination pack.
    /// </summary>
    public string TargetToolName { get; set; } = string.Empty;

    /// <summary>
    /// Optional target routing role when target tool name is not pinned.
    /// </summary>
    public string TargetRole { get; set; } = string.Empty;

    /// <summary>
    /// Source field to destination argument bindings.
    /// </summary>
    public IReadOnlyList<ToolHandoffBinding> Bindings { get; set; } = Array.Empty<ToolHandoffBinding>();

    /// <summary>
    /// Optional route conditions that must match before this handoff is eligible.
    /// </summary>
    public IReadOnlyList<ToolHandoffCondition> Conditions { get; set; } = Array.Empty<ToolHandoffCondition>();

    /// <summary>
    /// Optional short reason describing when this handoff is appropriate.
    /// </summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// Optional follow-up kind token describing the intent of this handoff.
    /// </summary>
    public string FollowUpKind { get; set; } = string.Empty;

    /// <summary>
    /// Optional follow-up priority hint in the range 0-100.
    /// Higher values indicate more important follow-up work.
    /// </summary>
    public int FollowUpPriority { get; set; }

    /// <summary>
    /// Validates the route descriptor.
    /// </summary>
    public void Validate() {
        if (string.IsNullOrWhiteSpace(TargetPackId)) {
            throw new InvalidOperationException("TargetPackId is required for handoff routes.");
        }

        if (string.IsNullOrWhiteSpace(TargetToolName) && string.IsNullOrWhiteSpace(TargetRole)) {
            throw new InvalidOperationException(
                "Handoff routes must declare TargetToolName or TargetRole.");
        }

        if (!string.IsNullOrWhiteSpace(TargetRole) && !ToolRoutingTaxonomy.IsAllowedRole(TargetRole.Trim())) {
            throw new InvalidOperationException(
                $"TargetRole must be one of: {string.Join(", ", ToolRoutingTaxonomy.AllowedRoles)}.");
        }

        if (Bindings is null || Bindings.Count == 0) {
            throw new InvalidOperationException("Handoff routes must include at least one binding.");
        }

        if (FollowUpPriority < 0 || FollowUpPriority > ToolHandoffFollowUpPriorities.Critical) {
            throw new InvalidOperationException("FollowUpPriority must be between 0 and 100.");
        }

        for (var i = 0; i < Bindings.Count; i++) {
            var binding = Bindings[i];
            if (binding is null) {
                throw new InvalidOperationException("Bindings cannot contain null entries.");
            }

            binding.Validate();
        }

        for (var i = 0; i < Conditions.Count; i++) {
            var condition = Conditions[i];
            if (condition is null) {
                throw new InvalidOperationException("Conditions cannot contain null entries.");
            }

            condition.Validate();
        }
    }
}

/// <summary>
/// Typed mapping from source field to destination argument.
/// </summary>
public sealed class ToolHandoffBinding {
    /// <summary>
    /// Source field name produced by current tool output context.
    /// </summary>
    public string SourceField { get; set; } = string.Empty;

    /// <summary>
    /// Destination argument name expected by the target tool.
    /// </summary>
    public string TargetArgument { get; set; } = string.Empty;

    /// <summary>
    /// True when this binding is required for a valid handoff.
    /// </summary>
    public bool IsRequired { get; set; } = true;

    /// <summary>
    /// Optional transform id applied while mapping source to destination.
    /// </summary>
    public string TransformId { get; set; } = string.Empty;

    /// <summary>
    /// Validates the binding descriptor.
    /// </summary>
    public void Validate() {
        if (string.IsNullOrWhiteSpace(SourceField)) {
            throw new InvalidOperationException("SourceField is required for handoff bindings.");
        }

        if (string.IsNullOrWhiteSpace(TargetArgument)) {
            throw new InvalidOperationException("TargetArgument is required for handoff bindings.");
        }
    }
}

/// <summary>
/// Typed equality condition that must match before a route can be prepared.
/// </summary>
public sealed class ToolHandoffCondition {
    /// <summary>
    /// Source field name produced by current tool output, metadata, or request context.
    /// </summary>
    public string SourceField { get; set; } = string.Empty;

    /// <summary>
    /// Expected normalized value for the source field.
    /// </summary>
    public string ExpectedValue { get; set; } = string.Empty;

    /// <summary>
    /// Validates the condition descriptor.
    /// </summary>
    public void Validate() {
        if (string.IsNullOrWhiteSpace(SourceField)) {
            throw new InvalidOperationException("SourceField is required for handoff conditions.");
        }

        if (string.IsNullOrWhiteSpace(ExpectedValue)) {
            throw new InvalidOperationException("ExpectedValue is required for handoff conditions.");
        }
    }
}
