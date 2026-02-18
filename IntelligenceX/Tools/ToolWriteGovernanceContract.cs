using System;
using IntelligenceX.Json;

namespace IntelligenceX.Tools;

/// <summary>
/// Declares write-governance requirements for a tool definition.
/// </summary>
public sealed class ToolWriteGovernanceContract {
    /// <summary>
    /// Default governance contract id for IX write authorization.
    /// </summary>
    public const string DefaultContractId = "ix.write-governance.v1";

    /// <summary>
    /// True when the tool can perform mutating/write actions.
    /// </summary>
    public bool IsWriteCapable { get; set; }

    /// <summary>
    /// True when external governance authorization is required for write execution.
    /// </summary>
    public bool RequiresGovernanceAuthorization { get; set; } = true;

    /// <summary>
    /// Stable governance contract identifier.
    /// </summary>
    public string GovernanceContractId { get; set; } = DefaultContractId;

    /// <summary>
    /// Strategy used to detect write intent from arguments.
    /// </summary>
    public ToolWriteIntentMode IntentMode { get; set; } = ToolWriteIntentMode.Always;

    /// <summary>
    /// Argument name used by <see cref="IntentMode"/>.
    /// </summary>
    public string IntentArgumentName { get; set; } = string.Empty;

    /// <summary>
    /// Required value for <see cref="ToolWriteIntentMode.StringEquals"/>.
    /// </summary>
    public string IntentStringValue { get; set; } = string.Empty;

    /// <summary>
    /// True when explicit confirmation argument must be present for write intent.
    /// </summary>
    public bool RequireExplicitConfirmation { get; set; } = true;

    /// <summary>
    /// Confirmation argument name. Expected to be boolean true.
    /// </summary>
    public string ConfirmationArgumentName { get; set; } = "allow_write";

    /// <summary>
    /// Validates the contract and throws when invalid.
    /// </summary>
    public void Validate() {
        if (!IsWriteCapable) {
            return;
        }

        if (RequiresGovernanceAuthorization && string.IsNullOrWhiteSpace(GovernanceContractId)) {
            throw new InvalidOperationException(
                "GovernanceContractId is required when RequiresGovernanceAuthorization is enabled.");
        }

        if (IntentMode == ToolWriteIntentMode.BooleanFlagTrue &&
            string.IsNullOrWhiteSpace(IntentArgumentName)) {
            throw new InvalidOperationException(
                "IntentArgumentName is required when IntentMode is BooleanFlagTrue.");
        }

        if (IntentMode == ToolWriteIntentMode.StringEquals) {
            if (string.IsNullOrWhiteSpace(IntentArgumentName)) {
                throw new InvalidOperationException(
                    "IntentArgumentName is required when IntentMode is StringEquals.");
            }
            if (string.IsNullOrWhiteSpace(IntentStringValue)) {
                throw new InvalidOperationException(
                    "IntentStringValue is required when IntentMode is StringEquals.");
            }
        }

        if (RequireExplicitConfirmation && string.IsNullOrWhiteSpace(ConfirmationArgumentName)) {
            throw new InvalidOperationException(
                "ConfirmationArgumentName is required when RequireExplicitConfirmation is enabled.");
        }
    }

    /// <summary>
    /// Returns true when provided arguments request write intent.
    /// </summary>
    public bool IsWriteRequested(JsonObject? arguments) {
        if (!IsWriteCapable) {
            return false;
        }

        if (IntentMode == ToolWriteIntentMode.Always) {
            return true;
        }

        if (arguments is null || string.IsNullOrWhiteSpace(IntentArgumentName)) {
            return false;
        }

        if (IntentMode == ToolWriteIntentMode.BooleanFlagTrue) {
            return arguments.GetBoolean(IntentArgumentName, defaultValue: false);
        }

        if (IntentMode == ToolWriteIntentMode.StringEquals) {
            string? value = arguments.GetString(IntentArgumentName);
            return string.Equals(
                value?.Trim(),
                IntentStringValue.Trim(),
                StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    /// <summary>
    /// Returns true when explicit confirmation requirement is satisfied.
    /// </summary>
    public bool HasExplicitConfirmation(JsonObject? arguments) {
        if (!RequireExplicitConfirmation) {
            return true;
        }

        if (arguments is null || string.IsNullOrWhiteSpace(ConfirmationArgumentName)) {
            return false;
        }

        return arguments.GetBoolean(ConfirmationArgumentName, defaultValue: false);
    }
}
