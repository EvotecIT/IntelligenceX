using System;
using System.Collections.Generic;

namespace IntelligenceX.Tools;

/// <summary>
/// Declares setup and prerequisite metadata for a tool.
/// </summary>
public sealed class ToolSetupContract {
    /// <summary>
    /// Default contract id for IX setup metadata.
    /// </summary>
    public const string DefaultContractId = "ix.tool-setup.v1";

    /// <summary>
    /// True when this tool participates in setup/precondition orchestration.
    /// </summary>
    public bool IsSetupAware { get; set; }

    /// <summary>
    /// Stable setup contract identifier.
    /// </summary>
    public string SetupContractId { get; set; } = DefaultContractId;

    /// <summary>
    /// Optional setup requirement descriptors.
    /// </summary>
    public IReadOnlyList<ToolSetupRequirement> Requirements { get; set; } = Array.Empty<ToolSetupRequirement>();

    /// <summary>
    /// Optional setup helper tool name (for example pack setup probe).
    /// </summary>
    public string SetupToolName { get; set; } = string.Empty;

    /// <summary>
    /// Optional setup hint keys for UI/prompting layers.
    /// </summary>
    public IReadOnlyList<string> SetupHintKeys { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Validates the contract and throws when invalid.
    /// </summary>
    public void Validate() {
        if (!IsSetupAware) {
            return;
        }

        if (string.IsNullOrWhiteSpace(SetupContractId)) {
            throw new InvalidOperationException("SetupContractId is required when IsSetupAware is enabled.");
        }

        if (Requirements is null) {
            throw new InvalidOperationException("Requirements cannot be null when IsSetupAware is enabled.");
        }

        var hasRequiredOrHintedSetup = false;
        for (var i = 0; i < Requirements.Count; i++) {
            var requirement = Requirements[i];
            if (requirement is null) {
                throw new InvalidOperationException("Requirements cannot contain null entries.");
            }

            requirement.Validate();
            hasRequiredOrHintedSetup |= requirement.IsRequired || requirement.HintKeys.Count > 0;
        }

        if (string.IsNullOrWhiteSpace(SetupToolName) && !hasRequiredOrHintedSetup && SetupHintKeys.Count == 0) {
            throw new InvalidOperationException(
                "Setup-aware tools must declare at least one requirement, setup hint, or SetupToolName.");
        }
    }
}

/// <summary>
/// Typed descriptor for a single setup requirement.
/// </summary>
public sealed class ToolSetupRequirement {
    /// <summary>
    /// Stable requirement identifier.
    /// </summary>
    public string RequirementId { get; set; } = string.Empty;

    /// <summary>
    /// Requirement kind token.
    /// </summary>
    public string Kind { get; set; } = ToolSetupRequirementKinds.Capability;

    /// <summary>
    /// True when this requirement blocks execution.
    /// </summary>
    public bool IsRequired { get; set; } = true;

    /// <summary>
    /// Optional hint keys linked to this requirement.
    /// </summary>
    public IReadOnlyList<string> HintKeys { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Validates the setup requirement.
    /// </summary>
    public void Validate() {
        if (string.IsNullOrWhiteSpace(RequirementId)) {
            throw new InvalidOperationException("RequirementId is required for setup requirements.");
        }

        var normalizedKind = (Kind ?? string.Empty).Trim();
        if (!ToolSetupRequirementKinds.IsAllowed(normalizedKind)) {
            throw new InvalidOperationException(
                $"Kind must be one of: {string.Join(", ", ToolSetupRequirementKinds.AllowedKinds)}.");
        }
    }
}

/// <summary>
/// Allowed setup requirement kind tokens.
/// </summary>
public static class ToolSetupRequirementKinds {
    /// <summary>Capability prerequisite.</summary>
    public const string Capability = "capability";
    /// <summary>Configuration prerequisite.</summary>
    public const string Configuration = "configuration";
    /// <summary>Connectivity prerequisite.</summary>
    public const string Connectivity = "connectivity";
    /// <summary>Authentication prerequisite.</summary>
    public const string Authentication = "authentication";

    /// <summary>
    /// Allowed setup requirement kind values.
    /// </summary>
    public static readonly IReadOnlyList<string> AllowedKinds = new[] {
        Capability,
        Configuration,
        Connectivity,
        Authentication
    };

    /// <summary>
    /// Determines whether the supplied value is an allowed setup requirement kind.
    /// </summary>
    public static bool IsAllowed(string? value) {
        if (value is null) {
            return false;
        }

        var normalized = value.Trim();
        if (normalized.Length == 0) {
            return false;
        }

        for (var i = 0; i < AllowedKinds.Count; i++) {
            if (string.Equals(normalized, AllowedKinds[i], StringComparison.Ordinal)) {
                return true;
            }
        }

        return false;
    }
}
